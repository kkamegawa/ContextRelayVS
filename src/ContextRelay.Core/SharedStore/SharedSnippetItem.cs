using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ContextRelay.Core.SharedStore;

public sealed class SharedSnippetItem
{
    public string Id { get; set; } = string.Empty;

    public string CreatedAt { get; set; } = string.Empty;

    public string UpdatedAt { get; set; } = string.Empty;

    public string? DeletedAt { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Source { get; set; } = string.Empty;

    public string? SourceUrl { get; set; }

    public string Snippet { get; set; } = string.Empty;

    public Dictionary<string, JsonElement> Metadata { get; set; } = new();

    [JsonExtensionData]
    public Dictionary<string, JsonElement> ExtensionData { get; set; } = new();
}
