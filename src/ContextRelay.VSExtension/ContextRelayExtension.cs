using System.Runtime.Versioning;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.Extensibility;
using ContextRelay.VSExtension.Services;
using ContextRelay.VSExtension.ToolWindows;

[assembly: SupportedOSPlatform("windows10.0.22621")]

namespace ContextRelay.VSExtension;

[VisualStudioContribution]
internal sealed class ContextRelayExtension : Extension
{
    public override ExtensionConfiguration ExtensionConfiguration => new()
    {
        // PR #43 intentionally kept the net8 tool window provider out-of-process.
        // The in-proc package is only for Options registration; letting devenv.exe try
        // to activate the net8 main extension in-process reintroduces the System.Runtime
        // load failure seen in the ActivityLog.
        Metadata = new(
            // Keep this corrected extension identity stable going forward so Visual Studio
            // upgrades in-place and restores existing tool window frames.
            id: "ContextRelayVS.KazushiKamegawa.d0dd4dd5-7d88-4b80-8d4d-9dd18fa4cf11",
            version: ExtensionAssemblyVersion,
            publisherName: "KazushiKamegawa",
            displayName: "ContextRelay for Visual Studio",
            description: "Surface Microsoft 365 context in a Visual Studio tool window."),
    };

    protected override void InitializeServices(IServiceCollection serviceCollection)
    {
        base.InitializeServices(serviceCollection);
        serviceCollection.AddSingleton<ContextRelaySettingsService>();
        serviceCollection.AddSingleton<ContextRelayOutputLogger>();
        serviceCollection.AddSingleton<IContextRelayPackageServices, ContextRelayVsServices>();
        serviceCollection.AddSingleton<ContextRelayHost>();
    }
}
