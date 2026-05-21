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

        // Keep deferred initialization independent from transient shell cancellation so
        // frame construction does not fail when the open-window command token is canceled.
        ObserveInitialization(InitializeToolWindowAsync(hostInstance, viewModel, CancellationToken.None));
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
            // Tool window initialization can be canceled by shell lifecycle operations.
            // Ignore and let the existing content remain available.
        }
        catch (OperationCanceledException ex)
        {
            // Unexpected cancellations should be surfaced for diagnostics instead of being silently ignored.
            await hostInstance.ReportToolWindowInitializationFailureAsync(ex, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await hostInstance.ReportToolWindowInitializationFailureAsync(ex, CancellationToken.None).ConfigureAwait(false);
        }
    }
}
