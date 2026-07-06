using ContextRelay.Core.Adapters;
using Xunit;

namespace ContextRelay.Core.Tests.Adapters;

public sealed class CopilotResponseIntegrityCheckerTests
{
    [Fact]
    public void Evaluate_DetectsUnbalancedCodeFence()
    {
        var result = CopilotResponseIntegrityChecker.Evaluate("```csharp\nConsole.WriteLine(\"hello\");");

        Assert.True(result.IsLikelyTruncated);
        Assert.Equal("unbalanced-code-fence", result.Reason);
    }

    [Fact]
    public void Evaluate_DetectsIncompleteMarkdownLink()
    {
        var result = CopilotResponseIntegrityChecker.Evaluate("See [deployment guide](<PLACEHOLDER>");

        Assert.True(result.IsLikelyTruncated);
        Assert.Equal("incomplete-markdown-link", result.Reason);
    }

    [Fact]
    public void Evaluate_DetectsIncompleteMarkdownTableRow()
    {
        var result = CopilotResponseIntegrityChecker.Evaluate("| Name | Value");

        Assert.True(result.IsLikelyTruncated);
        Assert.Equal("incomplete-markdown-table", result.Reason);
    }

    [Fact]
    public void Evaluate_DetectsLongEnglishResponseWithoutTerminalPunctuation()
    {
        var text = new string('a', 260);

        var result = CopilotResponseIntegrityChecker.Evaluate(text);

        Assert.True(result.IsLikelyTruncated);
        Assert.Equal("missing-terminal-punctuation", result.Reason);
    }

    [Fact]
    public void Evaluate_TreatsJapaneseFullStopAsComplete()
    {
        var result = CopilotResponseIntegrityChecker.Evaluate($"{new string('あ', 260)}。");

        Assert.False(result.IsLikelyTruncated);
    }

    [Fact]
    public void Evaluate_TreatsBalancedFenceAsComplete()
    {
        var result = CopilotResponseIntegrityChecker.Evaluate("```json\n{\"ok\":true}\n```");

        Assert.False(result.IsLikelyTruncated);
    }
}
