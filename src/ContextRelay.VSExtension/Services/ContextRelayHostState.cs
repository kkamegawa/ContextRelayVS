using System;
using System.Collections.Generic;
using ContextRelay.Core.Models;
using ContextRelay.Core.SharedStore;

namespace ContextRelay.VSExtension.Services;

internal sealed class ContextRelayHostState
{
    public string QueryText { get; set; } = string.Empty;

    public string HelpText { get; set; } = "Type a query to search Microsoft 365 content.";

    public string StatusMessage { get; set; } = "ContextRelay is ready.";

    public string? SignedInUser { get; set; }

    public string? LastHandoffPath { get; set; }

    public IReadOnlyList<ContextItem> SearchResults { get; set; } = Array.Empty<ContextItem>();

    public IReadOnlyList<SharedSnippetItem> Snippets { get; set; } = Array.Empty<SharedSnippetItem>();

    public IReadOnlyList<SharedChatHistoryItem> ChatHistory { get; set; } = Array.Empty<SharedChatHistoryItem>();
}
