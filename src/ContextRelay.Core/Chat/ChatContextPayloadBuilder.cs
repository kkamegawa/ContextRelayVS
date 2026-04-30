using System;
using System.Collections.Generic;
using ContextRelay.Core.Adapters;
using ContextRelay.Core.SharedStore;

namespace ContextRelay.Core.Chat;

public static class ChatContextPayloadBuilder
{
    public const int MaxChatContextChars = 60000;

    public static ChatContextPayload Build(IReadOnlyList<SharedSnippetItem> snippets, string? searchSummary = null)
    {
        if (snippets is null)
        {
            throw new ArgumentNullException(nameof(snippets));
        }

        var additionalContext = new List<CopilotContextMessage>();
        var fileResources = new List<CopilotContextualFileResource>();
        var labels = new List<string>();
        var remainingBudget = MaxChatContextChars;

        foreach (var snippet in snippets)
        {
            if (IsFileContextSnippet(snippet))
            {
                fileResources.Add(new CopilotContextualFileResource { Uri = snippet.SourceUrl! });
                labels.Add(GetSnippetLabel(snippet));
                continue;
            }

            var source = string.IsNullOrWhiteSpace(snippet.SourceUrl)
                ? snippet.Source
                : $"{snippet.Source} ({snippet.SourceUrl})";
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

    private static bool IsFileContextSnippet(SharedSnippetItem snippet)
    {
        var sourceUrl = snippet.SourceUrl?.Trim();
        if (sourceUrl is null ||
            sourceUrl.Length == 0 ||
            !Uri.TryCreate(sourceUrl, UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return string.Equals(snippet.Source, "sharepoint", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(snippet.Source, "onedrive", StringComparison.OrdinalIgnoreCase);
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

        var suffix = $"\n[truncated {value.Length - budget} chars]";
        if (suffix.Length >= budget)
        {
            return suffix.Substring(0, budget);
        }

        return value.Substring(0, budget - suffix.Length) + suffix;
    }
}
