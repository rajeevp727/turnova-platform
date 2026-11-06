using System.Net.Http;
using System.Text.RegularExpressions;

namespace TurnOva.Application.Assistance;

public sealed class AiAssistantOptions
{
    public bool Enabled { get; init; }
    public string? Provider { get; init; }
    public string? Model { get; init; }
    public string? Endpoint { get; init; }
    public string? CredentialReference { get; init; }
    public int RequestTimeoutSeconds { get; init; } = 4;
    public int MaxInputCharacters { get; init; } = 1000;
    public int MaxOutputCharacters { get; init; } = 600;
    public int MaxRequestsPerMinute { get; init; } = 10;
}

public sealed record TripIntentRequest(string? Text);

public sealed record TripIntentDraftResponse(
    string? Pickup,
    string? Dropoff,
    string ServiceClass,
    int? PassengerCount,
    bool NeedsClarification,
    IReadOnlyList<string> MissingFields,
    bool RequiresUserConfirmation,
    string Source);

public sealed record HelpRequest(string? Question, string? RideState = null);

public sealed record HelpResponse(string Answer, string SourceId, bool EscalationRequired, string Source);

public sealed record SafeTripIntentPrompt(
    string IntentSummary,
    string PickupToken,
    string DropoffToken,
    int? PassengerCount);

public sealed record TripIntentProviderResult(
    string? PickupToken,
    string? DropoffToken,
    string? ServiceClass,
    int? PassengerCount);

public sealed record SafeHelpPrompt(
    IReadOnlyList<string> AllowedKeywords,
    string? RideState,
    IReadOnlyList<string> AllowedSourceIds);

public sealed record HelpProviderResult(string? SourceId);

public interface IAiAssistantProvider
{
    bool IsAvailable { get; }

    Task<TripIntentProviderResult?> DraftTripIntentAsync(
        SafeTripIntentPrompt request,
        CancellationToken cancellationToken);

    Task<HelpProviderResult?> ClassifyHelpAsync(
        SafeHelpPrompt request,
        CancellationToken cancellationToken);
}

public sealed class UnavailableAiAssistantProvider : IAiAssistantProvider
{
    public bool IsAvailable => false;

    public Task<TripIntentProviderResult?> DraftTripIntentAsync(
        SafeTripIntentPrompt request,
        CancellationToken cancellationToken) =>
        Task.FromResult<TripIntentProviderResult?>(null);

    public Task<HelpProviderResult?> ClassifyHelpAsync(
        SafeHelpPrompt request,
        CancellationToken cancellationToken) =>
        Task.FromResult<HelpProviderResult?>(null);
}

public sealed class AiAssistanceService
{
    private const string PickupToken = "[PICKUP]";
    private const string DropoffToken = "[DROPOFF]";

    private static readonly Regex RoutePattern = new(
        @"\bfrom\s+(?<pickup>.+?)\s+to\s+(?<dropoff>.+?)(?:\s+(?:for|with)\s+(?<passengers>\d{1,2})\s+(?:passengers?|people|persons?)|\s+for\s+(?:a\s+)?(?:family|party)\s+of\s+(?<familyPassengers>\d{1,2}))?\s*[.!?]*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex PassengerPattern = new(
        @"\b(?:for|with)\s+(?<passengers>\d{1,2})\s+(?:passengers?|people|persons?)\b|\b(?:family|party)\s+of\s+(?<familyPassengers>\d{1,2})\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex SafeTokenPattern = new(
        @"[a-z]+|\d+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly HashSet<string> SafeIntentWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "are", "airport", "bags", "book", "cab", "child", "family", "for",
        "from", "get", "help", "luggage", "need", "now", "of", "one", "passenger",
        "passengers", "people", "person", "please", "ride", "seat", "station",
        "taxi", "to", "today", "tomorrow", "train", "trip", "two", "three",
        "four", "five", "six", "we", "wheelchair", "with", "accessible"
    };

