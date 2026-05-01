using System.Collections.Generic;
using System.Linq;
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

    [JsonIgnore]
    public bool IsActionableAssistant =>
        string.Equals(Role, "assistant", System.StringComparison.OrdinalIgnoreCase) &&
        Metadata.TryGetValue("kind", out var kind) &&
        kind.ValueKind == JsonValueKind.String &&
        (string.Equals(kind.GetString(), "chat", System.StringComparison.OrdinalIgnoreCase) ||
            string.Equals(kind.GetString(), "ask", System.StringComparison.OrdinalIgnoreCase) ||
            string.Equals(kind.GetString(), "workiq", System.StringComparison.OrdinalIgnoreCase));

    [JsonIgnore]
    public IReadOnlyList<string> ContextLabels => GetContextLabels();

    [JsonIgnore]
    public bool HasContextLabels => ContextLabels.Count > 0;

    [JsonIgnore]
    public string ContextLabelsJoinedDisplay => string.Join(", ", ContextLabels);

    private string[] GetContextLabels()
    {
        if (!Metadata.TryGetValue("contextLabels", out var labels) ||
            labels.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return labels
            .EnumerateArray()
            .Where(label => label.ValueKind == JsonValueKind.String)
            .Select(label => label.GetString())
            .Where(label => !string.IsNullOrWhiteSpace(label))
            .ToArray()!;
    }
}
