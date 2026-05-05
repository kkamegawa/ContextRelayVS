using System.Threading;
using System.Threading.Tasks;
using ContextRelay.VSExtension.Services;
using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.Commands;

namespace ContextRelay.VSExtension.Commands;

[VisualStudioContribution]
internal sealed class GenerateHandoffCommand : Command
{
    private readonly ContextRelayHost host;

    public GenerateHandoffCommand(VisualStudioExtensibility extensibility, ContextRelayHost host)
        : base(extensibility)
    {
        this.host = host;
    }

    public override CommandConfiguration CommandConfiguration => new("%ContextRelay.Command.GenerateHandoff.DisplayName%")
    {
        Placements = [CommandPlacement.KnownPlacements.ToolsMenu],
    };

    public override async Task ExecuteCommandAsync(IClientContext context, CancellationToken cancellationToken)
    {
        await host.GenerateHandoffAsync(cancellationToken).ConfigureAwait(false);
    }
}
