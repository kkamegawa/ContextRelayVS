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
        // RequiresInProcessHosting = true is required for VSSDK+VisualStudio.Extensibility hybrid mode.
        // When set to true, Metadata must be null - it is read from source.extension.vsixmanifest instead.
        RequiresInProcessHosting = true,
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
