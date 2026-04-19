using System.Collections.Generic;
using ContextRelay.Core.Router;

namespace ContextRelay.Core.Models;

public sealed class ContextItem
{
    public ContextSource Source { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Snippet { get; set; } = string.Empty;

    public string? Url { get; set; }

    public string? Timestamp { get; set; }

    public double? Relevance { get; set; }

    public ContextItemCacheInfo Cache { get; set; } = new();

    public Dictionary<string, string> Metadata { get; set; } = new();
}
