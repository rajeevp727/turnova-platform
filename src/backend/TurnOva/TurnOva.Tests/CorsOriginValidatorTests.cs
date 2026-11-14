using TurnOva.API;

namespace TurnOva.Tests;

public sealed class CorsOriginValidatorTests
{
    [Theory]
    [InlineData("https://*.turnova.test")]
    [InlineData("https://app*.turnova.test")]
    [InlineData("https://app.turnova.test/*")]
    public void ValidateOriginRejectsAnyWildcardCharacter(string origin)
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => CorsOriginValidator.ValidateOrigin(origin, allowHttp: false));

        Assert.Contains("wildcards", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("https://app.turnova.test")]
    [InlineData("https://app.turnova.test/")]
    public void ValidateOriginAllowsExplicitHttpsOrigins(string origin)
    {
        CorsOriginValidator.ValidateOrigin(origin, allowHttp: false);
    }

    [Fact]
    public void ValidateOriginAllowsHttpOnlyWhenDevelopmentAllowsIt()
    {
        CorsOriginValidator.ValidateOrigin("http://localhost:3000", allowHttp: true);
    }

    [Fact]
    public void ValidateOriginRejectsHttpWhenDevelopmentDoesNotAllowIt()
    {
        Assert.Throws<InvalidOperationException>(
            () => CorsOriginValidator.ValidateOrigin("http://localhost:3000", allowHttp: false));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("https://app.turnova.test/api")]
    [InlineData("https://user@app.turnova.test")]
    [InlineData("https://app.turnova.test?next=home")]
    [InlineData("https://app.turnova.test#fragment")]
    [InlineData("ftp://app.turnova.test")]
    public void ValidateOriginRejectsBlankOrNonOriginUrls(string? origin)
    {
        Assert.Throws<InvalidOperationException>(
            () => CorsOriginValidator.ValidateOrigin(origin, allowHttp: false));
    }
}
