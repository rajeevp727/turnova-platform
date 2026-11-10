using TurnOva.Application.Assistance;
using Xunit;

namespace TurnOva.Tests;

public sealed class AiAssistanceServiceTests
{
    [Fact]
    public async Task TripIntentBuildsCabDraftAndRequiresRiderReviewWhenProviderIsUnavailable()
    {
        var service = CreateService(new UnavailableAiAssistantProvider());

        var result = await service.DraftTripIntentAsync(
            "Please book a Cab from Nizampet to HITEC City for 2 passengers",
            CancellationToken.None);

        Assert.Equal("Nizampet", result.Pickup);
        Assert.Equal("HITEC City", result.Dropoff);
        Assert.Equal("Cab", result.ServiceClass);
        Assert.Equal(2, result.PassengerCount);
        Assert.False(result.NeedsClarification);
        Assert.True(result.RequiresUserConfirmation);
        Assert.Equal("deterministic-fallback", result.Source);
    }

    [Fact]
    public async Task ProviderReceivesOnlyRedactedTripTokensAndCannotReplaceRoute()
    {
        var provider = new FakeProvider();
        provider.TripHandler = (prompt, _) =>
        {
            Assert.Equal("[PICKUP]", prompt.PickupToken);
            Assert.Equal("[DROPOFF]", prompt.DropoffToken);
            Assert.DoesNotContain("12 Main Road", prompt.IntentSummary);
            Assert.DoesNotContain("HITEC City", prompt.IntentSummary);
            Assert.DoesNotContain("raja@example.com", prompt.IntentSummary);
            Assert.DoesNotContain("+91", prompt.IntentSummary);
            Assert.DoesNotContain("family", prompt.IntentSummary, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("child", prompt.IntentSummary, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("wheelchair", prompt.IntentSummary, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("accessible", prompt.IntentSummary, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("passenger", prompt.IntentSummary, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("4", prompt.IntentSummary, StringComparison.Ordinal);
            Assert.DoesNotContain(typeof(SafeTripIntentPrompt).GetProperties(), property => property.Name == "PassengerCount");
            return Task.FromResult<TripIntentProviderResult?>(
                new TripIntentProviderResult("[PICKUP]", "[DROPOFF]", "Cab"));
        };
        var service = CreateService(provider, enabled: true);

        var result = await service.DraftTripIntentAsync(
            "Please book a Cab for my family of 4 with a child and wheelchair accessible service, raja@example.com +91 9876543210, from 12 Main Road to HITEC City",
            CancellationToken.None);

        Assert.Equal("12 Main Road", result.Pickup);
        Assert.Equal("HITEC City", result.Dropoff);
        Assert.Equal(4, result.PassengerCount);
        Assert.Equal("provider", result.Source);
    }

    [Fact]
    public async Task InvalidProviderTripOutputFallsBackToLocallyParsedCabDraft()
    {
        var provider = new FakeProvider
        {
            TripHandler = (_, _) => Task.FromResult<TripIntentProviderResult?>(
                new TripIntentProviderResult("[PICKUP]", "[DROPOFF]", "Auto"))
        };
        var service = CreateService(provider, enabled: true);

        var result = await service.DraftTripIntentAsync(
            "Cab from Old Malakpet to Gachibowli for 3 passengers",
            CancellationToken.None);

        Assert.Equal("Old Malakpet", result.Pickup);
        Assert.Equal("Gachibowli", result.Dropoff);
        Assert.Equal("Cab", result.ServiceClass);
        Assert.Equal(3, result.PassengerCount);
        Assert.Equal("deterministic-fallback", result.Source);
    }

    [Fact]
    public async Task ProviderCannotInventPassengerCountWhenRiderDidNotSpecifyOne()
    {
        var provider = new FakeProvider
        {
            TripHandler = (prompt, _) =>
            {
                Assert.DoesNotContain("passenger", prompt.IntentSummary, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("1", prompt.IntentSummary, StringComparison.Ordinal);
                return Task.FromResult<TripIntentProviderResult?>(
                    new TripIntentProviderResult("[PICKUP]", "[DROPOFF]", "Cab"));
            }
        };
        var service = CreateService(provider, enabled: true);

        var result = await service.DraftTripIntentAsync(
            "Please book a Cab from Nizampet to HITEC City",
            CancellationToken.None);

        Assert.Null(result.PassengerCount);
        Assert.True(result.NeedsClarification);
        Assert.Contains("passengerCount", result.MissingFields);
        Assert.True(result.RequiresUserConfirmation);
        Assert.Equal("provider", result.Source);
    }

    [Fact]
    public async Task UnspecifiedPassengerCountIsReturnedForManualCompletionWhenProviderIsUnavailable()
    {
        var service = CreateService(new UnavailableAiAssistantProvider());

        var result = await service.DraftTripIntentAsync(
            "Cab from Nizampet to HITEC City",
            CancellationToken.None);

        Assert.Equal("Nizampet", result.Pickup);
        Assert.Equal("HITEC City", result.Dropoff);
        Assert.Null(result.PassengerCount);
        Assert.True(result.NeedsClarification);
        Assert.Contains("passengerCount", result.MissingFields);
        Assert.True(result.RequiresUserConfirmation);
    }

    [Fact]
    public async Task ProviderTimeoutUsesDeterministicTripFallback()
    {
        var provider = new FakeProvider
        {
            TripHandler = async (_, cancellationToken) =>
            {
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
                return new TripIntentProviderResult("[PICKUP]", "[DROPOFF]", "Cab");
            }
        };
        var service = CreateService(provider, enabled: true, timeoutSeconds: 1);

        var result = await service.DraftTripIntentAsync(
            "Cab from Nizampet to HITEC City for 2 passengers",
            CancellationToken.None);

        Assert.Equal("deterministic-fallback", result.Source);
        Assert.Equal("Nizampet", result.Pickup);
        Assert.Equal("HITEC City", result.Dropoff);
    }

    [Fact]
    public async Task ProviderIgnoringCancellationIsBoundedByTimeoutAndLateFaultDoesNotChangeFallback()
    {
        var providerStarted = new TaskCompletionSource<CancellationToken>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var pendingProvider = new TaskCompletionSource<TripIntentProviderResult?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = new FakeProvider
        {
            TripHandler = (_, token) =>
            {
                providerStarted.TrySetResult(token);
                return pendingProvider.Task;
            }
        };
        var service = CreateService(provider, enabled: true, timeoutSeconds: 1);

        var operation = service.DraftTripIntentAsync(
            "Cab from Nizampet to HITEC City for 2 passengers",
            CancellationToken.None);
        var providerToken = await providerStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var result = await operation.WaitAsync(TimeSpan.FromSeconds(3));

        Assert.True(providerToken.IsCancellationRequested);
        Assert.Equal("deterministic-fallback", result.Source);
        Assert.Equal("Nizampet", result.Pickup);
        Assert.Equal("HITEC City", result.Dropoff);

        // Complete the abandoned provider task with a late fault. The service has already
        // attached a fault observer and must not surface provider exception details.
        Assert.True(pendingProvider.TrySetException(new HttpRequestException("late provider failure")));
        await Task.Yield();
    }

    [Fact]
    public async Task ExplicitProviderTimeoutExceptionUsesDeterministicFallback()
    {
        var provider = new FakeProvider
        {
            TripHandler = (_, _) =>
                Task.FromException<TripIntentProviderResult?>(new TimeoutException("provider timeout"))
        };
        var service = CreateService(provider, enabled: true);

        var result = await service.DraftTripIntentAsync(
            "Cab from Nizampet to HITEC City for 2 passengers",
            CancellationToken.None);

        Assert.Equal("deterministic-fallback", result.Source);
        Assert.Equal("Nizampet", result.Pickup);
        Assert.Equal("HITEC City", result.Dropoff);
    }

    [Fact]
    public async Task TransientProviderHttpFailureUsesDeterministicFallback()
    {
        var provider = new FakeProvider
        {
            TripHandler = (_, _) =>
                Task.FromException<TripIntentProviderResult?>(new HttpRequestException("temporary provider failure"))
        };
        var service = CreateService(provider, enabled: true);

        var result = await service.DraftTripIntentAsync(
            "Cab from Nizampet to HITEC City for 2 passengers",
            CancellationToken.None);

        Assert.Equal("deterministic-fallback", result.Source);
        Assert.Equal("Nizampet", result.Pickup);
        Assert.Equal("HITEC City", result.Dropoff);
    }

    [Fact]
    public async Task SynchronousProviderHttpFailureUsesDeterministicFallback()
    {
        var provider = new FakeProvider
        {
            TripHandler = (_, _) => throw new HttpRequestException("temporary provider failure")
        };
        var service = CreateService(provider, enabled: true);

        var result = await service.DraftTripIntentAsync(
            "Cab from Nizampet to HITEC City for 2 passengers",
            CancellationToken.None);

        Assert.Equal("deterministic-fallback", result.Source);
        Assert.Equal("Nizampet", result.Pickup);
        Assert.Equal("HITEC City", result.Dropoff);
    }

    [Fact]
    public async Task SynchronousProviderTimeoutExceptionUsesDeterministicFallback()
    {
        var provider = new FakeProvider
        {
            TripHandler = (_, _) => throw new TimeoutException("provider timeout")
        };
        var service = CreateService(provider, enabled: true);

        var result = await service.DraftTripIntentAsync(
            "Cab from Nizampet to HITEC City for 2 passengers",
            CancellationToken.None);

        Assert.Equal("deterministic-fallback", result.Source);
        Assert.Equal("Nizampet", result.Pickup);
        Assert.Equal("HITEC City", result.Dropoff);
    }

    [Fact]
    public async Task UnexpectedAsynchronousProviderFailureUsesDeterministicTripFallback()
    {
        var provider = new FakeProvider
        {
            TripHandler = (_, _) =>
                Task.FromException<TripIntentProviderResult?>(
                    new InvalidOperationException("provider adapter payload failure"))
        };
        var service = CreateService(provider, enabled: true);

        var result = await service.DraftTripIntentAsync(
            "from Hyderabad to Secunderabad",
            CancellationToken.None);

        Assert.Equal("Hyderabad", result.Pickup);
        Assert.Equal("Secunderabad", result.Dropoff);
        Assert.Equal("deterministic-fallback", result.Source);
        Assert.True(result.RequiresUserConfirmation);
    }

    [Fact]
    public async Task UnexpectedSynchronousProviderFailureUsesDeterministicHelpFallback()
    {
        var provider = new FakeProvider
        {
            HelpHandler = (_, _) =>
                throw new InvalidOperationException("provider adapter parsing failure")
        };
        var service = CreateService(provider, enabled: true);

        var result = await service.ExplainHelpAsync(
            "How is the fare negotiated?",
            null,
            CancellationToken.None);

        Assert.Equal("fare-negotiation", result.SourceId);
        Assert.Equal("deterministic-fallback", result.Source);
        Assert.Contains("negotiate the fare directly", result.Answer, StringComparison.OrdinalIgnoreCase);
        Assert.False(result.EscalationRequired);
    }

    [Fact]
    public async Task CallerCancellationDuringInFlightProviderRequestIsPropagated()
    {
        var providerStarted = new TaskCompletionSource<CancellationToken>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var pendingProvider = new TaskCompletionSource<TripIntentProviderResult?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = new FakeProvider
        {
            TripHandler = (_, token) =>
            {
                providerStarted.TrySetResult(token);
                return pendingProvider.Task;
            }
        };
        var service = CreateService(provider, enabled: true, timeoutSeconds: 10);
        using var callerCancellation = new CancellationTokenSource();

        var operation = service.DraftTripIntentAsync(
            "Cab from Nizampet to HITEC City for 2 passengers",
            callerCancellation.Token);
        var providerToken = await providerStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        await callerCancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation);
        Assert.True(providerToken.IsCancellationRequested);
        pendingProvider.TrySetResult(null);
    }

    [Fact]
    public async Task CallerCancellationTakesPrecedenceOverAlreadyFaultedProviderHttpRequest()
    {
        using var callerCancellation = new CancellationTokenSource();
        var provider = new FakeProvider
        {
            TripHandler = (_, _) =>
            {
                var failed = Task.FromException<TripIntentProviderResult?>(
                    new HttpRequestException("temporary provider failure"));
                callerCancellation.Cancel();
                return failed;
            }
        };
        var service = CreateService(provider, enabled: true);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.DraftTripIntentAsync(
                "Cab from Nizampet to HITEC City for 2 passengers",
                callerCancellation.Token));
    }

    [Fact]
    public async Task CallerCancellationIsPropagatedInsteadOfConvertedToFallback()
    {
        var service = CreateService(new UnavailableAiAssistantProvider());
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.DraftTripIntentAsync(
                "Cab from Nizampet to HITEC City",
                cancellation.Token));
    }

    [Fact]
    public async Task MissingRouteAndInvalidPassengerCountRequireClarification()
    {
        var service = CreateService(new UnavailableAiAssistantProvider());

        var missingRoute = await service.DraftTripIntentAsync(
            "I need a Cab for 2 passengers",
            CancellationToken.None);
        var invalidCount = await service.DraftTripIntentAsync(
            "Cab from Nizampet to HITEC City for 7 passengers",
            CancellationToken.None);

        Assert.True(missingRoute.NeedsClarification);
        Assert.Contains("pickup", missingRoute.MissingFields);
        Assert.Contains("dropoff", missingRoute.MissingFields);
        Assert.True(invalidCount.NeedsClarification);
        Assert.Contains("passengerCount", invalidCount.MissingFields);
        Assert.Null(invalidCount.PassengerCount);
    }

    [Fact]
    public async Task KnownRideStateUsesExactApprovedInRepositoryExplanation()
    {
        var service = CreateService(new UnavailableAiAssistantProvider());

        var result = await service.ExplainHelpAsync(
            "What is happening with my trip?",
            "InProgress",
            CancellationToken.None);

        Assert.Equal("state-in-progress", result.SourceId);
        Assert.Contains("foreground", result.Answer, StringComparison.OrdinalIgnoreCase);
        Assert.False(result.EscalationRequired);
        Assert.Equal("approved-content", result.Source);
    }

    [Fact]
    public async Task UnsupportedHelpEscalatesInsteadOfInventingAnAnswer()
    {
        var service = CreateService(new UnavailableAiAssistantProvider());

        var result = await service.ExplainHelpAsync(
            "What is the weather on Mars?",
            null,
            CancellationToken.None);

        Assert.Equal("unsupported", result.SourceId);
        Assert.True(result.EscalationRequired);
        Assert.Contains("support contact", result.Answer, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ServiceRejectsOutputLimitTooSmallForEscalationFallback()
    {
        var options = new AiAssistantOptions { MaxOutputCharacters = 10 };

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new AiAssistanceService(new UnavailableAiAssistantProvider(), options));
    }

    [Fact]
    public async Task KnownRideStateExceedingConfiguredOutputLimitUsesBoundedEscalation()
    {
        var service = CreateService(new UnavailableAiAssistantProvider(), maxOutputCharacters: 100);

        var result = await service.ExplainHelpAsync(
            "What is happening with my trip?",
            "InProgress",
            CancellationToken.None);

        Assert.Equal("unsupported", result.SourceId);
        Assert.True(result.EscalationRequired);
        Assert.Equal("deterministic-fallback", result.Source);
        Assert.Contains("support contact", result.Answer, StringComparison.OrdinalIgnoreCase);
        Assert.InRange(result.Answer.Length, 1, 100);
    }

    [Fact]
    public async Task UnsupportedHelpFallbackRespectsConfiguredOutputLimit()
    {
        var service = CreateService(new UnavailableAiAssistantProvider(), maxOutputCharacters: 100);

        var result = await service.ExplainHelpAsync(
            "What is the weather on Mars?",
            null,
            CancellationToken.None);

        Assert.Equal("unsupported", result.SourceId);
        Assert.True(result.EscalationRequired);
        Assert.InRange(result.Answer.Length, 1, 100);
    }

    [Fact]
    public async Task ProviderSelectedApprovedContentExceedingOutputLimitUsesBoundedEscalation()
    {
        var provider = new FakeProvider
        {
            HelpHandler = (_, _) => Task.FromResult<HelpProviderResult?>(
                new HelpProviderResult("state-in-progress"))
        };
        var service = CreateService(provider, enabled: true, maxOutputCharacters: 100);

        var result = await service.ExplainHelpAsync(
            "Explain my trip progress",
            null,
            CancellationToken.None);

        Assert.Equal("unsupported", result.SourceId);
        Assert.True(result.EscalationRequired);
        Assert.Equal("deterministic-fallback", result.Source);
        Assert.InRange(result.Answer.Length, 1, 100);
    }

    [Fact]
    public async Task ExplicitStateNameInQuestionReturnsItsApprovedExplanation()
    {
        var service = CreateService(new UnavailableAiAssistantProvider());

        var result = await service.ExplainHelpAsync(
            "What does InProgress mean?",
            null,
            CancellationToken.None);

        Assert.Equal("state-in-progress", result.SourceId);
        Assert.Contains("InProgress", result.Answer, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AmbiguousGenericRideStatusQuestionEscalatesInsteadOfGuessingAState()
    {
        var service = CreateService(new UnavailableAiAssistantProvider());

        var result = await service.ExplainHelpAsync(
            "What is my ride status?",
            null,
            CancellationToken.None);

        Assert.True(result.EscalationRequired);
        Assert.Equal("unsupported", result.SourceId);
    }

    [Fact]
    public async Task QueueHelpExplainsRiderSelectionWithoutInventingInternalFifoPolicy()
    {
        var service = CreateService(new UnavailableAiAssistantProvider());

        var result = await service.ExplainHelpAsync(
            "What does my queue status mean?",
            null,
            CancellationToken.None);

        Assert.Equal("queue-status", result.SourceId);
        Assert.Contains("does not use FIFO", result.Answer, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("unresolved", result.Answer, StringComparison.OrdinalIgnoreCase);
        Assert.False(result.EscalationRequired);
    }

    [Fact]
    public async Task AvailabilityHelpExplainsOnlineAndOfflineEligibility()
    {
        var service = CreateService(new UnavailableAiAssistantProvider());

        var result = await service.ExplainHelpAsync(
            "Why am I offline and not eligible?",
            null,
            CancellationToken.None);

        Assert.Equal("availability-status", result.SourceId);
        Assert.Contains("Online is one input", result.Answer, StringComparison.Ordinal);
        Assert.False(result.EscalationRequired);
    }

    [Fact]
    public async Task ProviderCanSelectOnlyAnApprovedHelpSourceAndServiceReturnsRepositoryContent()
    {
        var provider = new FakeProvider
        {
            HelpHandler = (prompt, _) =>
            {
                Assert.Contains("direct-payment", prompt.AllowedSourceIds);
                Assert.Contains("payment", prompt.AllowedKeywords);
                return Task.FromResult<HelpProviderResult?>(new HelpProviderResult("direct-payment"));
            }
        };
        var service = CreateService(provider, enabled: true);

        var result = await service.ExplainHelpAsync(
            "How is the payment received after the trip reported?",
            null,
            CancellationToken.None);

        Assert.Equal("direct-payment", result.SourceId);
        Assert.Equal("provider-approved-content", result.Source);
        Assert.Contains("unverified", result.Answer, StringComparison.OrdinalIgnoreCase);
        Assert.False(result.EscalationRequired);
    }

    [Fact]
    public async Task ProviderCannotInjectUnapprovedHelpContent()
    {
        var provider = new FakeProvider
        {
            HelpHandler = (_, _) =>
                Task.FromResult<HelpProviderResult?>(new HelpProviderResult("made-up-policy"))
        };
        var service = CreateService(provider, enabled: true);

        var result = await service.ExplainHelpAsync(
            "How is the fare negotiated?",
            null,
            CancellationToken.None);

        Assert.NotEqual("made-up-policy", result.SourceId);
        Assert.Equal("fare-negotiation", result.SourceId);
        Assert.Equal("deterministic-fallback", result.Source);
    }

    [Fact]
    public async Task OversizedAssistantInputIsRejected()
    {
        var service = CreateService(new UnavailableAiAssistantProvider());

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.DraftTripIntentAsync(new string('x', 1001), CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.ExplainHelpAsync(new string('x', 1001), null, CancellationToken.None));
    }

    private static AiAssistanceService CreateService(
        IAiAssistantProvider provider,
        bool enabled = false,
        int timeoutSeconds = 4,
        int maxOutputCharacters = 600) =>
        new(provider, new AiAssistantOptions
        {
            Enabled = enabled,
            Provider = enabled ? "configured-provider-placeholder" : null,
            Model = enabled ? "configured-model-placeholder" : null,
            Endpoint = enabled ? "https://provider.example.test" : null,
            CredentialReference = enabled ? "secret-store-reference-placeholder" : null,
            RequestTimeoutSeconds = timeoutSeconds,
            MaxInputCharacters = 1000,
            MaxOutputCharacters = maxOutputCharacters,
            MaxRequestsPerMinute = 10
        });

    private sealed class FakeProvider : IAiAssistantProvider
    {
        public bool IsAvailable => true;

        public Func<SafeTripIntentPrompt, CancellationToken, Task<TripIntentProviderResult?>>? TripHandler { get; set; }
        public Func<SafeHelpPrompt, CancellationToken, Task<HelpProviderResult?>>? HelpHandler { get; init; }

        public Task<TripIntentProviderResult?> DraftTripIntentAsync(
            SafeTripIntentPrompt request,
            CancellationToken cancellationToken) =>
            TripHandler?.Invoke(request, cancellationToken) ??
            Task.FromResult<TripIntentProviderResult?>(null);

        public Task<HelpProviderResult?> ClassifyHelpAsync(
            SafeHelpPrompt request,
            CancellationToken cancellationToken) =>
            HelpHandler?.Invoke(request, cancellationToken) ??
            Task.FromResult<HelpProviderResult?>(null);
    }
}
