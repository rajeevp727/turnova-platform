using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Azure.Cosmos;
using Microsoft.IdentityModel.Tokens;

namespace TurnOva.API;

public sealed record CosmosRegisterRequest(string Email, string Password, string? Name);
public sealed record CosmosLoginRequest(string Email, string Password);
public sealed record CosmosRefreshRequest(string? RefreshToken);

public sealed class CosmosIdentityService(CosmosClient? cosmosClient = null, IConfiguration? configuration = null)
{
    private const string DatabaseNameDefault = "TurnOvaDb";
    private const string ContainerName = "TurnOvaUsers";
    private const int AccessTokenMinutes = 30;
    private const int RefreshTokenDays = 30;
    private const int Pbkdf2Iterations = 210_000;
    private Container? _container;
    private readonly SemaphoreSlim _containerLock = new(1, 1);

    private sealed record IdentityDocument
    {
        public required string Id { get; init; }
        public required string Role { get; init; }
        public required string Email { get; init; }
        public string? Name { get; init; }
        public string? PasswordHash { get; init; }
        public string? PasswordSalt { get; init; }
        public string? RefreshTokenHash { get; set; }
        public DateTimeOffset? RefreshTokenExpiresAtUtc { get; set; }
        public DateTimeOffset CreatedAtUtc { get; init; }
        public string DocumentType { get; init; } = "user";
    }

    private sealed record EmailReservation
    {
        public required string Id { get; init; }
        public required string Role { get; init; }
        public required string Email { get; init; }
        public string DocumentType { get; init; } = "email-reservation";
    }

    public async Task<IResult> RegisterAsync(string role, CosmosRegisterRequest request, CancellationToken ct)
    {
        var normalizedRole = NormalizeRole(role);
        if (normalizedRole is null) return Results.NotFound();
        var email = NormalizeEmail(request.Email);
        if (email is null || !IsValidEmail(email))
            return Results.BadRequest(new { message = "Enter a valid email address." });
        if (string.IsNullOrWhiteSpace(request.Password) || request.Password.Length < 10 ||
            request.Password.Length > 128 || !request.Password.Any(char.IsLetter) ||
            !request.Password.Any(char.IsDigit))
            return Results.BadRequest(new { message = "Password must be 10–128 characters and contain at least one letter and one number." });
        if (normalizedRole == "Driver" && string.IsNullOrWhiteSpace(request.Name))
            return Results.BadRequest(new { message = "Name is required for Driver registration." });

        var container = await GetContainerAsync(ct);
        if (container is null) return StorageUnavailable();

        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(request.Password, salt, Pbkdf2Iterations, HashAlgorithmName.SHA256, 32);
        var userId = Guid.NewGuid().ToString("N");
        var reservation = new EmailReservation
        {
            Id = $"email:{email}", Role = normalizedRole, Email = email
        };
        try
        {
            await container.CreateItemAsync(reservation, new PartitionKey(normalizedRole), cancellationToken: ct);
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Conflict)
        {
            return Results.Conflict(new { message = "An account with this email already exists for this app." });
        }
        catch (CosmosException)
        {
            return StorageUnavailable();
        }

        var refreshToken = CreateRefreshToken();
        var user = new IdentityDocument
        {
            Id = $"user:{userId}",
            Role = normalizedRole,
            Email = email,
            Name = request.Name?.Trim(),
            PasswordHash = Convert.ToBase64String(hash),
            PasswordSalt = Convert.ToBase64String(salt),
            RefreshTokenHash = HashRefreshToken(refreshToken),
            RefreshTokenExpiresAtUtc = DateTimeOffset.UtcNow.AddDays(RefreshTokenDays),
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
        try
        {
            await container.CreateItemAsync(user, new PartitionKey(normalizedRole), cancellationToken: ct);
        }
        catch
        {
            try { await container.DeleteItemAsync<EmailReservation>(reservation.Id, new PartitionKey(normalizedRole), cancellationToken: ct); }
            catch { /* Reservation cleanup is best-effort; never expose credentials in an error. */ }
            return StorageUnavailable();
        }

        return Results.Ok(CreateSession(user, refreshToken));
    }

    public async Task<IResult> LoginAsync(string role, CosmosLoginRequest request, CancellationToken ct)
    {
        var normalizedRole = NormalizeRole(role);
        if (normalizedRole is null) return Results.NotFound();
        var email = NormalizeEmail(request.Email);
        if (email is null || string.IsNullOrWhiteSpace(request.Password))
            return Results.BadRequest(new { message = "Email and password are required." });

        var container = await GetContainerAsync(ct);
        if (container is null) return StorageUnavailable();
        IdentityDocument? user;
        try
        {
            var query = new QueryDefinition("SELECT * FROM c WHERE c.id = @id AND c.documentType = 'user'")
                .WithParameter("@id", $"user:{email}");
            // Email is indexed as a normalized field. The query is scoped to the role partition.
            query = new QueryDefinition("SELECT * FROM c WHERE c.email = @email AND c.documentType = 'user'")
                .WithParameter("@email", email);
            using var iterator = container.GetItemQueryIterator<IdentityDocument>(query, requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey(normalizedRole), MaxItemCount = 1 });
            user = null;
            while (iterator.HasMoreResults && user is null)
            {
                var page = await iterator.ReadNextAsync(ct);
                user = page.FirstOrDefault();
            }
        }
        catch (CosmosException) { return StorageUnavailable(); }

        if (user is null || !VerifyPassword(request.Password, user.PasswordSalt, user.PasswordHash))
            return Results.Problem("Email or password is incorrect.", statusCode: 401, title: "Invalid credentials.");

        var refreshToken = CreateRefreshToken();
        user.RefreshTokenHash = HashRefreshToken(refreshToken);
        user.RefreshTokenExpiresAtUtc = DateTimeOffset.UtcNow.AddDays(RefreshTokenDays);
        try
        {
            await container.ReplaceItemAsync(user, user.Id, new PartitionKey(normalizedRole), cancellationToken: ct);
        }
        catch (CosmosException) { return StorageUnavailable(); }
        return Results.Ok(CreateSession(user, refreshToken));
    }

