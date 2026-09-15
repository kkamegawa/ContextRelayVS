using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using ContextRelay.Core.FileContext;
using Microsoft.VisualStudio.Extensibility.UI;

namespace ContextRelay.VSExtension.ToolWindows;

[DataContract]
internal sealed class PendingAttachmentViewModel
{
    private readonly string attachmentId;
    internal PendingAttachmentViewModel(ResolvedAttachment attachment, ContextRelayWindowViewModel parent)
    {
        attachmentId = attachment.Id;
        Label = attachment.Label;
        RemoveButtonText = ContextRelayLocalizedStrings.RemoveAttachmentButtonText;
        RemoveCommand = new AsyncCommand(async (_, ct) => await parent.RemovePendingAttachmentAsync(attachmentId, ct).ConfigureAwait(false));
    }
    [DataMember] public string Label { get; }
    [DataMember] public string RemoveButtonText { get; }
    [DataMember] public AsyncCommand RemoveCommand { get; }
}
