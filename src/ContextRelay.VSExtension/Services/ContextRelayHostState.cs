using System;
using System.Collections.Generic;
using ContextRelay.Core.Models;
using ContextRelay.Core.SharedStore;
using ContextRelay.VSExtension.ToolWindows;

namespace ContextRelay.VSExtension.Services;

internal sealed class ContextRelayHostState
{
    public string QueryText { get; set; } = string.Empty;

    public string HelpText { get; set; } = ContextRelayLocalizedStrings.GenericHelpText;

    public string StatusMessage { get; set; } = ContextRelayLocalizedStrings.ReadyStatus;

    public string? SignedInUser { get; set; }

    public string? LastHandoffPath { get; set; }

    public IReadOnlyList<ContextItem> SearchResults { get; set; } = Array.Empty<ContextItem>();

    public IReadOnlyList<SharedSnippetItem> Snippets { get; set; } = Array.Empty<SharedSnippetItem>();

    public IReadOnlyList<SharedChatHistoryItem> ChatHistory { get; set; } = Array.Empty<SharedChatHistoryItem>();

    public string SearchSummary { get; set; } = string.Empty;

    public IReadOnlyList<string> WorkspaceFiles { get; set; } = Array.Empty<string>();
}
