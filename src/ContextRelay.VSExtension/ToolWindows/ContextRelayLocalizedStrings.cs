using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Resources;
using ContextRelay.Core.Router;

namespace ContextRelay.VSExtension.ToolWindows;

internal static class ContextRelayLocalizedStrings
{
    private const string UiLanguageAuto = "auto";
    private const string UiLanguageEnglish = "en";
    private const string UiLanguageJapanese = "ja";
    private static readonly CultureInfo EnglishCulture = CultureInfo.GetCultureInfo(UiLanguageEnglish);
    private static readonly CultureInfo JapaneseCulture = CultureInfo.GetCultureInfo(UiLanguageJapanese);
    private static readonly ResourceManager ResourceManager = new(
        "ContextRelay.VSExtension.Resources.ContextRelayStrings",
        typeof(ContextRelayLocalizedStrings).Assembly);
    private static string configuredUiLanguage = UiLanguageAuto;

    private static readonly IReadOnlyDictionary<string, string> CommandDescriptionKeys =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["/mail"] = "CommandDescription_mail",
            ["/teams"] = "CommandDescription_teams",
            ["/sharepoint"] = "CommandDescription_sharepoint",
            ["/onedrive"] = "CommandDescription_onedrive",
            ["/onenote"] = "CommandDescription_onenote",
            ["/task"] = "CommandDescription_task",
            ["/connectors"] = "CommandDescription_connectors",
            ["/all"] = "CommandDescription_all",
            ["/ask"] = "CommandDescription_ask",
            ["/workiq"] = "CommandDescription_workiq",
            ["/clear"] = "CommandDescription_clear",
        };

    public static bool UseJapanese => ResolveLanguageCode(configuredUiLanguage).Equals(UiLanguageJapanese, StringComparison.OrdinalIgnoreCase);

    public static string CurrentUiLanguage => configuredUiLanguage;

    public static void SetUiLanguage(string? language)
    {
        configuredUiLanguage = NormalizeUiLanguage(language);
    }

    public static string GenerateHandoffButtonText => GetString("GenerateHandoffButtonText");
    public static string CopyPromptButtonText => GetString("CopyPromptButtonText");
    public static string OpenHandoffButtonText => GetString("OpenHandoffButtonText");
    public static string OpenCopilotButtonText => GetString("OpenCopilotButtonText");
    public static string AddFilesButtonText => GetString("AddFilesButtonText");
    public static string AddFilesToolTip => GetString("AddFilesToolTip");
    public static string ClearChatButtonText => GetString("ClearChatButtonText");
    public static string ClearSnippetsButtonText => GetString("ClearSnippetsButtonText");
    public static string ClearCacheButtonText => GetString("ClearCacheButtonText");
    public static string DebugLogOpenedStatus => GetString("DebugLogOpenedStatus");
    public static string SearchButtonText => GetString("SearchButtonText");
    public static string SearchResultsHeaderText => GetString("SearchResultsHeaderText");
    public static string WindowTitleText => GetString("WindowTitleText");
    public static string SnippetsHeaderText => GetString("SnippetsHeaderText");
    public static string ChatHistoryHeaderText => GetString("ChatHistoryHeaderText");
    public static string SearchToolTip => GetString("SearchToolTip");
    public static string CommandPopupHeaderText => GetString("CommandPopupHeaderText");
    public static string PinButtonText => GetString("PinButtonText");
    public static string OpenButtonText => GetString("OpenButtonText");
    public static string DeleteButtonText => GetString("DeleteButtonText");
    public static string CopyMenuText => GetString("CopyMenuText");
    public static string AppendToHandoffMenuText => GetString("AppendToHandoffMenuText");
    public static string OpenInBrowserMenuText => GetString("OpenInBrowserMenuText");
    public static string ReadyStatus => GetString("ReadyStatus");
    public static string GenericHelpText => GetString("GenericHelpText");
    public static string SignedOutText => GetString("SignedOutText");
    public static string RequestedSourceDisabledStatus => GetString("RequestedSourceDisabledStatus");
    public static string AskDisabledStatus => GetString("AskDisabledStatus");
    public static string ChatPreviewDisabledStatus => GetString("ChatPreviewDisabledStatus");
    public static string AskRequiresPinnedContextStatus => GetString("AskRequiresPinnedContextStatus");
    public static string FileMentionPromptEmptyStatus => GetString("FileMentionPromptEmptyStatus");
    public static string FilePickerWorkspaceUnavailableStatus => GetString("FilePickerWorkspaceUnavailableStatus");
    public static string FilePickerNoFilesSelectedStatus => GetString("FilePickerNoFilesSelectedStatus");
    public static string FilePickerNoWorkspaceFilesSelectedStatus => GetString("FilePickerNoWorkspaceFilesSelectedStatus");
    public static string FilePickerAddFilesFailedStatus => GetString("FilePickerAddFilesFailedStatus");
    public static string WorkIqLocalFileContextDisabledStatus => GetString("WorkIqLocalFileContextDisabledStatus");
    public static string FileMentionWorkspaceUnavailableStatus => GetString("FileMentionWorkspaceUnavailableStatus");
    public static string ChatAndSnippetsClearedStatus => GetString("ChatAndSnippetsClearedStatus");
    public static string ResultPinnedStatus => GetString("ResultPinnedStatus");
    public static string ResultPinnedWithExcerptFallbackStatus => GetString("ResultPinnedWithExcerptFallbackStatus");
    public static string ResultUnpinnedStatus => GetString("ResultUnpinnedStatus");
    public static string SnippetRemovedStatus => GetString("SnippetRemovedStatus");
    public static string SnippetsClearedStatus => GetString("SnippetsClearedStatus");
    public static string ChatHistoryClearedStatus => GetString("ChatHistoryClearedStatus");
    public static string SearchCacheClearedStatus => GetString("SearchCacheClearedStatus");
    public static string HandoffPromptCopiedStatus => GetString("HandoffPromptCopiedStatus");
    public static string OpenedHandoffStatus => GetString("OpenedHandoffStatus");
    public static string OpenCopilotPromptReadyStatus => GetString("OpenCopilotPromptReadyStatus");
    public static string OpenCopilotPromptAndPaneReadyStatus => GetString("OpenCopilotPromptAndPaneReadyStatus");
    public static string ResultCopiedStatus => GetString("ResultCopiedStatus");
    public static string SnippetCopiedStatus => GetString("SnippetCopiedStatus");
    public static string AppendedToHandoffStatus => GetString("AppendedToHandoffStatus");
    public static string ChatReplyShownStatus => GetString("ChatReplyShownStatus");
    public static string WorkIqReplyShownStatus => GetString("WorkIqReplyShownStatus");
    public static string AssistantResponseCopiedStatus => GetString("AssistantResponseCopiedStatus");
    public static string AssistantResponseAppendedStatus => GetString("AssistantResponseAppendedStatus");
    public static string AssistantResponseReplacedStatus => GetString("AssistantResponseReplacedStatus");
    public static string NoActiveEditorStatus => GetString("NoActiveEditorStatus");
    public static string CopyAssistantButtonText => GetString("CopyAssistantButtonText");
    public static string AppendAssistantButtonText => GetString("AppendAssistantButtonText");
    public static string ReplaceAssistantButtonText => GetString("ReplaceAssistantButtonText");
    public static string ContextLabelsPrefixText => GetString("ContextLabelsPrefixText");
    public static string NoResultsFoundStatus => GetString("NoResultsFoundStatus");
    public static string SourceLabelMail => GetString("SourceLabel_mail");
    public static string SourceLabelTeams => GetString("SourceLabel_teams");
    public static string SourceLabelSharePoint => GetString("SourceLabel_sharepoint");
    public static string SourceLabelOneDrive => GetString("SourceLabel_onedrive");
    public static string SourceLabelOneNote => GetString("SourceLabel_onenote");
    public static string SourceLabelPlanner => GetString("SourceLabel_planner");
    public static string SourceLabelTodo => GetString("SourceLabel_todo");
    public static string SourceLabelConnectors => GetString("SourceLabel_connectors");
    public static string SourceLabelAll => GetString("SourceLabel_all");
    public static string SourceLabelDefault => GetString("SourceLabel_default");
    public static string TypeQueryStatus => GenericHelpText;
    public static string AddFilesDialogTitle => GetString("AddFilesDialogTitle");
    public static string AddFilesDialogFilter => GetString("AddFilesDialogFilter");

    public static bool IsReadyStatus(string? statusMessage)
    {
        return string.Equals(statusMessage, GetString("ReadyStatus", EnglishCulture), StringComparison.Ordinal) ||
            string.Equals(statusMessage, GetString("ReadyStatus", JapaneseCulture), StringComparison.Ordinal);
    }

    public static string GetToolWindowInitializationFailedStatus(string? detail)
    {
        return string.IsNullOrWhiteSpace(detail)
            ? GetString("ToolWindowInitializationFailed_NoDetail")
            : Format("ToolWindowInitializationFailed_WithDetail", detail);
    }

    public static string GetSignedInUserText(string username) => Format("SignedInUser_Format", username);

    public static string GetFoundResultsStatus(int count) => Format("FoundResults_Format", count);

    public static string GetHandoffUpdatedStatus(int fileCount) => Format("HandoffUpdated_Format", fileCount);

    public static string GetAskPreviewOpenedStatus(string languageId, int snippetCount)
    {
        var languageName = GetLanguageDisplayName(languageId);
        return Format("AskPreviewOpenedStatus_Format", languageName, snippetCount);
    }

    public static string GetAskReplyShownStatus(int snippetCount) => Format("AskReplyShownStatus_Format", snippetCount);

    public static string GetAskReplyShownWithContextBreakdownStatus(int snippetCount, int localFileCount) =>
        Format("AskReplyShownWithContextBreakdownStatus_Format", snippetCount, localFileCount);

    public static string GetChatReplyShownWithContextStatus(int contextCount) => Format("ChatReplyShownWithContextStatus_Format", contextCount);

    public static string GetFilePickerFilesAddedStatus(int fileCount) => Format("FilePickerFilesAddedStatus_Format", fileCount);

    public static string GetFilePickerFilesAddedPartialStatus(int fileCount, int skippedCount) =>
        Format("FilePickerFilesAddedPartialStatus_Format", fileCount, skippedCount);

    public static string GetFilePickerMentionLimitReachedStatus(int maxMentions) =>
        Format("FilePickerMentionLimitReachedStatus_Format", maxMentions);

    public static string GetLocalFileContextLabel(string relativePath) =>
        Format("LocalFileContextLabel_Format", relativePath);

    public static string GetFileMentionNotFoundStatus(string rawPath) =>
        Format("FileMentionNotFoundStatus_Format", rawPath);

    public static string GetFileMentionOutsideWorkspaceStatus(string rawPath) =>
        Format("FileMentionOutsideWorkspaceStatus_Format", rawPath);

    public static string GetFileMentionAmbiguousStatus(string rawPath) =>
        Format("FileMentionAmbiguousStatus_Format", rawPath);

    public static string GetFileMentionUnsupportedFileTypeStatus(string rawPath) =>
        Format("FileMentionUnsupportedFileTypeStatus_Format", rawPath);

    public static string GetAskPreviewDocumentTitle(string query, string extension) =>
        string.IsNullOrWhiteSpace(query)
            ? $"ASK_RESPONSE_{DateTimeOffset.UtcNow:yyyyMMdd_HHmmss}.{NormalizeExtension(extension)}"
            : $"ASK_{DateTimeOffset.UtcNow:yyyyMMdd_HHmmss}_{SanitizeFileName(query)}.{NormalizeExtension(extension)}";

    public static string GetHelpTextForQuery(string? queryText)
    {
        var command = ExtractSlashCommand(queryText);
        if (string.IsNullOrEmpty(command))
        {
            return GenericHelpText;
        }

        var normalizedCommand = command!.ToLowerInvariant();
        return normalizedCommand switch
        {
            "/mail" => GetString("HelpFor_mail"),
            "/teams" => GetString("HelpFor_teams"),
            "/sharepoint" => GetString("HelpFor_sharepoint"),
            "/onedrive" => GetString("HelpFor_onedrive"),
            "/onenote" => GetString("HelpFor_onenote"),
            "/task" => GetString("HelpFor_task"),
            "/connectors" => GetString("HelpFor_connectors"),
            "/all" => GetString("HelpFor_all"),
            "/ask" => GetString("HelpFor_ask"),
            "/workiq" => GetString("HelpFor_workiq"),
            "/clear" => GetString("HelpFor_clear"),
            _ => GenericHelpText
        };
    }

    public static IReadOnlyList<SlashCommandSuggestion> GetCommandSuggestions(string? queryText)
    {
        var trimmed = queryText?.TrimStart() ?? string.Empty;
        if (!trimmed.StartsWith("/", StringComparison.Ordinal))
        {
            return Array.Empty<SlashCommandSuggestion>();
        }

        var whitespaceIndex = trimmed.IndexOfAny(new[] { ' ', '\t', '\r', '\n' });
        if (whitespaceIndex >= 0)
        {
            return Array.Empty<SlashCommandSuggestion>();
        }

        var prefix = trimmed;
        var suggestions = new List<SlashCommandSuggestion>();
        foreach (var command in SlashCommandRouter.GetSupportedCommands())
        {
            if (!command.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            suggestions.Add(new SlashCommandSuggestion
            {
                Icon = SourcePresentation.GetCommandIcon(command),
                Name = command,
                Description = GetCommandDescription(command)
            });
        }

        return suggestions;
    }

    public static string GetCommandDescription(string command)
    {
        if (CommandDescriptionKeys.TryGetValue(command, out var key))
        {
            return GetString(key);
        }

        return GenericHelpText;
    }

    private static string? ExtractSlashCommand(string? queryText)
    {
        var trimmed = queryText?.TrimStart() ?? string.Empty;
        if (!trimmed.StartsWith("/", StringComparison.Ordinal))
        {
            return null;
        }

        var whitespaceIndex = trimmed.IndexOfAny(new[] { ' ', '\t', '\r', '\n' });
        return whitespaceIndex >= 0 ? trimmed.Substring(0, whitespaceIndex) : trimmed;
    }

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var buffer = new char[Math.Min(48, value.Length)];
        var writeIndex = 0;

        foreach (var character in value)
        {
            if (writeIndex >= buffer.Length)
            {
                break;
            }

            if (Array.IndexOf(invalid, character) >= 0)
            {
                continue;
            }

            buffer[writeIndex++] = char.IsWhiteSpace(character) ? '_' : character;
        }

        var sanitized = writeIndex == 0 ? string.Empty : new string(buffer, 0, writeIndex);
        sanitized = sanitized.TrimEnd(' ', '.');

        return string.IsNullOrWhiteSpace(sanitized) || IsReservedWindowsFileName(sanitized)
            ? "response"
            : sanitized;
    }

    private static bool IsReservedWindowsFileName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var deviceName = value;
        var extensionIndex = deviceName.IndexOf('.');
        if (extensionIndex >= 0)
        {
            deviceName = deviceName.Substring(0, extensionIndex);
        }

        switch (deviceName.ToUpperInvariant())
        {
            case "CON":
            case "PRN":
            case "AUX":
            case "NUL":
            case "COM1":
            case "COM2":
            case "COM3":
            case "COM4":
            case "COM5":
            case "COM6":
            case "COM7":
            case "COM8":
            case "COM9":
            case "LPT1":
            case "LPT2":
            case "LPT3":
            case "LPT4":
            case "LPT5":
            case "LPT6":
            case "LPT7":
            case "LPT8":
            case "LPT9":
                return true;
            default:
                return false;
        }
    }

    private static string NormalizeExtension(string extension)
    {
        var normalized = (extension ?? string.Empty).Trim();
        if (normalized.StartsWith(".", StringComparison.Ordinal))
        {
            normalized = normalized.Substring(1);
        }

        return string.IsNullOrWhiteSpace(normalized) ? "md" : normalized;
    }

    private static string ResolveLanguageCode(string language)
    {
        var normalized = NormalizeUiLanguage(language);
        if (!normalized.Equals(UiLanguageAuto, StringComparison.OrdinalIgnoreCase))
        {
            return normalized;
        }

        return CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals(UiLanguageJapanese, StringComparison.OrdinalIgnoreCase)
            ? UiLanguageJapanese
            : UiLanguageEnglish;
    }

    private static string NormalizeUiLanguage(string? language)
    {
        var normalized = (language ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch
        {
            UiLanguageEnglish => UiLanguageEnglish,
            UiLanguageJapanese => UiLanguageJapanese,
            UiLanguageAuto => UiLanguageAuto,
            "en-us" => UiLanguageEnglish,
            "en-gb" => UiLanguageEnglish,
            "ja-jp" => UiLanguageJapanese,
            _ => UiLanguageAuto
        };
    }

    private static string GetLanguageDisplayName(string languageId)
    {
        var normalizedLanguageId = (languageId ?? string.Empty).ToLowerInvariant();
        return normalizedLanguageId switch
        {
            "markdown" => "Markdown",
            "json" => "JSON",
            "yaml" => "YAML",
            "html" => "HTML",
            "xml" => "XML",
            "csv" => "CSV",
            "typescript" => "TypeScript",
            "javascript" => "JavaScript",
            "python" => "Python",
            "sql" => "SQL",
            "powershell" => "PowerShell",
            "shellscript" => GetString("LanguageDisplay_shellscript"),
            "plaintext" => GetString("LanguageDisplay_plaintext"),
            _ => string.IsNullOrWhiteSpace(normalizedLanguageId) ? "Markdown" : normalizedLanguageId
        };
    }

    private static string Format(string key, params object[] args)
    {
        return string.Format(GetResolvedCulture(), GetString(key), args);
    }

    private static string GetString(string key)
    {
        return GetString(key, GetResolvedCulture());
    }

    private static string GetString(string key, CultureInfo culture)
    {
        return ResourceManager.GetString(key, culture) ??
            ResourceManager.GetString(key, EnglishCulture) ??
            key;
    }

    private static CultureInfo GetResolvedCulture()
    {
        return ResolveLanguageCode(configuredUiLanguage).Equals(UiLanguageJapanese, StringComparison.OrdinalIgnoreCase)
            ? JapaneseCulture
            : EnglishCulture;
    }
}
