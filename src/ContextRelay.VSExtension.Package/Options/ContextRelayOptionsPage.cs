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
/// Presents the supported UI languages as a fixed drop-down list; the stored values remain auto, en and ja.
/// </summary>
internal sealed class UiLanguageConverter : StringConverter
{
    private static readonly string[] Values = { "auto", "en", "ja" };

    public override bool GetStandardValuesSupported(ITypeDescriptorContext? context) => true;

    public override bool GetStandardValuesExclusive(ITypeDescriptorContext? context) => true;

    public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext? context) => new(Values);

    public override object? ConvertTo(ITypeDescriptorContext? context, System.Globalization.CultureInfo? culture, object? value, Type destinationType)
    {
        if (destinationType == typeof(string) && value is string text)
        {
            return ContextRelaySettingsStore.NormalizeUiLanguage(text) switch
            {
                "en" => "English (en)",
                "ja" => "日本語 (ja)",
                _ => OptionsLocalization.Get("UiLanguage.Auto"),
            };
        }

        return base.ConvertTo(context, culture, value, destinationType);
    }

    public override object? ConvertFrom(ITypeDescriptorContext? context, System.Globalization.CultureInfo? culture, object value)
    {
        if (value is string text)
        {
            return text.Contains("(en)") ? "en" : text.Contains("(ja)") ? "ja" : text == OptionsLocalization.Get("UiLanguage.Auto") ? "auto" : ContextRelaySettingsStore.NormalizeUiLanguage(text);
        }

        return base.ConvertFrom(context, culture, value);
    }
}

/// <summary>
/// Represents the editable ContextRelay settings shown in the Visual Studio options UI.
/// </summary>
public sealed class ContextRelayOptionsModel : BaseOptionModel<ContextRelayOptionsModel>
{
    private int chatMaxAttachedFiles = 5;
    private string uiLanguage = "auto";
    private string loadedUiLanguage = "auto";

    /// <summary>
    /// Gets or sets the maximum number of results returned for each ContextRelay search.
    /// </summary>
    [LocalizedCategory("GeneralCategory")]
    [LocalizedDisplayName("MaxResults.DisplayName")]
    [LocalizedDescription("MaxResults.Description")]
    [DefaultValue(10)]
    public int MaxResults { get; set; } = 10;

    /// <summary>
    /// Gets or sets the relative output directory used for generated handoff files.
    /// </summary>
    [LocalizedCategory("GeneralCategory")]
    [LocalizedDisplayName("OutputDirectory.DisplayName")]
    [LocalizedDescription("OutputDirectory.Description")]
    [DefaultValue(".contextrelay")]
    public string OutputDirectory { get; set; } = ".contextrelay";

    /// <summary>
    /// Gets or sets a value indicating whether preview chat features are enabled.
    /// </summary>
    [LocalizedCategory("ChatCategory")]
    [LocalizedDisplayName("EnableChatPreview.DisplayName")]
    [LocalizedDescription("EnableChatPreview.Description")]
    [DefaultValue(true)]
    public bool EnableChatPreview { get; set; } = true;

