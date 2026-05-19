using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using ContextRelay.Core.Adapters;
using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.Documents;

namespace ContextRelay.VSExtension.Services;

internal sealed class ContextRelayOutputLogger : IGraphLogger, IWorkIqLogger
{
    private readonly VisualStudioExtensibility extensibility;
    private readonly SemaphoreSlim initializationGate = new(1, 1);
    private OutputChannel? outputChannel;
    private OutputChannel? debugChannel;
    private volatile bool graphDebugLoggingEnabled;
    private volatile bool workIqDebugLoggingEnabled;

    public ContextRelayOutputLogger(VisualStudioExtensibility extensibility)
    {
        this.extensibility = extensibility;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (outputChannel is not null && debugChannel is not null)
        {
            return;
        }

        await initializationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (outputChannel is not null && debugChannel is not null)
            {
                return;
            }

            outputChannel = await extensibility.Views().Output.CreateOutputChannelAsync(
                "ContextRelay", cancellationToken).ConfigureAwait(false);
            debugChannel = await extensibility.Views().Output.CreateOutputChannelAsync(
                "ContextRelay Debug", cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            initializationGate.Release();
        }
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
        WriteOutput($"[{DateTimeOffset.UtcNow:O}] {message}");
    }

    public void LogWarning(string message)
    {
        WriteOutput($"[{DateTimeOffset.UtcNow:O}] WARNING: {message}");
    }

    public void LogError(string message, Exception? exception = null)
    {
        WriteOutput($"[{DateTimeOffset.UtcNow:O}] ERROR: {message}");
        if (exception is not null)
        {
            WriteDebug(exception.ToString());
        }
    }

    public void ShowDebugPane()
    {
        // Write a marker entry and attempt channel activation when the SDK exposes it.
        WriteDebug("Debug log activated.");
        ObserveFailures(EnsureDebugPaneVisibleAsync());
    }

    public void SetDebugLoggingEnabled(bool graphEnabled, bool workIqEnabled)
    {
        graphDebugLoggingEnabled = graphEnabled;
        workIqDebugLoggingEnabled = workIqEnabled;
    }

    private void WriteOutput(string message) => WriteLineToChannel(() => outputChannel, message);

    private void WriteDebug(string message) => WriteLineToChannel(() => debugChannel, message);

    private void WriteLineToChannel(Func<OutputChannel?> getChannel, string message)
    {
        var channel = getChannel();
        if (channel is null)
        {
            ObserveFailures(InitializeAndWriteLineAsync(getChannel, message));
            return;
        }

        ObserveFailures(WriteLineAsync(channel, message));
    }

    private async Task InitializeAndWriteLineAsync(Func<OutputChannel?> getChannel, string message)
    {
        await InitializeAsync().ConfigureAwait(false);
        var channel = getChannel();
        if (channel is not null)
        {
            await channel.WriteLineAsync(message).ConfigureAwait(false);
        }
    }

    private static async Task WriteLineAsync(OutputChannel channel, string message)
    {
        await channel.WriteLineAsync(message).ConfigureAwait(false);
    }

    private async Task EnsureDebugPaneVisibleAsync()
    {
        await InitializeAsync().ConfigureAwait(false);
        var channel = debugChannel;
        if (channel is null)
        {
            return;
        }

        await TryActivateOutputChannelAsync(channel).ConfigureAwait(false);
    }

    private static async Task TryActivateOutputChannelAsync(OutputChannel channel)
    {
        // SDK surface varies across versions. Use reflection to call the first available
        // activation method instead of relying on a single API name.
        foreach (var methodName in new[] { "ShowAsync", "ActivateAsync", "OpenAsync", "Show", "Activate", "Open" })
        {
            var method = channel.GetType().GetMethod(methodName, BindingFlags.Public | BindingFlags.Instance);
            if (method is null)
            {
                continue;
            }

            var parameters = method.GetParameters();
            object? invocationResult = null;
            if (parameters.Length == 0)
            {
                invocationResult = method.Invoke(channel, null);
            }
            else if (parameters.Length == 1 && parameters[0].ParameterType == typeof(CancellationToken))
            {
                invocationResult = method.Invoke(channel, new object[] { CancellationToken.None });
            }
            else
            {
                continue;
            }

            if (invocationResult is Task activationTask)
            {
                await activationTask.ConfigureAwait(false);
            }

            return;
        }
    }

    private static void ObserveFailures(Task task)
    {
        if (task.IsCompletedSuccessfully)
        {
            return;
        }

        _ = task.ContinueWith(
            static faultedTask => System.Diagnostics.Debug.WriteLine(faultedTask.Exception),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }
}
