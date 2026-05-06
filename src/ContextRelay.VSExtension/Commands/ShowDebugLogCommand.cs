using System.Threading;
using System.Threading.Tasks;
using ContextRelay.VSExtension.Services;
using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.Commands;

namespace ContextRelay.VSExtension.Commands;

[VisualStudioContribution]
internal sealed class ShowDebugLogCommand : Command
{
    private readonly ContextRelayHost host;

    public ShowDebugLogCommand(VisualStudioExtensibility extensibility, ContextRelayHost host)
        : base(extensibility)
    {
        this.host = host;
    }

    public override CommandConfiguration CommandConfiguration => new("%ContextRelay.Command.ShowDebugLog.DisplayName%")
    {
    };

    public override Task ExecuteCommandAsync(IClientContext context, CancellationToken cancellationToken)
    {
        host.ShowDebugLog();
        return Task.CompletedTask;
    }
}
