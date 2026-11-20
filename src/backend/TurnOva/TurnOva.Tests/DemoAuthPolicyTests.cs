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
    [InlineData("Testing", false, false)]
    [InlineData(null, true, false)]
    [InlineData("", true, false)]
    [InlineData(" ", true, false)]
    public void DemoAuthRequiresExplicitEnablementAndIsNeverEnabledInProduction(
        string? environmentName, bool configured, bool expected)
    {
        Assert.Equal(expected, DemoAuthPolicy.IsEnabled(environmentName, configured));
    }
}
