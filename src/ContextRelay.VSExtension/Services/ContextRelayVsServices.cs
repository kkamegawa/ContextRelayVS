using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ContextRelay.VSExtension.Options;
using Microsoft.VisualStudio.Extensibility;

namespace ContextRelay.VSExtension.Services;

internal sealed class ContextRelayVsServices : IContextRelayPackageServices
{
    private readonly VisualStudioExtensibility extensibility;
    private readonly ContextRelaySettingsService settingsService;

    public ContextRelayVsServices(VisualStudioExtensibility extensibility, ContextRelaySettingsService settingsService)
    {
        this.extensibility = extensibility;
        this.settingsService = settingsService;
    }

    public Task<ContextRelaySettingsSnapshot> GetSettingsSnapshotAsync(CancellationToken cancellationToken = default)
        => settingsService.LoadSettingsAsync(cancellationToken);

    public Task<string?> GetSolutionRootAsync(CancellationToken cancellationToken = default)
    {
        // Stub: workspace query API shape varies across SDK versions
        return Task.FromResult<string?>(null);
    }

    public async Task OpenDocumentAsync(string filePath, CancellationToken cancellationToken = default)
    {
        try
        {
            await extensibility.Documents().OpenDocumentAsync(new Uri(filePath, UriKind.Absolute), cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Best effort
        }
    }

    public Task<bool> AppendToActiveDocumentAsync(string text, CancellationToken cancellationToken = default)
    {
        // Editor API stub — requires checking exact VS Extensibility SDK 17.14 editor API signatures
        return Task.FromResult(false);
    }

    public Task<bool> ReplaceActiveDocumentAsync(string text, CancellationToken cancellationToken = default)
    {
        // Editor API stub — requires checking exact VS Extensibility SDK 17.14 editor API signatures
        return Task.FromResult(false);
    }

    public Task<bool> TryOpenCopilotChatAsync(CancellationToken cancellationToken = default)
    {
        // ExecuteCommandAsync is not available on ShellExtensibility in SDK 17.14.
        // Opening Copilot Chat from out-of-process extension is not supported in this version.
        return Task.FromResult(false);
    }

    public async Task CopyTextToClipboardAsync(string text, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

#pragma warning disable CA1416 // net8.0-windows target — clipboard STA thread is Windows-only
        var completion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellationRegistration = cancellationToken.Register(
            () => completion.TrySetCanceled(cancellationToken));

        var thread = new Thread(() =>
        {
            try
            {
                System.Windows.Clipboard.SetText(text);
                completion.TrySetResult(null);
            }
            catch (Exception ex)
            {
                completion.TrySetException(ex);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();

        await completion.Task.ConfigureAwait(false);
#pragma warning restore CA1416
    }

    public async Task OpenSettingsFileAsync(CancellationToken cancellationToken = default)
    {
        var filePath = settingsService.GetSettingsFilePath();
        var dir = Path.GetDirectoryName(filePath)!;
        Directory.CreateDirectory(dir);

        // Always normalize and persist settings so newly added properties
        // (e.g. UseBroker) are materialized in settings.json.
        var settings = await settingsService.LoadSettingsAsync(cancellationToken).ConfigureAwait(false);
        var json = System.Text.Json.JsonSerializer.Serialize(settings, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(filePath, json, cancellationToken).ConfigureAwait(false);

        await OpenDocumentAsync(filePath, cancellationToken).ConfigureAwait(false);
    }
}
