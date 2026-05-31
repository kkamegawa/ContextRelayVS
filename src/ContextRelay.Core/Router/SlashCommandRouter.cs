using System;
using System.Collections.Generic;
using System.Linq;

namespace ContextRelay.Core.Router;

public static class SlashCommandRouter
{
    private static readonly IReadOnlyList<string> CombinableSlashCommands = new[]
    {
        "/mail",
        "/teams",
        "/sharepoint",
        "/onedrive",
        "/onenote",
        "/task"
    };

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
            ["all"] = "Example: /all architecture decisions\nExample: /mail /onedrive architecture decisions\nPlain text without a slash command starts or continues Microsoft 365 Copilot chat.",
            ["ask"] = "Example: /ask #README.md Summarize this file as a bullet list\nExample: /ask Summarize the pinned docs as a bullet list\nPinned snippets or # file mentions are used as context and the Microsoft 365 Copilot response is shown in the panel.",
            ["workiq"] = "Example: /workiq Summarize my recent emails from Alice\nExample: /workiq #docs/plan.md Find related workplace context\nSends a natural language query to the Work IQ Gateway (A2A protocol). # file mentions send bounded local file text only when explicitly enabled.",
            ["clear"] = "Example: /clear\nClears the current chat transcript and discards all pinned snippets."
        };

    public static SlashCommandParseResult Parse(string input)
    {
        var trimmed = input?.Trim() ?? string.Empty;
        var normalized = NormalizeQuery(trimmed);

        if (!trimmed.StartsWith("/", StringComparison.Ordinal))
        {
            return new SlashCommandParseResult
            {
                Target = RouteTarget.Chat,
                Query = normalized,
                IsEmpty = normalized.Length == 0,
                TargetSources = Array.Empty<ContextSource>(),
                SourceCommandNames = Array.Empty<string>(),
                SearchScope = SearchScope.Operation
            };
        }

        var commandEnd = FindCommandEnd(trimmed);
        var command = trimmed.Substring(1, commandEnd - 1);
        var rawQuery = commandEnd >= trimmed.Length ? string.Empty : trimmed.Substring(commandEnd).TrimStart();

        if (SlashCommands.TryGetValue(command, out var operationTarget) &&
            operationTarget is RouteTarget.Ask or RouteTarget.WorkIq or RouteTarget.Clear)
        {
            var operationQuery = operationTarget == RouteTarget.Clear
                ? string.Empty
                : rawQuery.Trim();

            return new SlashCommandParseResult
            {
                Target = operationTarget,
                Query = operationQuery,
                IsEmpty = operationTarget == RouteTarget.Clear ? false : operationQuery.Length == 0,
                SlashCommandName = $"/{command.ToLowerInvariant()}",
                TargetSources = Array.Empty<ContextSource>(),
                SourceCommandNames = Array.Empty<string>(),
                SearchScope = SearchScope.Operation
            };
        }

        var tokens = trimmed.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var commandNames = new List<string>();
        var seenCommands = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queryTokens = new List<string>();

        foreach (var token in tokens)
        {
            if (queryTokens.Count > 0)
            {
                queryTokens.Add(token);
                continue;
            }

            if (!token.StartsWith("/", StringComparison.Ordinal))
            {
                queryTokens.Add(token);
                continue;
            }

            var rawCommandName = token.Substring(1);
            if (!SlashCommands.TryGetValue(rawCommandName, out var target) ||
                target is RouteTarget.Ask or RouteTarget.WorkIq or RouteTarget.Clear)
            {
                return BuildFallbackAllQuery(normalized);
            }

            var normalizedCommandName = rawCommandName.ToLowerInvariant();
            if (target == RouteTarget.Connectors && commandNames.Count > 0)
            {
                return BuildFallbackAllQuery(normalized);
            }

            if (normalizedCommandName == "all")
            {
                if (commandNames.Count > 0)
                {
                    return BuildFallbackAllQuery(normalized);
                }
            }
            else if (commandNames.Contains("all", StringComparer.OrdinalIgnoreCase))
            {
                return BuildFallbackAllQuery(normalized);
            }

            if (seenCommands.Add(normalizedCommandName))
            {
                commandNames.Add(normalizedCommandName);
            }
        }

        if (commandNames.Count == 0)
        {
            return BuildFallbackAllQuery(normalized);
        }

        var query = NormalizeQuery(string.Join(" ", queryTokens));
        var targetSources = ExpandSources(commandNames);
        var firstTarget = SlashCommands[commandNames[0]];
        var routeTarget = commandNames.Count == 1 ? firstTarget : RouteTarget.All;
        var slashCommandName = string.Join(" ", commandNames.Select(static commandName => $"/{commandName}"));

        return new SlashCommandParseResult
        {
            Target = routeTarget,
            Query = query,
            IsEmpty = query.Length == 0,
            SlashCommandName = slashCommandName,
            TargetSources = targetSources,
            SourceCommandNames = commandNames.Select(static commandName => $"/{commandName}").ToArray(),
            SearchScope = commandNames.Count == 1 && string.Equals(commandNames[0], "all", StringComparison.OrdinalIgnoreCase)
                ? SearchScope.All
                : SearchScope.Scoped
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

    public static string GetScopedHelpText(IReadOnlyList<string> commands)
    {
        if (commands is null || commands.Count == 0)
        {
            return "Type a message to search Microsoft 365 content.";
        }

        if (commands.Count == 1)
        {
            return GetHelpText(commands[0]);
        }

        var prefix = string.Join(" ", commands);
        return string.Join(
            "\n",
            $"Example: {prefix} architecture decisions",
            $"Example: {prefix} incident review",
            "Searches only the explicitly requested sources.");
    }

    public static IReadOnlyList<string> GetSupportedCommands()
    {
        return new List<string>(SupportedSlashCommands);
    }

    public static IReadOnlyList<string> GetCombinableCommands()
    {
        return new List<string>(CombinableSlashCommands);
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

    private static SlashCommandParseResult BuildFallbackAllQuery(string normalized)
    {
        return new SlashCommandParseResult
        {
            Target = RouteTarget.All,
            Query = normalized,
            IsEmpty = normalized.Length == 0,
            TargetSources = SearchAllSources,
            SourceCommandNames = Array.Empty<string>(),
            SearchScope = SearchScope.All
        };
    }

    private static IReadOnlyList<ContextSource> ExpandSources(IEnumerable<string> commandNames)
    {
        var expanded = new List<ContextSource>();
        var seenSources = new HashSet<ContextSource>();

        foreach (var commandName in commandNames)
        {
            foreach (var source in GetTargetSources(SlashCommands[commandName]))
            {
                if (seenSources.Add(source))
                {
                    expanded.Add(source);
                }
            }
        }

        return expanded;
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