    public async Task<IResult> RefreshAsync(CosmosRefreshRequest? request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request?.RefreshToken))
            return Results.BadRequest(new { message = "Refresh token is required." });
        var container = await GetContainerAsync(ct);
        if (container is null) return StorageUnavailable();
        var tokenHash = HashRefreshToken(request.RefreshToken);
        foreach (var role in new[] { "Rider", "Driver" })
        {
            try
            {
                var query = new QueryDefinition("SELECT * FROM c WHERE c.refreshTokenHash = @hash AND c.documentType = 'user'")
                    .WithParameter("@hash", tokenHash);
                using var iterator = container.GetItemQueryIterator<IdentityDocument>(query, requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey(role), MaxItemCount = 1 });
                while (iterator.HasMoreResults)
                {
                    var page = await iterator.ReadNextAsync(ct);
                    var user = page.FirstOrDefault();
                    if (user is null) continue;
                    if (user.RefreshTokenExpiresAtUtc is null || user.RefreshTokenExpiresAtUtc <= DateTimeOffset.UtcNow)
                        return InvalidRefreshToken();
                    var nextRefresh = CreateRefreshToken();
                    user.RefreshTokenHash = HashRefreshToken(nextRefresh);
                    user.RefreshTokenExpiresAtUtc = DateTimeOffset.UtcNow.AddDays(RefreshTokenDays);
                    await container.ReplaceItemAsync(user, user.Id, new PartitionKey(role), cancellationToken: ct);
                    return Results.Ok(CreateSession(user, nextRefresh));
                }
            }
            catch (CosmosException) { return StorageUnavailable(); }
        }
        return InvalidRefreshToken();
    }

    public async Task<IResult> LogoutAsync(ClaimsPrincipal principal, CancellationToken ct)
    {
        var userId = principal.FindFirstValue(JwtRegisteredClaimNames.Sub);
        var role = NormalizeRole(principal.FindFirstValue("role"));
        if (string.IsNullOrWhiteSpace(userId) || role is null) return Results.Unauthorized();
        var container = await GetContainerAsync(ct);
        if (container is null) return StorageUnavailable();
        try
        {
            var id = $"user:{userId}";
            var response = await container.ReadItemAsync<IdentityDocument>(id, new PartitionKey(role), cancellationToken: ct);
            var user = response.Resource;
            user.RefreshTokenHash = null;
            user.RefreshTokenExpiresAtUtc = null;
            await container.ReplaceItemAsync(user, id, new PartitionKey(role), cancellationToken: ct);
            return Results.NoContent();
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound) { return Results.NoContent(); }
        catch (CosmosException) { return StorageUnavailable(); }
    }

    public static IResult Me(ClaimsPrincipal principal)
    {
        var id = principal.FindFirstValue(JwtRegisteredClaimNames.Sub);
        var email = principal.FindFirstValue(JwtRegisteredClaimNames.Email);
        var name = principal.FindFirstValue("name");
        var role = principal.FindFirstValue("role");
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(email) || NormalizeRole(role) is null)
            return Results.Unauthorized();
        return Results.Ok(new { user = new { id, email, name, role } });
    }

    private async Task<Container?> GetContainerAsync(CancellationToken ct)
    {
        if (cosmosClient is null) return null;
        if (_container is not null) return _container;
        await _containerLock.WaitAsync(ct);
        try
        {
            if (_container is not null) return _container;
            var database = cosmosClient!.GetDatabase(configuration?["CosmosDb:DatabaseName"] ?? DatabaseNameDefault);
            await database.CreateContainerIfNotExistsAsync(new ContainerProperties(ContainerName, "/role"), cancellationToken: ct);
            _container = database.GetContainer(ContainerName);
            return _container;
        }
        catch (CosmosException) { return null; }
        finally { _containerLock.Release(); }
    }

    private object CreateSession(IdentityDocument user, string refreshToken)
    {
        var issuer = configuration?["Auth:Jwt:Issuer"];
        var audience = configuration?["Auth:Jwt:Audience"];
        var signingKey = configuration?["Auth:Jwt:SigningKey"];
        if (string.IsNullOrWhiteSpace(issuer) || string.IsNullOrWhiteSpace(audience) ||
            string.IsNullOrWhiteSpace(signingKey) || Encoding.UTF8.GetByteCount(signingKey) < 32)
            throw new InvalidOperationException("JWT issuer, audience and a signing key of at least 32 bytes must be configured.");
        var now = DateTime.UtcNow;
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id["user:".Length..]),
            new Claim(JwtRegisteredClaimNames.Email, user.Email),
            new Claim("name", user.Name ?? user.Email),
            new Claim("role", user.Role)
        };
        var credentials = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)), SecurityAlgorithms.HmacSha256);
        var jwt = new JwtSecurityToken(issuer, audience, claims, now, now.AddMinutes(AccessTokenMinutes), credentials);
        return new
        {
            accessToken = new JwtSecurityTokenHandler().WriteToken(jwt),
            refreshToken,
            expiresIn = AccessTokenMinutes * 60,
            isDemo = false,
            user = new { id = user.Id["user:".Length..], email = user.Email, name = user.Name ?? user.Email, role = user.Role, isDemo = false }
        };
    }

    private static bool VerifyPassword(string password, string? saltValue, string? hashValue)
    {
        if (string.IsNullOrWhiteSpace(saltValue) || string.IsNullOrWhiteSpace(hashValue)) return false;
        try
        {
            var salt = Convert.FromBase64String(saltValue);
            var expected = Convert.FromBase64String(hashValue);
            var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, Pbkdf2Iterations, HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException) { return false; }
    }

    private static string? NormalizeRole(string? role) => role?.Trim().ToLowerInvariant() switch
    {
        "rider" => "Rider",
        "driver" => "Driver",
        _ => null
    };
    private static string? NormalizeEmail(string? email) => string.IsNullOrWhiteSpace(email) ? null : email.Trim().ToLowerInvariant();
    private static bool IsValidEmail(string email) => email.Length <= 254 && System.Net.Mail.MailAddress.TryCreate(email, out var address) && address.Address.Equals(email, StringComparison.OrdinalIgnoreCase);
    private static string CreateRefreshToken() => Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(48));
    private static string HashRefreshToken(string token) => Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    private static IResult InvalidRefreshToken() => Results.Problem("Refresh token is invalid or expired. Please sign in again.", statusCode: 401, title: "Invalid refresh token.");
    private static IResult StorageUnavailable() => Results.Problem("TurnOva account storage is unavailable. Check the Cosmos DB connection and retry.", statusCode: 503, title: "Account storage unavailable.");
}
