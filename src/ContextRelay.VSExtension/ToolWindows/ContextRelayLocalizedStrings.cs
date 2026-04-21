using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using ContextRelay.Core.Router;

namespace ContextRelay.VSExtension.ToolWindows;

internal static class ContextRelayLocalizedStrings
{
    private static readonly IReadOnlyDictionary<string, (string English, string Japanese)> CommandDescriptions =
        new Dictionary<string, (string English, string Japanese)>(StringComparer.OrdinalIgnoreCase)
        {
            ["/mail"] = ("Search Exchange mail.", "Exchange メールを検索します。"),
            ["/teams"] = ("Search Teams messages.", "Teams メッセージを検索します。"),
            ["/sharepoint"] = ("Search SharePoint content.", "SharePoint コンテンツを検索します。"),
            ["/onedrive"] = ("Search OneDrive content.", "OneDrive コンテンツを検索します。"),
            ["/connectors"] = ("Search connector content.", "コネクタのコンテンツを検索します。"),
            ["/all"] = ("Search all enabled Microsoft 365 sources.", "有効な Microsoft 365 ソース全体を検索します。"),
            ["/ask"] = ("Send the pinned context to Microsoft 365 Copilot.", "ピン留めしたコンテキストを Microsoft 365 Copilot に送信します。"),
            ["/clear"] = ("Clear the current chat transcript and pinned snippets.", "現在のチャット履歴とピン留めスニペットをクリアします。")
        };

    public static bool UseJapanese =>
        CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("ja", StringComparison.OrdinalIgnoreCase);

    public static string GenerateHandoffButtonText => UseJapanese ? "引き継ぎ文書を生成" : "Generate handoff";

    public static string CopyPromptButtonText => UseJapanese ? "プロンプトをコピー" : "Copy prompt";

    public static string OpenHandoffButtonText => UseJapanese ? "引き継ぎ文書を開く" : "Open handoff";

    public static string OpenCopilotButtonText => UseJapanese ? "Copilot を開く" : "Open Copilot";

    public static string ClearChatButtonText => UseJapanese ? "チャットをクリア" : "Clear chat";

    public static string ClearSnippetsButtonText => UseJapanese ? "スニペットをクリア" : "Clear snippets";

    public static string ClearCacheButtonText => UseJapanese ? "キャッシュをクリア" : "Clear cache";

    public static string SettingsButtonText => UseJapanese ? "設定" : "Settings";

    public static string DebugLogButtonText => UseJapanese ? "デバッグ ログ" : "Debug log";

    public static string SearchButtonText => UseJapanese ? "検索" : "Search";

    public static string SearchResultsHeaderText => UseJapanese ? "検索結果" : "Search results";

    public static string SnippetsHeaderText => UseJapanese ? "ピン留めしたスニペット" : "Pinned snippets";

    public static string ChatHistoryHeaderText => UseJapanese ? "共有チャット履歴" : "Shared chat history";

    public static string SearchToolTip =>
        UseJapanese
            ? "/mail /teams /sharepoint /onedrive /connectors /all /ask /clear を使用できます"
            : "Use /mail /teams /sharepoint /onedrive /connectors /all /ask /clear";

    public static string CommandPopupHeaderText => UseJapanese ? "スラッシュ コマンド" : "Slash commands";

    public static string PinButtonText => UseJapanese ? "ピン留め" : "Pin";

    public static string OpenButtonText => UseJapanese ? "開く" : "Open";

    public static string DeleteButtonText => UseJapanese ? "削除" : "Delete";

    public static string CopyMenuText => UseJapanese ? "コピー" : "Copy";

    public static string AppendToHandoffMenuText => UseJapanese ? "HANDOFF に追記" : "Append to handoff";

    public static string OpenInBrowserMenuText => UseJapanese ? "ブラウザーで開く" : "Open in browser";

    public static string ReadyStatus => UseJapanese ? "ContextRelay の準備ができました。" : "ContextRelay is ready.";

