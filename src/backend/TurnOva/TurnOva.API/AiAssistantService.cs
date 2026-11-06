using System.Net.Http;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace TurnOva.API;

public sealed class AiAssistantService : IAiAssistantService
{
    private static readonly Regex RoutePattern = new(
        @"\bfrom\s+(?<pickup>.+?)\s+to\s+(?<dropoff>.+?)(?=\s+(?:for|with)\s+\d+\s+(?:passengers?|people|persons?)\b|[.!?]|$)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex PassengerCountPattern = new(
        @"\b(?<count>\d{1,2})\s*(?:passengers?|people|persons?)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex UnsupportedCategoryPattern = new(
        @"\b(?:bike|scooty|scooter|auto|rickshaw)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private readonly IAiAssistantProvider _provider;
    private readonly AiAssistantOptions _options;
    private readonly ILogger<AiAssistantService> _logger;

    public AiAssistantService(
        IAiAssistantProvider provider,
        IOptions<AiAssistantOptions> options,
        ILogger<AiAssistantService> logger)
    {
        _provider = provider;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<TripIntentDraftResponse> CreateTripIntentDraftAsync(
        string? text,
        CancellationToken cancellationToken)
    {
        var input = ValidateInput(text);
        if (UnsupportedCategoryPattern.IsMatch(input))
        {
            _logger.LogInformation("AI trip draft rejected unsupported service category. InputLength {InputLength}", input.Length);
            return new TripIntentDraftResponse(
                null, null, "Cab", null, false, true, "DeterministicFallback",
                "TurnOva currently supports Cab rides only.");
        }

        var route = RoutePattern.Match(input);
        var pickup = route.Success ? CleanLocation(route.Groups["pickup"].Value) : null;
        var dropoff = route.Success ? CleanLocation(route.Groups["dropoff"].Value) : null;
        var passengerCount = ParsePassengerCount(input);
        var providerUsed = false;

        if (_options.Enabled)
        {
            var safeIntent = AiInputRedactor.RedactTripIntent(input);
            var signals = await TryProviderAsync(
                ct => _provider.ExtractTripIntentSignalsAsync(safeIntent, ct),
                cancellationToken,
                "trip-intent");

            if (passengerCount is null && signals?.PassengerCount is > 0)
            {
                passengerCount = signals.PassengerCount;
                providerUsed = true;
            }
        }

        var routeParsed = pickup is not null && dropoff is not null;
        var message = routeParsed
            ? "Review and edit this Cab trip draft before submitting a ride request."
            : "Add pickup and drop-off details, then review the Cab trip draft before submitting a ride request.";

        _logger.LogInformation(
            "AI trip draft generated. InputLength {InputLength} RouteParsed {RouteParsed} ProviderUsed {ProviderUsed}",
            input.Length,
            routeParsed,
            providerUsed);

        return new TripIntentDraftResponse(
            pickup,
            dropoff,
            "Cab",
            passengerCount,
            true,
            true,
            providerUsed ? "AI-assisted" : "DeterministicFallback",
            message);
    }

    public async Task<ProductHelpResponse> GetProductHelpAsync(
        string? question,
        CancellationToken cancellationToken)
    {
        var input = ValidateInput(question);
        AiFaqEntry? entry = null;
        var providerUsed = false;

        if (_options.Enabled)
        {
            var safeQuestion = AiInputRedactor.RedactQuestion(input);
            var topicId = await TryProviderAsync(
                ct => _provider.ClassifyHelpTopicAsync(safeQuestion, AiApprovedContent.TopicIds, ct),
                cancellationToken,
                "product-help");

            entry = AiApprovedContent.FindById(topicId);
            providerUsed = entry is not null;
        }

        entry ??= AiApprovedContent.Match(input);
        if (entry is null)
        {
            _logger.LogInformation(
                "AI product help escalated. InputLength {InputLength} ProviderUsed {ProviderUsed}",
                input.Length,
                providerUsed);
            return new ProductHelpResponse(
                null,
                "This question is not covered by approved TurnOva help content and needs a human support response.",
                true,
                "Escalation");
        }

        _logger.LogInformation(
            "AI product help answered from approved content. TopicId {TopicId} ProviderUsed {ProviderUsed}",
            entry.Id,
            providerUsed);

        return new ProductHelpResponse(
            entry.Id,
            entry.Answer,
            false,
            providerUsed ? "AI-assisted-approved-content" : "ApprovedContentFallback");
    }

    private string ValidateInput(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new AiAssistantInputException("Text is required.");

        var input = value.Trim();
        if (input.Length > _options.MaxInputCharacters)
            throw new AiAssistantInputException($"Text must be {_options.MaxInputCharacters} characters or fewer.");

        return input;
    }

    private async Task<T?> TryProviderAsync<T>(
        Func<CancellationToken, Task<T?>> operation,
        CancellationToken requestCancellationToken,
        string useCase) where T : class
    {
        if (!_options.Enabled)
            return null;

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(requestCancellationToken);
        timeoutSource.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));

        try
        {
            return await operation(timeoutSource.Token);
        }
        catch (OperationCanceledException) when (!requestCancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("AI provider timed out; deterministic fallback used. UseCase {UseCase}", useCase);
            return null;
        }
        catch (HttpRequestException)
        {
            _logger.LogWarning("AI provider unavailable; deterministic fallback used. UseCase {UseCase}", useCase);
            return null;
        }
        catch (TimeoutException)
        {
            _logger.LogWarning("AI provider unavailable; deterministic fallback used. UseCase {UseCase}", useCase);
            return null;
        }
    }

    private static int? ParsePassengerCount(string input)
    {
        var match = PassengerCountPattern.Match(input);
        return match.Success && int.TryParse(match.Groups["count"].Value, out var count) && count > 0
            ? count
            : null;
    }

    private static string? CleanLocation(string value)
    {
        var cleaned = value.Trim().Trim(' ', '.', ',', ';', ':', '"', '\'');
        return string.IsNullOrWhiteSpace(cleaned) ? null : cleaned;
    }
}
