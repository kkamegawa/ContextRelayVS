using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace ContextRelay.Core.FileContext;

/// <summary>
/// Builds bounded text prompts from local file mentions for services that cannot consume file resources.
/// </summary>
public static class FileContextPromptBuilder
{
    /// <summary>
    /// Gets the total local file context budget for Work IQ prompts.
    /// </summary>
    public const int MaxWorkIqFileContextChars = 12000;

    /// <summary>
    /// Gets the per-file local context budget for Work IQ prompts.
    /// </summary>
    public const int MaxWorkIqFileChars = 4000;

    /// <summary>
    /// Appends local file sections to a Work IQ prompt.
    /// </summary>
    /// <param name="prompt">The cleaned user prompt.</param>
    /// <param name="files">The resolved local files.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The prompt with bounded local file context.</returns>
    public static async Task<string> BuildWorkIqPromptAsync(
        string prompt,
        IReadOnlyList<ResolvedFileMention> files,
        CancellationToken cancellationToken = default)
    {
        if (files is null || files.Count == 0)
        {
            return prompt;
        }

        var remainingBudget = MaxWorkIqFileContextChars;
        var sections = new List<string>();
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var separatorLength = sections.Count == 0 ? 0 : 2;
            var sectionBudget = remainingBudget - separatorLength;
            if (sectionBudget <= 0)
            {
                break;
            }

            var content = NormalizeExtractedText(await ReadAllTextAsync(file.AbsolutePath, cancellationToken).ConfigureAwait(false));
            var section = BuildFileSection(
                file.RelativePath,
                string.IsNullOrWhiteSpace(content) ? "(empty file)" : content,
                sectionBudget);
            if (string.IsNullOrWhiteSpace(section))
            {
                continue;
            }

            sections.Add(section);
            remainingBudget -= section.Length + separatorLength;
        }

        if (sections.Count == 0)
        {
            return prompt;
        }

        return $"{prompt}\n\nContextRelay local file context:\n{string.Join("\n\n", sections)}";
    }

    /// <summary>
    /// Normalizes text extracted from local files.
    /// </summary>
    /// <param name="value">The file text.</param>
    /// <returns>The normalized text.</returns>
    public static string NormalizeExtractedText(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var normalized = value
            .Replace("\uFEFF", string.Empty)
            .Replace("\r\n", "\n")
            .Replace('\r', '\n');
        return Regex.Replace(normalized, "\n{3,}", "\n\n").Trim();
    }

    private static async Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var buffer = new char[MaxWorkIqFileChars * 4];
        var read = await reader.ReadBlockAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
        var text = new string(buffer, 0, read);
        if (!reader.EndOfStream)
        {
            text += "\n[additional file content omitted]";
        }

        cancellationToken.ThrowIfCancellationRequested();
        return text;
    }

    private static string TruncateForBudget(string value, int budget)
    {
        if (budget <= 0)
        {
            return string.Empty;
        }

        if (value.Length <= budget)
        {
            return value;
        }

        var omittedChars = value.Length - budget;
        while (true)
        {
            var suffix = $"\n[truncated {omittedChars} chars]";
            var retainedChars = budget - suffix.Length;
            if (retainedChars <= 0)
            {
                return suffix.Length <= budget ? suffix : suffix.Substring(0, budget);
            }

            var adjustedOmittedChars = value.Length - retainedChars;
            if (adjustedOmittedChars == omittedChars)
            {
                return value.Substring(0, retainedChars) + suffix;
            }

            omittedChars = adjustedOmittedChars;
        }
    }

    private static string BuildFileSection(string relativePath, string content, int sectionBudget)
    {
        var header = $"[File: {relativePath}]\n";
        if (sectionBudget <= header.Length)
        {
            return string.Empty;
        }

        var contentBudget = Math.Min(MaxWorkIqFileChars, sectionBudget - header.Length);
        var bounded = TruncateForBudget(content, contentBudget);
        if (string.IsNullOrWhiteSpace(bounded))
        {
            return string.Empty;
        }

        return header + bounded;
    }
}
