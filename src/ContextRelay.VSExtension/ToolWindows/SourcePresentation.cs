using System;
using System.Collections.Generic;

namespace ContextRelay.VSExtension.ToolWindows;

/// <summary>
/// Provides source labels and icons aligned with VS Code source presentation.
/// </summary>
internal static class SourcePresentation
{
    private const string FallbackIcon = "📎";

    private static readonly IReadOnlyDictionary<string, Func<string>> SourceLabels =
        new Dictionary<string, Func<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["mail"] = () => ContextRelayLocalizedStrings.SourceLabelMail,
            ["teams"] = () => ContextRelayLocalizedStrings.SourceLabelTeams,
            ["sharepoint"] = () => ContextRelayLocalizedStrings.SourceLabelSharePoint,
            ["onedrive"] = () => ContextRelayLocalizedStrings.SourceLabelOneDrive,
            ["onenote"] = () => ContextRelayLocalizedStrings.SourceLabelOneNote,
            ["planner"] = () => ContextRelayLocalizedStrings.SourceLabelPlanner,
            ["todo"] = () => ContextRelayLocalizedStrings.SourceLabelTodo,
            ["connectors"] = () => ContextRelayLocalizedStrings.SourceLabelConnectors,
            ["all"] = () => ContextRelayLocalizedStrings.SourceLabelAll,
        };

    private static readonly IReadOnlyDictionary<string, string> SourceIcons =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["mail"] = "📧",
            ["teams"] = "💬",
            ["sharepoint"] = "📄",
            ["onedrive"] = "☁️",
            ["onenote"] = "🗒️",
            ["planner"] = "✅",
            ["todo"] = "☑️",
            ["connectors"] = "🔗",
            ["all"] = "🔍",
        };

    private static readonly IReadOnlyDictionary<string, string> CommandIcons =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["/mail"] = "📧",
            ["/teams"] = "💬",
            ["/sharepoint"] = "📄",
            ["/onedrive"] = "☁️",
            ["/onenote"] = "🗒️",
            ["/task"] = "☑️",
            ["/connectors"] = "🔗",
            ["/all"] = "🔍",
            ["/ask"] = "🤖",
            ["/workiq"] = "🧠",
            ["/clear"] = "🧹",
        };

    internal static string GetSourceLabel(string source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return ContextRelayLocalizedStrings.SourceLabelDefault;
        }

        if (SourceLabels.TryGetValue(source, out var labelFactory))
        {
            return labelFactory();
        }

        return $"{char.ToUpperInvariant(source[0])}{source.Substring(1)}";
    }

    internal static string GetSourceIcon(string source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return FallbackIcon;
        }

        return SourceIcons.TryGetValue(source, out var icon) ? icon : FallbackIcon;
    }

    internal static string GetCommandIcon(string commandName)
    {
        if (string.IsNullOrWhiteSpace(commandName))
        {
            return FallbackIcon;
        }

        return CommandIcons.TryGetValue(commandName, out var icon) ? icon : FallbackIcon;
    }
}
