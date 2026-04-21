using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace ContextRelay.Core.Handoff;

public static class AskPreviewLanguageDetector
{
    private static readonly IReadOnlyList<LanguageEntry> KeywordLanguages = new[]
    {
        new LanguageEntry("markdown", new[] { "markdown", "md format", "md file", ".md" }),
        new LanguageEntry("json", new[] { "json" }),
        new LanguageEntry("yaml", new[] { "yaml", "yml" }),
        new LanguageEntry("html", new[] { "html" }),
        new LanguageEntry("xml", new[] { "xml" }),
        new LanguageEntry("csv", new[] { "csv" }),
        new LanguageEntry("typescript", new[] { "typescript", "tsx" }),
        new LanguageEntry("javascript", new[] { "javascript", "jsx" }),
        new LanguageEntry("python", new[] { "python" }),
        new LanguageEntry("sql", new[] { "sql" }),
        new LanguageEntry("shellscript", new[] { "bash", "shell", "sh script" }),
        new LanguageEntry("powershell", new[] { "powershell", "ps1" })
    };

    private static readonly IReadOnlyDictionary<string, string> FenceAliases =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["md"] = "markdown",
            ["markdown"] = "markdown",
            ["json"] = "json",
            ["yaml"] = "yaml",
            ["yml"] = "yaml",
            ["html"] = "html",
            ["htm"] = "html",
            ["xml"] = "xml",
            ["csv"] = "csv",
            ["ts"] = "typescript",
            ["tsx"] = "typescript",
            ["typescript"] = "typescript",
            ["js"] = "javascript",
            ["jsx"] = "javascript",
            ["javascript"] = "javascript",
            ["py"] = "python",
            ["python"] = "python",
            ["sql"] = "sql",
            ["sh"] = "shellscript",
            ["bash"] = "shellscript",
            ["shell"] = "shellscript",
            ["ps1"] = "powershell",
            ["powershell"] = "powershell",
            ["txt"] = "plaintext",
            ["text"] = "plaintext"
        };

    public static AskPreviewDocument Detect(string prompt, string response)
    {
        if (prompt is null)
        {
            throw new ArgumentNullException(nameof(prompt));
        }

        if (response is null)
        {
            throw new ArgumentNullException(nameof(response));
        }

        var wrapped = StripSingleWrappingFence(response);
        if (wrapped is not null)
        {
            return wrapped;
        }

        var dominantFence = FindDominantFenceLanguage(response);
        if (!string.IsNullOrWhiteSpace(dominantFence))
        {
            return new AskPreviewDocument(dominantFence!, response);
        }

        var fromPrompt = FindLanguageFromPrompt(prompt);
        if (!string.IsNullOrWhiteSpace(fromPrompt))
        {
            return new AskPreviewDocument(fromPrompt!, response);
        }

        return new AskPreviewDocument("markdown", response);
    }

    public static string GetFileExtension(string languageId)
    {
        if (string.IsNullOrWhiteSpace(languageId))
        {
            return "md";
        }

        return NormalizeFenceLanguage(languageId) switch
        {
            "markdown" => "md",
            "json" => "json",
            "yaml" => "yml",
            "html" => "html",
            "xml" => "xml",
            "csv" => "csv",
            "typescript" => "ts",
            "javascript" => "js",
            "python" => "py",
            "sql" => "sql",
            "shellscript" => "sh",
            "powershell" => "ps1",
            "plaintext" => "txt",
            _ => "md"
        };
    }

    private static AskPreviewDocument? StripSingleWrappingFence(string response)
    {
        var trimmed = response.Trim();
        var match = Regex.Match(trimmed, "^```([^\\r\\n`]*)\\r?\\n([\\s\\S]*?)\\r?\\n```$");
        if (!match.Success)
        {
            return null;
        }

        var inner = match.Groups[2].Value;
        if (inner.Contains("```", StringComparison.Ordinal))
        {
            return null;
        }

        var language = NormalizeFenceLanguage(match.Groups[1].Value) ?? "markdown";
        return new AskPreviewDocument(language, inner);
    }

    private static string? FindDominantFenceLanguage(string response)
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in Regex.Matches(response, "```([^\\r\\n`]*)\\r?\\n"))
        {
            var language = NormalizeFenceLanguage(match.Groups[1].Value);
            if (string.IsNullOrWhiteSpace(language))
            {
                continue;
            }

            counts[language!] = counts.TryGetValue(language!, out var count) ? count + 1 : 1;
        }

        return counts.Count == 0
            ? null
            : counts.OrderByDescending(pair => pair.Value).First().Key;
    }

    private static string? FindLanguageFromPrompt(string prompt)
    {
        var lower = prompt.ToLowerInvariant();
        foreach (var entry in KeywordLanguages)
        {
            if (entry.Keywords.Any(keyword => lower.Contains(keyword)))
            {
                return entry.LanguageId;
            }
        }

        return null;
    }

    private static string? NormalizeFenceLanguage(string raw)
    {
        var key = (raw ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        return FenceAliases.TryGetValue(key, out var normalized) ? normalized : key.ToLowerInvariant();
    }

    private sealed class LanguageEntry
    {
        public LanguageEntry(string languageId, IReadOnlyList<string> keywords)
        {
            LanguageId = languageId;
            Keywords = keywords;
        }

        public string LanguageId { get; }

        public IReadOnlyList<string> Keywords { get; }
    }
}

public sealed class AskPreviewDocument
{
    public AskPreviewDocument(string languageId, string content)
    {
        LanguageId = languageId ?? throw new ArgumentNullException(nameof(languageId));
        Content = content ?? throw new ArgumentNullException(nameof(content));
    }

    public string LanguageId { get; }

    public string Content { get; }
}
