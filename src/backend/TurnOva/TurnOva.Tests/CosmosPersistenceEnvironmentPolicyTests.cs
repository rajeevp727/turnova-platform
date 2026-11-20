using TurnOva.API;

namespace TurnOva.Tests;

public sealed class CosmosPersistenceEnvironmentPolicyTests
{
    [Theory]
    [InlineData("Production")]
    [InlineData("production")]
    public void ProductionWithoutCosmosFailsClosed(string environmentName)
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => CosmosPersistenceEnvironmentPolicy.Validate(environmentName, null, null));

        Assert.Contains("Production requires CosmosDb:Endpoint and CosmosDb:Key", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Testing")]
    [InlineData("Staging")]
    [InlineData("QA")]
    public void NonProductionMayUseInMemoryWhenCosmosIsAbsent(string environmentName)
    {
        CosmosPersistenceEnvironmentPolicy.Validate(environmentName, null, null);
    }

    [Theory]
    [InlineData("Development", "https://cosmos.example.test", " ")]
    [InlineData("Testing", " ", "test-key")]
    [InlineData("Staging", "https://cosmos.example.test", null)]
    [InlineData("Production", null, "test-key")]
    public void PartialCosmosConfigurationFailsInEveryEnvironment(
        string environmentName, string? endpoint, string? key)
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => CosmosPersistenceEnvironmentPolicy.Validate(environmentName, endpoint, key));

        Assert.Contains("Both CosmosDb:Endpoint and CosmosDb:Key must be configured", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CompleteCosmosConfigurationIsAccepted()
    {
        CosmosPersistenceEnvironmentPolicy.Validate("Production", "https://cosmos.example.test", "test-key");
    }
}
