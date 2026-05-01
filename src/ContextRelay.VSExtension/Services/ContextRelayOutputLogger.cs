using System;
using System.Threading.Tasks;
using ContextRelay.Core.Adapters;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace ContextRelay.VSExtension.Services;

internal sealed class ContextRelayOutputLogger : IGraphLogger, IWorkIqLogger
{
    private static readonly Guid OutputPaneGuid = new("955732fc-d13a-4c6b-8a1f-e3a525f2d4f0");
    private static readonly Guid DebugPaneGuid = new("ffcb490d-b2b1-49c0-a5ef-24d89848c283");
    private readonly ContextRelayPackage package;
    private IVsOutputWindowPane? outputPane;
    private IVsOutputWindowPane? debugPane;
    private volatile bool graphDebugLoggingEnabled;
    private volatile bool workIqDebugLoggingEnabled;

    public ContextRelayOutputLogger(ContextRelayPackage package)
    {
        this.package = package;
    }

    public async Task InitializeAsync()
    {
        await package.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);

        var outputWindow = await package.GetServiceAsync(typeof(SVsOutputWindow)).ConfigureAwait(true) as IVsOutputWindow;
        if (outputWindow is null)
        {
            throw new InvalidOperationException("SVsOutputWindow is unavailable.");
        }

        Microsoft.VisualStudio.ErrorHandler.ThrowOnFailure(outputWindow.CreatePane(OutputPaneGuid, "ContextRelay", 1, 1));
        Microsoft.VisualStudio.ErrorHandler.ThrowOnFailure(outputWindow.CreatePane(DebugPaneGuid, "ContextRelay Debug", 1, 1));
        Microsoft.VisualStudio.ErrorHandler.ThrowOnFailure(outputWindow.GetPane(OutputPaneGuid, out outputPane));
        Microsoft.VisualStudio.ErrorHandler.ThrowOnFailure(outputWindow.GetPane(DebugPaneGuid, out debugPane));
    }

    public void Log(string message)
    {
        if (!graphDebugLoggingEnabled)
        {
            return;
        }

        WriteDebug($"[{DateTimeOffset.UtcNow:O}] {message}");
    }

    public void LogWorkIq(string message)
    {
        if (!workIqDebugLoggingEnabled)
        {
            return;
        }

        WriteDebug($"[{DateTimeOffset.UtcNow:O}] {message}");
    }

    public void LogInformation(string message)
    {
        ActivityLog.LogInformation("ContextRelay", message);
        WriteOutput($"[{DateTimeOffset.UtcNow:O}] {message}");
    }

    public void LogWarning(string message)
    {
        ActivityLog.LogWarning("ContextRelay", message);
        WriteOutput($"[{DateTimeOffset.UtcNow:O}] WARNING: {message}");
    }

    public void LogError(string message, Exception? exception = null)
    {
        ActivityLog.LogError("ContextRelay", exception is null ? message : $"{message}: {exception}");
        WriteOutput($"[{DateTimeOffset.UtcNow:O}] ERROR: {message}");
        if (exception is not null)
        {
            WriteDebug(exception.ToString());
        }
    }

    public void ShowDebugPane()
    {
        _ = package.JoinableTaskFactory.RunAsync(async delegate
        {
            await package.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);
            debugPane?.Activate();
        });
    }

    public void SetDebugLoggingEnabled(bool graphEnabled, bool workIqEnabled)
    {
        graphDebugLoggingEnabled = graphEnabled;
        workIqDebugLoggingEnabled = workIqEnabled;
    }

    private void WriteOutput(string message)
    {
        WriteLine(outputPane, message);
    }

    private void WriteDebug(string message)
    {
        WriteLine(debugPane, message);
    }

    private void WriteLine(IVsOutputWindowPane? pane, string message)
    {
        if (pane is null)
        {
            return;
        }

        _ = package.JoinableTaskFactory.RunAsync(async delegate
        {
            await package.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);
            pane.OutputStringThreadSafe(message + Environment.NewLine);
        });
    }
}
