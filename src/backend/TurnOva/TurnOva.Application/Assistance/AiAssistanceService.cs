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
    string DropoffToken);

public sealed record TripIntentProviderResult(
    string? PickupToken,
    string? DropoffToken,
    string? ServiceClass);

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
    private const string UnsupportedHelpAnswer = "This is outside approved help. Please use the support contact.";

    private static readonly Regex RoutePattern = new(
        @"\bfrom\s+(?<pickup>.+?)\s+to\s+(?<dropoff>.+?)(?:\s+(?:for|with)\s+(?<passengers>\d{1,2})\s+(?:passengers?|people|persons?)|\s+for\s+(?:a\s+)?(?:family|party)\s+of\s+(?<familyPassengers>\d{1,2}))?\s*[.!?]*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex PassengerPattern = new(
        @"\b(?:for|with)\s+(?<passengers>\d{1,2})\s+(?:passengers?|people|persons?)\b|\b(?:family|party)\s+of\s+(?<familyPassengers>\d{1,2})\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex SafeTokenPattern = new(
        @"[a-z]+|\d+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    // Only generic trip-action words may leave the API. Passenger counts, family/child
    // context, accessibility/health details, luggage and location-type hints stay local.
    private static readonly HashSet<string> SafeIntentWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "book", "cab", "get", "help", "need", "please", "ride", "taxi", "trip"
    };

    private static readonly HashSet<string> SafeHelpWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "agreement", "arrived", "cancel", "cab", "confirm", "counter", "driver",
        "accepted", "agreementconfirmed", "arrival", "availability", "cancelled", "canceled", "completed", "completion", "counter", "discovery", "eligible", "expired", "fare", "fifo", "help", "inprogress", "progress", "negotiate", "negotiated", "negotiating",
        "offline", "online", "overlap", "paid", "payment", "photo", "pickup", "price", "proximity", "proposal", "queue", "queued", "received", "report", "reservation", "reserved", "ride",
        "select", "selection", "selfie", "status", "trip", "union", "version"
    };

    private static readonly IReadOnlyList<HelpEntry> HelpEntries =
    [
        new("state-negotiating",
            "In Negotiating, the Rider and selected Driver negotiate directly. TurnOva does not set or recommend a fare. Both parties must confirm the same current agreement version before the Driver can start.",
            ["negotiating"]),
        new("state-agreement-confirmed",
            "In AgreementConfirmed, the Rider and Driver have confirmed the current agreement version. The Driver must still follow the server-owned trip start flow. Any new fare or route version requires both parties to confirm that new version.",
            ["agreementconfirmed"]),
        new("state-accepted",
            "In Accepted, the selected Driver has accepted the trip. The API remains the source of truth for trip status; reconnecting refreshes the latest ride state.",
            ["accepted"]),
        new("state-arrived",
            "In Arrived, the selected Driver has marked arrival at the pickup. Check the trip details and current mutually confirmed agreement before the ride starts.",
            ["arrived", "arrival"]),
        new("state-in-progress",
            "In InProgress, the Driver has started the trip. Active-trip location sharing is available only while the Driver app is in the foreground and the required location permission remains granted.",
            ["inprogress", "progress"]),
        new("state-completed",
            "In Completed, the trip is marked completed. The Driver can report the amount received directly from the Rider. That report is labelled Driver-reported and unverified.",
            ["completed", "completion"]),
        new("state-cancelled",
            "In Cancelled, the selected trip is cancelled and cannot be started. A new trip requires a fresh discovery and Driver selection.",
            ["cancelled", "canceled"]),
        new("state-expired",
            "In Expired, the selected-driver negotiation expired. The API reconciles the ride state and releases only the matching Driver reservation.",
            ["expired"]),
        new("fare-negotiation",
            "The Rider and selected Driver negotiate the fare directly. TurnOva does not calculate, quote or recommend a fare. Both parties must confirm the current agreement version before the Driver starts.",
            ["fare", "price", "negotiate", "negotiated", "counter"]),
        new("agreement-version",
            "A fare or route change creates a new immutable agreement version. The previous version remains in history, and both Rider and Driver must confirm the new current version.",
            ["agreement", "confirm", "version"]),
        new("direct-payment",
            "The Rider pays the Driver directly outside TurnOva. TurnOva does not collect money, operate a payment gateway or hold funds. After completion, the Driver reports the amount received; this report is unverified.",
            ["payment", "received", "paid", "report"]),
        new("driver-photo",
            "A Driver photo is shown to Riders only after approval by that Driver's current Union. A Union transfer resets selfie approval until the new current Union approves it.",
            ["photo", "selfie"]),
        new("driver-selection",
            "Riders choose an eligible Cab Driver from approved nearby Unions. Discovery is grouped by Union and ordered by Driver-to-pickup proximity; it does not use customer-facing FIFO ranking or automatic assignment.",
            ["cab", "discovery", "pickup", "proximity", "select", "selection", "union"]),
        new("reservation",
            "A selected Driver is reserved against overlapping negotiations and active rides. Cancellation or expiry releases only the reservation belonging to that exact ride.",
            ["overlap", "reservation", "reserved"]),
        new("queue-status",
            "Rider discovery does not use FIFO queue rank or automatic assignment. Riders see eligible approved Cab Drivers grouped by Union and ordered by pickup proximity, then select one. Whether legacy FIFO remains an internal fairness mechanism is unresolved; do not infer a customer ranking or dispatch rule from it.",
            ["queue", "queued", "fifo"]),
        new("availability-status",
            "Online is one input to server-side Cab eligibility and nearby discovery; it does not guarantee that a Driver will be shown or selected. Offline means the Driver is not available for new ride discovery. Rider selection is explicit and no FIFO assignment occurs.",
            ["online", "offline", "eligible", "availability"])
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
        if (options.MaxOutputCharacters < UnsupportedHelpAnswer.Length)
            throw new ArgumentOutOfRangeException(nameof(options), $"MaxOutputCharacters must be at least {UnsupportedHelpAnswer.Length}.");

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
        if (explicitPassengerCount is null || invalidPassengerCount)
            missingFields.Add("passengerCount");

        var source = "deterministic-fallback";
        int? passengerCount = invalidPassengerCount ? null : explicitPassengerCount;

        if (pickup is not null && dropoff is not null && !invalidPassengerCount &&
            _options.Enabled && _provider.IsAvailable)
        {
            var prompt = BuildSafeTripPrompt(normalized, routeMatch);
            var providerResult = await InvokeProviderAsync(
                ct => _provider.DraftTripIntentAsync(prompt, ct),
                cancellationToken);

            if (IsValidProviderTripResult(providerResult))
                source = "provider";
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
            return EnsureHelpOutputLimit(new HelpResponse(stateEntry.Answer, stateEntry.Id, false, "approved-content"));
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

        return EnsureHelpOutputLimit(new HelpResponse(selectedEntry.Answer, selectedEntry.Id, false, source));
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
            return (null, false);

        var raw = match.Groups["passengers"].Success
            ? match.Groups["passengers"].Value
            : match.Groups["familyPassengers"].Value;

        return int.TryParse(raw, out var count) && count is >= 1 and <= 6
            ? (count, false)
            : (null, true);
    }

    private static SafeTripIntentPrompt BuildSafeTripPrompt(string text, Match routeMatch)
    {
        // Remove the complete pickup-to-drop-off span before building the provider prompt.
        // Only generic action words are retained. Passenger counts and family, child,
        // accessibility/health, luggage, location-type and arbitrary free-text details stay local.
        var remainder = text.Remove(routeMatch.Index, routeMatch.Length);
        var safeWords = SafeTokenPattern.Matches(remainder)
            .Select(match => match.Value)
            .Where(SafeIntentWords.Contains)
            .Take(12);
        var summary = $"Cab trip; pickup {PickupToken}; destination {DropoffToken}; {string.Join(' ', safeWords)}.";

        return new SafeTripIntentPrompt(summary, PickupToken, DropoffToken);
    }

    private static bool IsValidProviderTripResult(TripIntentProviderResult? result) =>
        result is not null &&
        string.Equals(result.PickupToken, PickupToken, StringComparison.Ordinal) &&
        string.Equals(result.DropoffToken, DropoffToken, StringComparison.Ordinal) &&
        string.Equals(result.ServiceClass, "Cab", StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<string> ExtractSafeHelpKeywords(string question) =>
        SafeTokenPattern.Matches(question.ToLowerInvariant())
            .Select(match => match.Value)
            .Where(SafeHelpWords.Contains)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .ToArray();

    private static HelpEntry? FindDeterministicHelpEntry(IReadOnlyList<string> keywords)
    {
        var ranked = HelpEntries
            .Select(entry => new
            {
                Entry = entry,
                Score = entry.Keywords.Count(keyword => keywords.Contains(keyword, StringComparer.OrdinalIgnoreCase))
            })
            .Where(item => item.Score > 0)
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Entry.Id, StringComparer.Ordinal)
            .ToArray();

        if (ranked.Length == 0 || (ranked.Length > 1 && ranked[0].Score == ranked[1].Score))
            return null;

        return ranked[0].Entry;
    }

    private HelpResponse EnsureHelpOutputLimit(HelpResponse response) =>
        response.Answer.Length <= _options.MaxOutputCharacters
            ? response
            : UnsupportedHelp("deterministic-fallback");

    private static HelpResponse UnsupportedHelp(string source) =>
        new(UnsupportedHelpAnswer, "unsupported", true, source);

    private async Task<T?> InvokeProviderAsync<T>(
        Func<CancellationToken, Task<T?>> operation,
        CancellationToken callerCancellationToken)
    {
        callerCancellationToken.ThrowIfCancellationRequested();

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(callerCancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(_options.RequestTimeoutSeconds));

        Task<T?> providerTask;
        try
        {
            providerTask = operation(timeout.Token);
            ArgumentNullException.ThrowIfNull(providerTask);
        }
        catch (OperationCanceledException)
        {
            callerCancellationToken.ThrowIfCancellationRequested();
            return default;
        }
        catch (HttpRequestException)
        {
            callerCancellationToken.ThrowIfCancellationRequested();
            return default;
        }
        catch (TimeoutException)
        {
            callerCancellationToken.ThrowIfCancellationRequested();
            return default;
        }

        try
        {
            // WaitAsync enforces the request deadline even when a provider ignores its
            // cancellation token. The linked token still asks a cooperative provider to stop.
            return await providerTask.WaitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Caller cancellation always wins over timeout/provider cancellation. If the
            // provider is still running, observe a later fault without logging its contents.
            if (!providerTask.IsCompleted)
                ObserveLateProviderTask(providerTask);

            callerCancellationToken.ThrowIfCancellationRequested();
            return default;
        }
        catch (HttpRequestException)
        {
            callerCancellationToken.ThrowIfCancellationRequested();
            return default;
        }
        catch (TimeoutException)
        {
            callerCancellationToken.ThrowIfCancellationRequested();
            return default;
        }
    }

    private static void ObserveLateProviderTask(Task providerTask)
    {
        _ = providerTask.ContinueWith(
            static faultedTask => _ = faultedTask.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private sealed record HelpEntry(string Id, string Answer, IReadOnlyList<string> Keywords);
}
