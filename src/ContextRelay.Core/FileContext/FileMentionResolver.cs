using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace ContextRelay.Core.FileContext;

/// <summary>
/// Parses and resolves <c>#file</c> mentions against trusted workspace roots.
/// </summary>
public static class FileMentionResolver
{
    /// <summary>
    /// Gets the maximum number of file mentions allowed in a single prompt.
    /// </summary>
    public const int MaxFileMentions = 5;

    private static readonly Regex MentionPattern = new(
        "(^|\\s)#(?:\"([^\"]+)\"|'([^']+)'|([^\\s#]+))",
        RegexOptions.Compiled);

    private static readonly Regex OnlyDigitsPattern = new("^\\d+$", RegexOptions.Compiled);

    /// <summary>
    /// Extracts candidate <c>#file</c> mention tokens from the prompt.
    /// </summary>
    /// <param name="input">The raw prompt text.</param>
    /// <returns>The mention candidates.</returns>
    public static IReadOnlyList<FileMentionCandidate> ExtractCandidates(string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return Array.Empty<FileMentionCandidate>();
        }

        var candidates = new List<FileMentionCandidate>();
        foreach (Match match in MentionPattern.Matches(input))
        {
            var rawPath = (match.Groups[2].Success ? match.Groups[2].Value :
                    match.Groups[3].Success ? match.Groups[3].Value :
                    match.Groups[4].Success ? match.Groups[4].Value : string.Empty)
                .Trim();
            if (rawPath.Length == 0 || OnlyDigitsPattern.IsMatch(rawPath))
            {
                continue;
            }

            var prefixLength = match.Groups[1].Value.Length;
            candidates.Add(new FileMentionCandidate
            {
                RawPath = rawPath,
                RemoveStart = match.Index + prefixLength,
                RemoveEnd = match.Index + match.Length
            });
        }

