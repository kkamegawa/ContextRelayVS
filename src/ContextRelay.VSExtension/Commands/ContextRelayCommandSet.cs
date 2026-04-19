using System;
using System.ComponentModel.Design;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;

namespace ContextRelay.VSExtension.Commands;

internal sealed class ContextRelayCommandSet
{
    private readonly ContextRelayPackage package;

    private ContextRelayCommandSet(ContextRelayPackage package, OleMenuCommandService commandService)
    {
        this.package = package;

        AddCommand(commandService, PackageIds.OpenContextRelayWindow, async () => await package.ShowContextRelayToolWindowAsync(focusSearch: false).ConfigureAwait(false));
        AddCommand(commandService, PackageIds.Search, async () => await package.ShowContextRelayToolWindowAsync(focusSearch: true).ConfigureAwait(false));
        AddCommand(commandService, PackageIds.ClearChat, async () => await package.Host.ClearChatAsync().ConfigureAwait(false));
        AddCommand(commandService, PackageIds.ClearCache, async () => await package.Host.ClearCacheAsync().ConfigureAwait(false));
        AddCommand(commandService, PackageIds.ClearSnippets, async () => await package.Host.ClearSnippetsAsync().ConfigureAwait(false));
        AddCommand(commandService, PackageIds.GenerateHandoffDocs, async () => await package.Host.GenerateHandoffAsync().ConfigureAwait(false));
        AddCommand(commandService, PackageIds.OpenCopilotChatWithPrompt, async () => await package.Host.OpenCopilotChatWithPromptAsync().ConfigureAwait(false));
        AddCommand(commandService, PackageIds.OpenHandoffDoc, async () => await package.Host.OpenHandoffDocumentAsync().ConfigureAwait(false));
        AddCommand(commandService, PackageIds.CopyHandoffPrompt, async () => await package.Host.CopyHandoffPromptAsync().ConfigureAwait(false));
        AddCommand(commandService, PackageIds.ShowDebugLog, delegate
        {
            package.Host.ShowDebugLog();
            return Task.CompletedTask;
        });
        AddCommand(commandService, PackageIds.OpenSettings, async delegate
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            package.Host.OpenSettings();
        });
    }

    public static async Task InitializeAsync(ContextRelayPackage package)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);
        var commandService = await package.GetServiceAsync(typeof(IMenuCommandService)).ConfigureAwait(true) as OleMenuCommandService;
        if (commandService is null)
        {
            throw new InvalidOperationException("OleMenuCommandService is unavailable.");
        }

        _ = new ContextRelayCommandSet(package, commandService);
    }

    private void AddCommand(OleMenuCommandService commandService, int id, Func<Task> executeAsync)
    {
        var menuCommandId = new CommandID(new Guid(ContextRelayPackageGuids.CommandSetString), id);
        var menuItem = new MenuCommand((_, _) => _ = package.JoinableTaskFactory.RunAsync(executeAsync), menuCommandId);
        commandService.AddCommand(menuItem);
    }
}
