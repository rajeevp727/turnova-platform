using TurnOva.API;

namespace TurnOva.Tests;

public sealed class OpenApiEndpointPolicyTests
{
    [Theory]
    [InlineData("Development")]
    [InlineData("development")]
    [InlineData("Testing")]
    [InlineData("testing")]
    public void IsEnabledOnlyForDevelopmentAndTesting(string environmentName)
    {
        Assert.True(OpenApiEndpointPolicy.IsEnabled(environmentName));
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("production")]
    [InlineData("Staging")]
    [InlineData("QA")]
    [InlineData("Custom")]
    [InlineData("")]
    [InlineData(null)]
    public void IsDisabledForEveryOtherEnvironment(string? environmentName)
    {
        Assert.False(OpenApiEndpointPolicy.IsEnabled(environmentName));
    }
}
