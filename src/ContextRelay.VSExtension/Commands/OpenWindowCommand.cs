using System.Threading;
using System.Threading.Tasks;
using ContextRelay.VSExtension.ToolWindows;
using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.Commands;

namespace ContextRelay.VSExtension.Commands;

[VisualStudioContribution]
internal sealed class OpenWindowCommand : Command
{
    public OpenWindowCommand(VisualStudioExtensibility extensibility)
        : base(extensibility) { }

    public override CommandConfiguration CommandConfiguration => new("Open ContextRelay")
    {
        Placements =
        [
            CommandPlacement.KnownPlacements.ToolsMenu,
            CommandPlacement.KnownPlacements.ViewOtherWindowsMenu,
        ],
    };

    public override async Task ExecuteCommandAsync(IClientContext context, CancellationToken cancellationToken)
    {
        await Extensibility.Shell().ShowToolWindowAsync<ContextRelayToolWindowDef>(activate: true, cancellationToken).ConfigureAwait(false);
    }
}
