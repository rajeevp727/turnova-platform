using TurnOva.API;

namespace TurnOva.Tests;

public sealed class DemoAuthPolicyTests
{
    [Theory]
    [InlineData("Production", true, false)]
    [InlineData("production", true, false)]
    [InlineData("PRODUCTION", true, false)]
    [InlineData("Staging", true, true)]
    [InlineData("QA", true, true)]
    [InlineData("Testing", true, true)]
    [InlineData("Development", true, true)]
    [InlineData("Demo", true, true)]
    [InlineData("Testing", false, false)]
    [InlineData(null, true, false)]
    [InlineData("", true, false)]
    [InlineData(" ", true, false)]
    [InlineData("Preview", true, false)]
    [InlineData("Unknown", true, false)]
    [InlineData("Prod", true, false)]
    [InlineData("Production-East", true, false)]
    public void DemoAuthRequiresExplicitEnablementAndAnAllowlistedNonProductionEnvironment(
        string? environmentName, bool configured, bool expected)
    {
        Assert.Equal(expected, DemoAuthPolicy.IsEnabled(environmentName, configured));
    }
}
