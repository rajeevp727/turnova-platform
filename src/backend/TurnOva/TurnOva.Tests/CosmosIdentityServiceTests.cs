using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using Moq;
using TurnOva.API;

namespace TurnOva.Tests;

public sealed class CosmosIdentityServiceTests
{
    private const string SigningKey = "turnova-auth-integration-test-signing-key-32-bytes";
    private const string Password = "SecurePass123";

    [Fact]
    public async Task RegisterCreatesEmailReservationAndUserInOnePartitionBatch()
    {
        var (container, batch, _) = CreateBatchResponse(HttpStatusCode.OK, success: true);
        var service = CreateService(container.Object);

        var result = await service.RegisterAsync(
            "rider",
            new CosmosRegisterRequest("Rider@Example.com", Password, "Rider"),
            CancellationToken.None);

        Assert.Equal(StatusCodes.Status200OK, GetStatusCode(result));
        container.Verify(value => value.CreateTransactionalBatch(
            It.Is<PartitionKey>(key => key.Equals(new PartitionKey("Rider")))), Times.Once);
        batch.Verify(value => value.CreateItem(
            It.Is<CosmosIdentityService.EmailReservation>(reservation =>
                reservation.Id == "email:rider@example.com" &&
                reservation.Role == "Rider" &&
                reservation.Email == "rider@example.com"),
            It.IsAny<TransactionalBatchItemRequestOptions?>()), Times.Once);
        batch.Verify(value => value.CreateItem(
            It.Is<CosmosIdentityService.IdentityDocument>(user =>
                user.Role == "Rider" &&
                user.Email == "rider@example.com" &&
                !string.IsNullOrWhiteSpace(user.PasswordHash) &&
                Convert.FromBase64String(user.PasswordHash).Length == 32 &&
                !string.IsNullOrWhiteSpace(user.PasswordSalt) &&
                Convert.FromBase64String(user.PasswordSalt).Length == 16 &&
                !string.IsNullOrWhiteSpace(user.RefreshTokenHash)),
            It.IsAny<TransactionalBatchItemRequestOptions?>()), Times.Once);
        batch.Verify(value => value.ExecuteAsync(It.IsAny<CancellationToken>()), Times.Once);
        Assert.DoesNotContain(container.Invocations, invocation =>
            invocation.Method.Name is "CreateItemAsync" or "DeleteItemAsync");
    }

    [Fact]
    public async Task RegisterConflictDoesNotAttemptCompensatingDelete()
    {
        var (container, batch, _) = CreateBatchResponse(HttpStatusCode.Conflict, success: false);
        var service = CreateService(container.Object);

        var result = await service.RegisterAsync(
            "rider",
            new CosmosRegisterRequest("rider@example.com", Password, "Rider"),
            CancellationToken.None);

        Assert.Equal(StatusCodes.Status409Conflict, GetStatusCode(result));
        batch.Verify(value => value.ExecuteAsync(It.IsAny<CancellationToken>()), Times.Once);
        Assert.DoesNotContain(container.Invocations, invocation =>
            invocation.Method.Name is "CreateItemAsync" or "DeleteItemAsync");
    }

    [Fact]
    public async Task RegisterPropagatesCancellationWithoutRunningCompensation()
    {
        var (container, batch, _) = CreateBatchResponse(HttpStatusCode.OK, success: true);
        batch.Setup(value => value.ExecuteAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());
        var service = CreateService(container.Object);

        await Assert.ThrowsAsync<OperationCanceledException>(() => service.RegisterAsync(
            "rider",
            new CosmosRegisterRequest("rider@example.com", Password, "Rider"),
            CancellationToken.None));

        Assert.DoesNotContain(container.Invocations, invocation =>
            invocation.Method.Name is "CreateItemAsync" or "DeleteItemAsync");
    }

