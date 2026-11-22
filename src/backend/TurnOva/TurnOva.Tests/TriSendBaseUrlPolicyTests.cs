using TurnOva.API;

namespace TurnOva.Tests;

public sealed class TriSendBaseUrlPolicyTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AllowsHttpsBaseUrlInEveryEnvironment(bool isDevelopment)
    {
        Assert.True(TriSendBaseUrlPolicy.IsAllowed("https://auth.example.test", isDevelopment));
    }

    [Theory]
    [InlineData("http://localhost:5001")]
    [InlineData("http://127.0.0.1:5001")]
    [InlineData("http://[::1]:5001")]
    public void AllowsLoopbackHttpOnlyInDevelopment(string baseUrl)
    {
        Assert.True(TriSendBaseUrlPolicy.IsAllowed(baseUrl, isDevelopment: true));
    }

    [Theory]
    [InlineData("http://auth.test.invalid")]
    [InlineData("http://192.0.2.10:5001")]
    public void RejectsRemoteHttpInDevelopment(string baseUrl)
    {
        Assert.False(TriSendBaseUrlPolicy.IsAllowed(baseUrl, isDevelopment: true));
    }

    [Theory]
    [InlineData("http://localhost:5001")]
    [InlineData("http://127.0.0.1:5001")]
    [InlineData("http://[::1]:5001")]
    public void RejectsLoopbackHttpOutsideDevelopment(string baseUrl)
    {
        Assert.False(TriSendBaseUrlPolicy.IsAllowed(baseUrl, isDevelopment: false));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("not-a-valid-url")]
    [InlineData("https://user:secret@auth.example.test")]
    [InlineData("https://auth.example.test/?access_token=secret")]
    [InlineData("https://auth.example.test/#access_token=secret")]
    public void RejectsInvalidOrCredentialBearingUrls(string? baseUrl)
    {
        Assert.False(TriSendBaseUrlPolicy.IsAllowed(baseUrl, isDevelopment: true));
        Assert.False(TriSendBaseUrlPolicy.IsAllowed(baseUrl, isDevelopment: false));
    }
}
