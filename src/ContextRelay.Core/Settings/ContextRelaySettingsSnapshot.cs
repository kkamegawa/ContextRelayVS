using ContextRelay.Core.Auth;

namespace ContextRelay.Core.Settings;

/// <summary>
/// Represents the persisted ContextRelay settings shared by all hosts.
/// </summary>
public sealed class ContextRelaySettingsSnapshot
{
    /// <summary>
    /// Gets or sets the maximum number of search results returned for a query.
    /// </summary>
    public int MaxResults { get; set; } = 10;

    /// <summary>
    /// Gets or sets the output directory for generated handoff content.
    /// </summary>
    public string OutputDirectory { get; set; } = ".contextrelay";

    /// <summary>
    /// Gets or sets a value indicating whether preview chat features are enabled.
    /// </summary>
    public bool EnableChatPreview { get; set; } = true;

    /// <summary>
    /// Gets or sets the preferred UI language.
    /// </summary>
    public string UiLanguage { get; set; } = "auto";

    /// <summary>
    /// Gets or sets a value indicating whether Microsoft Graph debug logging is enabled.
    /// </summary>
    public bool EnableGraphDebugLogging { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether Work IQ debug logging is enabled.
    /// </summary>
    public bool EnableWorkIqDebugLogging { get; set; }

    /// <summary>
    /// Gets or sets the Microsoft Entra application client ID.
    /// </summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the tenant identifier used during sign-in.
    /// </summary>
    public string TenantId { get; set; } = "organizations";

    /// <summary>
    /// Gets or sets the cloud environment used for authentication and Graph access.
    /// </summary>
    public CloudEnvironment CloudEnvironment { get; set; } = CloudEnvironment.Global;

    /// <summary>
    /// Gets or sets the custom Microsoft Graph endpoint.
    /// </summary>
    public string CustomGraphEndpoint { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the custom authentication endpoint.
    /// </summary>
    public string CustomAuthEndpoint { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether brokered authentication is enabled.
    /// </summary>
    public bool UseBroker { get; set; }

    /// <summary>
    /// Gets or sets the cache time-to-live in seconds.
    /// </summary>
    public int CacheTtlSeconds { get; set; } = 300;

    /// <summary>
    /// Gets or sets the maximum number of cached entries.
    /// </summary>
    public int CacheMaxEntries { get; set; } = 200;

    /// <summary>
    /// Gets or sets a value indicating whether workspace-specific state is persisted.
    /// </summary>
    public bool PersistWorkspaceState { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether Exchange mail search is enabled.
    /// </summary>
    public bool MailEnabled { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether Teams search is enabled.
    /// </summary>
    public bool TeamsEnabled { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether SharePoint search is enabled.
    /// </summary>
    public bool SharePointEnabled { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether OneDrive search is enabled.
    /// </summary>
    public bool OneDriveEnabled { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether connector search is enabled.
    /// </summary>
    public bool ConnectorsEnabled { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether OneNote search is enabled.
    /// </summary>
    public bool OneNoteEnabled { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether Planner search is enabled.
    /// </summary>
    public bool PlannerEnabled { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether Microsoft To Do search is enabled.
    /// </summary>
    public bool TodoEnabled { get; set; }

    /// <summary>
    /// Converts the settings into authentication settings.
    /// </summary>
    /// <returns>The authentication settings for the current snapshot.</returns>
    public ContextRelayAuthSettings ToAuthSettings()
    {
        return new ContextRelayAuthSettings
        {
            ClientId = ClientId ?? string.Empty,
            TenantId = string.IsNullOrWhiteSpace(TenantId) ? "organizations" : TenantId,
            CloudEnvironment = CloudEnvironment,
            CustomGraphEndpoint = CustomGraphEndpoint ?? string.Empty,
            CustomAuthEndpoint = CustomAuthEndpoint ?? string.Empty,
            UseBroker = UseBroker,
        };
    }

    /// <summary>
    /// Converts the settings into feature options.
    /// </summary>
    /// <returns>The feature options for the current snapshot.</returns>
    public ContextRelayFeatureOptions ToFeatureOptions()
    {
        return new ContextRelayFeatureOptions
        {
            MailEnabled = MailEnabled,
            TeamsEnabled = TeamsEnabled,
            SharePointEnabled = SharePointEnabled,
            OneDriveEnabled = OneDriveEnabled,
            ConnectorsEnabled = ConnectorsEnabled,
            OneNoteEnabled = OneNoteEnabled,
            PlannerEnabled = PlannerEnabled,
            TodoEnabled = TodoEnabled,
            ChatPreviewEnabled = EnableChatPreview,
        };
    }
}