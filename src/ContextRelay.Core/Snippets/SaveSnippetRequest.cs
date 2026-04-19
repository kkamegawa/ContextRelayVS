using System.Collections.Generic;
using System.Text.Json;

namespace ContextRelay.Core.Snippets;

public sealed class SaveSnippetRequest
{
    public string? Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public SnippetSource Source { get; set; }

    public string Snippet { get; set; } = string.Empty;

    public string? SourceUrl { get; set; }

    public Dictionary<string, JsonElement> Metadata { get; set; } = new();
}
