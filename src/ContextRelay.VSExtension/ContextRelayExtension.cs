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
        Metadata = new(
            id: "ContextRelayVS.KazushiKamegawa.d0dd4dd5-7d88-4b80-8d4d-9dd18fa4cf11",
            version: ExtensionAssemblyVersion,
            publisherName: "KazushiKamegawa",
            displayName: "ContextRelay for Visual Studio",
            description: "ContextRelay connects Visual Studio with Microsoft 365 Copilot context."),
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
