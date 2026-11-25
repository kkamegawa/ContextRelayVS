using System;
using System.Collections.Generic;
using ContextRelay.Core.Models;
using ContextRelay.Core.FileContext;
using ContextRelay.Core.Settings;
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

    public string? ContinuableCopilotAssistantItemId { get; set; }

    public string SearchSummary { get; set; } = string.Empty;

    public IReadOnlyList<string> WorkspaceFiles { get; set; } = Array.Empty<string>();

    public bool IsStreaming { get; set; }

    public string StreamingResponseText { get; set; } = string.Empty;

    public IReadOnlyList<ResolvedAttachment> PendingAttachments { get; set; } = Array.Empty<ResolvedAttachment>();

    /// <summary>Whether /embedded can run: enabled, Global cloud, and at least one valid source.</summary>
    public bool IsEmbeddedActive { get; set; }

    /// <summary>The enabled, valid Embedded sources in configured order, for the source selector.</summary>
    public IReadOnlyList<EmbeddedSourceSettings> EmbeddedSources { get; set; } = Array.Empty<EmbeddedSourceSettings>();

    /// <summary>The source the next /embedded request uses, or null when the user must choose.</summary>
    public string? SelectedEmbeddedContainerTypeId { get; set; }

    /// <summary>The last source chosen in this session; the selector highlights it first.</summary>
    public string? LastSelectedEmbeddedContainerTypeId { get; set; }

    /// <summary>The number of enabled Embedded entries skipped because they are invalid.</summary>
    public int EmbeddedInvalidSourceCount { get; set; }
}
