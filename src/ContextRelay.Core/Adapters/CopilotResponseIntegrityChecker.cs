using System;
using System.Text;
using System.Text.RegularExpressions;

namespace ContextRelay.Core.Adapters;

public static class CopilotResponseIntegrityChecker
{
    private const int MinimumSoftTruncationLength = 240;
    private const int MinimumTruncatedLineLength = 80;
    private static readonly Regex CodeFenceRegex = new(@"(^|\r?\n)```", RegexOptions.Compiled);
    private static readonly Regex IncompleteMarkdownLinkRegex = new(@"\[[^\]\r\n]+\]\([^)\r\n]*$", RegexOptions.Compiled);
    private static readonly Regex HeadingLineRegex = new(@"^#{1,6}\s+\S", RegexOptions.Compiled);
    private static readonly Regex ListLineRegex = new(@"^([-*+]\s|\d+[.)]\s)", RegexOptions.Compiled);
    private static readonly Regex ThematicBreakLineRegex = new(@"^\s{0,3}((\*\s*){3,}|(_\s*){3,}|(-\s*){3,})\s*$", RegexOptions.Compiled);
    private static readonly char[] TerminalCharacters =
    {
        '.', '!', '?', ':', ';', ')', ']', '}', '"', '\'', '`', '>', '|',
        '\u3002', '\uFF01', '\uFF1F', '\u300D', '\u300F', '\u201D'
    };

    public static CopilotResponseIntegrityResult Evaluate(string response)
    {
        if (response is null)
        {
            throw new ArgumentNullException(nameof(response));
        }

        var trimmed = response.Trim();
        if (trimmed.Length == 0)
        {
            return CopilotResponseIntegrityResult.Complete;
        }

        if (HasUnbalancedCodeFences(trimmed))
        {
            return CopilotResponseIntegrityResult.Truncated("unbalanced-code-fence");
        }

        if (IncompleteMarkdownLinkRegex.IsMatch(trimmed))
        {
            return CopilotResponseIntegrityResult.Truncated("incomplete-markdown-link");
        }

        if (HasIncompleteTableRow(trimmed))
        {
            return CopilotResponseIntegrityResult.Truncated("incomplete-markdown-table");
        }

        if (HasUnbalancedBoldMarker(trimmed))
        {
            return CopilotResponseIntegrityResult.Truncated("unbalanced-bold-marker");
        }

        if (trimmed.Length >= MinimumSoftTruncationLength &&
            HeadingLineRegex.IsMatch(GetLastNonEmptyLine(trimmed)))
        {
            return CopilotResponseIntegrityResult.Truncated("dangling-heading");
        }

        if (LooksSoftTruncated(trimmed))
        {
            return CopilotResponseIntegrityResult.Truncated("missing-terminal-punctuation");
        }

        return CopilotResponseIntegrityResult.Complete;
    }

    private static bool HasUnbalancedCodeFences(string value)
    {
        return CodeFenceRegex.Matches(value).Count % 2 != 0;
    }

    private static bool HasIncompleteTableRow(string value)
    {
        var lines = value.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        string? lastLine = null;
        for (var index = lines.Length - 1; index >= 0; index--)
        {
            var candidate = lines[index].Trim();
            if (candidate.Length == 0)
            {
                continue;
            }

            lastLine = candidate;
            break;
        }

        return lastLine is not null &&
            lastLine.StartsWith("|", StringComparison.Ordinal) &&
            !lastLine.EndsWith("|", StringComparison.Ordinal);
    }

    private static bool LooksSoftTruncated(string value)
    {
        if (value.Length < MinimumSoftTruncationLength)
        {
            return false;
        }

        var last = value[value.Length - 1];
        if (Array.IndexOf(TerminalCharacters, last) >= 0)
        {
            return false;
        }

        // A trailing comma almost never ends a complete response, so treat it
        // as truncated regardless of the shape of the final line.
        if (last == ',' || last == '\u3001')
        {
            return true;
        }

        if (!(char.IsLetterOrDigit(last) || last == '-'))
        {
            return false;
        }

        var lastLine = GetLastNonEmptyLine(value);
        if (ListLineRegex.IsMatch(lastLine))
        {
            return lastLine.Length >= MinimumTruncatedLineLength;
        }

        return lastLine.Length >= MinimumTruncatedLineLength;
    }

    private static string GetLastNonEmptyLine(string value)
    {
        var lines = value.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        for (var index = lines.Length - 1; index >= 0; index--)
        {
            var candidate = lines[index].Trim();
            if (candidate.Length > 0)
            {
                return candidate;
            }
        }

        return string.Empty;
    }

    private static bool HasUnbalancedBoldMarker(string value)
    {
        var textWithoutCode = RemoveThematicBreakLines(RemoveMarkdownCodeSections(value));
        return Regex.Matches(textWithoutCode, @"(?<!\\)\*\*").Count % 2 != 0 ||
            Regex.Matches(textWithoutCode, @"(?<![\\_])__(?!_)").Count % 2 != 0;
    }

    private static string RemoveThematicBreakLines(string value)
    {
        var builder = new StringBuilder(value.Length);
        var lines = value.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        foreach (var line in lines)
        {
            if (!ThematicBreakLineRegex.IsMatch(line))
            {
                builder.AppendLine(line);
            }
        }

        return builder.ToString();
    }

    private static string RemoveMarkdownCodeSections(string value)
    {
        var builder = new StringBuilder(value.Length);
        var insideFence = false;

        for (var index = 0; index < value.Length; index++)
        {
            if (IsFenceMarkerAt(value, index))
            {
                insideFence = !insideFence;
                index += 2;
                continue;
            }

            if (insideFence)
            {
                continue;
            }

            if (value[index] == '`')
            {
                var delimiterLength = CountBacktickRun(value, index);
                var closingIndex = FindMatchingBacktickRun(value, index + delimiterLength, delimiterLength);
                if (closingIndex >= 0)
                {
                    index = closingIndex + delimiterLength - 1;
                    continue;
                }
            }

            builder.Append(value[index]);
        }

        return builder.ToString();
    }

    private static bool IsFenceMarkerAt(string value, int index)
    {
        return index + 2 < value.Length &&
            value[index] == '`' &&
            value[index + 1] == '`' &&
            value[index + 2] == '`' &&
            (index == 0 || value[index - 1] == '\n' || value[index - 1] == '\r');
    }

    private static int CountBacktickRun(string value, int index)
    {
        var length = 0;
        while (index + length < value.Length && value[index + length] == '`')
        {
            length++;
        }

        return length;
    }

    private static int FindMatchingBacktickRun(string value, int startIndex, int delimiterLength)
    {
        for (var index = startIndex; index < value.Length; index++)
        {
            if (value[index] != '`')
            {
                continue;
            }

            var candidateLength = CountBacktickRun(value, index);
            if (candidateLength == delimiterLength)
            {
                return index;
            }

            index += candidateLength - 1;
        }

        return -1;
    }
}

public sealed class CopilotResponseIntegrityResult
{
    public static CopilotResponseIntegrityResult Complete { get; } = new(false, null);

    private CopilotResponseIntegrityResult(bool isLikelyTruncated, string? reason)
    {
        IsLikelyTruncated = isLikelyTruncated;
        Reason = reason;
    }

    public bool IsLikelyTruncated { get; }

    public string? Reason { get; }

    public static CopilotResponseIntegrityResult Truncated(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("Truncation reason must not be empty.", nameof(reason));
        }

        return new CopilotResponseIntegrityResult(true, reason);
    }
}