    /// <summary>
    /// Gets or sets the maximum number of files that can be attached to a chat request.
    /// </summary>
    [LocalizedCategory("ChatCategory")]
    [LocalizedDisplayName("ChatMaxAttachedFiles.DisplayName")]
    [LocalizedDescription("ChatMaxAttachedFiles.Description")]
    [DefaultValue(5)]
    public int ChatMaxAttachedFiles
    {
        get => chatMaxAttachedFiles;
        set => chatMaxAttachedFiles = Math.Max(0, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether the active editor is attached to chat requests.
    /// </summary>
    [LocalizedCategory("ChatCategory")]
    [LocalizedDisplayName("ChatAttachActiveEditor.DisplayName")]
    [LocalizedDescription("ChatAttachActiveEditor.Description")]
    [DefaultValue(false)]
    public bool ChatAttachActiveEditor { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether chat responses are streamed as they arrive.
    /// </summary>
    [LocalizedCategory("ChatCategory")]
    [LocalizedDisplayName("ChatStreamResponses.DisplayName")]
    [LocalizedDescription("ChatStreamResponses.Description")]
    [DefaultValue(true)]
    public bool ChatStreamResponses { get; set; } = true;

    /// <summary>
    /// Gets or sets the preferred ContextRelay UI language.
    /// </summary>
    [LocalizedCategory("GeneralCategory")]
    [LocalizedDisplayName("UiLanguage.DisplayName")]
    [LocalizedDescription("UiLanguage.Description")]
    [DefaultValue("auto")]
    [TypeConverter(typeof(UiLanguageConverter))]
    public string UiLanguage
    {
        get => uiLanguage;
        set
        {
            uiLanguage = ContextRelaySettingsStore.NormalizeUiLanguage(value);

            // Apply on assignment so editing this value, loading the shared settings file, or a tool
            // window language change all switch the localized option labels without waiting for Save.
            ApplyUiLanguage(uiLanguage);
        }
    }

    /// <summary>
    /// Gets or sets a value indicating whether verbose Microsoft Graph logging is enabled.
    /// </summary>
    [LocalizedCategory("DiagnosticsCategory")]
    [LocalizedDisplayName("EnableGraphDebugLogging.DisplayName")]
    [LocalizedDescription("EnableGraphDebugLogging.Description")]
    [DefaultValue(false)]
    public bool EnableGraphDebugLogging { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether verbose Work IQ logging is enabled.
    /// </summary>
    [LocalizedCategory("DiagnosticsCategory")]
    [LocalizedDisplayName("EnableWorkIqDebugLogging.DisplayName")]
    [LocalizedDescription("EnableWorkIqDebugLogging.Description")]
    [DefaultValue(false)]
    public bool EnableWorkIqDebugLogging { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether Work IQ queries may include local file content.
    /// </summary>
    [LocalizedCategory("PrivacyCategory")]
    [LocalizedDisplayName("AllowLocalFileContextForWorkIq.DisplayName")]
    [LocalizedDescription("AllowLocalFileContextForWorkIq.Description")]
    [DefaultValue(false)]
    public bool AllowLocalFileContextForWorkIq { get; set; }

    /// <summary>
    /// Gets or sets the Microsoft Entra application client identifier.
    /// </summary>
    [LocalizedCategory("AuthenticationCategory")]
    [LocalizedDisplayName("ClientId.DisplayName")]
    [LocalizedDescription("ClientId.Description")]
    [DefaultValue("")]
    public string ClientId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the tenant identifier used for sign-in.
    /// </summary>
    [LocalizedCategory("AuthenticationCategory")]
    [LocalizedDisplayName("TenantId.DisplayName")]
    [LocalizedDescription("TenantId.Description")]
    [DefaultValue("organizations")]
    public string TenantId { get; set; } = "organizations";

    /// <summary>
    /// Gets or sets the Microsoft cloud environment used by ContextRelay.
    /// </summary>
    [LocalizedCategory("AuthenticationCategory")]
    [LocalizedDisplayName("CloudEnvironment.DisplayName")]
    [LocalizedDescription("CloudEnvironment.Description")]
    [DefaultValue(CloudEnvironment.Global)]
    public CloudEnvironment CloudEnvironment { get; set; } = CloudEnvironment.Global;

    /// <summary>
    /// Gets or sets the custom Microsoft Graph endpoint.
    /// </summary>
    [LocalizedCategory("AuthenticationCategory")]
    [LocalizedDisplayName("CustomGraphEndpoint.DisplayName")]
    [LocalizedDescription("CustomGraphEndpoint.Description")]
    [DefaultValue("")]
    public string CustomGraphEndpoint { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the custom authentication endpoint.
    /// </summary>
    [LocalizedCategory("AuthenticationCategory")]
    [LocalizedDisplayName("CustomAuthEndpoint.DisplayName")]
    [LocalizedDescription("CustomAuthEndpoint.Description")]
    [DefaultValue("")]
    public string CustomAuthEndpoint { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether brokered authentication is used when available.
    /// </summary>
    [LocalizedCategory("AuthenticationCategory")]
    [LocalizedDisplayName("UseBroker.DisplayName")]
    [LocalizedDescription("UseBroker.Description")]
    [DefaultValue(false)]
    public bool UseBroker { get; set; }

    /// <summary>
    /// Gets or sets the cache time-to-live in seconds.
    /// </summary>
    [LocalizedCategory("CachingCategory")]
    [LocalizedDisplayName("CacheTtlSeconds.DisplayName")]
    [LocalizedDescription("CacheTtlSeconds.Description")]
    [DefaultValue(300)]
    public int CacheTtlSeconds { get; set; } = 300;

    /// <summary>
    /// Gets or sets the maximum number of cached search result entries.
    /// </summary>
    [LocalizedCategory("CachingCategory")]
    [LocalizedDisplayName("CacheMaxEntries.DisplayName")]
    [LocalizedDescription("CacheMaxEntries.Description")]
    [DefaultValue(200)]
    public int CacheMaxEntries { get; set; } = 200;

    /// <summary>
    /// Gets or sets a value indicating whether workspace-specific cache and state are persisted.
    /// </summary>
    [LocalizedCategory("CachingCategory")]
    [LocalizedDisplayName("PersistWorkspaceState.DisplayName")]
    [LocalizedDescription("PersistWorkspaceState.Description")]
    [DefaultValue(true)]
    public bool PersistWorkspaceState { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether Exchange mail search integration is enabled.
    /// </summary>
    [LocalizedCategory("SearchSourcesCategory")]
    [LocalizedDisplayName("MailEnabled.DisplayName")]
    [LocalizedDescription("MailEnabled.Description")]
    [DefaultValue(true)]
    public bool MailEnabled { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether Microsoft Teams search integration is enabled.
    /// </summary>
    [LocalizedCategory("SearchSourcesCategory")]
    [LocalizedDisplayName("TeamsEnabled.DisplayName")]
    [LocalizedDescription("TeamsEnabled.Description")]
    [DefaultValue(true)]
    public bool TeamsEnabled { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether SharePoint search integration is enabled.
    /// </summary>
    [LocalizedCategory("SearchSourcesCategory")]
    [LocalizedDisplayName("SharePointEnabled.DisplayName")]
    [LocalizedDescription("SharePointEnabled.Description")]
    [DefaultValue(true)]
    public bool SharePointEnabled { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether OneDrive search integration is enabled.
    /// </summary>
    [LocalizedCategory("SearchSourcesCategory")]
    [LocalizedDisplayName("OneDriveEnabled.DisplayName")]
    [LocalizedDescription("OneDriveEnabled.Description")]
    [DefaultValue(true)]
    public bool OneDriveEnabled { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether Microsoft 365 connector search integration is enabled.
    /// </summary>
    [LocalizedCategory("SearchSourcesCategory")]
    [LocalizedDisplayName("ConnectorsEnabled.DisplayName")]
    [LocalizedDescription("ConnectorsEnabled.Description")]
    [DefaultValue(false)]
    public bool ConnectorsEnabled { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether OneNote search integration is enabled.
    /// </summary>
    [LocalizedCategory("SearchSourcesCategory")]
    [LocalizedDisplayName("OneNoteEnabled.DisplayName")]
    [LocalizedDescription("OneNoteEnabled.Description")]
    [DefaultValue(true)]
    public bool OneNoteEnabled { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether Microsoft Planner task search integration is enabled.
    /// </summary>
    [LocalizedCategory("SearchSourcesCategory")]
    [LocalizedDisplayName("PlannerEnabled.DisplayName")]
    [LocalizedDescription("PlannerEnabled.Description")]
    [DefaultValue(true)]
    public bool PlannerEnabled { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether Microsoft To Do task search integration is enabled.
    /// </summary>
    [LocalizedCategory("SearchSourcesCategory")]
    [LocalizedDisplayName("TodoEnabled.DisplayName")]
    [LocalizedDescription("TodoEnabled.Description")]
    [DefaultValue(true)]
    public bool TodoEnabled { get; set; } = true;

    /// <summary>
    /// Applies the configured ContextRelay UI language to the option labels and refreshes the
    /// property descriptors so the property grid re-reads them after a language change.
    /// </summary>
    /// <param name="uiLanguage">The configured UI language value.</param>
    private void ApplyUiLanguage(string uiLanguage)
    {
        var normalized = ContextRelaySettingsStore.NormalizeUiLanguage(uiLanguage);
        if (string.Equals(normalized, OptionsLocalization.CurrentUiLanguage, StringComparison.Ordinal))
        {
            return;
        }

        OptionsLocalization.SetUiLanguage(normalized);
        TypeDescriptor.Refresh(this);
    }

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
        // Merge only this page's fields into the latest file so the Embedded page and the tool window's
        // language command keep the fields they own.
        var settings = ContextRelaySettingsStore.UpdateSettings(ApplyTo);
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
        ChatMaxAttachedFiles = settings.ChatMaxAttachedFiles;
        ChatAttachActiveEditor = settings.ChatAttachActiveEditor;
        ChatStreamResponses = settings.ChatStreamResponses;
        UiLanguage = settings.UiLanguage;
        EnableGraphDebugLogging = settings.EnableGraphDebugLogging;
        EnableWorkIqDebugLogging = settings.EnableWorkIqDebugLogging;
        AllowLocalFileContextForWorkIq = settings.AllowLocalFileContextForWorkIq;
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
        loadedUiLanguage = uiLanguage;
    }

    /// <summary>
    /// Copies the fields this page owns into a settings snapshot, leaving every other field unchanged.
    /// </summary>
    /// <param name="settings">The latest persisted settings to update.</param>
    internal void ApplyTo(ContextRelaySettingsSnapshot settings)
    {
        if (settings is null)
        {
            throw new ArgumentNullException(nameof(settings));
        }

        settings.MaxResults = MaxResults;
        settings.OutputDirectory = OutputDirectory ?? string.Empty;
        settings.EnableChatPreview = EnableChatPreview;
        settings.ChatMaxAttachedFiles = ChatMaxAttachedFiles;
        settings.ChatAttachActiveEditor = ChatAttachActiveEditor;
        settings.ChatStreamResponses = ChatStreamResponses;
        var currentUiLanguage = ContextRelaySettingsStore.NormalizeUiLanguage(UiLanguage);
        if (!string.Equals(currentUiLanguage, loadedUiLanguage, StringComparison.Ordinal))
        {
            settings.UiLanguage = currentUiLanguage;
        }
        settings.EnableGraphDebugLogging = EnableGraphDebugLogging;
        settings.EnableWorkIqDebugLogging = EnableWorkIqDebugLogging;
        settings.AllowLocalFileContextForWorkIq = AllowLocalFileContextForWorkIq;
        settings.ClientId = ClientId ?? string.Empty;
        settings.TenantId = TenantId ?? string.Empty;
        settings.CloudEnvironment = CloudEnvironment;
        settings.CustomGraphEndpoint = CustomGraphEndpoint ?? string.Empty;
        settings.CustomAuthEndpoint = CustomAuthEndpoint ?? string.Empty;
        settings.UseBroker = UseBroker;
        settings.CacheTtlSeconds = CacheTtlSeconds;
        settings.CacheMaxEntries = CacheMaxEntries;
        settings.PersistWorkspaceState = PersistWorkspaceState;
        settings.MailEnabled = MailEnabled;
        settings.TeamsEnabled = TeamsEnabled;
        settings.SharePointEnabled = SharePointEnabled;
        settings.OneDriveEnabled = OneDriveEnabled;
        settings.ConnectorsEnabled = ConnectorsEnabled;
        settings.OneNoteEnabled = OneNoteEnabled;
        settings.PlannerEnabled = PlannerEnabled;
        settings.TodoEnabled = TodoEnabled;
    }
}
