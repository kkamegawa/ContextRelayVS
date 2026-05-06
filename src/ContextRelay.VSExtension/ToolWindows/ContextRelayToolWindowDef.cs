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

    public override Task<IRemoteUserControl> GetContentAsync(CancellationToken cancellationToken)
    {
        var hostInstance = host ??= serviceProvider.GetRequiredService<ContextRelayHost>();
        var viewModel = new ContextRelayWindowViewModel(hostInstance);
        var content = new ContextRelayWindowContent(viewModel);

        ObserveInitialization(InitializeToolWindowAsync(hostInstance, viewModel, cancellationToken));
        return Task.FromResult<IRemoteUserControl>(content);
    }

    private static void ObserveInitialization(Task task)
    {
        if (task.IsCompletedSuccessfully)
        {
            return;
        }

        _ = task.ContinueWith(
            static failedTask => System.Diagnostics.Debug.WriteLine(failedTask.Exception),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private static async Task InitializeToolWindowAsync(
        ContextRelayHost hostInstance,
        ContextRelayWindowViewModel viewModel,
        CancellationToken cancellationToken)
    {
        try
        {
            await hostInstance.InitializeAsync(cancellationToken).ConfigureAwait(false);
            await viewModel.InitializeAsync(cancellationToken).ConfigureAwait(false);
            hostInstance.StartDeferredSignedInUserResolution();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            await hostInstance.ReportToolWindowInitializationFailureAsync(ex, CancellationToken.None).ConfigureAwait(false);
        }
    }
}