    public static string GenericHelpText =>
        UseJapanese ? "クエリを入力して Microsoft 365 コンテンツを検索します。" : "Type a query to search Microsoft 365 content.";

    public static string SignedOutText =>
        UseJapanese
            ? "未サインインです。 [ツール] > [オプション] > [ContextRelay] で Client ID を設定してください。"
            : "Not signed in. Configure Client ID under Tools > Options > ContextRelay.";

    public static string RequestedSourceDisabledStatus =>
        UseJapanese ? "指定したソースは ContextRelay のオプションで無効です。" : "The requested source is disabled in ContextRelay options.";

    public static string AskDisabledStatus =>
        UseJapanese ? "/ask は無効です。ContextRelay のオプションで chat preview を有効にしてください。" : "/ask is disabled. Enable chat preview in ContextRelay options.";

    public static string AskRequiresPinnedContextStatus =>
        UseJapanese
            ? "/ask を使うには先に 1 件以上のスニペットをピン留めしてください。ピン留めした内容が Microsoft 365 Copilot にコンテキストとして送信されます。"
            : "Pin one or more snippets before using /ask. The pinned content is sent to Microsoft 365 Copilot as context.";

    public static string ChatAndSnippetsClearedStatus =>
        UseJapanese ? "チャット履歴とスニペットをクリアしました。" : "Chat and snippets cleared.";

    public static string ResultPinnedStatus => UseJapanese ? "結果をスニペットにピン留めしました。" : "Result pinned to snippets.";

    public static string ResultPinnedWithExcerptFallbackStatus =>
        UseJapanese
            ? "結果をスニペットにピン留めしました。全文の取得には失敗したため、検索結果の抜粋を保存しました。"
            : "Result pinned to snippets. Full content could not be fetched, so the saved snippet uses the search excerpt.";

    public static string ResultUnpinnedStatus => UseJapanese ? "結果のピン留めを解除しました。" : "Snippet unpinned.";

    public static string SnippetRemovedStatus => UseJapanese ? "スニペットを削除しました。" : "Snippet removed.";

    public static string SnippetsClearedStatus => UseJapanese ? "すべてのスニペットをクリアしました。" : "All snippets cleared.";

    public static string ChatHistoryClearedStatus => UseJapanese ? "チャット履歴をクリアしました。" : "Chat history cleared.";

    public static string SearchCacheClearedStatus => UseJapanese ? "検索キャッシュをクリアしました。" : "Search cache cleared.";

    public static string HandoffPromptCopiedStatus => UseJapanese ? "引き継ぎプロンプトをクリップボードにコピーしました。" : "Handoff prompt copied to clipboard.";

    public static string OpenedHandoffStatus => UseJapanese ? "HANDOFF.md を開きました。" : "Opened HANDOFF.md.";

    public static string OpenCopilotPromptReadyStatus =>
        UseJapanese ? "プロンプトをコピーしました。Visual Studio の GitHub Copilot Chat に貼り付けてください。" : "Prompt copied. Paste it into GitHub Copilot Chat in Visual Studio.";

    public static string OpenCopilotPromptAndPaneReadyStatus =>
        UseJapanese
            ? "プロンプトをコピーし、GitHub Copilot Chat を開きました。Visual Studio 上で貼り付けて送信してください。"
            : "Prompt copied and GitHub Copilot Chat opened. Paste the prompt in Visual Studio and send it.";

    public static string ResultCopiedStatus => UseJapanese ? "結果をクリップボードにコピーしました。" : "Result copied to clipboard.";

    public static string SnippetCopiedStatus => UseJapanese ? "スニペットをクリップボードにコピーしました。" : "Snippet copied to clipboard.";

    public static string AppendedToHandoffStatus => UseJapanese ? "結果を HANDOFF.md に追記しました。" : "Result appended to HANDOFF.md.";

