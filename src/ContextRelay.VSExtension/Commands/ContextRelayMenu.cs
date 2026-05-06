using System;
using ContextRelay.VSExtension.Commands;
using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.Commands;

namespace ContextRelay.VSExtension;

/// <summary>
/// Defines the ContextRelay parent menu and its command group under Tools.
/// </summary>
internal static class ContextRelayMenuDefinitions
{
    private static readonly Guid MainMenuGuid = new("D309F791-903F-11D0-9EFC-00A0C911004F");
    private const uint ToolsMenuGroupId = 0x00000085;
    private const ushort ContextRelayToolsMenuPriority = 0x7000;

    /// <summary>
    /// The ContextRelay menu that appears under Tools menu.
    /// </summary>
    [VisualStudioContribution]
    internal static MenuConfiguration ContextRelayMenu => new("%ContextRelay.Menu.DisplayName%")
    {
        Children = new MenuChild[]
        {
            // Main commands
            MenuChild.Command<SearchCommand>(),
            MenuChild.Command<OpenWindowCommand>(),
            MenuChild.Command<GenerateHandoffCommand>(),
            MenuChild.Command<CopyHandoffPromptCommand>(),
            MenuChild.Command<OpenCopilotChatCommand>(),
            MenuChild.Command<OpenHandoffDocCommand>(),

            // Separator
            MenuChild.Separator,

            // Utility/Clear commands
            MenuChild.Command<ClearCacheCommand>(),
            MenuChild.Command<ClearChatCommand>(),
            MenuChild.Command<ClearSnippetsCommand>(),
            MenuChild.Command<ShowDebugLogCommand>(),
        },
    };

    /// <summary>
    /// The group that places the ContextRelay menu under the Tools menu.
    /// </summary>
    [VisualStudioContribution]
    internal static CommandGroupConfiguration ContextRelayMenuGroup => new(GroupPlacement.VsctParent(MainMenuGuid, ToolsMenuGroupId, ContextRelayToolsMenuPriority))
    {
        Children = new GroupChild[]
        {
            GroupChild.Menu(ContextRelayMenu),
        },
    };
}
