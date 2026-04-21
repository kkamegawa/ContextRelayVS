using System;
using ContextRelay.Core.Handoff;
using ContextRelay.Core.SharedStore;
using Xunit;

namespace ContextRelay.Core.Tests.Handoff;

public sealed class AskPromptBuilderTests
{
    [Fact]
    public void Build_IncludesSnippetHeadersAndInstruction()
    {
        var result = AskPromptBuilder.Build(
            "translate",
            new[]
            {
                new SharedSnippetItem
                {
                    Name = "Architecture",
                    Source = "sharepoint",
                    SourceUrl = "https://contoso.sharepoint.com/doc.md",
                    Snippet = "Important content"
                }
            });

        Assert.Contains("### Pinned document 1: Architecture", result);
        Assert.Contains("Source: sharepoint - https://contoso.sharepoint.com/doc.md", result);
        Assert.Contains("Important content", result);
        Assert.Contains("User instruction:\ntranslate", result);
    }

    [Fact]
    public void Build_TruncatesContextToBudget()
    {
        var hugeSnippet = new string('a', AskPromptBuilder.MaxAskContextChars + 5000);

        var result = AskPromptBuilder.Build(
            "summarize",
            new[]
            {
                new SharedSnippetItem
                {
                    Name = "Huge",
                    Source = "mail",
                    Snippet = hugeSnippet,
                    CreatedAt = DateTimeOffset.UtcNow.ToString("O"),
                    UpdatedAt = DateTimeOffset.UtcNow.ToString("O"),
                    Id = "snippet-1"
                }
            });

        Assert.Contains("[truncated ", result);
        Assert.True(result.Length < AskPromptBuilder.MaxAskContextChars + 1000);
    }
}
