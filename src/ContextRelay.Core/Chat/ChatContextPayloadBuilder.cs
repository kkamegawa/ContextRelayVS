using System;
using System.Collections.Generic;
using ContextRelay.Core.Adapters;
using ContextRelay.Core.FileContext;
using ContextRelay.Core.SharedStore;

namespace ContextRelay.Core.Chat;

public static class ChatContextPayloadBuilder
{
    public const int MaxChatContextChars = 60000;

    public static ChatContextPayload Build(
        IReadOnlyList<SharedSnippetItem> snippets,
        string? searchSummary = null,
        IReadOnlyList<ResolvedFileMention>? localFiles = null)
    {
        if (snippets is null)
        {
            throw new ArgumentNullException(nameof(snippets));
        }

        var additionalContext = new List<CopilotContextMessage>();
        var fileResources = new List<CopilotContextualFileResource>();
        var fileResourceUris = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var labels = new List<string>();
        var remainingBudget = MaxChatContextChars;

        foreach (var snippet in snippets)
        {
            if (TryGetFileContextUri(snippet, out var fileContextUri))
            {
                AddFileResource(fileResources, fileResourceUris, labels, fileContextUri, GetSnippetLabel(snippet));
                continue;
            }

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

        AddTextContext(
            additionalContext,
            labels,
            "Latest ContextRelay search summary",
            searchSummary ?? string.Empty,
            ref remainingBudget);

        foreach (var localFile in localFiles ?? Array.Empty<ResolvedFileMention>())
        {
            AddFileResource(
                fileResources,
                fileResourceUris,
                labels,
                localFile.Uri,
                $"Local file: {localFile.RelativePath}");
        }

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

        return new ChatContextPayload
        {
            SendOptions = sendOptions,
            Labels = labels
        };
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
