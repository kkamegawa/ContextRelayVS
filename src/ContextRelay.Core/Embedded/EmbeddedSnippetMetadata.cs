using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using ContextRelay.Core.Models;
using ContextRelay.Core.Router;
using ContextRelay.Core.SharedStore;

namespace ContextRelay.Core.Embedded;

/// <summary>
/// Builds and reads the SharePoint Embedded parts of pins and context text so that Chat, Ask, and
/// handoff all describe an Embedded result the same way.
/// </summary>
public static class EmbeddedSnippetMetadata
{
    public const string SourceDisplayName = "SharePoint Embedded";

    public static bool IsEmbedded(SharedSnippetItem snippet)
    {
        return snippet is not null &&
            string.Equals(snippet.Source, ContextSourceSchema.SharePointEmbedded, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The source line for context text, such as <c>SharePoint Embedded: Contoso Legal</c>.</summary>
    public static string GetSourceLabel(string? sourceName)
    {
        return string.IsNullOrWhiteSpace(sourceName)
            ? SourceDisplayName
            : $"{SourceDisplayName}: {sourceName!.Trim()}";
    }

    public static string GetSourceLabel(ContextItem item)
    {
        item.Metadata.TryGetValue(EmbeddedMetadataKeys.SourceName, out var sourceName);
        return GetSourceLabel(sourceName);
    }

    public static string GetSourceLabel(SharedSnippetItem snippet)
    {
        return GetSourceLabel(TryGetString(snippet, EmbeddedMetadataKeys.SourceName));
    }

    /// <summary>The source text for a pin: the Embedded label with its source name, or the schema value for other sources.</summary>
    public static string DescribeSource(SharedSnippetItem snippet)
    {
        return IsEmbedded(snippet) ? GetSourceLabel(snippet) : snippet.Source;
    }

    /// <summary>
    /// The text sent as context: every extract in response order, separated by blank lines.
    /// Falls back to the summary when the item has no extracts.
    /// </summary>
    public static string BuildContextText(ContextItem item)
    {
        if (item is null)
        {
            throw new ArgumentNullException(nameof(item));
        }

        var texts = item.Extracts
            .Select(extract => extract.Text?.Trim() ?? string.Empty)
            .Where(text => text.Length > 0)
            .ToArray();
        return texts.Length == 0 ? item.Snippet : string.Join("\n\n", texts);
    }

    /// <summary>
    /// Adds the Embedded metadata keys to a pin. <c>extracts</c> is a JSON array of
    /// <c>{ text, relevanceScore? }</c> objects.
    /// </summary>
    public static void AddPinMetadata(ContextItem item, IDictionary<string, JsonElement> metadata)
    {
        if (item is null)
        {
            throw new ArgumentNullException(nameof(item));
        }

        if (metadata is null)
        {
            throw new ArgumentNullException(nameof(metadata));
        }

        CopyString(item, metadata, EmbeddedMetadataKeys.SourceName);
        CopyString(item, metadata, EmbeddedMetadataKeys.ContainerTypeId);
        CopyString(item, metadata, EmbeddedMetadataKeys.SensitivityLabelDisplayName);
        CopyString(item, metadata, EmbeddedMetadataKeys.SensitivityLabelId);

        var extracts = item.Extracts
            .Select(extract => extract.RelevanceScore.HasValue
                ? (object)new { text = extract.Text, relevanceScore = extract.RelevanceScore.Value }
                : new { text = extract.Text })
            .ToArray();
        metadata[EmbeddedMetadataKeys.Extracts] = JsonSerializer.SerializeToElement(extracts);
    }

    /// <summary>Reads the extracts stored by <see cref="AddPinMetadata"/>; returns an empty list when absent or malformed.</summary>
    public static IReadOnlyList<ContextItemExtract> ReadExtracts(SharedSnippetItem snippet)
    {
        if (snippet?.Metadata is null ||
            !snippet.Metadata.TryGetValue(EmbeddedMetadataKeys.Extracts, out var element) ||
            element.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<ContextItemExtract>();
        }

        var extracts = new List<ContextItemExtract>();
        foreach (var entry in element.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object ||
                !entry.TryGetProperty("text", out var text) ||
                text.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            double? score = entry.TryGetProperty("relevanceScore", out var scoreElement) &&
                scoreElement.ValueKind == JsonValueKind.Number
                    ? scoreElement.GetDouble()
                    : null;
            extracts.Add(new ContextItemExtract { Text = text.GetString() ?? string.Empty, RelevanceScore = score });
        }

        return extracts;
    }

    public static string? TryGetString(SharedSnippetItem snippet, string key)
    {
        return snippet?.Metadata is not null &&
            snippet.Metadata.TryGetValue(key, out var element) &&
            element.ValueKind == JsonValueKind.String
                ? element.GetString()
                : null;
    }

    private static void CopyString(ContextItem item, IDictionary<string, JsonElement> metadata, string key)
    {
        if (item.Metadata.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
        {
            metadata[key] = JsonSerializer.SerializeToElement(value);
        }
    }
}