    public static string NoResultsFoundStatus => UseJapanese ? "結果が見つかりませんでした。" : "No results found.";

    public static string TypeQueryStatus => GenericHelpText;

    public static string GetSignedInUserText(string username) =>
        UseJapanese ? $"サインイン中: {username}" : $"Signed in as {username}";

    public static string GetFoundResultsStatus(int count) =>
        UseJapanese ? $"{count} 件の結果が見つかりました。" : $"Found {count} result(s).";

    public static string GetHandoffUpdatedStatus(int fileCount) =>
        UseJapanese ? $"引き継ぎ文書を更新しました ({fileCount} ファイル)。" : $"Handoff docs updated ({fileCount} files).";

    public static string GetAskPreviewOpenedStatus(string languageId, int snippetCount)
    {
        var languageName = GetLanguageDisplayName(languageId);
        return UseJapanese
            ? $"Microsoft 365 Copilot の応答を新しいエディター タブで開きました ({languageName})。{snippetCount} 件のピン留めスニペットをコンテキストとして使用し、共有チャット履歴にも保存しました。"
            : $"Opened the Microsoft 365 Copilot response in a new editor tab ({languageName}). Used {snippetCount} pinned snippet(s) as context and saved the reply to chat.";
    }

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

        var normalizedCommand = command!;
        normalizedCommand = normalizedCommand.ToLowerInvariant();
        return normalizedCommand switch
        {
            "/mail" => UseJapanese
                ? "例: /mail from:alice subject:budget\n例: /mail incident review"
                : "Example: /mail from:alice subject:budget\nExample: /mail incident review",
            "/teams" => UseJapanese
                ? "例: /teams sprint review\n例: /teams from:bob mentions:me"
                : "Example: /teams sprint review\nExample: /teams from:bob mentions:me",
            "/sharepoint" => UseJapanese
                ? "例: /sharepoint VPN setup guide\n例: /sharepoint architecture"
                : "Example: /sharepoint VPN setup guide\nExample: /sharepoint architecture",
            "/onedrive" => UseJapanese
                ? "例: /onedrive architecture diagram\n例: /onedrive Q3 report"
                : "Example: /onedrive architecture diagram\nExample: /onedrive Q3 report",
            "/connectors" => UseJapanese
                ? "例: /connectors incident tracker\n例: /connectors external knowledge base"
                : "Example: /connectors incident tracker\nExample: /connectors external knowledge base",
            "/all" => UseJapanese
                ? "例: /all architecture decisions\nまたはスラッシュ コマンドなしでクエリを入力してください。"
                : "Example: /all architecture decisions\nOr just type a query without a slash command.",
            "/ask" => UseJapanese
                ? "例: /ask 日本語に翻訳して markdown にして\n例: /ask ピン留めした情報を箇条書きで要約して\nピン留めスニペットをコンテキストとして使用し、応答は新しいエディター タブで開きます。"
                : "Example: /ask Translate this to Japanese and format as markdown\nExample: /ask Summarize the pinned docs as a bullet list\nPinned snippets are used as context and the response is opened in a new editor tab.",
            "/clear" => UseJapanese
                ? "例: /clear\n現在のチャット履歴とピン留めスニペットを破棄します。"
                : "Example: /clear\nClears the current chat transcript and discards all pinned snippets.",
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
                Name = command,
                Description = GetCommandDescription(command)
            });
        }

        return suggestions;
    }

    public static string GetCommandDescription(string command)
    {
        if (CommandDescriptions.TryGetValue(command, out var description))
        {
            return UseJapanese ? description.Japanese : description.English;
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
            "shellscript" => UseJapanese ? "Shell スクリプト" : "Shell script",
            "powershell" => "PowerShell",
            "plaintext" => UseJapanese ? "プレーン テキスト" : "Plain text",
            _ => string.IsNullOrWhiteSpace(normalizedLanguageId) ? "Markdown" : normalizedLanguageId
        };
    }
}
