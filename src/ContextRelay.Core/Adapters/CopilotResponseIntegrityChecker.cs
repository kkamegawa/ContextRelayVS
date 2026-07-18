using System;
using System.Text;
using System.Text.RegularExpressions;

namespace ContextRelay.Core.Adapters;

public static class CopilotResponseIntegrityChecker
{
    private const int MinimumSoftTruncationLength = 240;
    private const int MinimumTruncatedLineLength = 80;
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
        var insideFence = false;
        var fenceMarker = '\0';
        var fenceLength = 0;

        for (var index = 0; index < value.Length; index++)
        {
            if (!TryReadFenceMarker(value, index, out var marker, out var length, out var markerIndex))
            {
                continue;
            }

            if (!insideFence)
            {
                insideFence = true;
                fenceMarker = marker;
                fenceLength = length;
            }
            else if (marker == fenceMarker &&
                length >= fenceLength &&
                HasOnlyWhitespaceUntilLineEnd(value, markerIndex + length))
            {
                insideFence = false;
                fenceMarker = '\0';
                fenceLength = 0;
            }

            index += length - 1;
        }

        return insideFence;
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
        return HasUnbalancedStrongDelimiter(textWithoutCode, '*') ||
            HasUnbalancedStrongDelimiter(textWithoutCode, '_');
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
        var fenceMarker = '\0';
        var fenceLength = 0;

        for (var index = 0; index < value.Length; index++)
        {
            if (TryReadFenceMarker(value, index, out var marker, out var length, out var markerIndex))
            {
                if (!insideFence)
                {
                    insideFence = true;
                    fenceMarker = marker;
                    fenceLength = length;
                    index += length - 1;
                    continue;
                }

                if (marker == fenceMarker &&
                    length >= fenceLength &&
                    HasOnlyWhitespaceUntilLineEnd(value, markerIndex + length))
                {
                    insideFence = false;
                    fenceMarker = '\0';
                    fenceLength = 0;
                    index += length - 1;
                    continue;
                }
            }

            if (insideFence)
            {
                continue;
            }

            if (value[index] == '`')
            {
                var delimiterLength = CountRun(value, index, '`');
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

    private static bool HasUnbalancedStrongDelimiter(string value, char marker)
    {
        var openers = 0;

        for (var index = 0; index < value.Length; index++)
        {
            if (value[index] != marker || IsEscaped(value, index))
            {
                continue;
            }

            var runLength = CountRun(value, index, marker);
            if (runLength < 2)
            {
                index += runLength - 1;
                continue;
            }

            var previous = index == 0 ? '\0' : value[index - 1];
            var nextIndex = index + runLength;
            var next = nextIndex >= value.Length ? '\0' : value[nextIndex];
            if (char.IsLetterOrDigit(previous) && char.IsLetterOrDigit(next))
            {
                index += runLength - 1;
                continue;
            }

            var delimiterPairs = runLength / 2;
            var canOpen = CanOpenStrongDelimiter(previous, next, marker);
            var canClose = CanCloseStrongDelimiter(previous, next, marker);
            if (canOpen && canClose && openers == 0 && IsPunctuation(previous) && IsPunctuation(next))
            {
                index += runLength - 1;
                continue;
            }

            if (canClose)
            {
                var matched = Math.Min(openers, delimiterPairs);
                openers -= matched;
                delimiterPairs -= matched;
            }

            if (canOpen)
            {
                openers += delimiterPairs;
            }

            index += runLength - 1;
        }

        return openers > 0;
    }

    private static bool CanOpenStrongDelimiter(char previous, char next, char marker)
    {
        var leftFlanking = IsLeftFlanking(previous, next);
        var rightFlanking = IsRightFlanking(previous, next);
        if (marker == '_')
        {
            return leftFlanking && (!rightFlanking || IsPunctuation(previous));
        }

        return leftFlanking;
    }

    private static bool CanCloseStrongDelimiter(char previous, char next, char marker)
    {
        var leftFlanking = IsLeftFlanking(previous, next);
        var rightFlanking = IsRightFlanking(previous, next);
        if (marker == '_')
        {
            return rightFlanking && (!leftFlanking || IsPunctuation(next));
        }

        return rightFlanking;
    }

    private static bool IsLeftFlanking(char previous, char next)
    {
        return next != '\0' &&
            !char.IsWhiteSpace(next) &&
            (!IsPunctuation(next) || previous == '\0' || char.IsWhiteSpace(previous) || IsPunctuation(previous));
    }

    private static bool IsRightFlanking(char previous, char next)
    {
        return previous != '\0' &&
            !char.IsWhiteSpace(previous) &&
            (!IsPunctuation(previous) || next == '\0' || char.IsWhiteSpace(next) || IsPunctuation(next));
    }

    private static bool IsPunctuation(char value)
    {
        return value != '\0' && char.IsPunctuation(value);
    }

    private static bool IsEscaped(string value, int index)
    {
        var slashCount = 0;
        for (var cursor = index - 1; cursor >= 0 && value[cursor] == '\\'; cursor--)
        {
            slashCount++;
        }

        return slashCount % 2 != 0;
    }

    private static bool TryReadFenceMarker(string value, int index, out char marker, out int length, out int markerIndex)
    {
        marker = '\0';
        length = 0;
        markerIndex = -1;
        if (!IsLineStart(value, index))
        {
            return false;
        }

        var cursor = index;
        var spaces = 0;
        while (cursor < value.Length && value[cursor] == ' ' && spaces < 4)
        {
            cursor++;
            spaces++;
        }

        if (spaces > 3 || cursor >= value.Length || (value[cursor] != '`' && value[cursor] != '~'))
        {
            return false;
        }

        marker = value[cursor];
        markerIndex = cursor;
        length = CountRun(value, cursor, marker);
        return length >= 3;
    }

    private static bool HasOnlyWhitespaceUntilLineEnd(string value, int index)
    {
        for (var cursor = index; cursor < value.Length; cursor++)
        {
            var current = value[cursor];
            if (current == '\r' || current == '\n')
            {
                return true;
            }

            if (!char.IsWhiteSpace(current))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsLineStart(string value, int index)
    {
        return index == 0 || value[index - 1] == '\n' || value[index - 1] == '\r';
    }

    private static int CountRun(string value, int index, char marker)
    {
        var length = 0;
        while (index + length < value.Length && value[index + length] == marker)
        {
            length++;
        }

        return length;
    }

    private static int CountBacktickRun(string value, int index)
    {
        return CountRun(value, index, '`');
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
