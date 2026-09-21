using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ContextRelay.Core.Tests.ToolWindows;

/// <summary>
/// Reads the display-name resource tokens that the extension manifest references and the
/// <c>string-resources.json</c> files that define them.
/// </summary>
internal static class ContextRelayResourceTokens
{
    private static readonly Regex TokenPattern = new("%(ContextRelay\\.[A-Za-z0-9_.]+)%", RegexOptions.Compiled);

    /// <summary>
    /// Finds every <c>%ContextRelay.*%</c> token in manifest text.
    /// </summary>
    /// <param name="manifestText">The extension manifest JSON text.</param>
    /// <returns>The distinct token keys, without the surrounding percent signs.</returns>
    public static IReadOnlyList<string> FindTokens(string manifestText)
    {
        return TokenPattern.Matches(manifestText)
            .Select(match => match.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>
    /// Reads a flat <c>string-resources.json</c> file into a key/value map.
    /// </summary>
    /// <param name="path">The resource file path.</param>
    /// <returns>The keys mapped to their display text.</returns>
    public static IReadOnlyDictionary<string, string> ReadResourceMap(string path)
    {
        return Parse(File.ReadAllText(path));
    }

    /// <summary>
    /// Reads a flat <c>string-resources.json</c> entry from a VSIX archive into a key/value map.
    /// </summary>
    /// <param name="entry">The archive entry that holds the resource file.</param>
    /// <returns>The keys mapped to their display text.</returns>
    public static IReadOnlyDictionary<string, string> ReadResourceMap(ZipArchiveEntry entry)
    {
        using var reader = new StreamReader(entry.Open());
        return Parse(reader.ReadToEnd());
    }

    private static IReadOnlyDictionary<string, string> Parse(string json)
    {
        using var document = JsonDocument.Parse(json.TrimStart('﻿'));
        return document.RootElement.EnumerateObject()
            .ToDictionary(property => property.Name, property => property.Value.GetString() ?? string.Empty, StringComparer.Ordinal);
    }
}
