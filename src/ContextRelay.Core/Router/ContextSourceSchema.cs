using System;
using System.Collections.Generic;
using ContextRelay.Core.Snippets;

namespace ContextRelay.Core.Router;

/// <summary>
/// Maps <see cref="ContextSource"/> to the lowercase source values used by the shared store,
/// the tool window view models, and source presentation. Every place that needs a source string
/// uses this mapping so that SharePoint Embedded round-trips as <c>sharepoint-embedded</c>.
/// </summary>
public static class ContextSourceSchema
{
    public const string SharePointEmbedded = "sharepoint-embedded";

    private static readonly IReadOnlyDictionary<ContextSource, string> SchemaValues =
        new Dictionary<ContextSource, string>
        {
            [ContextSource.Mail] = "mail",
            [ContextSource.Teams] = "teams",
            [ContextSource.SharePoint] = "sharepoint",
            [ContextSource.OneDrive] = "onedrive",
            [ContextSource.Connectors] = "connectors",
            [ContextSource.OneNote] = "onenote",
            [ContextSource.Planner] = "planner",
            [ContextSource.Todo] = "todo",
            [ContextSource.SharePointEmbedded] = SharePointEmbedded
        };

    private static readonly IReadOnlyDictionary<string, ContextSource> SourcesBySchemaValue = BuildReverseMap();

    public static string ToSchemaValue(ContextSource source)
    {
        return SchemaValues.TryGetValue(source, out var value)
            ? value
            : throw new ArgumentOutOfRangeException(nameof(source), source, "Unknown context source.");
    }

    public static bool TryParse(string? value, out ContextSource source)
    {
        if (!string.IsNullOrWhiteSpace(value) &&
            SourcesBySchemaValue.TryGetValue(value!.Trim(), out source))
        {
            return true;
        }

        source = default;
        return false;
    }

    public static SnippetSource ToSnippetSource(ContextSource source)
    {
        return source switch
        {
            ContextSource.Mail => SnippetSource.Mail,
            ContextSource.Teams => SnippetSource.Teams,
            ContextSource.SharePoint => SnippetSource.SharePoint,
            ContextSource.OneDrive => SnippetSource.OneDrive,
            ContextSource.Connectors => SnippetSource.Connectors,
            ContextSource.OneNote => SnippetSource.OneNote,
            ContextSource.Planner => SnippetSource.Planner,
            ContextSource.Todo => SnippetSource.Todo,
            ContextSource.SharePointEmbedded => SnippetSource.SharePointEmbedded,
            _ => throw new ArgumentOutOfRangeException(nameof(source), source, "Unknown context source.")
        };
    }

    private static IReadOnlyDictionary<string, ContextSource> BuildReverseMap()
    {
        var map = new Dictionary<string, ContextSource>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in SchemaValues)
        {
            map[pair.Value] = pair.Key;
        }

        return map;
    }
}
