using TurnOva.API;
using Xunit;

namespace TurnOva.Tests;

public sealed class CosmosPersistenceEnvironmentGuardTests
{
    [Theory]
    [InlineData("Development")]
    [InlineData("development")]
    [InlineData("Testing")]
    [InlineData("testing")]
    public void InMemoryPersistenceIsAllowedOnlyForDevelopmentAndTesting(string environmentName)
    {
        var exception = Record.Exception(() =>
            CosmosPersistenceEnvironmentGuard.Validate(environmentName, useCosmos: false));

        Assert.Null(exception);
        Assert.True(CosmosPersistenceEnvironmentGuard.IsDevelopmentOrTesting(environmentName));
    }

    [Theory]
    [InlineData("Staging")]
    [InlineData("Production")]
    [InlineData("QA")]
    [InlineData("Custom")]
    public void NonDevelopmentTestEnvironmentsRequireCosmos(string environmentName)
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            CosmosPersistenceEnvironmentGuard.Validate(environmentName, useCosmos: false));

        Assert.Contains("Non-development/test environments require CosmosDb:Endpoint and CosmosDb:Key", exception.Message);
        Assert.False(CosmosPersistenceEnvironmentGuard.IsDevelopmentOrTesting(environmentName));
    }

    [Theory]
    [InlineData("Staging")]
    [InlineData("Production")]
    [InlineData("QA")]
    public void NonDevelopmentTestEnvironmentsAcceptConfiguredCosmos(string environmentName)
    {
        var exception = Record.Exception(() =>
            CosmosPersistenceEnvironmentGuard.Validate(environmentName, useCosmos: true));

        Assert.Null(exception);
    }

    [Fact]
    public void BlankEnvironmentNameIsRejectedBeforePersistenceModeIsChosen()
    {
        Assert.Throws<ArgumentException>(() =>
            CosmosPersistenceEnvironmentGuard.Validate(" ", useCosmos: false));
    }
}
