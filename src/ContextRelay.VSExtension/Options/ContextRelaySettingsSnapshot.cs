using ContextRelay.Core.Auth;

namespace ContextRelay.VSExtension.Options;

internal sealed class ContextRelaySettingsSnapshot
{
    public int MaxResults { get; set; } = 10;

    public string OutputDirectory { get; set; } = ".contextrelay";

    public bool EnableChatPreview { get; set; } = true;

    public bool EnableGraphDebugLogging { get; set; }

    public string ClientId { get; set; } = string.Empty;

    public string TenantId { get; set; } = "organizations";

    public CloudEnvironment CloudEnvironment { get; set; } = CloudEnvironment.Global;

    public string CustomGraphEndpoint { get; set; } = string.Empty;

    public string CustomAuthEndpoint { get; set; } = string.Empty;

    public int CacheTtlSeconds { get; set; } = 300;

    public int CacheMaxEntries { get; set; } = 200;

    public bool PersistWorkspaceState { get; set; } = true;

    public bool MailEnabled { get; set; } = true;

    public bool TeamsEnabled { get; set; } = true;

    public bool SharePointEnabled { get; set; } = true;

    public bool OneDriveEnabled { get; set; } = true;

    public bool ConnectorsEnabled { get; set; }

    public ContextRelayAuthSettings ToAuthSettings()
    {
        return new ContextRelayAuthSettings
        {
            ClientId = ClientId ?? string.Empty,
            TenantId = string.IsNullOrWhiteSpace(TenantId) ? "organizations" : TenantId,
            CloudEnvironment = CloudEnvironment,
            CustomGraphEndpoint = CustomGraphEndpoint ?? string.Empty,
            CustomAuthEndpoint = CustomAuthEndpoint ?? string.Empty
        };
    }

    public ContextRelayFeatureOptions ToFeatureOptions()
    {
        return new ContextRelayFeatureOptions
        {
            MailEnabled = MailEnabled,
            TeamsEnabled = TeamsEnabled,
            SharePointEnabled = SharePointEnabled,
            OneDriveEnabled = OneDriveEnabled,
            ConnectorsEnabled = ConnectorsEnabled,
            ChatPreviewEnabled = EnableChatPreview
        };
    }
}
