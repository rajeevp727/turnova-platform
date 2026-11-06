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
            return Task.FromResult<TripIntentProviderResult?>(
                new TripIntentProviderResult("[PICKUP]", "[DROPOFF]", "Cab", 2));
        };
        var service = CreateService(provider, enabled: true);

        var result = await service.DraftTripIntentAsync(
            "Please book a Cab for raja@example.com, call +91 9876543210, from 12 Main Road to HITEC City for 2 passengers",
            CancellationToken.None);

        Assert.Equal("12 Main Road", result.Pickup);
        Assert.Equal("HITEC City", result.Dropoff);
        Assert.Equal(2, result.PassengerCount);
        Assert.Equal("provider", result.Source);
    }

    [Fact]
    public async Task InvalidProviderTripOutputFallsBackToLocallyParsedCabDraft()
    {
        var provider = new FakeProvider
        {
            TripHandler = (_, _) => Task.FromResult<TripIntentProviderResult?>(
                new TripIntentProviderResult("[PICKUP]", "[DROPOFF]", "Auto", 4))
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
    public async Task ProviderTimeoutUsesDeterministicTripFallback()
    {
        var provider = new FakeProvider
        {
            TripHandler = async (_, cancellationToken) =>
            {
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
                return new TripIntentProviderResult("[PICKUP]", "[DROPOFF]", "Cab", 2);
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
            "How is money received after the trip reported?",
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
        int timeoutSeconds = 4) =>
        new(provider, new AiAssistantOptions
        {
            Enabled = enabled,
            Provider = enabled ? "configured-provider-placeholder" : null,
            Model = enabled ? "configured-model-placeholder" : null,
            Endpoint = enabled ? "https://provider.example.test" : null,
            CredentialReference = enabled ? "secret-store-reference-placeholder" : null,
            RequestTimeoutSeconds = timeoutSeconds,
            MaxInputCharacters = 1000,
            MaxOutputCharacters = 600,
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
