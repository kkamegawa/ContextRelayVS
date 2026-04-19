using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ContextRelay.Core.SharedStore;

public sealed class SharedHandoffIndexItem
{
    public string WorkspaceRoot { get; set; } = string.Empty;

    public string UpdatedAt { get; set; } = string.Empty;

    public HandoffDocumentPaths Docs { get; set; } = new();

    [JsonExtensionData]
    public Dictionary<string, JsonElement> ExtensionData { get; set; } = new();
}