    private static readonly HashSet<string> SafeHelpWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "agreement", "arrived", "cancel", "cab", "confirm", "counter", "driver",
        "expired", "fare", "help", "inprogress", "negotiate", "negotiating",
        "payment", "photo", "pickup", "price", "queue", "received", "ride",
        "selfie", "status", "trip", "union", "version"
    };

    private static readonly IReadOnlyList<HelpEntry> HelpEntries =
    [
        new("state-negotiating",
            "You and the selected Driver are negotiating directly. TurnOva does not set or recommend a fare. Both parties must confirm the same current agreement version before the Driver can start.",
            ["negotiating", "negotiate", "fare", "counter", "proposal"]),
        new("state-agreement-confirmed",
            "The Rider and Driver have confirmed the current agreement version. The Driver must still follow the server-owned trip start flow. Any new fare or route version requires both parties to confirm that new version.",
            ["agreement", "confirm", "version"]),
        new("state-accepted",
            "The selected Driver has accepted the trip. The API remains the source of truth for trip status; reconnecting refreshes the latest ride state.",
            ["driver", "status", "ride"]),
        new("state-arrived",
            "The selected Driver has marked arrival at the pickup. Check the trip details and current mutually confirmed agreement before the ride starts.",
            ["arrived", "pickup", "driver"]),
        new("state-in-progress",
            "The Driver has started the trip. Active-trip location sharing is available only while the Driver app is in the foreground and the required location permission remains granted.",
            ["inprogress", "ride", "status"]),
        new("state-completed",
            "The trip is marked completed. The Driver can report the amount received directly from the Rider. That report is labelled Driver-reported and unverified.",
            ["ride", "status", "payment", "received"]),
        new("state-cancelled",
            "The selected trip is cancelled and cannot be started. A new trip requires a fresh discovery and Driver selection.",
            ["cancel", "ride", "status"]),
        new("state-expired",
            "The selected-driver negotiation expired. The API reconciles the ride state and releases only the matching Driver reservation.",
            ["expired", "negotiating", "driver"]),
        new("fare-negotiation",
            "The Rider and selected Driver negotiate the fare directly. TurnOva does not calculate, quote or recommend a fare. Both parties must confirm the current agreement version before the Driver starts.",
            ["fare", "price", "negotiate", "counter"]),
        new("agreement-version",
            "A fare or route change creates a new immutable agreement version. The previous version remains in history, and both Rider and Driver must confirm the new current version.",
            ["agreement", "confirm", "version"]),
        new("direct-payment",
            "The Rider pays the Driver directly outside TurnOva. TurnOva does not collect money, operate a payment gateway or hold funds. After completion, the Driver reports the amount received; this report is unverified.",
            ["payment", "received", "fare"]),
        new("driver-photo",
            "A Driver photo is shown to Riders only after approval by that Driver's current Union. A Union transfer resets selfie approval until the new current Union approves it.",
            ["driver", "photo", "selfie", "union"]),
        new("driver-selection",
            "Riders choose an eligible Cab Driver from approved nearby Unions. Discovery is grouped by Union and ordered by Driver-to-pickup proximity; it does not use customer-facing FIFO ranking or automatic assignment.",
            ["driver", "cab", "pickup", "queue", "union"]),
        new("reservation",
            "A selected Driver is reserved against overlapping negotiations and active rides. Cancellation or expiry releases only the reservation belonging to that exact ride.",
            ["driver", "negotiating", "expired", "cancel"])
    ];

    private static readonly IReadOnlyDictionary<string, string> RideStateEntryIds =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Negotiating"] = "state-negotiating",
            ["AgreementConfirmed"] = "state-agreement-confirmed",
            ["Accepted"] = "state-accepted",
            ["Arrived"] = "state-arrived",
            ["InProgress"] = "state-in-progress",
            ["Completed"] = "state-completed",
            ["Cancelled"] = "state-cancelled",
            ["Expired"] = "state-expired"
        };

    private readonly IAiAssistantProvider _provider;
    private readonly AiAssistantOptions _options;

    public AiAssistanceService(IAiAssistantProvider provider, AiAssistantOptions options)
    {
        _provider = provider;
        _options = options;
    }

    public async Task<TripIntentDraftResponse> DraftTripIntentAsync(
        string? text,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var normalized = ValidateInput(text, nameof(text));
        var routeMatch = RoutePattern.Match(normalized);
        var pickup = routeMatch.Success ? CleanRouteSegment(routeMatch.Groups["pickup"].Value) : null;
        var dropoff = routeMatch.Success ? CleanRouteSegment(routeMatch.Groups["dropoff"].Value) : null;
        var (explicitPassengerCount, invalidPassengerCount) = ParsePassengerCount(normalized);

        var missingFields = new List<string>();
        if (pickup is null) missingFields.Add("pickup");
        if (dropoff is null) missingFields.Add("dropoff");
        if (invalidPassengerCount) missingFields.Add("passengerCount");

        var source = "deterministic-fallback";
        int? passengerCount = invalidPassengerCount ? null : explicitPassengerCount ?? 1;

        if (pickup is not null && dropoff is not null && !invalidPassengerCount &&
            _options.Enabled && _provider.IsAvailable)
        {
            var prompt = BuildSafeTripPrompt(normalized, routeMatch, explicitPassengerCount);
            var providerResult = await InvokeProviderAsync(
                ct => _provider.DraftTripIntentAsync(prompt, ct),
                cancellationToken);

            if (IsValidProviderTripResult(providerResult))
            {
                passengerCount = explicitPassengerCount ?? providerResult!.PassengerCount ?? 1;
                source = "provider";
            }
        }

        return new TripIntentDraftResponse(
            pickup,
            dropoff,
            "Cab",
            passengerCount,
            missingFields.Count > 0,
            missingFields,
            true,
            source);
    }

    public async Task<HelpResponse> ExplainHelpAsync(
        string? question,
        string? rideState,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var normalizedQuestion = ValidateInput(question, nameof(question));
        if (rideState is { Length: > 40 })
            throw new ArgumentException("Ride state is invalid.", nameof(rideState));

        if (!string.IsNullOrWhiteSpace(rideState))
        {
            if (!RideStateEntryIds.TryGetValue(rideState.Trim(), out var stateEntryId))
                return UnsupportedHelp("deterministic-fallback");

            var stateEntry = HelpEntries.First(entry => entry.Id == stateEntryId);
            return new HelpResponse(stateEntry.Answer, stateEntry.Id, false, "approved-content");
        }

        var safeKeywords = ExtractSafeHelpKeywords(normalizedQuestion);
        if (safeKeywords.Count == 0)
            return UnsupportedHelp("deterministic-fallback");

        var source = "deterministic-fallback";
        HelpEntry? selectedEntry = null;

        if (_options.Enabled && _provider.IsAvailable)
        {
            var prompt = new SafeHelpPrompt(
                safeKeywords,
                null,
                HelpEntries.Select(entry => entry.Id).ToArray());
            var providerResult = await InvokeProviderAsync(
                ct => _provider.ClassifyHelpAsync(prompt, ct),
                cancellationToken);

            if (providerResult?.SourceId is { Length: > 0 and <= 80 } sourceId)
            {
                selectedEntry = HelpEntries.FirstOrDefault(entry =>
                    string.Equals(entry.Id, sourceId, StringComparison.Ordinal));
                if (selectedEntry is not null)
                    source = "provider-approved-content";
            }
        }

        selectedEntry ??= FindDeterministicHelpEntry(safeKeywords);
        if (selectedEntry is null)
            return UnsupportedHelp(source);

        if (selectedEntry.Answer.Length > _options.MaxOutputCharacters)
            return UnsupportedHelp("deterministic-fallback");

        return new HelpResponse(selectedEntry.Answer, selectedEntry.Id, false, source);
    }

    private string ValidateInput(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("A non-empty request is required.", parameterName);

        var normalized = value.Trim();
        if (normalized.Length > _options.MaxInputCharacters)
            throw new ArgumentException($"Request must be {_options.MaxInputCharacters} characters or fewer.", parameterName);

        return normalized;
    }

    private static string? CleanRouteSegment(string value)
    {
        var cleaned = value.Trim().Trim(',', ';', '.', '!', '?');
        return cleaned.Length is > 0 and <= 160 ? cleaned : null;
    }

    private static (int? Count, bool Invalid) ParsePassengerCount(string text)
    {
        var match = PassengerPattern.Match(text);
        if (!match.Success)
            return (1, false);

        var raw = match.Groups["passengers"].Success
            ? match.Groups["passengers"].Value
            : match.Groups["familyPassengers"].Value;

        return int.TryParse(raw, out var count) && count is >= 1 and <= 6
            ? (count, false)
            : (null, true);
    }

    private static SafeTripIntentPrompt BuildSafeTripPrompt(
        string text,
        Match routeMatch,
        int? explicitPassengerCount)
    {
        // Remove the complete pickup-to-drop-off span before building the provider prompt.
        // Only an allow-list of generic intent words is retained; addresses, names, phone
        // numbers, email addresses, tokens and all other free text are never forwarded.
        var remainder = text.Remove(routeMatch.Index, routeMatch.Length);
        var safeWords = SafeTokenPattern.Matches(remainder)
            .Select(match => match.Value)
            .Where(SafeIntentWords.Contains)
            .Take(20);
        var countText = explicitPassengerCount?.ToString() ?? "unspecified";
        var summary = $"Cab trip; pickup {PickupToken}; destination {DropoffToken}; {string.Join(' ', safeWords)}; passenger count {countText}.";

        return new SafeTripIntentPrompt(summary, PickupToken, DropoffToken, explicitPassengerCount);
    }

    private static bool IsValidProviderTripResult(TripIntentProviderResult? result) =>
        result is not null &&
        string.Equals(result.PickupToken, PickupToken, StringComparison.Ordinal) &&
        string.Equals(result.DropoffToken, DropoffToken, StringComparison.Ordinal) &&
        string.Equals(result.ServiceClass, "Cab", StringComparison.OrdinalIgnoreCase) &&
        (result.PassengerCount is null or >= 1 and <= 6);

    private static IReadOnlyList<string> ExtractSafeHelpKeywords(string question) =>
        SafeTokenPattern.Matches(question.ToLowerInvariant())
            .Select(match => match.Value)
            .Where(SafeHelpWords.Contains)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .ToArray();

    private static HelpEntry? FindDeterministicHelpEntry(IReadOnlyList<string> keywords) =>
        HelpEntries
            .Select(entry => new
            {
                Entry = entry,
                Score = entry.Keywords.Count(keyword => keywords.Contains(keyword, StringComparer.OrdinalIgnoreCase))
            })
            .Where(item => item.Score > 0)
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Entry.Id, StringComparer.Ordinal)
            .Select(item => item.Entry)
            .FirstOrDefault();

    private static HelpResponse UnsupportedHelp(string source) =>
        new(
            "This question is outside the approved TurnOva help content. Please use the support contact made available to you or ask your Union manager to escalate it.",
            "unsupported",
            true,
            source);

    private async Task<T?> InvokeProviderAsync<T>(
        Func<CancellationToken, Task<T?>> operation,
        CancellationToken callerCancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(callerCancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(_options.RequestTimeoutSeconds));

        try
        {
            return await operation(timeout.Token);
        }
        catch (OperationCanceledException) when (!callerCancellationToken.IsCancellationRequested)
        {
            return default;
        }
        catch (HttpRequestException)
        {
            return default;
        }
    }

    private sealed record HelpEntry(string Id, string Answer, IReadOnlyList<string> Keywords);
}
