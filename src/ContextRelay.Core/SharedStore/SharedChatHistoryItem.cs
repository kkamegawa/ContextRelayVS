using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ContextRelay.Core.SharedStore;

public sealed class SharedChatHistoryItem
{
    public string Id { get; set; } = string.Empty;

    public string Timestamp { get; set; } = string.Empty;

    public string Role { get; set; } = string.Empty;

    public string Text { get; set; } = string.Empty;

    public Dictionary<string, JsonElement> Metadata { get; set; } = new();

    [JsonExtensionData]
    public Dictionary<string, JsonElement> ExtensionData { get; set; } = new();
}
