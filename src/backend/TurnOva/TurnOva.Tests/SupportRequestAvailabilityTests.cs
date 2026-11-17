using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Moq;
using TurnOva.API.Support;

namespace TurnOva.Tests;

public sealed class SupportRequestAvailabilityTests
{
    [Theory]
    [InlineData("Development", false, true)]
    [InlineData("Testing", false, true)]
    [InlineData("Staging", false, false)]
    [InlineData("Production", false, false)]
    [InlineData("Staging", true, true)]
    [InlineData("Production", true, true)]
    public void NonDevelopmentSupportIntakeRequiresExplicitPolicyApproval(
        string environmentName,
        bool policyApproved,
        bool expectedEnabled)
    {
        var environment = new Mock<IHostEnvironment>();
        environment.SetupGet(value => value.EnvironmentName).Returns(environmentName);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SupportRequests:PolicyApproved"] = policyApproved ? "true" : "false"
            })
            .Build();

        var enabled = SupportRequestEndpoints.IsEnabledForEnvironment(environment.Object, configuration);

        Assert.Equal(expectedEnabled, enabled);
    }
}