    [Fact]
    public async Task LoginUsesPointReadEtagForRefreshTokenRotation()
    {
        var user = CreateIdentity();
        var (container, iterator) = SetupIdentityQuery(user);
        var read = CreateItemResponse(user, "login-observed-etag");
        container.Setup(value => value.ReadItemAsync<CosmosIdentityService.IdentityDocument>(
                user.Id, It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(read.Object);
        container.Setup(value => value.ReplaceItemAsync(
                It.IsAny<CosmosIdentityService.IdentityDocument>(),
                user.Id,
                It.IsAny<PartitionKey?>(),
                It.Is<ItemRequestOptions?>(options => options != null && options.IfMatchEtag == "login-observed-etag"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Mock<ItemResponse<CosmosIdentityService.IdentityDocument>>().Object);
        var service = CreateService(container.Object);

        var result = await service.LoginAsync(
            "RIDER",
            new CosmosLoginRequest("RIDER@example.com", Password),
            CancellationToken.None);

        Assert.Equal(StatusCodes.Status200OK, GetStatusCode(result));
        container.Verify(value => value.ReplaceItemAsync(
            It.Is<CosmosIdentityService.IdentityDocument>(item =>
                item.Id == user.Id && !string.IsNullOrWhiteSpace(item.RefreshTokenHash)),
            user.Id,
            It.Is<PartitionKey?>(key => key.HasValue && key.Value.Equals(new PartitionKey("Rider"))),
            It.Is<ItemRequestOptions?>(options => options != null && options.IfMatchEtag == "login-observed-etag"),
            It.IsAny<CancellationToken>()), Times.Once);
        iterator.Verify(value => value.ReadNextAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task LoginRejectsStaleEtagInsteadOfReturningAnUnpersistedSession()
    {
        var user = CreateIdentity();
        var read = CreateItemResponse(user, "login-stale-etag");
        var container = new Mock<Container>();
        var iterator = CreateQueryIterator(user);
        container.Setup(value => value.GetItemQueryIterator<CosmosIdentityService.IdentityDocument>(
                It.IsAny<QueryDefinition>(), It.IsAny<string?>(), It.IsAny<QueryRequestOptions?>()))
            .Returns(iterator.Object);
        container.Setup(value => value.ReadItemAsync<CosmosIdentityService.IdentityDocument>(
                user.Id, It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(read.Object);
        container.Setup(value => value.ReplaceItemAsync(
                It.IsAny<CosmosIdentityService.IdentityDocument>(),
                user.Id,
                It.IsAny<PartitionKey?>(),
                It.IsAny<ItemRequestOptions?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new CosmosException("etag changed", HttpStatusCode.PreconditionFailed, 0, "auth-test", 0));
        var service = CreateService(container.Object);

        var result = await service.LoginAsync(
            "rider",
            new CosmosLoginRequest(user.Email, Password),
            CancellationToken.None);

        Assert.Equal(StatusCodes.Status409Conflict, GetStatusCode(result));
        container.Verify(value => value.ReplaceItemAsync(
            It.IsAny<CosmosIdentityService.IdentityDocument>(),
            user.Id,
            It.IsAny<PartitionKey?>(),
            It.Is<ItemRequestOptions?>(options => options != null && options.IfMatchEtag == "login-stale-etag"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RefreshUsesObservedEtagAndPersistsANewRefreshTokenHash()
    {
        const string refreshToken = "refresh-token-success-test";
        var tokenHash = HashRefreshToken(refreshToken);
        var user = CreateIdentity(refreshTokenHash: tokenHash);
        var iterator = CreateQueryIterator(user);
        var read = CreateItemResponse(user, "refresh-success-etag");
        var container = new Mock<Container>();
        container.Setup(value => value.GetItemQueryIterator<CosmosIdentityService.IdentityDocument>(
                It.IsAny<QueryDefinition>(), It.IsAny<string?>(), It.IsAny<QueryRequestOptions?>()))
            .Returns(iterator.Object);
        container.Setup(value => value.ReadItemAsync<CosmosIdentityService.IdentityDocument>(
                user.Id, It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(read.Object);
        container.Setup(value => value.ReplaceItemAsync(
                It.IsAny<CosmosIdentityService.IdentityDocument>(),
                user.Id,
                It.IsAny<PartitionKey?>(),
                It.Is<ItemRequestOptions?>(options => options != null && options.IfMatchEtag == "refresh-success-etag"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Mock<ItemResponse<CosmosIdentityService.IdentityDocument>>().Object);
        var service = CreateService(container.Object);

        var result = await service.RefreshAsync(new CosmosRefreshRequest(refreshToken), CancellationToken.None);

        Assert.Equal(StatusCodes.Status200OK, GetStatusCode(result));
        var write = Assert.Single(container.Invocations, invocation => invocation.Method.Name == "ReplaceItemAsync");
        var updatedUser = Assert.IsType<CosmosIdentityService.IdentityDocument>(write.Arguments[0]);
        Assert.NotEqual(tokenHash, updatedUser.RefreshTokenHash);
        Assert.True(updatedUser.RefreshTokenExpiresAtUtc.HasValue && updatedUser.RefreshTokenExpiresAtUtc.Value > DateTimeOffset.UtcNow);
        Assert.Equal("refresh-success-etag", (write.Arguments[3] as ItemRequestOptions)?.IfMatchEtag);
    }

    [Fact]
    public async Task RefreshRejectsAStaleConcurrentRotationUsingTheObservedEtag()
    {
        const string refreshToken = "refresh-token-for-concurrency-test";
        var tokenHash = HashRefreshToken(refreshToken);
        var user = CreateIdentity(refreshTokenHash: tokenHash);
        var iterator = CreateQueryIterator(user);
        var read = CreateItemResponse(user, "refresh-observed-etag");
        var container = new Mock<Container>();
        container.Setup(value => value.GetItemQueryIterator<CosmosIdentityService.IdentityDocument>(
                It.IsAny<QueryDefinition>(), It.IsAny<string?>(), It.IsAny<QueryRequestOptions?>()))
            .Returns(iterator.Object);
        container.Setup(value => value.ReadItemAsync<CosmosIdentityService.IdentityDocument>(
                user.Id, It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(read.Object);
        container.Setup(value => value.ReplaceItemAsync(
                It.IsAny<CosmosIdentityService.IdentityDocument>(),
                user.Id,
                It.IsAny<PartitionKey?>(),
                It.Is<ItemRequestOptions?>(options => options != null && options.IfMatchEtag == "refresh-observed-etag"),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new CosmosException("etag changed", HttpStatusCode.PreconditionFailed, 0, "auth-test", 0));
        var service = CreateService(container.Object);

        var result = await service.RefreshAsync(new CosmosRefreshRequest(refreshToken), CancellationToken.None);

        Assert.Equal(StatusCodes.Status401Unauthorized, GetStatusCode(result));
        container.Verify(value => value.ReplaceItemAsync(
            It.IsAny<CosmosIdentityService.IdentityDocument>(),
            user.Id,
            It.IsAny<PartitionKey?>(),
            It.Is<ItemRequestOptions?>(options => options != null && options.IfMatchEtag == "refresh-observed-etag"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task LogoutRetriesAfterConcurrentSessionChangeAndUsesFreshEtag()
    {
        var user = CreateIdentity(refreshTokenHash: "old-refresh-hash");
        var refreshedUser = user with
        {
            RefreshTokenHash = "new-refresh-hash",
            RefreshTokenExpiresAtUtc = DateTimeOffset.UtcNow.AddDays(1)
        };
        var firstRead = CreateItemResponse(user, "logout-old-etag");
        var secondRead = CreateItemResponse(refreshedUser, "logout-new-etag");
        var container = new Mock<Container>();
        container.SetupSequence(value => value.ReadItemAsync<CosmosIdentityService.IdentityDocument>(
                user.Id, It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(firstRead.Object)
            .ReturnsAsync(secondRead.Object);
        container.SetupSequence(value => value.ReplaceItemAsync(
                It.IsAny<CosmosIdentityService.IdentityDocument>(),
                user.Id,
                It.IsAny<PartitionKey?>(),
                It.IsAny<ItemRequestOptions?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new CosmosException("etag changed", HttpStatusCode.PreconditionFailed, 0, "auth-test", 0))
            .ReturnsAsync(new Mock<ItemResponse<CosmosIdentityService.IdentityDocument>>().Object);
        var service = CreateService(container.Object);
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(JwtRegisteredClaimNames.Sub, "user-1"),
            new Claim("role", "Rider")
        ], "integration-test"));

        var result = await service.LogoutAsync(principal, CancellationToken.None);

        Assert.Equal(StatusCodes.Status204NoContent, GetStatusCode(result));
        var writes = container.Invocations.Where(invocation => invocation.Method.Name == "ReplaceItemAsync").ToArray();
        Assert.Equal(2, writes.Length);
        Assert.Equal("logout-old-etag", (writes[0].Arguments[3] as ItemRequestOptions)?.IfMatchEtag);
        Assert.Equal("logout-new-etag", (writes[1].Arguments[3] as ItemRequestOptions)?.IfMatchEtag);
        Assert.Null((writes[1].Arguments[0] as CosmosIdentityService.IdentityDocument)?.RefreshTokenHash);
    }



    [Fact]
    public async Task RegisterPreservesCancellationWhenBatchReturnsConflict()
    {
        using var cts = new CancellationTokenSource();
        var (container, batch, _) = CreateBatchResponse(HttpStatusCode.OK, success: true);
        batch.Setup(value => value.ExecuteAsync(It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                cts.Cancel();
                return Task.FromException<TransactionalBatchResponse>(
                    new CosmosException("conflict", HttpStatusCode.Conflict, 0, "auth-cancel-test", 0));
            });
        var service = CreateService(container.Object);

        await Assert.ThrowsAsync<OperationCanceledException>(() => service.RegisterAsync(
            "rider",
            new CosmosRegisterRequest("rider@example.com", Password, "Rider"),
            cts.Token));

        batch.Verify(value => value.ExecuteAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RegisterPreservesCancellationWhenBatchReturnsProviderFailure()
    {
        using var cts = new CancellationTokenSource();
        var (container, batch, _) = CreateBatchResponse(HttpStatusCode.OK, success: true);
        batch.Setup(value => value.ExecuteAsync(It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                cts.Cancel();
                return Task.FromException<TransactionalBatchResponse>(
                    new CosmosException("provider unavailable", HttpStatusCode.ServiceUnavailable, 0, "auth-cancel-test", 0));
            });
        var service = CreateService(container.Object);

        await Assert.ThrowsAsync<OperationCanceledException>(() => service.RegisterAsync(
            "rider",
            new CosmosRegisterRequest("rider@example.com", Password, "Rider"),
            cts.Token));

        batch.Verify(value => value.ExecuteAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task LoginPreservesCancellationWhenQueryReturnsProviderFailure()
    {
        using var cts = new CancellationTokenSource();
        var container = new Mock<Container>();
        container.Setup(value => value.GetItemQueryIterator<CosmosIdentityService.IdentityDocument>(
                It.IsAny<QueryDefinition>(), It.IsAny<string?>(), It.IsAny<QueryRequestOptions?>()))
            .Returns(() =>
            {
                cts.Cancel();
                throw new CosmosException("provider unavailable", HttpStatusCode.ServiceUnavailable, 0, "auth-cancel-test", 0);
            });
        var service = CreateService(container.Object);

        await Assert.ThrowsAsync<OperationCanceledException>(() => service.LoginAsync(
            "rider",
            new CosmosLoginRequest("rider@example.com", Password),
            cts.Token));

        Assert.DoesNotContain(container.Invocations, invocation => invocation.Method.Name is "ReadItemAsync" or "ReplaceItemAsync");
    }

    [Fact]
    public async Task LoginPreservesCancellationWhenPointReadReturnsNotFound()
    {
        using var cts = new CancellationTokenSource();
        var user = CreateIdentity();
        var (container, _) = SetupIdentityQuery(user);
        container.Setup(value => value.ReadItemAsync<CosmosIdentityService.IdentityDocument>(
                user.Id, It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                cts.Cancel();
                return Task.FromException<ItemResponse<CosmosIdentityService.IdentityDocument>>(
                    new CosmosException("not found", HttpStatusCode.NotFound, 0, "auth-cancel-test", 0));
            });
        var service = CreateService(container.Object);

        await Assert.ThrowsAsync<OperationCanceledException>(() => service.LoginAsync(
            "rider",
            new CosmosLoginRequest(user.Email, Password),
            cts.Token));

        container.Verify(value => value.ReplaceItemAsync(
            It.IsAny<CosmosIdentityService.IdentityDocument>(),
            It.IsAny<string>(),
            It.IsAny<PartitionKey?>(),
            It.IsAny<ItemRequestOptions?>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task LoginPreservesCancellationWhenRefreshTokenRotationReturnsPreconditionFailed()
    {
        using var cts = new CancellationTokenSource();
        var user = CreateIdentity();
        var (container, _) = SetupIdentityQuery(user);
        var read = CreateItemResponse(user, "login-cancelled-etag");
        container.Setup(value => value.ReadItemAsync<CosmosIdentityService.IdentityDocument>(
                user.Id, It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(read.Object);
        container.Setup(value => value.ReplaceItemAsync(
                It.IsAny<CosmosIdentityService.IdentityDocument>(),
                user.Id,
                It.IsAny<PartitionKey?>(),
                It.IsAny<ItemRequestOptions?>(),
                It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                cts.Cancel();
                return Task.FromException<ItemResponse<CosmosIdentityService.IdentityDocument>>(
                    new CosmosException("etag changed", HttpStatusCode.PreconditionFailed, 0, "auth-cancel-test", 0));
            });
        var service = CreateService(container.Object);

        await Assert.ThrowsAsync<OperationCanceledException>(() => service.LoginAsync(
            "rider",
            new CosmosLoginRequest(user.Email, Password),
            cts.Token));
    }

    [Fact]
    public async Task RefreshPreservesCancellationWhenQueryReturnsProviderFailure()
    {
        using var cts = new CancellationTokenSource();
        var container = new Mock<Container>();
        container.Setup(value => value.GetItemQueryIterator<CosmosIdentityService.IdentityDocument>(
                It.IsAny<QueryDefinition>(), It.IsAny<string?>(), It.IsAny<QueryRequestOptions?>()))
            .Returns(() =>
            {
                cts.Cancel();
                throw new CosmosException("provider unavailable", HttpStatusCode.ServiceUnavailable, 0, "auth-cancel-test", 0);
            });
        var service = CreateService(container.Object);

        await Assert.ThrowsAsync<OperationCanceledException>(() => service.RefreshAsync(
            new CosmosRefreshRequest("refresh-token"), cts.Token));

        Assert.DoesNotContain(container.Invocations, invocation => invocation.Method.Name is "ReadItemAsync" or "ReplaceItemAsync");
    }

    [Fact]
    public async Task RefreshPreservesCancellationWhenPointReadReturnsNotFound()
    {
        using var cts = new CancellationTokenSource();
        const string refreshToken = "refresh-token-not-found-cancelled";
        var user = CreateIdentity(refreshTokenHash: HashRefreshToken(refreshToken));
        var iterator = CreateQueryIterator(user);
        var container = new Mock<Container>();
        container.Setup(value => value.GetItemQueryIterator<CosmosIdentityService.IdentityDocument>(
                It.IsAny<QueryDefinition>(), It.IsAny<string?>(), It.IsAny<QueryRequestOptions?>()))
            .Returns(iterator.Object);
        container.Setup(value => value.ReadItemAsync<CosmosIdentityService.IdentityDocument>(
                user.Id, It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                cts.Cancel();
                return Task.FromException<ItemResponse<CosmosIdentityService.IdentityDocument>>(
                    new CosmosException("not found", HttpStatusCode.NotFound, 0, "auth-cancel-test", 0));
            });
        var service = CreateService(container.Object);

        await Assert.ThrowsAsync<OperationCanceledException>(() => service.RefreshAsync(
            new CosmosRefreshRequest(refreshToken), cts.Token));

        container.Verify(value => value.ReplaceItemAsync(
            It.IsAny<CosmosIdentityService.IdentityDocument>(),
            It.IsAny<string>(),
            It.IsAny<PartitionKey?>(),
            It.IsAny<ItemRequestOptions?>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RefreshPreservesCancellationWhenTokenRotationReturnsPreconditionFailed()
    {
        using var cts = new CancellationTokenSource();
        const string refreshToken = "refresh-token-rotation-cancelled";
        var user = CreateIdentity(refreshTokenHash: HashRefreshToken(refreshToken));
        var iterator = CreateQueryIterator(user);
        var read = CreateItemResponse(user, "refresh-cancelled-etag");
        var container = new Mock<Container>();
        container.Setup(value => value.GetItemQueryIterator<CosmosIdentityService.IdentityDocument>(
                It.IsAny<QueryDefinition>(), It.IsAny<string?>(), It.IsAny<QueryRequestOptions?>()))
            .Returns(iterator.Object);
        container.Setup(value => value.ReadItemAsync<CosmosIdentityService.IdentityDocument>(
                user.Id, It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(read.Object);
        container.Setup(value => value.ReplaceItemAsync(
                It.IsAny<CosmosIdentityService.IdentityDocument>(),
                user.Id,
                It.IsAny<PartitionKey?>(),
                It.IsAny<ItemRequestOptions?>(),
                It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                cts.Cancel();
                return Task.FromException<ItemResponse<CosmosIdentityService.IdentityDocument>>(
                    new CosmosException("etag changed", HttpStatusCode.PreconditionFailed, 0, "auth-cancel-test", 0));
            });
        var service = CreateService(container.Object);

        await Assert.ThrowsAsync<OperationCanceledException>(() => service.RefreshAsync(
            new CosmosRefreshRequest(refreshToken), cts.Token));
    }

    [Fact]
    public async Task LogoutPreservesCancellationWhenPointReadReturnsNotFound()
    {
        using var cts = new CancellationTokenSource();
        var container = new Mock<Container>();
        container.Setup(value => value.ReadItemAsync<CosmosIdentityService.IdentityDocument>(
                "user:user-1", It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                cts.Cancel();
                return Task.FromException<ItemResponse<CosmosIdentityService.IdentityDocument>>(
                    new CosmosException("not found", HttpStatusCode.NotFound, 0, "auth-cancel-test", 0));
            });
        var service = CreateService(container.Object);
        var principal = CreateRiderPrincipal();

        await Assert.ThrowsAsync<OperationCanceledException>(() => service.LogoutAsync(principal, cts.Token));

        container.Verify(value => value.ReplaceItemAsync(
            It.IsAny<CosmosIdentityService.IdentityDocument>(),
            It.IsAny<string>(),
            It.IsAny<PartitionKey?>(),
            It.IsAny<ItemRequestOptions?>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task LogoutPreservesCancellationWhenPointReadReturnsProviderFailure()
    {
        using var cts = new CancellationTokenSource();
        var container = new Mock<Container>();
        container.Setup(value => value.ReadItemAsync<CosmosIdentityService.IdentityDocument>(
                "user:user-1", It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                cts.Cancel();
                return Task.FromException<ItemResponse<CosmosIdentityService.IdentityDocument>>(
                    new CosmosException("provider unavailable", HttpStatusCode.ServiceUnavailable, 0, "auth-cancel-test", 0));
            });
        var service = CreateService(container.Object);
        var principal = CreateRiderPrincipal();

        await Assert.ThrowsAsync<OperationCanceledException>(() => service.LogoutAsync(principal, cts.Token));

        container.Verify(value => value.ReplaceItemAsync(
            It.IsAny<CosmosIdentityService.IdentityDocument>(),
            It.IsAny<string>(),
            It.IsAny<PartitionKey?>(),
            It.IsAny<ItemRequestOptions?>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task LoginHonorsPreCancelledTokenBeforeCosmosQuery()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var container = new Mock<Container>();
        var service = CreateService(container.Object);

        await Assert.ThrowsAsync<OperationCanceledException>(() => service.LoginAsync(
            "rider",
            new CosmosLoginRequest("rider@example.com", Password),
            cts.Token));

        Assert.Empty(container.Invocations);
    }

    [Fact]
    public async Task LoginStopsBeforePointReadWhenQueryCompletesAfterCancellation()
    {
        using var cts = new CancellationTokenSource();
        var user = CreateIdentity();
        var iterator = CreateQueryIterator(user, cts.Cancel);
        var container = new Mock<Container>();
        container.Setup(value => value.GetItemQueryIterator<CosmosIdentityService.IdentityDocument>(
                It.IsAny<QueryDefinition>(), It.IsAny<string?>(), It.IsAny<QueryRequestOptions?>()))
            .Returns(iterator.Object);
        var service = CreateService(container.Object);

        await Assert.ThrowsAsync<OperationCanceledException>(() => service.LoginAsync(
            "rider",
            new CosmosLoginRequest(user.Email, Password),
            cts.Token));

        container.Verify(value => value.ReadItemAsync<CosmosIdentityService.IdentityDocument>(
            It.IsAny<string>(), It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task LoginStopsBeforeRefreshTokenWriteWhenPointReadCompletesAfterCancellation()
    {
        using var cts = new CancellationTokenSource();
        var user = CreateIdentity();
        var iterator = CreateQueryIterator(user);
        var read = CreateItemResponse(user, "login-cancelled-etag");
        var container = new Mock<Container>();
        container.Setup(value => value.GetItemQueryIterator<CosmosIdentityService.IdentityDocument>(
                It.IsAny<QueryDefinition>(), It.IsAny<string?>(), It.IsAny<QueryRequestOptions?>()))
            .Returns(iterator.Object);
        container.Setup(value => value.ReadItemAsync<CosmosIdentityService.IdentityDocument>(
                user.Id, It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                cts.Cancel();
                return Task.FromResult(read.Object);
            });
        var service = CreateService(container.Object);

        await Assert.ThrowsAsync<OperationCanceledException>(() => service.LoginAsync(
            "rider",
            new CosmosLoginRequest(user.Email, Password),
            cts.Token));

        container.Verify(value => value.ReplaceItemAsync(
            It.IsAny<CosmosIdentityService.IdentityDocument>(),
            It.IsAny<string>(),
            It.IsAny<PartitionKey?>(),
            It.IsAny<ItemRequestOptions?>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RefreshHonorsPreCancelledTokenBeforeCosmosQuery()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var container = new Mock<Container>();
        var service = CreateService(container.Object);

        await Assert.ThrowsAsync<OperationCanceledException>(() => service.RefreshAsync(
            new CosmosRefreshRequest("refresh-token"), cts.Token));

        Assert.Empty(container.Invocations);
    }

    [Fact]
    public async Task RefreshStopsBeforePointReadWhenQueryCompletesAfterCancellation()
    {
        using var cts = new CancellationTokenSource();
        const string refreshToken = "refresh-token-query-cancelled";
        var user = CreateIdentity(refreshTokenHash: HashRefreshToken(refreshToken));
        var iterator = CreateQueryIterator(user, cts.Cancel);
        var container = new Mock<Container>();
        container.Setup(value => value.GetItemQueryIterator<CosmosIdentityService.IdentityDocument>(
                It.IsAny<QueryDefinition>(), It.IsAny<string?>(), It.IsAny<QueryRequestOptions?>()))
            .Returns(iterator.Object);
        var service = CreateService(container.Object);

        await Assert.ThrowsAsync<OperationCanceledException>(() => service.RefreshAsync(
            new CosmosRefreshRequest(refreshToken), cts.Token));

        container.Verify(value => value.ReadItemAsync<CosmosIdentityService.IdentityDocument>(
            It.IsAny<string>(), It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RefreshStopsBeforeTokenRotationWhenPointReadCompletesAfterCancellation()
    {
        using var cts = new CancellationTokenSource();
        const string refreshToken = "refresh-token-point-read-cancelled";
        var user = CreateIdentity(refreshTokenHash: HashRefreshToken(refreshToken));
        var iterator = CreateQueryIterator(user);
        var read = CreateItemResponse(user, "refresh-cancelled-etag");
        var container = new Mock<Container>();
        container.Setup(value => value.GetItemQueryIterator<CosmosIdentityService.IdentityDocument>(
                It.IsAny<QueryDefinition>(), It.IsAny<string?>(), It.IsAny<QueryRequestOptions?>()))
            .Returns(iterator.Object);
        container.Setup(value => value.ReadItemAsync<CosmosIdentityService.IdentityDocument>(
                user.Id, It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                cts.Cancel();
                return Task.FromResult(read.Object);
            });
        var service = CreateService(container.Object);

        await Assert.ThrowsAsync<OperationCanceledException>(() => service.RefreshAsync(
            new CosmosRefreshRequest(refreshToken), cts.Token));

        container.Verify(value => value.ReplaceItemAsync(
            It.IsAny<CosmosIdentityService.IdentityDocument>(),
            It.IsAny<string>(),
            It.IsAny<PartitionKey?>(),
            It.IsAny<ItemRequestOptions?>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task LogoutHonorsPreCancelledTokenBeforeCosmosRead()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var container = new Mock<Container>();
        var service = CreateService(container.Object);
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(JwtRegisteredClaimNames.Sub, "user-1"),
            new Claim("role", "Rider")
        ], "integration-test"));

        await Assert.ThrowsAsync<OperationCanceledException>(() => service.LogoutAsync(principal, cts.Token));

        Assert.Empty(container.Invocations);
    }

    [Fact]
    public async Task LogoutStopsBeforeRevocationWhenPointReadCompletesAfterCancellation()
    {
        using var cts = new CancellationTokenSource();
        var user = CreateIdentity(refreshTokenHash: "active-refresh-hash");
        var read = CreateItemResponse(user, "logout-cancelled-etag");
        var container = new Mock<Container>();
        container.Setup(value => value.ReadItemAsync<CosmosIdentityService.IdentityDocument>(
                user.Id, It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                cts.Cancel();
                return Task.FromResult(read.Object);
            });
        var service = CreateService(container.Object);
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(JwtRegisteredClaimNames.Sub, "user-1"),
            new Claim("role", "Rider")
        ], "integration-test"));

        await Assert.ThrowsAsync<OperationCanceledException>(() => service.LogoutAsync(principal, cts.Token));

        container.Verify(value => value.ReplaceItemAsync(
            It.IsAny<CosmosIdentityService.IdentityDocument>(),
            It.IsAny<string>(),
            It.IsAny<PartitionKey?>(),
            It.IsAny<ItemRequestOptions?>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task LogoutDoesNotRetryAfterCancellationDuringEtagConflict()
    {
        using var cts = new CancellationTokenSource();
        var user = CreateIdentity(refreshTokenHash: "active-refresh-hash");
        var read = CreateItemResponse(user, "logout-conflict-etag");
        var container = new Mock<Container>();
        container.Setup(value => value.ReadItemAsync<CosmosIdentityService.IdentityDocument>(
                user.Id, It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(read.Object);
        container.Setup(value => value.ReplaceItemAsync(
                It.IsAny<CosmosIdentityService.IdentityDocument>(),
                user.Id,
                It.IsAny<PartitionKey?>(),
                It.IsAny<ItemRequestOptions?>(),
                It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                cts.Cancel();
                return Task.FromException<ItemResponse<CosmosIdentityService.IdentityDocument>>(
                    new CosmosException("etag changed", HttpStatusCode.PreconditionFailed, 0, "auth-test", 0));
            });
        var service = CreateService(container.Object);
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(JwtRegisteredClaimNames.Sub, "user-1"),
            new Claim("role", "Rider")
        ], "integration-test"));

        await Assert.ThrowsAsync<OperationCanceledException>(() => service.LogoutAsync(principal, cts.Token));

        container.Verify(value => value.ReadItemAsync<CosmosIdentityService.IdentityDocument>(
            user.Id, It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()), Times.Once);
        container.Verify(value => value.ReplaceItemAsync(
            It.IsAny<CosmosIdentityService.IdentityDocument>(),
            It.IsAny<string>(),
            It.IsAny<PartitionKey?>(),
            It.IsAny<ItemRequestOptions?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }


    [Fact]
    public async Task LoginRejectsIdentityWithoutUserIdSuffixBeforePointRead()
    {
        var user = CreateIdentity() with { Id = "user:" };
        var (container, _) = SetupIdentityQuery(user);
        var service = CreateService(container.Object);

        var result = await service.LoginAsync(
            "rider",
            new CosmosLoginRequest(user.Email, Password),
            CancellationToken.None);

        Assert.Equal(StatusCodes.Status401Unauthorized, GetStatusCode(result));
        container.Verify(value => value.ReadItemAsync<CosmosIdentityService.IdentityDocument>(
            It.IsAny<string>(), It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()), Times.Never);
        container.Verify(value => value.ReplaceItemAsync(
            It.IsAny<CosmosIdentityService.IdentityDocument>(), It.IsAny<string>(), It.IsAny<PartitionKey?>(),
            It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RefreshRejectsIdentityWithoutUserIdSuffixBeforePointRead()
    {
        const string refreshToken = "refresh-token-malformed-user-id";
        var user = CreateIdentity(refreshTokenHash: HashRefreshToken(refreshToken)) with { Id = "user:" };
        var (container, _) = SetupIdentityQuery(user);
        var service = CreateService(container.Object);

        var result = await service.RefreshAsync(new CosmosRefreshRequest(refreshToken), CancellationToken.None);

        Assert.Equal(StatusCodes.Status401Unauthorized, GetStatusCode(result));
        container.Verify(value => value.ReadItemAsync<CosmosIdentityService.IdentityDocument>(
            It.IsAny<string>(), It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()), Times.Never);
        container.Verify(value => value.ReplaceItemAsync(
            It.IsAny<CosmosIdentityService.IdentityDocument>(), It.IsAny<string>(), It.IsAny<PartitionKey?>(),
            It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task LoginRejectsPersistedPasswordHashWithUnexpectedLength()
    {
        var user = CreateIdentity() with { PasswordHash = Convert.ToBase64String(new byte[1]) };
        var (container, _) = SetupIdentityQuery(user);
        var read = CreateItemResponse(user, "malformed-hash-etag");
        container.Setup(value => value.ReadItemAsync<CosmosIdentityService.IdentityDocument>(
                user.Id, It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(read.Object);
        var service = CreateService(container.Object);

        var result = await service.LoginAsync(
            "rider",
            new CosmosLoginRequest(user.Email, Password),
            CancellationToken.None);

        Assert.Equal(StatusCodes.Status401Unauthorized, GetStatusCode(result));
        container.Verify(value => value.ReplaceItemAsync(
            It.IsAny<CosmosIdentityService.IdentityDocument>(), It.IsAny<string>(), It.IsAny<PartitionKey?>(),
            It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task LoginRejectsPersistedPasswordSaltWithUnexpectedLength()
    {
        var user = CreateIdentity() with { PasswordSalt = Convert.ToBase64String(new byte[1]) };
        var (container, _) = SetupIdentityQuery(user);
        var read = CreateItemResponse(user, "malformed-salt-etag");
        container.Setup(value => value.ReadItemAsync<CosmosIdentityService.IdentityDocument>(
                user.Id, It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(read.Object);
        var service = CreateService(container.Object);

        var result = await service.LoginAsync(
            "rider",
            new CosmosLoginRequest(user.Email, Password),
            CancellationToken.None);

        Assert.Equal(StatusCodes.Status401Unauthorized, GetStatusCode(result));
        container.Verify(value => value.ReplaceItemAsync(
            It.IsAny<CosmosIdentityService.IdentityDocument>(), It.IsAny<string>(), It.IsAny<PartitionKey?>(),
            It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static CosmosIdentityService CreateService(Container container) =>
        new(null, CreateConfiguration(), container);

    private static IConfiguration CreateConfiguration() =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Auth:Jwt:Issuer"] = "turnova-auth-tests",
            ["Auth:Jwt:Audience"] = "turnova-api",
            ["Auth:Jwt:SigningKey"] = SigningKey
        }).Build();

    private static (Mock<Container> Container, Mock<TransactionalBatch> Batch, Mock<TransactionalBatchResponse> Response)
        CreateBatchResponse(HttpStatusCode statusCode, bool success)
    {
        var container = new Mock<Container>();
        var batch = new Mock<TransactionalBatch>();
        var response = new Mock<TransactionalBatchResponse>();
        response.SetupGet(value => value.StatusCode).Returns(statusCode);
        response.SetupGet(value => value.IsSuccessStatusCode).Returns(success);
        batch.Setup(value => value.CreateItem(
                It.IsAny<CosmosIdentityService.EmailReservation>(),
                It.IsAny<TransactionalBatchItemRequestOptions?>()))
            .Returns(batch.Object);
        batch.Setup(value => value.CreateItem(
                It.IsAny<CosmosIdentityService.IdentityDocument>(),
                It.IsAny<TransactionalBatchItemRequestOptions?>()))
            .Returns(batch.Object);
        batch.Setup(value => value.ExecuteAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(response.Object);
        container.Setup(value => value.CreateTransactionalBatch(It.IsAny<PartitionKey>()))
            .Returns(batch.Object);
        return (container, batch, response);
    }

    private static (Mock<Container> Container, Mock<FeedIterator<CosmosIdentityService.IdentityDocument>> Iterator)
        SetupIdentityQuery(CosmosIdentityService.IdentityDocument user)
    {
        var iterator = CreateQueryIterator(user);
        var container = new Mock<Container>();
        container.Setup(value => value.GetItemQueryIterator<CosmosIdentityService.IdentityDocument>(
                It.IsAny<QueryDefinition>(), It.IsAny<string?>(), It.IsAny<QueryRequestOptions?>()))
            .Returns(iterator.Object);
        return (container, iterator);
    }

    private static Mock<FeedIterator<CosmosIdentityService.IdentityDocument>> CreateQueryIterator(
        CosmosIdentityService.IdentityDocument user,
        Action? onRead = null)
    {
        var page = new Mock<FeedResponse<CosmosIdentityService.IdentityDocument>>();
        page.As<IEnumerable<CosmosIdentityService.IdentityDocument>>()
            .Setup(value => value.GetEnumerator())
            .Returns(() => new[] { user }.AsEnumerable().GetEnumerator());
        var iterator = new Mock<FeedIterator<CosmosIdentityService.IdentityDocument>>();
        iterator.SetupSequence(value => value.HasMoreResults).Returns(true).Returns(false);
        iterator.Setup(value => value.ReadNextAsync(It.IsAny<CancellationToken>()))
            .Returns((CancellationToken _) =>
            {
                onRead?.Invoke();
                return Task.FromResult(page.Object);
            });
        return iterator;
    }

    private static Mock<ItemResponse<CosmosIdentityService.IdentityDocument>> CreateItemResponse(
        CosmosIdentityService.IdentityDocument user,
        string etag)
    {
        var response = new Mock<ItemResponse<CosmosIdentityService.IdentityDocument>>();
        response.SetupGet(value => value.Resource).Returns(user);
        response.SetupGet(value => value.ETag).Returns(etag);
        return response;
    }

    private static ClaimsPrincipal CreateRiderPrincipal() => new(new ClaimsIdentity(
    [
        new Claim(JwtRegisteredClaimNames.Sub, "user-1"),
        new Claim("role", "Rider")
    ], "integration-test"));

    private static CosmosIdentityService.IdentityDocument CreateIdentity(string? refreshTokenHash = null)
    {
        var salt = Enumerable.Range(1, 16).Select(value => (byte)value).ToArray();
        var passwordHash = Rfc2898DeriveBytes.Pbkdf2(
            Password, salt, 210_000, HashAlgorithmName.SHA256, 32);
        return new CosmosIdentityService.IdentityDocument
        {
            Id = "user:user-1",
            Role = "Rider",
            Email = "rider@example.com",
            Name = "Rider",
            PasswordHash = Convert.ToBase64String(passwordHash),
            PasswordSalt = Convert.ToBase64String(salt),
            RefreshTokenHash = refreshTokenHash,
            RefreshTokenExpiresAtUtc = refreshTokenHash is null ? null : DateTimeOffset.UtcNow.AddDays(1),
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
    }

    private static string HashRefreshToken(string token) =>
        Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private static int GetStatusCode(IResult result) =>
        Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode ?? 0;
}
