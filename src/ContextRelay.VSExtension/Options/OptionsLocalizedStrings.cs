using System.Globalization;
using ContextRelay.Core.Auth;

namespace ContextRelay.VSExtension.Options;

internal static class OptionsLocalizedStrings
{
    private static bool UseJapanese =>
        CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("ja", System.StringComparison.OrdinalIgnoreCase);

    // --- General ---
    public static string GeneralPageTitle => UseJapanese ? "全般" : "General";
    public static string MaxResultsLabel => UseJapanese ? "最大結果数" : "Max results";
    public static string MaxResultsDescription => UseJapanese
        ? "ソースごとの検索で返される結果の最大数。"
        : "Maximum number of results returned per source search.";
    public static string OutputDirectoryLabel => UseJapanese ? "出力ディレクトリ" : "Output directory";
    public static string OutputDirectoryDescription => UseJapanese
        ? "生成される引き継ぎ文書の相対パスまたは絶対パス。"
        : "Relative or absolute directory used for generated handoff documents.";
    public static string EnableChatPreviewLabel => UseJapanese ? "チャット プレビューを有効にする" : "Enable chat preview";
    public static string EnableChatPreviewDescription => UseJapanese
        ? "Microsoft 365 Copilot チャット プレビュー API に対する /ask を有効にします。"
        : "Enable /ask against the Microsoft 365 Copilot chat preview API.";
    public static string EnableGraphDebugLoggingLabel => UseJapanese ? "Graph デバッグ ログを有効にする" : "Enable Graph debug logging";
    public static string EnableGraphDebugLoggingDescription => UseJapanese
        ? "Graph 要求/応答の概要を ContextRelay デバッグ出力ペインに書き込みます。"
        : "Write Graph request/response summaries to the ContextRelay Debug output pane.";

    // --- Authentication ---
    public static string AuthenticationPageTitle => UseJapanese ? "認証" : "Authentication";
    public static string ClientIdLabel => UseJapanese ? "クライアント ID" : "Client ID";
    public static string ClientIdDescription => UseJapanese
        ? "MSAL.NET サインインに使用する Microsoft Entra パブリック クライアント アプリケーション ID。"
        : "Microsoft Entra public-client application ID used for MSAL.NET sign-in.";
    public static string TenantIdLabel => UseJapanese ? "テナント ID" : "Tenant ID";
    public static string TenantIdDescription => UseJapanese
        ? "テナント ID または organizations/common 権限セグメント。"
        : "Tenant ID or organizations/common authority segment.";

    // --- Graph API ---
    public static string GraphApiPageTitle => UseJapanese ? "Graph API" : "Graph API";
    public static string CloudEnvironmentLabel => UseJapanese ? "クラウド環境" : "Cloud environment";
    public static string CloudEnvironmentDescription => UseJapanese
        ? "接続先の Microsoft クラウド環境を選択します。ソブリン クラウド (US Gov、ドイツ、中国) はエンドポイントと認証を自動的に構成します。"
        : "Select the Microsoft cloud environment to connect to. Sovereign clouds (US Gov, Germany, China) auto-configure endpoints and authentication.";
    public static string CustomGraphEndpointLabel => UseJapanese ? "カスタム Graph エンドポイント" : "Custom Graph endpoint";
    public static string CustomGraphEndpointDescription => UseJapanese
        ? "カスタム クラウド環境の Microsoft Graph エンドポイント URL。"
        : "Microsoft Graph endpoint URL for the custom cloud environment.";
    public static string CustomAuthEndpointLabel => UseJapanese ? "カスタム認証エンドポイント" : "Custom auth endpoint";
    public static string CustomAuthEndpointDescription => UseJapanese
        ? "カスタム クラウド環境の認証エンドポイント URL。"
        : "Authentication endpoint URL for the custom cloud environment.";
    public static string EffectiveGraphEndpointLabel => UseJapanese ? "有効な Graph エンドポイント" : "Effective Graph endpoint";
    public static string EffectiveAuthEndpointLabel => UseJapanese ? "有効な認証エンドポイント" : "Effective auth endpoint";
    public static string RequiredScopesLabel => UseJapanese ? "要求されるスコープ" : "Required scopes";
    public static string RequiredScopesDescription => UseJapanese
        ? "有効なアダプターに基づいて計算された Microsoft Graph 委任アクセス許可。"
        : "Microsoft Graph delegated permissions computed from enabled adapters.";

    // --- Cache ---
    public static string CachePageTitle => UseJapanese ? "キャッシュ" : "Cache";
    public static string TtlSecondsLabel => UseJapanese ? "TTL (秒)" : "TTL seconds";
    public static string TtlSecondsDescription => UseJapanese
        ? "キャッシュされた検索結果が期限切れになるまでの秒数。"
        : "Number of seconds before cached search results expire.";
    public static string MaxEntriesLabel => UseJapanese ? "最大エントリ数" : "Max entries";
    public static string MaxEntriesDescription => UseJapanese
        ? "LRU キャッシュに保持される検索結果エントリの最大数。"
        : "Maximum number of search result entries kept in the LRU cache.";
    public static string PersistWorkspaceStateLabel => UseJapanese ? "ワークスペースの状態を保持する" : "Persist workspace state";
    public static string PersistWorkspaceStateDescription => UseJapanese
        ? "現在のソリューションの .vs ディレクトリに検索キャッシュを保持します。"
        : "Persist the search cache under the current solution's .vs directory.";

    // --- Adapters ---
    public static string AdaptersPageTitle => UseJapanese ? "アダプター" : "Adapters";
    public static string MailLabel => UseJapanese ? "メール" : "Mail";
    public static string MailDescription => UseJapanese ? "Exchange / Outlook 検索を有効にします。" : "Enable Exchange / Outlook search.";
    public static string TeamsLabel => "Teams";
    public static string TeamsDescription => UseJapanese ? "Teams メッセージ検索を有効にします。" : "Enable Teams message search.";
    public static string SharePointLabel => "SharePoint";
    public static string SharePointDescription => UseJapanese ? "SharePoint 検索を有効にします。" : "Enable SharePoint search.";
    public static string OneDriveLabel => "OneDrive";
    public static string OneDriveDescription => UseJapanese ? "OneDrive 検索を有効にします。" : "Enable OneDrive search.";
    public static string ConnectorsLabel => UseJapanese ? "コネクタ" : "Connectors";
    public static string ConnectorsDescription => UseJapanese
        ? "Graph コネクタの外部項目の取得を有効にします。"
        : "Enable external-item retrieval for Graph connectors.";

    public static string GetCloudEnvironmentDisplayName(CloudEnvironment environment)
    {
        return CloudEndpoints.GetDisplayName(environment);
    }
}
