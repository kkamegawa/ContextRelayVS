using ContextRelay.Core.Handoff;
using Xunit;

namespace ContextRelay.Core.Tests.Handoff;

public sealed class AskPreviewLanguageDetectorTests
{
    [Fact]
    public void Detect_SingleWrappedFence_StripsFenceAndUsesFenceLanguage()
    {
        var result = AskPreviewLanguageDetector.Detect("convert to json", "```json\n{\"ok\":true}\n```");

        Assert.Equal("json", result.LanguageId);
        Assert.Equal("{\"ok\":true}", result.Content);
    }

    [Fact]
    public void Detect_PromptKeyword_FallsBackToRequestedLanguage()
    {
        var result = AskPreviewLanguageDetector.Detect("translate to Japanese and return as HTML", "Plain text reply.");

        Assert.Equal("html", result.LanguageId);
        Assert.Equal("Plain text reply.", result.Content);
    }

    [Fact]
    public void GetFileExtension_MapsKnownLanguageIds()
    {
        Assert.Equal("ps1", AskPreviewLanguageDetector.GetFileExtension("powershell"));
        Assert.Equal("md", AskPreviewLanguageDetector.GetFileExtension("markdown"));
        Assert.Equal("txt", AskPreviewLanguageDetector.GetFileExtension("plaintext"));
    }
}
