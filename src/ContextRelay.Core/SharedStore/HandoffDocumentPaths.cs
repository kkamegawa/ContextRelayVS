using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ContextRelay.Core.SharedStore;

public sealed class HandoffDocumentPaths
{
    public string? Plan { get; set; }

    public string? Tasks { get; set; }

    public string? TestPlan { get; set; }

    public string? Handoff { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement> ExtensionData { get; set; } = new();
}
