using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ContextRelay.Core.SharedStore;

public sealed class SharedStoreSchemaMetadata
{
    public int SchemaVersion { get; set; } = SharedStoreOptions.CurrentSchemaVersion;

    public string UpdatedAt { get; set; } = string.Empty;

    public string UpdatedBy { get; set; } = string.Empty;

    public string ProducerVersion { get; set; } = string.Empty;

    public List<string> Files { get; set; } = new();

    [JsonExtensionData]
    public Dictionary<string, JsonElement> ExtensionData { get; set; } = new();
}
