using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using ContextRelay.VSExtension.Commands;
using ContextRelay.VSExtension.Options;
using ContextRelay.VSExtension.Services;
using ContextRelay.VSExtension.ToolWindows;
using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;

namespace ContextRelay.VSExtension;

[PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
[InstalledProductRegistration("ContextRelay", "ContextRelay for Visual Studio", "0.1.0")]
[ProvideMenuResource("Menus.ctmenu", 1)]
[ProvideOptionPage(typeof(ContextRelayGeneralOptionsPage), "ContextRelay", "General", 0, 0, true)]
[ProvideOptionPage(typeof(ContextRelayAuthenticationOptionsPage), "ContextRelay", "Authentication", 0, 0, true)]
[ProvideOptionPage(typeof(ContextRelayGraphApiOptionsPage), "ContextRelay", "Graph API", 0, 0, true)]
[ProvideOptionPage(typeof(ContextRelayCacheOptionsPage), "ContextRelay", "Cache", 0, 0, true)]
[ProvideOptionPage(typeof(ContextRelayAdaptersOptionsPage), "ContextRelay", "Adapters", 0, 0, true)]
[ProvideToolWindow(typeof(ContextRelayToolWindow))]
[Guid(ContextRelayPackageGuids.PackageString)]
public sealed class ContextRelayPackage : AsyncPackage
{
    internal static ContextRelayPackage? Instance { get; private set; }

    internal ContextRelayHost Host { get; private set; } = null!;

    internal string ExtensionVersion => typeof(ContextRelayPackage).Assembly.GetName().Version?.ToString() ?? "0.1.0";

    protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
    {
        Instance = this;

        var logger = new ContextRelayOutputLogger(this);
        await logger.InitializeAsync().ConfigureAwait(true);

        Host = new ContextRelayHost(this, logger);
        await Host.InitializeAsync().ConfigureAwait(false);
        await ContextRelayCommandSet.InitializeAsync(this).ConfigureAwait(false);
    }

    internal ContextRelaySettingsSnapshot GetSettingsSnapshot()
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        var general = (ContextRelayGeneralOptionsPage)GetDialogPage(typeof(ContextRelayGeneralOptionsPage));
        var auth = (ContextRelayAuthenticationOptionsPage)GetDialogPage(typeof(ContextRelayAuthenticationOptionsPage));
        var graphApi = (ContextRelayGraphApiOptionsPage)GetDialogPage(typeof(ContextRelayGraphApiOptionsPage));
        var cache = (ContextRelayCacheOptionsPage)GetDialogPage(typeof(ContextRelayCacheOptionsPage));
        var adapters = (ContextRelayAdaptersOptionsPage)GetDialogPage(typeof(ContextRelayAdaptersOptionsPage));

        return new ContextRelaySettingsSnapshot
        {
            MaxResults = general.MaxResults,
            OutputDirectory = general.OutputDirectory,
            EnableChatPreview = general.EnableChatPreview,
            EnableGraphDebugLogging = general.EnableGraphDebugLogging,
            ClientId = auth.ClientId,
            TenantId = auth.TenantId,
            CloudEnvironment = graphApi.CloudEnvironment,
            CustomGraphEndpoint = graphApi.CustomGraphEndpoint,
            CustomAuthEndpoint = graphApi.CustomAuthEndpoint,
            CacheTtlSeconds = cache.TtlSeconds,
            CacheMaxEntries = cache.MaxEntries,
            PersistWorkspaceState = cache.PersistWorkspaceState,
            MailEnabled = adapters.Mail,
            TeamsEnabled = adapters.Teams,
            SharePointEnabled = adapters.SharePoint,
            OneDriveEnabled = adapters.OneDrive,
            ConnectorsEnabled = adapters.Connectors
        };
    }

    internal async Task<ContextRelaySettingsSnapshot> GetSettingsSnapshotAsync(CancellationToken cancellationToken = default)
    {
        await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
        return GetSettingsSnapshot();
    }

    internal void OpenSettings()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        ShowOptionPage(typeof(ContextRelayGeneralOptionsPage));
    }

    internal async Task<string?> GetSolutionRootAsync(CancellationToken cancellationToken = default)
    {
        await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
        var dte = await GetServiceAsync(typeof(DTE)).ConfigureAwait(true) as DTE2;
        var solutionFullName = dte?.Solution?.FullName;
        return string.IsNullOrWhiteSpace(solutionFullName) ? null : Path.GetDirectoryName(solutionFullName);
    }

    internal async Task OpenDocumentAsync(string filePath, CancellationToken cancellationToken = default)
    {
        await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
        var dte = await GetServiceAsync(typeof(DTE)).ConfigureAwait(true) as DTE2;
        dte?.ItemOperations?.OpenFile(filePath);
    }

    internal async Task<bool> TryOpenCopilotChatAsync(CancellationToken cancellationToken = default)
    {
        await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
        var dte = await GetServiceAsync(typeof(DTE)).ConfigureAwait(true) as DTE2;
        if (dte is null)
        {
            return false;
        }

        return TryExecuteCommand(dte, "View.GitHubCopilotChat") ||
            TryExecuteCommand(dte, "GitHub.Copilot.OpenCopilotChat");
    }

    internal async Task ShowContextRelayToolWindowAsync(bool focusSearch)
    {
        await JoinableTaskFactory.SwitchToMainThreadAsync(DisposalToken);
        var window = await ShowToolWindowAsync(typeof(ContextRelayToolWindow), 0, true, DisposalToken).ConfigureAwait(true);
        if (window?.Frame is null)
        {
            throw new NotSupportedException("Unable to create ContextRelay tool window.");
        }

        Microsoft.VisualStudio.ErrorHandler.ThrowOnFailure(((Microsoft.VisualStudio.Shell.Interop.IVsWindowFrame)window.Frame).Show());
        if (focusSearch && window.Content is ContextRelayToolWindowControl control)
        {
            control.FocusSearchBox();
        }
    }

    private static bool TryExecuteCommand(DTE2 dte, string commandName)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        try
        {
            dte.ExecuteCommand(commandName);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (COMException)
        {
            return false;
        }
    }
}
