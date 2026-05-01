using System;
using System.Collections.Generic;

namespace ContextRelay.Core.Router;

public static class SlashCommandRouter
{
    private static readonly IReadOnlyList<ContextSource> SearchAllSources = new[]
    {
        ContextSource.SharePoint,
        ContextSource.OneDrive,
        ContextSource.Mail,
        ContextSource.Teams,
        ContextSource.OneNote,
        ContextSource.Planner,
        ContextSource.Todo
    };

    private static readonly IReadOnlyList<string> SupportedSlashCommands = new[]
    {
        "/mail",
        "/teams",
        "/sharepoint",
        "/onedrive",
        "/onenote",
        "/task",
        "/connectors",
        "/all",
        "/ask",
        "/workiq",
        "/clear"
    };

    private static readonly IReadOnlyDictionary<string, RouteTarget> SlashCommands =
        new Dictionary<string, RouteTarget>(StringComparer.OrdinalIgnoreCase)
        {
            ["mail"] = RouteTarget.Mail,
            ["teams"] = RouteTarget.Teams,
            ["sharepoint"] = RouteTarget.SharePoint,
            ["onedrive"] = RouteTarget.OneDrive,
            ["onenote"] = RouteTarget.OneNote,
            ["task"] = RouteTarget.Task,
            ["connectors"] = RouteTarget.Connectors,
            ["all"] = RouteTarget.All,
            ["ask"] = RouteTarget.Ask,
            ["workiq"] = RouteTarget.WorkIq,
            ["clear"] = RouteTarget.Clear
        };

    private static readonly IReadOnlyDictionary<string, string> HelpText =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["mail"] = "Example: /mail from:alice subject:budget\nExample: /mail incident review",
            ["teams"] = "Example: /teams sprint review\nExample: /teams from:bob mentions:me",
            ["sharepoint"] = "Example: /sharepoint VPN setup guide\nExample: /sharepoint architecture",
            ["onedrive"] = "Example: /onedrive architecture diagram\nExample: /onedrive Q3 report",
            ["onenote"] = "Example: /onenote architecture decision log\nExample: /onenote section notebook architecture",
            ["task"] = "Example: /task release checklist\nExample: /task metadata comments onboarding",
            ["connectors"] = "Example: /connectors incident tracker\nExample: /connectors external knowledge base",
            ["all"] = "Example: /all architecture decisions\nPlain text without a slash command starts or continues Microsoft 365 Copilot chat.",
            ["ask"] = "Example: /ask 日本語に翻訳してmarkdownにして\nExample: /ask Summarize the pinned docs as a bullet list\nPinned snippets are used as context and the Microsoft 365 Copilot response is shown in the panel.",
            ["workiq"] = "Example: /workiq Summarize my recent emails from Alice\nExample: /workiq What meetings do I have today?\nSends a natural language query to the Work IQ Gateway (A2A protocol). Requires Microsoft 365 Copilot license.",
            ["clear"] = "Example: /clear\nClears the current chat transcript and discards all pinned snippets."
        };

    public static SlashCommandParseResult Parse(string input)
    {
        var trimmed = input?.Trim() ?? string.Empty;
        var normalized = NormalizeQuery(trimmed);

        if (trimmed.StartsWith("/", StringComparison.Ordinal))
        {
            var commandEnd = FindCommandEnd(trimmed);
            var command = trimmed.Substring(1, commandEnd - 1);
            var rawQuery = commandEnd >= trimmed.Length ? string.Empty : trimmed.Substring(commandEnd).TrimStart();

            if (SlashCommands.TryGetValue(command, out var target))
            {
                var query = target == RouteTarget.Ask || target == RouteTarget.WorkIq
                    ? rawQuery.Trim()
                    : target == RouteTarget.Clear
                        ? string.Empty
                        : NormalizeQuery(rawQuery);

                return new SlashCommandParseResult
                {
                    Target = target,
                    Query = query,
                    IsEmpty = target == RouteTarget.Clear ? false : query.Length == 0,
                    SlashCommandName = $"/{command.ToLowerInvariant()}",
                    TargetSources = GetTargetSources(target)
                };
            }
        }

        return new SlashCommandParseResult
        {
            Target = RouteTarget.Chat,
            Query = normalized,
            IsEmpty = normalized.Length == 0,
            TargetSources = Array.Empty<ContextSource>()
        };
    }

    public static string GetHelpText(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return "Type a message to chat with Microsoft 365 Copilot, or use /all to search Microsoft 365 content.";
        }

        var normalized = command.StartsWith("/", StringComparison.Ordinal) ? command.Substring(1) : command;
        return HelpText.TryGetValue(normalized, out var help)
            ? help
            : "Type a message to chat with Microsoft 365 Copilot, or use /all to search Microsoft 365 content.";
    }

    public static IReadOnlyList<string> GetSupportedCommands()
    {
        return new List<string>(SupportedSlashCommands);
    }

    private static int FindCommandEnd(string value)
    {
        for (var index = 1; index < value.Length; index++)
        {
            if (char.IsWhiteSpace(value[index]))
            {
                return index;
            }
        }

        return value.Length;
    }

    private static IReadOnlyList<ContextSource> GetTargetSources(RouteTarget target)
    {
        return target switch
        {
            RouteTarget.Mail => new[] { ContextSource.Mail },
            RouteTarget.Teams => new[] { ContextSource.Teams },
            RouteTarget.SharePoint => new[] { ContextSource.SharePoint },
            RouteTarget.OneDrive => new[] { ContextSource.OneDrive },
            RouteTarget.OneNote => new[] { ContextSource.OneNote },
            RouteTarget.Task => new[] { ContextSource.Planner, ContextSource.Todo },
            RouteTarget.Connectors => new[] { ContextSource.Connectors },
            RouteTarget.All => SearchAllSources,
            _ => Array.Empty<ContextSource>()
        };
    }

    private static string NormalizeQuery(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return string.Empty;
        }

        var buffer = new char[input.Length];
        var writeIndex = 0;
        var previousWasWhitespace = false;

        foreach (var character in input)
        {
            if (char.IsWhiteSpace(character))
            {
                if (previousWasWhitespace)
                {
                    continue;
                }

                buffer[writeIndex++] = ' ';
                previousWasWhitespace = true;
            }
            else
            {
                buffer[writeIndex++] = character;
                previousWasWhitespace = false;
            }
        }

        return new string(buffer, 0, writeIndex).Trim();
    }
}
