using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Community.VisualStudio.Toolkit;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.ServiceBroker;
using Microsoft.VisualStudio;
using ContextRelay.VisualStudio;
using ContextRelay.VSExtension.Package.Services;

namespace ContextRelay.VSExtension.Package.Options;

/// <summary>
/// Registers the ContextRelay Tools > Options page inside the Visual Studio process.
/// </summary>
[PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
[InstalledProductRegistration("ContextRelay", "ContextRelay options integration.", "0.2.1")]
[ProvideOptionPage(typeof(OptionsProvider.GeneralOptions), "ContextRelay", "General", 0, 0, true, new[] { "contextrelay", "settings", "options" }, IsInUnifiedSettings = false)]
[ProvideProfile(typeof(OptionsProvider.GeneralOptions), "ContextRelay", "General", 0, 0, true)]
// Audience must include PublicSdk: the out-of-process VisualStudio.Extensibility tool window
// requests this service as "Local, PublicSdk", and the shell declines the request with
// ServiceAudienceMismatch when the proffered audience is Local alone (Issue #200).
[ProvideBrokeredService(VisualStudioLanguageService.ServiceName, VisualStudioLanguageService.ServiceVersion, Audience = ServiceAudience.Local | ServiceAudience.PublicSdk)]
[Guid(ContextRelayPackageGuids.OptionsPackageString)]
public sealed class ContextRelayOptionsPackage : ToolkitPackage
{
    private IDisposable? languageServiceProffer;

    /// <inheritdoc />
    protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
    {
        await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
        VisualStudioLanguageServiceExport.CaptureUiLocale();

        var brokeredServices = await GetServiceAsync(typeof(SVsBrokeredServiceContainer)).ConfigureAwait(true)
            as IBrokeredServiceContainer;
        languageServiceProffer = brokeredServices?.Proffer(
            VisualStudioLanguageService.Descriptor,
            VisualStudioLanguageServiceExport.CreateServiceAsync);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            languageServiceProffer?.Dispose();
            languageServiceProffer = null;
        }

        base.Dispose(disposing);
    }
}
