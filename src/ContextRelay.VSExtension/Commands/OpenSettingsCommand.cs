using System.Threading;
using System.Threading.Tasks;
using ContextRelay.VSExtension.Services;
using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.Commands;

namespace ContextRelay.VSExtension.Commands;

[VisualStudioContribution]
internal sealed class OpenSettingsCommand : Command
{
    private readonly IContextRelayPackageServices packageServices;

    public OpenSettingsCommand(VisualStudioExtensibility extensibility, IContextRelayPackageServices packageServices)
        : base(extensibility)
    {
        this.packageServices = packageServices;
    }

    public override CommandConfiguration CommandConfiguration => new("ContextRelay Settings")
    {
        Placements = [CommandPlacement.KnownPlacements.ToolsMenu],
    };

    public override async Task ExecuteCommandAsync(IClientContext context, CancellationToken cancellationToken)
    {
        await packageServices.OpenSettingsFileAsync(cancellationToken).ConfigureAwait(false);
    }
}
