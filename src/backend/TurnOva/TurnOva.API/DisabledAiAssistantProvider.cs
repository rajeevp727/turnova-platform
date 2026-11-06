namespace TurnOva.API;

/// <summary>
/// Safe default until a provider and credentials are explicitly configured.
/// </summary>
public sealed class DisabledAiAssistantProvider : IAiAssistantProvider
{
    public Task<AiTripIntentSignals?> ExtractTripIntentSignalsAsync(
        string redactedIntent,
        CancellationToken cancellationToken) =>
        Task.FromResult<AiTripIntentSignals?>(null);

    public Task<string?> ClassifyHelpTopicAsync(
        string redactedQuestion,
        IReadOnlyCollection<string> allowedTopicIds,
        CancellationToken cancellationToken) =>
        Task.FromResult<string?>(null);
}
