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

        Assert.True(result.Succeeded, FailureText(result));
    }

    [Theory]
    [MemberData(nameof(UnsafeEndpoints))]
    public void EnabledProviderRejectsUnsafeOrAmbiguousEndpointForms(string endpoint)
    {
        var result = Validate(EnabledOptions(endpoint));

        Assert.False(result.Succeeded);
        Assert.Contains(result.Failures ?? Array.Empty<string>(), failure =>
            failure.Contains("AiAssistant:Endpoint", StringComparison.Ordinal));
    }

    public static IEnumerable<object[]> UnsafeEndpoints()
    {
        yield return new object[] { "http://api.example.com" };
        yield return new object[] { "https:/api.example.com" };
        yield return new object[] { "https:///v1/assist" };
        yield return new object[] { "/v1/assist" };
        yield return new object[] { "https://user:password@api.example.com" };
        yield return new object[] { "https://@api.example.com" };
        yield return new object[] { "https://api.example.com?token=value" };
        yield return new object[] { "https://api.example.com?" };
        yield return new object[] { "https://api.example.com#fragment" };
        yield return new object[] { "https://api.example.com#" };
        yield return new object[] { " https://api.example.com" };
        yield return new object[] { "https://api.example.com/path with space" };
        yield return new object[] { "https://api.example.com/path" + (char)9 + "segment" };
        yield return new object[] { "https://api.example.com" + (char)92 + "path" };
        yield return new object[] { "https://api.example.com/" + (char)0x0000 };
        yield return new object[] { "https://api.example.com/" + (char)0x000A };
        yield return new object[] { "https://api.example.com/" + (char)0x000D };
        yield return new object[] { "https://api.example.com/" + (char)0x0080 };
        yield return new object[] { "https://api.example.com/" + (char)0x0085 };
        yield return new object[] { "https://api.example.com/" + (char)0x009F };
        yield return new object[] { "https://api.example.com/" + (char)0x2028 };
        yield return new object[] { "https://api.example.com/" + (char)0x2029 };
    }

    [Fact]
    public void DisabledProviderAllowsEmptyPlaceholders()
    {
        var result = Validate(new AiAssistantOptions { Enabled = false });

        Assert.True(result.Succeeded, FailureText(result));
    }

    [Fact]
    public void EnabledProviderRequiresProviderModelEndpointAndManagedCredentialReference()
    {
        var result = Validate(new AiAssistantOptions { Enabled = true });

        Assert.False(result.Succeeded);
        var failures = FailureText(result);
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
        Assert.DoesNotContain(secret, FailureText(result), StringComparison.Ordinal);
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

    private static string FailureText(ValidateOptionsResult result) =>
        string.Join(Environment.NewLine, result.Failures ?? Array.Empty<string>());
}
