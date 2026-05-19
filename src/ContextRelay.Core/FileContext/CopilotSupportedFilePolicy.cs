using System;
using System.Collections.Generic;
using System.IO;

namespace ContextRelay.Core.FileContext;

/// <summary>
/// Validates whether a local file extension is suitable for Copilot file context.
/// </summary>
public static class CopilotSupportedFilePolicy
{
    private static readonly ISet<string> SupportedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".markdown", ".rst",
        ".json", ".jsonc", ".yaml", ".yml", ".toml", ".ini", ".xml",
        ".js", ".mjs", ".cjs", ".ts", ".tsx", ".jsx",
        ".py", ".java", ".cs", ".cpp", ".c", ".h", ".hpp",
        ".go", ".rs", ".rb", ".php", ".swift", ".kt", ".kts",
        ".scala", ".sql", ".sh", ".ps1", ".psm1", ".psd1",
        ".html", ".htm", ".css", ".scss", ".less",
        ".vue", ".svelte",
        ".dockerfile", ".env",
        ".gitignore", ".gitattributes",
        ".csv"
    };

    private static readonly ISet<string> SupportedBasenames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "dockerfile",
        "makefile",
        "readme",
        "license"
    };

    /// <summary>
    /// Determines whether the specified file path can be referenced as local Copilot context.
    /// </summary>
    /// <param name="filePath">The candidate file path.</param>
    /// <returns><see langword="true"/> when the file extension or basename is supported.</returns>
    public static bool IsSupported(string filePath)
    {
        var basename = Path.GetFileName(filePath);
        if (string.IsNullOrWhiteSpace(basename))
        {
            return false;
        }

        if (SupportedBasenames.Contains(basename))
        {
            return true;
        }

        var extension = Path.GetExtension(filePath);
        if (string.IsNullOrEmpty(extension) && basename.StartsWith(".", StringComparison.Ordinal))
        {
            extension = basename;
        }

        return SupportedExtensions.Contains(extension);
    }
}
