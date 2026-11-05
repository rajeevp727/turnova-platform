using TurnOva.Infrastructure.Database;

namespace TurnOva.Tests;

public sealed class CosmosDbOptionsTests
{
    [Fact]
    public void Validate_AcceptsValidHttpsConfiguration()
    {
        var options = new CosmosDbOptions
        {
            Endpoint = "https://turnova.documents.azure.com:443/",
            Key = "test-key",
            DatabaseName = "TurnOvaDb"
        };

        options.Validate();
    }

    [Theory]
    [InlineData("http://turnova.documents.azure.com")]
    [InlineData("not-a-uri")]
    [InlineData("")]
    public void Validate_RejectsInvalidEndpoint(string endpoint)
    {
        var options = ValidOptions();
        options.Endpoint = endpoint;

        Assert.Throws<InvalidOperationException>(options.Validate);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("bad/name")]
    [InlineData("bad\\name")]
    [InlineData("bad?name")]
    [InlineData("bad#name")]
    public void Validate_RejectsInvalidDatabaseName(string databaseName)
    {
        var options = ValidOptions();
        options.DatabaseName = databaseName;

        Assert.Throws<InvalidOperationException>(options.Validate);
    }

    [Fact]
    public void Validate_RejectsMissingKey()
    {
        var options = ValidOptions();
        options.Key = " ";

        Assert.Throws<InvalidOperationException>(options.Validate);
    }

    private static CosmosDbOptions ValidOptions() => new()
    {
        Endpoint = "https://turnova.documents.azure.com/",
        Key = "test-key",
        DatabaseName = "TurnOvaDb"
    };
}
