using System;
using System.Collections.Generic;
using ContextRelay.Core.SharedStore;

namespace ContextRelay.Core.Handoff;

public sealed class HandoffContext
{
    public IReadOnlyList<SharedSnippetItem> Snippets { get; set; } = Array.Empty<SharedSnippetItem>();

    public string? SearchSummary { get; set; }
}
