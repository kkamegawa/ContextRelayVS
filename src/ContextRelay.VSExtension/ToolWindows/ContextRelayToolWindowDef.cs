using System.Threading;
using System.Threading.Tasks;
using ContextRelay.VSExtension.Services;
using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.ToolWindows;
using Microsoft.VisualStudio.RpcContracts.RemoteUI;

namespace ContextRelay.VSExtension.ToolWindows;

[VisualStudioContribution]
internal sealed class ContextRelayToolWindowDef : ToolWindow
{
    private readonly ContextRelayHost host;

    public ContextRelayToolWindowDef(VisualStudioExtensibility extensibility, ContextRelayHost host)
        : base(extensibility)
    {
        this.host = host;
        Title = "ContextRelay";
    }

    public override ToolWindowConfiguration ToolWindowConfiguration => new()
    {
        Placement = ToolWindowPlacement.DocumentWell,
    };

    public override async Task<IRemoteUserControl> GetContentAsync(CancellationToken cancellationToken)
    {
        await host.InitializeAsync(cancellationToken).ConfigureAwait(false);
        var viewModel = new ContextRelayWindowViewModel(host);
        await viewModel.InitializeAsync(cancellationToken).ConfigureAwait(false);
        return new ContextRelayWindowContent(viewModel);
    }
}
