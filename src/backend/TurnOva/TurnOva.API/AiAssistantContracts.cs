namespace TurnOva.API;

public sealed record TripIntentDraftRequest(string? Text);
public sealed record TripIntentDraftResponse(
    string? Pickup,
    string? Dropoff,
    string ServiceClass,
    int? PassengerCount,
    bool IsSupported,
    bool NeedsReview,
    string Source,
    string Message);

public sealed record ProductHelpRequest(string? Question);
public sealed record ProductHelpResponse(string? TopicId, string Answer, bool EscalationRequired, string Source);

public sealed record AiTripIntentSignals(int? PassengerCount);

public interface IAiAssistantProvider
{
    Task<AiTripIntentSignals?> ExtractTripIntentSignalsAsync(string redactedIntent, CancellationToken cancellationToken);
    Task<string?> ClassifyHelpTopicAsync(
        string redactedQuestion,
        IReadOnlyCollection<string> allowedTopicIds,
        CancellationToken cancellationToken);
}

public interface IAiAssistantService
{
    Task<TripIntentDraftResponse> CreateTripIntentDraftAsync(string? text, CancellationToken cancellationToken);
    Task<ProductHelpResponse> GetProductHelpAsync(string? question, CancellationToken cancellationToken);
}

public sealed class AiAssistantInputException : Exception
{
    public AiAssistantInputException(string message) : base(message) { }
}
