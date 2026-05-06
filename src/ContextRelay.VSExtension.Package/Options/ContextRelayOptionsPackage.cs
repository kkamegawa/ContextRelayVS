using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Community.VisualStudio.Toolkit;
using Microsoft.VisualStudio.Shell;

namespace ContextRelay.VSExtension.Package.Options;

/// <summary>
/// Registers the ContextRelay Tools > Options page inside the Visual Studio process.
/// </summary>
[PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
[InstalledProductRegistration("ContextRelay", "ContextRelay options integration.", "0.3.0")]
[ProvideOptionPage(typeof(OptionsProvider.GeneralOptions), "ContextRelay", "General", 0, 0, true, new[] { "contextrelay", "settings", "options" }, IsInUnifiedSettings = false)]
[ProvideProfile(typeof(OptionsProvider.GeneralOptions), "ContextRelay", "General", 0, 0, true)]
[Guid(ContextRelayPackageGuids.OptionsPackageString)]
public sealed class ContextRelayOptionsPackage : ToolkitPackage
{
    /// <inheritdoc />
    protected override Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
    {
        return Task.CompletedTask;
    }
}
