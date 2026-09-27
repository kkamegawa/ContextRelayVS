using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ContextRelay.Core.Adapters;
using ContextRelay.Core.FileContext;
using ContextRelay.Core.SharedStore;

namespace ContextRelay.Core.Chat;

public static class ChatContextPayloadBuilder
{
    public const int MaxChatContextChars = 60000;
    public const int MaxLocalAttachmentChars = 12000;

    public const string GroundingInstructionText =
        "Use the explicit attached files and pinned context as the primary sources for this request. " +
        "If they do not contain enough information, say so clearly.";

    /// <summary>
    /// Builds the common chat payload, reading local attachments only at send time.
    /// </summary>
    public static async Task<ChatContextPayload> BuildAsync(
        IReadOnlyList<ResolvedAttachment> attachments,
        IReadOnlyList<SharedSnippetItem> pinnedSnippets,
        string? searchSummary = null,
        CancellationToken cancellationToken = default)
    {
        if (attachments is null) throw new ArgumentNullException(nameof(attachments));
        if (pinnedSnippets is null) throw new ArgumentNullException(nameof(pinnedSnippets));

        var additionalContext = new List<CopilotContextMessage>();
        var fileResources = new List<CopilotContextualFileResource>();
        var labels = new List<string>();
        var includedAttachmentIds = new List<string>();
        var includedPinnedSnippetCount = 0;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var remaining = MaxChatContextChars;

        foreach (var attachment in attachments)
        {
            if (remaining <= 0)
            {
                break;
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (attachment is null || !seen.Add(Path.GetFullPath(attachment.AbsolutePath))) continue;
            var content = await WorkspaceFileAttachmentResolver.ReadTextAsync(attachment, cancellationToken).ConfigureAwait(false);
            if (content is null) continue;
            var body = $"[File: {attachment.RelativePath}]\n{(string.IsNullOrWhiteSpace(content) ? "(empty file)" : content)}";
            var contextCount = additionalContext.Count;
            AddTextContext(additionalContext, labels, attachment.Label, body, ref remaining);
            if (additionalContext.Count > contextCount)
            {
                includedAttachmentIds.Add(attachment.Id);
            }
        }

        foreach (var snippet in pinnedSnippets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var contextCount = additionalContext.Count;
            var fileResourceCount = fileResources.Count;
            if (TryGetFileContextUri(snippet, out var uri))
            {
                if (fileResources.TrueForAll(item => !string.Equals(item.Uri, uri, StringComparison.OrdinalIgnoreCase)))
                {
                    fileResources.Add(new CopilotContextualFileResource { Uri = uri });
                    labels.Add(GetSnippetLabel(snippet));
                }
            }
            else
            {
                AddSnippetText(additionalContext, labels, snippet, ref remaining);
            }

            if (additionalContext.Count > contextCount || fileResources.Count > fileResourceCount)
            {
                includedPinnedSnippetCount++;
            }
        }

        var grounded = additionalContext.Count > 0 || fileResources.Count > 0;
        // A search summary is useful orientation, but it is not explicit grounding.
        AddTextContext(additionalContext, labels, "Latest ContextRelay search summary", searchSummary ?? string.Empty, ref remaining);
        var options = new CopilotChatSendOptions
        {
            AdditionalContext = additionalContext,
            WebContext = grounded ? new CopilotWebContext { IsWebEnabled = false } : null
        };
        if (fileResources.Count > 0)
            options.ContextualResources = new CopilotContextualResources { Files = fileResources };
        return new ChatContextPayload
        {
            SendOptions = options,
            Labels = labels,
            IncludedAttachmentIds = includedAttachmentIds,
            IncludedPinnedSnippetCount = includedPinnedSnippetCount,
            HasGroundingContext = grounded,
            GroundingInstruction = grounded ? GroundingInstructionText : null
        };
    }

    public static ChatContextPayload Build(
        IReadOnlyList<SharedSnippetItem> snippets,
        string? searchSummary = null)
    {
        if (snippets is null)
        {
            throw new ArgumentNullException(nameof(snippets));
        }

        var additionalContext = new List<CopilotContextMessage>();
        var fileResources = new List<CopilotContextualFileResource>();
        var fileResourceUris = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var labels = new List<string>();
        var includedPinnedSnippetCount = 0;
        var remainingBudget = MaxChatContextChars;

        foreach (var snippet in snippets)
        {
            var contextCount = additionalContext.Count;
            var fileResourceCount = fileResources.Count;
            if (TryGetFileContextUri(snippet, out var fileContextUri))
            {
                AddFileResource(fileResources, fileResourceUris, labels, fileContextUri, GetSnippetLabel(snippet));
            }
            else
            {
                var sourceUrl = snippet.SourceUrl?.Trim();
                var source = string.IsNullOrWhiteSpace(sourceUrl)
                    ? snippet.Source
                    : $"{snippet.Source} ({sourceUrl})";
                var body = string.Join(
                    "\n",
                    $"Title: {GetSnippetLabel(snippet)}",
                    $"Source: {source}",
                    string.Empty,
                    snippet.Snippet ?? string.Empty);

                AddTextContext(additionalContext, labels, GetSnippetLabel(snippet), body, ref remainingBudget);
            }

            if (additionalContext.Count > contextCount || fileResources.Count > fileResourceCount)
            {
                includedPinnedSnippetCount++;
            }
        }

        var grounded = additionalContext.Count > 0 || fileResources.Count > 0;
        AddTextContext(
            additionalContext,
            labels,
            "Latest ContextRelay search summary",
            searchSummary ?? string.Empty,
            ref remainingBudget);

        var sendOptions = new CopilotChatSendOptions
        {
            AdditionalContext = additionalContext
        };

        if (fileResources.Count > 0)
        {
            sendOptions.ContextualResources = new CopilotContextualResources
            {
                Files = fileResources
            };
        }

        sendOptions.WebContext = grounded ? new CopilotWebContext { IsWebEnabled = false } : null;
        return new ChatContextPayload
        {
            SendOptions = sendOptions,
            Labels = labels,
            IncludedPinnedSnippetCount = includedPinnedSnippetCount,
            HasGroundingContext = grounded,
            GroundingInstruction = grounded ? GroundingInstructionText : null
        };
    }

    private static void AddSnippetText(
        ICollection<CopilotContextMessage> additionalContext,
        ICollection<string> labels,
        SharedSnippetItem snippet,
        ref int remainingBudget)
    {
        var sourceUrl = snippet.SourceUrl?.Trim();
        var source = string.IsNullOrWhiteSpace(sourceUrl) ? snippet.Source : $"{snippet.Source} ({sourceUrl})";
        AddTextContext(additionalContext, labels, GetSnippetLabel(snippet), string.Join("\n", $"Title: {GetSnippetLabel(snippet)}", $"Source: {source}", string.Empty, snippet.Snippet ?? string.Empty), ref remainingBudget);
    }

    private static bool TryGetFileContextUri(SharedSnippetItem snippet, out string normalizedUri)
    {
        normalizedUri = string.Empty;
        var sourceUrl = snippet.SourceUrl?.Trim();
        if (sourceUrl is null ||
            sourceUrl.Length == 0 ||
            !Uri.TryCreate(sourceUrl, UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.Equals(snippet.Source, "sharepoint", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(snippet.Source, "onedrive", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        normalizedUri = sourceUrl;
        return true;
    }

    private static void AddFileResource(
        ICollection<CopilotContextualFileResource> fileResources,
        ISet<string> fileResourceUris,
        ICollection<string> labels,
        string uri,
        string label)
    {
        var normalizedUri = uri.Trim();
        if (normalizedUri.Length == 0 || !fileResourceUris.Add(normalizedUri))
        {
            return;
        }

        fileResources.Add(new CopilotContextualFileResource { Uri = normalizedUri });
        labels.Add(label);
    }

    private static void AddTextContext(
        ICollection<CopilotContextMessage> additionalContext,
        ICollection<string> labels,
        string description,
        string text,
        ref int remainingBudget)
    {
        var trimmed = text.Trim();
        if (trimmed.Length == 0 || remainingBudget <= 0)
        {
            return;
        }

        var truncated = TruncateToBudget(trimmed, remainingBudget);
        if (truncated.Trim().Length == 0)
        {
            return;
        }

        additionalContext.Add(new CopilotContextMessage
        {
            Description = description,
            Text = truncated
        });
        labels.Add(description);
        remainingBudget -= truncated.Length;
    }

    private static string GetSnippetLabel(SharedSnippetItem snippet)
    {
        return string.IsNullOrWhiteSpace(snippet.Name) ? "Untitled snippet" : snippet.Name;
    }

    private static string TruncateToBudget(string value, int budget)
    {
        if (budget <= 0)
        {
            return string.Empty;
        }

        if (value.Length <= budget)
        {
            return value;
        }

        var omittedChars = value.Length - budget;
        while (true)
        {
            var suffix = $"\n[truncated {omittedChars} chars]";
            var retainedChars = budget - suffix.Length;
            if (retainedChars <= 0)
            {
                return suffix.Length <= budget
                    ? suffix
                    : suffix.Substring(0, budget);
            }

            var adjustedOmittedChars = value.Length - retainedChars;
            if (adjustedOmittedChars == omittedChars)
            {
                return value.Substring(0, retainedChars) + suffix;
            }

            omittedChars = adjustedOmittedChars;
        }
    }
}
