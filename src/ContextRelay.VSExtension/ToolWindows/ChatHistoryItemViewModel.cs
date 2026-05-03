using System.Runtime.Serialization;
using System.Threading.Tasks;
using ContextRelay.Core.SharedStore;
using Microsoft.VisualStudio.Extensibility.UI;

namespace ContextRelay.VSExtension.ToolWindows;

[DataContract]
internal sealed class ChatHistoryItemViewModel
{
    internal ChatHistoryItemViewModel(SharedChatHistoryItem item, ContextRelayWindowViewModel parent)
    {
        Id = item.Id;
        Role = item.Role;
        Text = item.Text;
        Timestamp = item.Timestamp;
        IsActionableAssistant = item.IsActionableAssistant;
        HasContextLabels = item.HasContextLabels;
        ContextLabelsJoinedDisplay = item.ContextLabelsJoinedDisplay;
        ContextLabelsPrefixText = ContextRelayLocalizedStrings.ContextLabelsPrefixText;
        CopyAssistantButtonText = ContextRelayLocalizedStrings.CopyAssistantButtonText;
        AppendAssistantButtonText = ContextRelayLocalizedStrings.AppendAssistantButtonText;
        ReplaceAssistantButtonText = ContextRelayLocalizedStrings.ReplaceAssistantButtonText;
        var text = item.Text;
        CopyAssistantCommand = new AsyncCommand(async (_, ct) => await parent.CopyAssistantTextAsync(text, ct).ConfigureAwait(false));
        AppendAssistantCommand = new AsyncCommand(async (_, ct) => await parent.AppendAssistantTextAsync(text, ct).ConfigureAwait(false));
        ReplaceAssistantCommand = new AsyncCommand(async (_, ct) => await parent.ReplaceEditorWithAssistantTextAsync(text, ct).ConfigureAwait(false));
    }

    [DataMember] public string Id { get; private set; }
    [DataMember] public string Role { get; private set; }
    [DataMember] public string Text { get; private set; }
    [DataMember] public string Timestamp { get; private set; }
    [DataMember] public bool IsActionableAssistant { get; private set; }
    [DataMember] public bool HasContextLabels { get; private set; }
    [DataMember] public string ContextLabelsJoinedDisplay { get; private set; }
    [DataMember] public string ContextLabelsPrefixText { get; private set; }
    [DataMember] public string CopyAssistantButtonText { get; private set; }
    [DataMember] public string AppendAssistantButtonText { get; private set; }
    [DataMember] public string ReplaceAssistantButtonText { get; private set; }
    [DataMember] public AsyncCommand CopyAssistantCommand { get; }
    [DataMember] public AsyncCommand AppendAssistantCommand { get; }
    [DataMember] public AsyncCommand ReplaceAssistantCommand { get; }
}
