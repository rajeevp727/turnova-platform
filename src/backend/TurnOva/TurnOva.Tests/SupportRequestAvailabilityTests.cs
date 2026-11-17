using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Moq;
using TurnOva.API.Support;

namespace TurnOva.Tests;

public sealed class SupportRequestAvailabilityTests
{
    [Theory]
    [InlineData("7", 604800)]
    [InlineData("1", 86400)]
    [InlineData("0", null)]
    [InlineData("-1", null)]
    [InlineData("24856", null)]
    [InlineData("not-a-number", null)]
    [InlineData(null, null)]
    public void RetentionDaysMustConvertToPositiveCosmosTtlSeconds(string? configuredDays, int? expectedTtlSeconds)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SupportRequests:RetentionDays"] = configuredDays
            })
            .Build();

        Assert.Equal(expectedTtlSeconds, SupportRequestEndpoints.GetRetentionTtlSeconds(configuration));
    }

    [Fact]
    public void PersistentSupportIntakeRechecksPolicyAndRetentionAfterConfigurationReload()
    {
        var environment = new Mock<IHostEnvironment>();
        environment.SetupGet(value => value.EnvironmentName).Returns("Staging");
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SupportRequests:PolicyApproved"] = "true",
                ["SupportRequests:RetentionDays"] = "7"
            })
            .Build();

        Assert.True(SupportRequestEndpoints.TryGetRequestTtl(environment.Object, configuration, requireRetention: true, out var ttlSeconds));
        Assert.Equal(7 * 86_400, ttlSeconds);

        configuration["SupportRequests:RetentionDays"] = null;
        Assert.False(SupportRequestEndpoints.TryGetRequestTtl(environment.Object, configuration, requireRetention: true, out ttlSeconds));
        Assert.Null(ttlSeconds);

        configuration["SupportRequests:RetentionDays"] = "7";
        configuration["SupportRequests:PolicyApproved"] = "false";
        Assert.False(SupportRequestEndpoints.TryGetRequestTtl(environment.Object, configuration, requireRetention: true, out ttlSeconds));
        Assert.Null(ttlSeconds);
    }

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
