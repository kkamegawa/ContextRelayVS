using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Community.VisualStudio.Toolkit;
using ContextRelay.Core.Auth;
using ContextRelay.Core.Settings;

namespace ContextRelay.VSExtension.Package.Options;

/// <summary>
/// Provides the ContextRelay options page registration used by Visual Studio.
/// </summary>
internal partial class OptionsProvider
{
    /// <summary>
    /// Hosts the ContextRelay settings page in Tools &gt; Options.
    /// </summary>
    [ComVisible(true)]
    [Guid(ContextRelayPackageGuids.OptionsPageString)]
    public sealed class GeneralOptions : BaseOptionPage<ContextRelayOptionsModel>
    {
    }
}

/// <summary>
/// Represents the editable ContextRelay settings shown in the Visual Studio options UI.
/// </summary>
public sealed class ContextRelayOptionsModel : BaseOptionModel<ContextRelayOptionsModel>
{
    /// <summary>
    /// Gets or sets the maximum number of results returned for each ContextRelay search.
    /// </summary>
    [Category("General")]
    [DisplayName("Max results")]
    [Description("Specifies the maximum number of results returned for each ContextRelay search.")]
    [DefaultValue(10)]
    public int MaxResults { get; set; } = 10;

    /// <summary>
    /// Gets or sets the relative output directory used for generated handoff files.
    /// </summary>
    [Category("General")]
    [DisplayName("Output directory")]
    [Description("Specifies the relative output directory used for generated handoff files.")]
    [DefaultValue(".contextrelay")]
    public string OutputDirectory { get; set; } = ".contextrelay";

    /// <summary>
    /// Gets or sets a value indicating whether preview chat features are enabled.
    /// </summary>
    [Category("General")]
    [DisplayName("Enable chat preview")]
    [Description("Enables preview chat features such as /ask and Copilot chat integration.")]
    [DefaultValue(true)]
    public bool EnableChatPreview { get; set; } = true;

    /// <summary>
    /// Gets or sets the preferred ContextRelay UI language.
    /// </summary>
    [Category("General")]
    [DisplayName("UI language")]
    [Description("Selects the ContextRelay UI language. Use 'auto' to follow the Visual Studio language.")]
    [DefaultValue("auto")]
    public string UiLanguage { get; set; } = "auto";

