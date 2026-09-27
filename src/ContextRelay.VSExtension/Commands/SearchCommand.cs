using System.Threading;
using System.Threading.Tasks;
using ContextRelay.VSExtension.ToolWindows;
using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.Commands;

namespace ContextRelay.VSExtension.Commands;

[VisualStudioContribution]
internal sealed class SearchCommand : Command
{
    public SearchCommand(VisualStudioExtensibility extensibility)
        : base(extensibility) { }

    public override CommandConfiguration CommandConfiguration => new("%ContextRelay.Command.Search.DisplayName%")
    {
    };

    public override async Task ExecuteCommandAsync(IClientContext context, CancellationToken cancellationToken)
    {
        await Extensibility.Shell().ShowToolWindowAsync<ContextRelayToolWindowDef>(activate: true, CancellationToken.None).ConfigureAwait(false);
    }
}
