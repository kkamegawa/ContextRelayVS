using System.Threading;
using System.Threading.Tasks;
using ContextRelay.VSExtension.Services;
using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.Commands;

namespace ContextRelay.VSExtension.Commands;

[VisualStudioContribution]
internal sealed class ClearChatCommand : Command
{
    private readonly ContextRelayHost host;

    public ClearChatCommand(VisualStudioExtensibility extensibility, ContextRelayHost host)
        : base(extensibility)
    {
        this.host = host;
    }

    public override CommandConfiguration CommandConfiguration => new("%ContextRelay.Command.ClearChat.DisplayName%")
    {
    };

    public override async Task ExecuteCommandAsync(IClientContext context, CancellationToken cancellationToken)
    {
        await host.ClearChatAsync(cancellationToken).ConfigureAwait(false);
    }
}
