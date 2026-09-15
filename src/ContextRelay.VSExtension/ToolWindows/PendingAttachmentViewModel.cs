using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using ContextRelay.Core.FileContext;
using Microsoft.VisualStudio.Extensibility.UI;

namespace ContextRelay.VSExtension.ToolWindows;

[DataContract]
internal sealed class PendingAttachmentViewModel
{
    internal PendingAttachmentViewModel(ResolvedAttachment attachment, ContextRelayWindowViewModel parent)
    {
        Id = attachment.Id;
        Label = attachment.Label;
        RemoveButtonText = ContextRelayLocalizedStrings.RemoveAttachmentButtonText;
        RemoveAutomationName = ContextRelayLocalizedStrings.GetRemoveAttachmentAutomationName(Label);
        RemoveCommand = new AsyncCommand(async (_, ct) => await parent.RemovePendingAttachmentAsync(Id, ct).ConfigureAwait(false));
    }
    [DataMember] public string Id { get; }
    [DataMember] public string Label { get; }
    [DataMember] public string RemoveButtonText { get; }
    [DataMember] public string RemoveAutomationName { get; }
    [DataMember] public AsyncCommand RemoveCommand { get; }
}
