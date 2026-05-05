using System;
using System.Threading;
using System.Threading.Tasks;
using ContextRelay.VSExtension.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.ToolWindows;
using Microsoft.VisualStudio.RpcContracts.RemoteUI;

namespace ContextRelay.VSExtension.ToolWindows;

[VisualStudioContribution]
internal sealed class ContextRelayToolWindowDef : ToolWindow
{
    private readonly IServiceProvider serviceProvider;
    private ContextRelayHost? host;

    public ContextRelayToolWindowDef(VisualStudioExtensibility extensibility, IServiceProvider serviceProvider)
        : base(extensibility)
    {
        this.serviceProvider = serviceProvider;
        Title = "ContextRelay";
    }

    public override ToolWindowConfiguration ToolWindowConfiguration => new()
    {
        Placement = ToolWindowPlacement.DocumentWell,
    };

    public override async Task<IRemoteUserControl> GetContentAsync(CancellationToken cancellationToken)
    {
        var hostInstance = host ??= serviceProvider.GetRequiredService<ContextRelayHost>();

        await hostInstance.InitializeAsync(cancellationToken).ConfigureAwait(false);
        var viewModel = new ContextRelayWindowViewModel(hostInstance);
        await viewModel.InitializeAsync(cancellationToken).ConfigureAwait(false);
        hostInstance.StartDeferredSignedInUserResolution();
        return new ContextRelayWindowContent(viewModel);
    }
}
