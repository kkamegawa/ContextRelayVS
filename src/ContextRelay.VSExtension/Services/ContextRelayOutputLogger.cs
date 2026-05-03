using System;
using System.Threading;
using System.Threading.Tasks;
using ContextRelay.Core.Adapters;
using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.Documents;

namespace ContextRelay.VSExtension.Services;

internal sealed class ContextRelayOutputLogger : IGraphLogger, IWorkIqLogger
{
    private readonly VisualStudioExtensibility extensibility;
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
        outputChannel = await extensibility.Views().Output.CreateOutputChannelAsync(
            "ContextRelay", cancellationToken).ConfigureAwait(false);
        debugChannel = await extensibility.Views().Output.CreateOutputChannelAsync(
            "ContextRelay Debug", cancellationToken).ConfigureAwait(false);
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
        // OutputChannel in this SDK version does not expose a Show method;
        // writing to the channel will activate it automatically.
        WriteDebug("Debug log activated.");
    }

    public void SetDebugLoggingEnabled(bool graphEnabled, bool workIqEnabled)
    {
        graphDebugLoggingEnabled = graphEnabled;
        workIqDebugLoggingEnabled = workIqEnabled;
    }

    private void WriteOutput(string message) => WriteLineToChannel(outputChannel, message);

    private void WriteDebug(string message) => WriteLineToChannel(debugChannel, message);

    private static void WriteLineToChannel(OutputChannel? channel, string message)
    {
        if (channel is null)
        {
            return;
        }

        _ = channel.WriteLineAsync(message);
    }
}
