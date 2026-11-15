using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TurnOva.API.Assistance;
using TurnOva.Application.Assistance;
using Xunit;

namespace TurnOva.Tests;

public sealed class AiAssistantOptionsValidatorTests
{
    [Theory]
    [InlineData("https://api.example.com")]
    [InlineData("HTTPS://api.example.com/v1/assist")]
    [InlineData("https://api.example.com/v1/models%20stable")]
    [InlineData("https://[2001:db8::1]/v1")]
    public void EnabledProviderAcceptsWellFormedHttpsEndpoints(string endpoint)
    {
        var result = Validate(EnabledOptions(endpoint));

        Assert.True(result.Succeeded, string.Join(Environment.NewLine, result.Failures));
    }

    [Theory]
    [InlineData("http://api.example.com")]
    [InlineData("https:/api.example.com")]
    [InlineData("https:///v1/assist")]
    [InlineData("/v1/assist")]
    [InlineData("https://user:password@api.example.com")]
    [InlineData("https://@api.example.com")]
    [InlineData("https://api.example.com?token=value")]
    [InlineData("https://api.example.com?")]
    [InlineData("https://api.example.com#fragment")]
    [InlineData("https://api.example.com#")]
    [InlineData(" https://api.example.com")]
    [InlineData("https://api.example.com/path with space")]
    [InlineData("https://api.example.com/path\\tsegment")]
    [InlineData("https://api.example.com\\path")]
    [InlineData("https://api.example.com/\\u0000")]
    [InlineData("https://api.example.com/\\u0080")]
    [InlineData("https://api.example.com/\\u0085")]
    [InlineData("https://api.example.com/\\u009f")]
    [InlineData("https://api.example.com/\\u2028")]
    [InlineData("https://api.example.com/\\u2029")]
    public void EnabledProviderRejectsUnsafeOrAmbiguousEndpointForms(string endpoint)
    {
        var result = Validate(EnabledOptions(endpoint));

        Assert.False(result.Succeeded);
        Assert.Contains(result.Failures, failure =>
            failure.Contains("AiAssistant:Endpoint", StringComparison.Ordinal));
    }

    [Fact]
    public void DisabledProviderAllowsEmptyPlaceholders()
    {
        var result = Validate(new AiAssistantOptions { Enabled = false });

        Assert.True(result.Succeeded, string.Join(Environment.NewLine, result.Failures));
    }

    [Fact]
    public void EnabledProviderRequiresProviderModelEndpointAndManagedCredentialReference()
    {
        var result = Validate(new AiAssistantOptions { Enabled = true });

        Assert.False(result.Succeeded);
        var failures = string.Join(Environment.NewLine, result.Failures);
        Assert.Contains("AiAssistant:Provider", failures, StringComparison.Ordinal);
        Assert.Contains("AiAssistant:Model", failures, StringComparison.Ordinal);
        Assert.Contains("AiAssistant:Endpoint", failures, StringComparison.Ordinal);
        Assert.Contains("AiAssistant:CredentialReference", failures, StringComparison.Ordinal);
    }

    [Fact]
    public void EndpointValidationErrorsNeverEchoCredentialOrQueryValues()
    {
        const string secret = "test-only-secret-value";
        var result = Validate(EnabledOptions(
            $"https://user:{secret}@api.example.com?token={secret}"));

        Assert.False(result.Succeeded);
        Assert.DoesNotContain(secret, string.Join(Environment.NewLine, result.Failures), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0, 1000, 600, 10)]
    [InlineData(11, 1000, 600, 10)]
    [InlineData(4, 63, 600, 10)]
    [InlineData(4, 2001, 600, 10)]
    [InlineData(4, 1000, 99, 10)]
    [InlineData(4, 1000, 1001, 10)]
    [InlineData(4, 1000, 600, 0)]
    [InlineData(4, 1000, 600, 61)]
    public void NumericLimitsRemainValidatedWhenProviderIsDisabled(
        int timeoutSeconds,
        int maxInputCharacters,
        int maxOutputCharacters,
        int maxRequestsPerMinute)
    {
        var result = Validate(new AiAssistantOptions
        {
            Enabled = false,
            RequestTimeoutSeconds = timeoutSeconds,
            MaxInputCharacters = maxInputCharacters,
            MaxOutputCharacters = maxOutputCharacters,
            MaxRequestsPerMinute = maxRequestsPerMinute
        });

        Assert.False(result.Succeeded);
    }

    [Fact]
    public void AddAiAssistanceUsesTheExplicitOptionsValidator()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AiAssistant:Enabled"] = "true",
                ["AiAssistant:Provider"] = "provider-placeholder",
                ["AiAssistant:Model"] = "model-placeholder",
                ["AiAssistant:Endpoint"] = "https://@api.example.com",
                ["AiAssistant:CredentialReference"] = "managed-secret-reference-placeholder"
            })
            .Build();
        var services = new ServiceCollection();
        services.AddAiAssistance(configuration);

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<AiAssistantOptions>>();

        var exception = Assert.Throws<OptionsValidationException>(() => _ = options.Value);
        Assert.Contains(exception.Failures, failure =>
            failure.Contains("AiAssistant:Endpoint", StringComparison.Ordinal));
    }

    private static AiAssistantOptions EnabledOptions(string endpoint) => new()
    {
        Enabled = true,
        Provider = "provider-placeholder",
        Model = "model-placeholder",
        Endpoint = endpoint,
        CredentialReference = "managed-secret-reference-placeholder"
    };

    private static ValidateOptionsResult Validate(AiAssistantOptions options) =>
        new AiAssistantOptionsValidator().Validate(Options.DefaultName, options);
}