    /// <summary>
    /// Gets or sets a value indicating whether verbose Microsoft Graph logging is enabled.
    /// </summary>
    [Category("Diagnostics")]
    [DisplayName("Enable Graph debug logging")]
    [Description("Enables verbose debug logging for Microsoft Graph requests.")]
    [DefaultValue(false)]
    public bool EnableGraphDebugLogging { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether verbose Work IQ logging is enabled.
    /// </summary>
    [Category("Diagnostics")]
    [DisplayName("Enable Work IQ debug logging")]
    [Description("Enables verbose debug logging for Work IQ requests.")]
    [DefaultValue(false)]
    public bool EnableWorkIqDebugLogging { get; set; }

    /// <summary>
    /// Gets or sets the Microsoft Entra application client identifier.
    /// </summary>
    [Category("Authentication")]
    [DisplayName("Client ID")]
    [Description("Specifies the Microsoft Entra application client ID used for sign-in.")]
    [DefaultValue("")]
    public string ClientId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the tenant identifier used for sign-in.
    /// </summary>
    [Category("Authentication")]
    [DisplayName("Tenant ID")]
    [Description("Specifies the tenant ID or 'organizations' when using multi-tenant sign-in.")]
    [DefaultValue("organizations")]
    public string TenantId { get; set; } = "organizations";

    /// <summary>
    /// Gets or sets the Microsoft cloud environment used by ContextRelay.
    /// </summary>
    [Category("Authentication")]
    [DisplayName("Cloud environment")]
    [Description("Selects the Microsoft cloud environment used by ContextRelay.")]
    [DefaultValue(CloudEnvironment.Global)]
    public CloudEnvironment CloudEnvironment { get; set; } = CloudEnvironment.Global;

    /// <summary>
    /// Gets or sets the custom Microsoft Graph endpoint.
    /// </summary>
    [Category("Authentication")]
    [DisplayName("Custom Graph endpoint")]
    [Description("Overrides the Microsoft Graph endpoint when a custom cloud configuration is required.")]
    [DefaultValue("")]
    public string CustomGraphEndpoint { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the custom authentication endpoint.
    /// </summary>
    [Category("Authentication")]
    [DisplayName("Custom auth endpoint")]
    [Description("Overrides the Microsoft identity authority endpoint when a custom cloud configuration is required.")]
    [DefaultValue("")]
    public string CustomAuthEndpoint { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether brokered authentication is used when available.
    /// </summary>
    [Category("Authentication")]
    [DisplayName("Use broker")]
    [Description("Uses brokered authentication when available.")]
    [DefaultValue(false)]
    public bool UseBroker { get; set; }

    /// <summary>
    /// Gets or sets the cache time-to-live in seconds.
    /// </summary>
    [Category("Caching")]
    [DisplayName("Cache TTL seconds")]
    [Description("Specifies how long cached search results remain valid.")]
    [DefaultValue(300)]
    public int CacheTtlSeconds { get; set; } = 300;

    /// <summary>
    /// Gets or sets the maximum number of cached search result entries.
    /// </summary>
    [Category("Caching")]
    [DisplayName("Cache max entries")]
    [Description("Specifies the maximum number of cached search result entries.")]
    [DefaultValue(200)]
    public int CacheMaxEntries { get; set; } = 200;

    /// <summary>
    /// Gets or sets a value indicating whether workspace-specific cache and state are persisted.
    /// </summary>
    [Category("Caching")]
    [DisplayName("Persist workspace state")]
    [Description("Persists workspace-specific cache and state between sessions.")]
    [DefaultValue(true)]
    public bool PersistWorkspaceState { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether Exchange mail search integration is enabled.
    /// </summary>
    [Category("Features")]
    [DisplayName("Mail")]
    [Description("Enables Exchange mail search integration.")]
    [DefaultValue(true)]
    public bool MailEnabled { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether Microsoft Teams search integration is enabled.
    /// </summary>
    [Category("Features")]
    [DisplayName("Teams")]
    [Description("Enables Microsoft Teams message search integration.")]
    [DefaultValue(true)]
    public bool TeamsEnabled { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether SharePoint search integration is enabled.
    /// </summary>
    [Category("Features")]
    [DisplayName("SharePoint")]
    [Description("Enables SharePoint content search integration.")]
    [DefaultValue(true)]
    public bool SharePointEnabled { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether OneDrive search integration is enabled.
    /// </summary>
    [Category("Features")]
    [DisplayName("OneDrive")]
    [Description("Enables OneDrive content search integration.")]
    [DefaultValue(true)]
    public bool OneDriveEnabled { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether Microsoft 365 connector search integration is enabled.
    /// </summary>
    [Category("Features")]
    [DisplayName("Connectors")]
    [Description("Enables Microsoft 365 connector content search integration.")]
    [DefaultValue(false)]
    public bool ConnectorsEnabled { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether OneNote search integration is enabled.
    /// </summary>
    [Category("Features")]
    [DisplayName("OneNote")]
    [Description("Enables OneNote page search integration.")]
    [DefaultValue(false)]
    public bool OneNoteEnabled { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether Microsoft Planner task search integration is enabled.
    /// </summary>
    [Category("Features")]
    [DisplayName("Planner")]
    [Description("Enables Microsoft Planner task search integration.")]
    [DefaultValue(false)]
    public bool PlannerEnabled { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether Microsoft To Do task search integration is enabled.
    /// </summary>
    [Category("Features")]
    [DisplayName("To Do")]
    [Description("Enables Microsoft To Do task search integration.")]
    [DefaultValue(false)]
    public bool TodoEnabled { get; set; }

    /// <inheritdoc />
    public override void Load()
    {
        LoadSnapshot(ContextRelaySettingsStore.LoadSettings());
    }

    /// <inheritdoc />
    public override Task LoadAsync()
    {
        Load();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public override void Save()
    {
        var settings = CreateSettingsSnapshot();
        settings.UiLanguage = ContextRelaySettingsStore.NormalizeUiLanguage(settings.UiLanguage);
        ContextRelaySettingsStore.SaveSettings(settings);
        LoadSnapshot(settings);
    }

    /// <inheritdoc />
    public override Task SaveAsync()
    {
        Save();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Applies a persisted settings snapshot to the editable model.
    /// </summary>
    /// <param name="settings">The settings snapshot to load.</param>
    internal void LoadSnapshot(ContextRelaySettingsSnapshot settings)
    {
        if (settings is null)
        {
            throw new ArgumentNullException(nameof(settings));
        }

        MaxResults = settings.MaxResults;
        OutputDirectory = settings.OutputDirectory;
        EnableChatPreview = settings.EnableChatPreview;
        UiLanguage = ContextRelaySettingsStore.NormalizeUiLanguage(settings.UiLanguage);
        EnableGraphDebugLogging = settings.EnableGraphDebugLogging;
        EnableWorkIqDebugLogging = settings.EnableWorkIqDebugLogging;
        ClientId = settings.ClientId;
        TenantId = settings.TenantId;
        CloudEnvironment = settings.CloudEnvironment;
        CustomGraphEndpoint = settings.CustomGraphEndpoint;
        CustomAuthEndpoint = settings.CustomAuthEndpoint;
        UseBroker = settings.UseBroker;
        CacheTtlSeconds = settings.CacheTtlSeconds;
        CacheMaxEntries = settings.CacheMaxEntries;
        PersistWorkspaceState = settings.PersistWorkspaceState;
        MailEnabled = settings.MailEnabled;
        TeamsEnabled = settings.TeamsEnabled;
        SharePointEnabled = settings.SharePointEnabled;
        OneDriveEnabled = settings.OneDriveEnabled;
        ConnectorsEnabled = settings.ConnectorsEnabled;
        OneNoteEnabled = settings.OneNoteEnabled;
        PlannerEnabled = settings.PlannerEnabled;
        TodoEnabled = settings.TodoEnabled;
    }

    /// <summary>
    /// Creates a snapshot that matches the format consumed by the shared settings store.
    /// </summary>
    /// <returns>The persisted settings snapshot.</returns>
    internal ContextRelaySettingsSnapshot CreateSettingsSnapshot()
    {
        return new ContextRelaySettingsSnapshot
        {
            MaxResults = MaxResults,
            OutputDirectory = OutputDirectory ?? string.Empty,
            EnableChatPreview = EnableChatPreview,
            UiLanguage = ContextRelaySettingsStore.NormalizeUiLanguage(UiLanguage),
            EnableGraphDebugLogging = EnableGraphDebugLogging,
            EnableWorkIqDebugLogging = EnableWorkIqDebugLogging,
            ClientId = ClientId ?? string.Empty,
            TenantId = TenantId ?? string.Empty,
            CloudEnvironment = CloudEnvironment,
            CustomGraphEndpoint = CustomGraphEndpoint ?? string.Empty,
            CustomAuthEndpoint = CustomAuthEndpoint ?? string.Empty,
            UseBroker = UseBroker,
            CacheTtlSeconds = CacheTtlSeconds,
            CacheMaxEntries = CacheMaxEntries,
            PersistWorkspaceState = PersistWorkspaceState,
            MailEnabled = MailEnabled,
            TeamsEnabled = TeamsEnabled,
            SharePointEnabled = SharePointEnabled,
            OneDriveEnabled = OneDriveEnabled,
            ConnectorsEnabled = ConnectorsEnabled,
            OneNoteEnabled = OneNoteEnabled,
            PlannerEnabled = PlannerEnabled,
            TodoEnabled = TodoEnabled,
        };
    }
}
