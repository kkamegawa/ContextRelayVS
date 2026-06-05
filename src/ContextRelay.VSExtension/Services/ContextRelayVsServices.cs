using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ContextRelay.Core.Settings;
using ContextRelay.VSExtension.ToolWindows;
using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.Shell.FileDialog;
using Microsoft.VisualStudio.ProjectSystem.Query;

namespace ContextRelay.VSExtension.Services;

internal sealed class ContextRelayVsServices : IContextRelayPackageServices
{
    private readonly VisualStudioExtensibility extensibility;
    private readonly ContextRelaySettingsService settingsService;
    private readonly object selectedWorkspaceRootsGate = new();
    private string[] selectedWorkspaceRoots = Array.Empty<string>();

    public ContextRelayVsServices(VisualStudioExtensibility extensibility, ContextRelaySettingsService settingsService)
    {
        this.extensibility = extensibility;
        this.settingsService = settingsService;
    }

    public async Task<ContextRelaySettingsSnapshot> GetSettingsSnapshotAsync(CancellationToken cancellationToken = default)
    {
        var settings = await settingsService.LoadSettingsAsync(cancellationToken).ConfigureAwait(false);
        ContextRelayLocalizedStrings.SetUiLanguage(settings.UiLanguage);
        return settings;
    }

    public async Task<IReadOnlyList<string>> GetWorkspaceRootsAsync(CancellationToken cancellationToken = default)
    {
        var roots = new List<string>();
        var documents = await extensibility.Documents().GetOpenDocumentsAsync(cancellationToken).ConfigureAwait(false);
        foreach (var document in documents)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!document.Moniker.IsFile)
            {
                continue;
            }

            var root = WorkspaceRootInference.InferWorkspaceRootFromPath(document.Moniker.LocalPath);
            if (!string.IsNullOrWhiteSpace(root))
            {
                roots.Add(root!);
            }
        }

        var currentDirectoryRoot = WorkspaceRootInference.InferWorkspaceRootFromPath(
            Environment.CurrentDirectory,
            requireWorkspaceMarker: true);
        if (!string.IsNullOrWhiteSpace(currentDirectoryRoot))
        {
            roots.Add(currentDirectoryRoot);
        }

        lock (selectedWorkspaceRootsGate)
        {
            roots.AddRange(selectedWorkspaceRoots);
        }

        return roots
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<IReadOnlyList<string>> PickWorkspaceFilesAsync(string? initialDirectory, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var selectedFiles = await extensibility.Shell()
            .ShowOpenMultipleFilesDialogAsync(CreateFileDialogOptions(initialDirectory), cancellationToken)
            .ConfigureAwait(false) ?? Array.Empty<string>();
        RememberWorkspaceRootsFromSelectedFiles(selectedFiles);
        return selectedFiles;
    }

    public async Task<string?> PickWorkspaceFolderAsync(string? initialDirectory, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var selectedFolder = await extensibility.Shell()
            .ShowOpenFolderDialogAsync(CreateFolderDialogOptions(initialDirectory), cancellationToken)
            .ConfigureAwait(false);
        RememberWorkspaceRoot(selectedFolder);
        return selectedFolder;
    }

    private static FileDialogOptions CreateFileDialogOptions(string? initialDirectory)
    {
        var initialDirectoryValue = !string.IsNullOrWhiteSpace(initialDirectory) && Directory.Exists(initialDirectory)
            ? initialDirectory
            : string.Empty;

        return new FileDialogOptions
        {
            Title = ContextRelayLocalizedStrings.AddFilesDialogTitle,
            InitialDirectory = initialDirectoryValue,
            Filters = CreateAllFilesDialogFilter()
        };
    }

    private static DialogFilters CreateAllFilesDialogFilter()
    {
        var filterText = ContextRelayLocalizedStrings.AddFilesDialogFilter;
        var separatorIndex = filterText.IndexOf('|', StringComparison.Ordinal);
        var displayValue = separatorIndex > 0
            ? filterText[..separatorIndex]
            : filterText;
        var filters = separatorIndex >= 0 && separatorIndex + 1 < filterText.Length
            ? filterText[(separatorIndex + 1)..].Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : new[] { "*.*" };

        return new DialogFilters(new DialogFilter(displayValue, filters));
    }

    private static FolderDialogOptions CreateFolderDialogOptions(string? initialDirectory)
    {
        var initialDirectoryValue = !string.IsNullOrWhiteSpace(initialDirectory) && Directory.Exists(initialDirectory)
            ? initialDirectory
            : string.Empty;

        return new FolderDialogOptions
        {
            Title = ContextRelayLocalizedStrings.CreatedFilesFolderDialogTitle,
            InitialDirectory = initialDirectoryValue
        };
    }

    public async Task<string?> GetSolutionRootAsync(CancellationToken cancellationToken = default)
    {
        var roots = await GetWorkspaceRootsAsync(cancellationToken).ConfigureAwait(false);
        return roots.Count > 0 ? roots[0] : null;
    }

    public async Task<int> TryAddFilesToSolutionAsync(IReadOnlyList<string> filePaths, CancellationToken cancellationToken = default)
    {
        if (filePaths.Count == 0)
        {
            return 0;
        }

        var normalizedPaths = filePaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .Where(File.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (normalizedPaths.Length == 0)
        {
            return 0;
        }

        try
        {
            await extensibility.Workspaces()
                .UpdateSolutionAsync(
                    solutions => solutions,
                    solutions => solutions.AddFiles(normalizedPaths),
                    cancellationToken)
                .ConfigureAwait(false);
            return normalizedPaths.Length;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Best effort — propagate cancellation, swallow other failures
            return 0;
        }
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

    public async Task UpdateUiLanguageAsync(string uiLanguage, CancellationToken cancellationToken = default)
    {
        await settingsService.UpdateUiLanguageAsync(uiLanguage, cancellationToken).ConfigureAwait(false);
        ContextRelayLocalizedStrings.SetUiLanguage(ContextRelaySettingsService.NormalizeUiLanguage(uiLanguage));
    }

    private void RememberWorkspaceRootsFromSelectedFiles(IReadOnlyList<string> selectedFiles)
    {
        if (selectedFiles.Count == 0)
        {
            return;
        }

        var inferredRoots = selectedFiles
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => Path.GetFullPath(path))
            .Where(File.Exists)
            .Select(path =>
            {
                var inferred = WorkspaceRootInference.InferWorkspaceRootFromPath(path);
                return string.IsNullOrWhiteSpace(inferred)
                    ? Path.GetDirectoryName(path)
                    : inferred;
            })
            .Where(path => !string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
            .Select(path => Path.GetFullPath(path!))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (inferredRoots.Length == 0)
        {
            return;
        }

        lock (selectedWorkspaceRootsGate)
        {
            selectedWorkspaceRoots = selectedWorkspaceRoots
                .Concat(inferredRoots)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
    }

    private void RememberWorkspaceRoot(string? rootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath))
        {
            return;
        }

        var normalizedRoot = Path.GetFullPath(rootPath);
        lock (selectedWorkspaceRootsGate)
        {
            selectedWorkspaceRoots = selectedWorkspaceRoots
                .Concat(new[] { normalizedRoot })
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
    }

}
