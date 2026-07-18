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

    [Fact]
    public void Evaluate_TreatsShortAnswerEndingInDigitAsComplete()
    {
        var text = new string('a', 220) + "\n\nThe final count is 42";

        var result = CopilotResponseIntegrityChecker.Evaluate(text);

        Assert.False(result.IsLikelyTruncated);
    }

    [Fact]
    public void Evaluate_TreatsShortBulletListEndingWithoutPunctuationAsComplete()
    {
        var text = new string('a', 220) + "\n\n- Final item";

        var result = CopilotResponseIntegrityChecker.Evaluate(text);

        Assert.False(result.IsLikelyTruncated);
    }

    [Fact]
    public void Evaluate_TreatsShortStandaloneHeadingAsComplete()
    {
        var result = CopilotResponseIntegrityChecker.Evaluate("## Next steps");

        Assert.False(result.IsLikelyTruncated);
    }

    [Fact]
    public void Evaluate_DetectsDanglingHeadingInLongResponse()
    {
        var text = new string('a', 260) + "\n\n## Next steps";

        var result = CopilotResponseIntegrityChecker.Evaluate(text);

        Assert.True(result.IsLikelyTruncated);
        Assert.Equal("dangling-heading", result.Reason);
    }

    [Fact]
    public void Evaluate_DetectsLongUnterminatedListItem()
    {
        var text = new string('a', 220) + "\n\n- " + new string('b', 100);

        var result = CopilotResponseIntegrityChecker.Evaluate(text);

        Assert.True(result.IsLikelyTruncated);
        Assert.Equal("missing-terminal-punctuation", result.Reason);
    }

    [Fact]
    public void Evaluate_DetectsUnbalancedBoldMarkerOnLastLine()
    {
        var result = CopilotResponseIntegrityChecker.Evaluate($"{new string('a', 240)}\n\nThis ends with **partial emphasis");

        Assert.True(result.IsLikelyTruncated);
        Assert.Equal("unbalanced-bold-marker", result.Reason);
    }

    [Fact]
    public void Evaluate_TreatsMultilineBoldEmphasisAsComplete()
    {
        var result = CopilotResponseIntegrityChecker.Evaluate("**bold text\ncontinued**");

        Assert.False(result.IsLikelyTruncated);
    }

    [Fact]
    public void Evaluate_TreatsBoldMarkerInsideFencedCodeAsComplete()
    {
        var result = CopilotResponseIntegrityChecker.Evaluate("```text\nliteral ** marker\n```");

        Assert.False(result.IsLikelyTruncated);
    }

    [Fact]
    public void Evaluate_TreatsBoldMarkerInsideTildeFencedCodeAsComplete()
    {
        var result = CopilotResponseIntegrityChecker.Evaluate("~~~text\nliteral ** marker\n~~~");

        Assert.False(result.IsLikelyTruncated);
    }

    [Fact]
    public void Evaluate_TreatsFenceContentLineThatStartsWithFenceMarkerAsComplete()
    {
        var result = CopilotResponseIntegrityChecker.Evaluate("```text\n```not-a-close\nliteral ** marker\n```");

        Assert.False(result.IsLikelyTruncated);
    }

    [Fact]
    public void Evaluate_DetectsUnbalancedTildeCodeFence()
    {
        var result = CopilotResponseIntegrityChecker.Evaluate("~~~text\nliteral content");

        Assert.True(result.IsLikelyTruncated);
        Assert.Equal("unbalanced-code-fence", result.Reason);
    }

    [Fact]
    public void Evaluate_TreatsBoldMarkerInsideInlineCodeAsComplete()
    {
        var result = CopilotResponseIntegrityChecker.Evaluate("The literal `**` marker is not emphasis.");

        Assert.False(result.IsLikelyTruncated);
    }

    [Fact]
    public void Evaluate_TreatsUnderscoreBoldMarkerInsideInlineCodeAsComplete()
    {
        var result = CopilotResponseIntegrityChecker.Evaluate("The literal `__` marker is not emphasis.");

        Assert.False(result.IsLikelyTruncated);
    }

    [Fact]
    public void Evaluate_TreatsBoldMarkerInsideDoubleBacktickCodeSpanAsComplete()
    {
        var result = CopilotResponseIntegrityChecker.Evaluate("The literal ``**`` marker is not emphasis.");

        Assert.False(result.IsLikelyTruncated);
    }

    [Fact]
    public void Evaluate_TreatsThematicBreakAsComplete()
    {
        var result = CopilotResponseIntegrityChecker.Evaluate("***");

        Assert.False(result.IsLikelyTruncated);
    }

    [Fact]
    public void Evaluate_TreatsThematicBreakInsideResponseAsComplete()
    {
        var result = CopilotResponseIntegrityChecker.Evaluate("First section.\n\n***\n\nSecond section.");

        Assert.False(result.IsLikelyTruncated);
    }

    [Fact]
    public void Evaluate_TreatsAsteriskOperatorAsComplete()
    {
        var result = CopilotResponseIntegrityChecker.Evaluate("The expression is 2**3.");

        Assert.False(result.IsLikelyTruncated);
    }

    [Fact]
    public void Evaluate_TreatsIntrawordUnderscoresAsComplete()
    {
        var result = CopilotResponseIntegrityChecker.Evaluate("The token is FOO__BAR.");

        Assert.False(result.IsLikelyTruncated);
    }

    [Fact]
    public void Evaluate_DetectsUnmatchedOpenerWithLetterNeighbors()
    {
        // Unlike "2**3" (digit-digit, an operator) or "FOO__BAR" (identifier), letters
        // surrounding an unmatched "**" are a genuine unclosed bold marker.
        var result = CopilotResponseIntegrityChecker.Evaluate("word**partial emphasis");

        Assert.True(result.IsLikelyTruncated);
        Assert.Equal("unbalanced-bold-marker", result.Reason);
    }

    [Fact]
    public void Evaluate_TreatsClosingOnlyAsteriskRunAsComplete()
    {
        var result = CopilotResponseIntegrityChecker.Evaluate("Use the glob src/**.");

        Assert.False(result.IsLikelyTruncated);
    }

    [Fact]
    public void Evaluate_TreatsClosingOnlyUnderscoreRunAsComplete()
    {
        var result = CopilotResponseIntegrityChecker.Evaluate("The generated suffix is name__.");

        Assert.False(result.IsLikelyTruncated);
    }

    [Fact]
    public void Evaluate_DetectsUnbalancedBoldMarkerAcrossCompleteResponse()
    {
        var result = CopilotResponseIntegrityChecker.Evaluate($"{new string('a', 240)}\n\nThis starts **partial emphasis\ncontinued");

        Assert.True(result.IsLikelyTruncated);
        Assert.Equal("unbalanced-bold-marker", result.Reason);
    }

    [Fact]
    public void Evaluate_DetectsUnbalancedUnderscoreBoldMarkerAcrossCompleteResponse()
    {
        var result = CopilotResponseIntegrityChecker.Evaluate($"{new string('a', 240)}\n\nThis starts __partial emphasis\ncontinued");

        Assert.True(result.IsLikelyTruncated);
        Assert.Equal("unbalanced-bold-marker", result.Reason);
    }

    [Fact]
    public void Evaluate_DetectsTrailingCommaRegardlessOfLineLength()
    {
        var text = new string('a', 240) + "\n\nshort,";

        var result = CopilotResponseIntegrityChecker.Evaluate(text);

        Assert.True(result.IsLikelyTruncated);
        Assert.Equal("missing-terminal-punctuation", result.Reason);
    }

    [Fact]
    public void Evaluate_TreatsPathGlobDoubleAsteriskAsComplete()
    {
        // **/ is a path glob, not an unmatched bold opener.
        var result = CopilotResponseIntegrityChecker.Evaluate(
            $"{new string('a', 240)}\n\nAdd files matching **/*.cs to the project.");

        Assert.False(result.IsLikelyTruncated);
    }

    [Fact]
    public void Evaluate_TreatsWindowsPathGlobAsComplete()
    {
        // **\ is also a glob separator (Windows path).
        var result = CopilotResponseIntegrityChecker.Evaluate(
            $"{new string('a', 240)}\n\nPattern: **\\*.cs");

        Assert.False(result.IsLikelyTruncated);
    }
}
