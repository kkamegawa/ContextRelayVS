using System;
using System.Collections.Generic;
using System.Text;
using ContextRelay.Core.SharedStore;

namespace ContextRelay.Core.Handoff;

public static class AskPromptBuilder
{
    public const int MaxAskContextChars = 60000;

    public static string Build(string userPrompt, IReadOnlyList<SharedSnippetItem> snippets)
    {
        if (userPrompt is null)
        {
            throw new ArgumentNullException(nameof(userPrompt));
        }

        if (snippets is null)
        {
            throw new ArgumentNullException(nameof(snippets));
        }

        var contextBlocks = new StringBuilder();
        var remainingContextBudget = MaxAskContextChars;

        for (var index = 0; index < snippets.Count && remainingContextBudget > 0; index++)
        {
            var snippet = snippets[index];
            var body = (snippet.Snippet ?? string.Empty).Trim();
            var header = string.Join(
                "\n",
                $"### Pinned document {index + 1}: {GetSnippetTitle(snippet)}",
                $"Source: {snippet.Source}{(string.IsNullOrWhiteSpace(snippet.SourceUrl) ? string.Empty : $" - {snippet.SourceUrl}")}");
            var separator = contextBlocks.Length > 0 ? "\n\n" : string.Empty;
            var blockPrefix = $"{header}\n\n";
            var fixedCost = separator.Length + blockPrefix.Length;
            if (fixedCost >= remainingContextBudget)
            {
                break;
            }

            var truncated = TruncateToBudget(body, remainingContextBudget - fixedCost);
            contextBlocks.Append(separator);
            contextBlocks.Append(blockPrefix);
            contextBlocks.Append(truncated);
            remainingContextBudget -= separator.Length + blockPrefix.Length + truncated.Length;
        }

        return string.Join(
            "\n",
            "You are Microsoft 365 Copilot responding inside the ContextRelay extension.",
            "Use the pinned documents below as the primary context. Follow the user instruction exactly.",
            "If the user asks for a specific output format (markdown, JSON, HTML, etc.), produce only that format with no additional commentary.",
            string.Empty,
            "--- Pinned context ---",
            contextBlocks.ToString(),
            "--- End of pinned context ---",
            string.Empty,
            "User instruction:",
            userPrompt.Trim());
    }

    private static string GetSnippetTitle(SharedSnippetItem snippet)
    {
        return string.IsNullOrWhiteSpace(snippet.Name) ? "Untitled snippet" : snippet.Name;
    }

    private static string TruncateToBudget(string body, int budget)
    {
        if (budget <= 0)
        {
            return string.Empty;
        }

        if (body.Length <= budget)
        {
            return body;
        }

        for (var prefixLength = Math.Min(body.Length, budget); prefixLength > 0; prefixLength--)
        {
            var omittedChars = body.Length - prefixLength;
            var suffix = $"\n...[truncated {omittedChars} chars]";
            if (prefixLength + suffix.Length <= budget)
            {
                return $"{body.Substring(0, prefixLength)}{suffix}";
            }
        }

        var markerOnly = $"\n...[truncated {body.Length} chars]";
        return markerOnly.Length <= budget
            ? markerOnly
            : markerOnly.Substring(0, budget);
    }
}
