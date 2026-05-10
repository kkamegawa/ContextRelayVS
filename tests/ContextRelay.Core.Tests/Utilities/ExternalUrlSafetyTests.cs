using ContextRelay.Core.Utilities;
using Xunit;

namespace ContextRelay.Core.Tests.Utilities;

public sealed class ExternalUrlSafetyTests
{
    [Theory]
    [InlineData("https://contoso.example/path?q=1")]
    [InlineData("http://contoso.example/path")]
    [InlineData("mailto:alice@contoso.com")]
    public void TryNormalizeExternalUrl_AllowsKnownSafeSchemes(string value)
    {
        var result = ExternalUrlSafety.TryNormalizeExternalUrl(value, out var normalizedUrl);

        Assert.True(result);
        Assert.False(string.IsNullOrWhiteSpace(normalizedUrl));
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html;base64,PHNjcmlwdD5hbGVydCgxKTwvc2NyaXB0Pg==")]
    [InlineData("file:///C:/Windows/System32/calc.exe")]
    [InlineData("\\\\attacker\\share\\payload.exe")]
    [InlineData("C:\\Windows\\System32\\notepad.exe")]
    [InlineData("vscode://file/c:/temp")]
    [InlineData("ms-settings:")]
    public void TryNormalizeExternalUrl_BlocksUnsafeSchemesAndPaths(string value)
    {
        var result = ExternalUrlSafety.TryNormalizeExternalUrl(value, out var normalizedUrl);

        Assert.False(result);
        Assert.Equal(string.Empty, normalizedUrl);
    }

    [Fact]
    public void TryNormalizeExternalUrl_BlocksControlCharacters()
    {
        var result = ExternalUrlSafety.TryNormalizeExternalUrl("https://contoso.example/\u0001path", out var normalizedUrl);

        Assert.False(result);
        Assert.Equal(string.Empty, normalizedUrl);
    }
}