        return candidates;
    }

    /// <summary>
    /// Resolves file mention tokens against the provided workspace roots.
    /// </summary>
    /// <param name="input">The raw prompt text.</param>
    /// <param name="workspaceRoots">Trusted workspace root directories.</param>
    /// <returns>The cleaned prompt, resolved files, and errors.</returns>
    public static FileMentionResolutionResult Resolve(string input, IReadOnlyList<string> workspaceRoots)
    {
        var candidates = ExtractCandidates(input ?? string.Empty);
        if (candidates.Count == 0)
        {
            return new FileMentionResolutionResult
            {
                CleanedPrompt = (input ?? string.Empty).Trim()
            };
        }

        var cleanedPrompt = StripMentionTokens(input ?? string.Empty, candidates);
        var normalizedRoots = NormalizeWorkspaceRoots(workspaceRoots);
        if (normalizedRoots.Count == 0)
        {
            return new FileMentionResolutionResult
            {
                CleanedPrompt = cleanedPrompt,
                Errors = new[] { "# file mentions require an opened solution or folder." }
            };
        }

        if (candidates.Count > MaxFileMentions)
        {
            return new FileMentionResolutionResult
            {
                CleanedPrompt = cleanedPrompt,
                Errors = new[] { $"You can reference up to {MaxFileMentions} files per message." }
            };
        }

        var files = new List<ResolvedFileMention>();
        var errors = new List<string>();
        var seenUris = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in candidates)
        {
            var resolved = ResolveSingleMention(candidate.RawPath, normalizedRoots, out var error);
            if (resolved is null)
            {
                errors.Add(error ?? $"File not found for #{candidate.RawPath}.");
                continue;
            }

            if (seenUris.Add(resolved.Uri))
            {
                files.Add(resolved);
            }
        }

        return new FileMentionResolutionResult
        {
            CleanedPrompt = cleanedPrompt,
            Files = files,
            Errors = errors
        };
    }

    private static ResolvedFileMention? ResolveSingleMention(
        string rawPath,
        IReadOnlyList<string> workspaceRoots,
        out string? error)
    {
        var matches = new List<ResolvedFileMention>();
        var isRooted = Path.IsPathRooted(rawPath);
        if (isRooted)
        {
            var candidate = ResolveCandidatePath(Path.GetFullPath(rawPath), workspaceRoots, preferredRoot: null, rawPath, out error);
            if (candidate is null)
            {
                return null;
            }

            matches.Add(candidate);
        }
        else
        {
            foreach (var root in workspaceRoots)
            {
                var candidatePath = Path.GetFullPath(Path.Combine(root, rawPath));
                var candidate = ResolveCandidatePath(candidatePath, workspaceRoots, root, rawPath, out _);
                if (candidate is not null)
                {
                    matches.Add(candidate);
                }
            }
        }

        if (matches.Count == 0)
        {
            error = $"File not found for #{rawPath}.";
            return null;
        }

        if (!isRooted && matches.Count > 1)
        {
            error = $"File path \"#{rawPath}\" is ambiguous across workspace roots. Use a unique path.";
            return null;
        }

        var selected = matches[0];
        if (!CopilotSupportedFilePolicy.IsSupported(selected.AbsolutePath))
        {
            error = $"Unsupported file type for #{rawPath}. Only Copilot-supported file extensions are allowed.";
            return null;
        }

        error = null;
        return selected;
    }

    private static ResolvedFileMention? ResolveCandidatePath(
        string candidatePath,
        IReadOnlyList<string> workspaceRoots,
        string? preferredRoot,
        string rawPath,
        out string? error)
    {
        if (!File.Exists(candidatePath))
        {
            error = $"File not found for #{rawPath}.";
            return null;
        }

        var canonicalPath = Path.GetFullPath(candidatePath);
        var root = preferredRoot is null || !IsPathWithinRoot(canonicalPath, preferredRoot)
            ? workspaceRoots.FirstOrDefault(workspaceRoot => IsPathWithinRoot(canonicalPath, workspaceRoot))
            : preferredRoot;
        if (root is null)
        {
            error = $"File #{rawPath} is outside the opened workspace.";
            return null;
        }

        error = null;
        return new ResolvedFileMention
        {
            AbsolutePath = canonicalPath,
            WorkspaceRoot = root,
            RelativePath = GetRelativePath(root, canonicalPath),
            Uri = new Uri(canonicalPath).AbsoluteUri
        };
    }

    private static IReadOnlyList<string> NormalizeWorkspaceRoots(IReadOnlyList<string>? workspaceRoots)
    {
        if (workspaceRoots is null || workspaceRoots.Count == 0)
        {
            return Array.Empty<string>();
        }

        return workspaceRoots
            .Where(root => !string.IsNullOrWhiteSpace(root))
            .Select(root => TrimTrailingSeparators(Path.GetFullPath(root)))
            .Where(Directory.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool IsPathWithinRoot(string candidatePath, string workspaceRoot)
    {
        var normalizedRoot = TrimTrailingSeparators(Path.GetFullPath(workspaceRoot));
        var normalizedCandidate = Path.GetFullPath(candidatePath);
        if (string.Equals(normalizedCandidate, normalizedRoot, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var rootWithSeparator = normalizedRoot + Path.DirectorySeparatorChar;
        return normalizedCandidate.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase);
    }

    private static string GetRelativePath(string root, string absolutePath)
    {
        var normalizedRoot = TrimTrailingSeparators(Path.GetFullPath(root));
        var normalizedPath = Path.GetFullPath(absolutePath);
        var rootWithSeparator = normalizedRoot + Path.DirectorySeparatorChar;
        var relative = normalizedPath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase)
            ? normalizedPath.Substring(rootWithSeparator.Length)
            : Path.GetFileName(normalizedPath);
        return relative.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');
    }

    private static string TrimTrailingSeparators(string value)
    {
        return value.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static string StripMentionTokens(string input, IReadOnlyList<FileMentionCandidate> candidates)
    {
        if (candidates.Count == 0)
        {
            return input.Trim();
        }

        var sorted = candidates.OrderBy(candidate => candidate.RemoveStart).ToArray();
        var result = new System.Text.StringBuilder(input.Length);
        var cursor = 0;
        foreach (var candidate in sorted)
        {
            if (candidate.RemoveStart > cursor)
            {
                result.Append(input.Substring(cursor, candidate.RemoveStart - cursor));
            }

            cursor = candidate.RemoveEnd;
        }

        if (cursor < input.Length)
        {
            result.Append(input.Substring(cursor));
        }

        return Regex.Replace(result.ToString(), "[ \\t]{2,}", " ")
            .Replace(" \r\n", "\r\n")
            .Replace(" \n", "\n")
            .Trim();
    }
}
