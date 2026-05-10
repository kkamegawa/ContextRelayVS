using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using ContextRelay.Core.Models;
using ContextRelay.Core.Router;
using Microsoft.VisualStudio.Extensibility.UI;

namespace ContextRelay.VSExtension.ToolWindows;

[DataContract]
internal sealed class ContextItemViewModel
{
    internal ContextItemViewModel(ContextItem item, ContextRelayWindowViewModel parent)
    {
        Title = item.Title;
        Snippet = item.Snippet;
        Source = item.Source.ToString();
        SourceLabel = SourcePresentation.GetSourceLabel(Source);
        SourceIcon = SourcePresentation.GetSourceIcon(Source);
        Timestamp = item.Timestamp ?? string.Empty;
        Url = item.Url ?? string.Empty;
        PinButtonText = ContextRelayLocalizedStrings.PinButtonText;
        OpenButtonText = ContextRelayLocalizedStrings.OpenButtonText;
        CopyMenuText = ContextRelayLocalizedStrings.CopyMenuText;
        AppendToHandoffMenuText = ContextRelayLocalizedStrings.AppendToHandoffMenuText;
        OpenInBrowserMenuText = ContextRelayLocalizedStrings.OpenInBrowserMenuText;
        PinCommand = new AsyncCommand(async (_, ct) => await parent.PinResultAsync(this, ct).ConfigureAwait(false));
        CopyCommand = new AsyncCommand(async (_, ct) => await parent.CopyResultAsync(this, ct).ConfigureAwait(false));
        AppendToHandoffCommand = new AsyncCommand(async (_, ct) => await parent.AppendResultAsync(this, ct).ConfigureAwait(false));
        OpenCommand = new AsyncCommand((_, _) => { parent.OpenUrl(Url); return Task.CompletedTask; });
    }

    [DataMember] public string Title { get; private set; }
    [DataMember] public string Snippet { get; private set; }
    [DataMember] public string Source { get; private set; }
    [DataMember] public string SourceLabel { get; private set; }
    [DataMember] public string SourceIcon { get; private set; }
    [DataMember] public string Timestamp { get; private set; }
    [DataMember] public string Url { get; private set; }
    [DataMember] public string PinButtonText { get; private set; }
    [DataMember] public string OpenButtonText { get; private set; }
    [DataMember] public string CopyMenuText { get; private set; }
    [DataMember] public string AppendToHandoffMenuText { get; private set; }
    [DataMember] public string OpenInBrowserMenuText { get; private set; }
    [DataMember] public AsyncCommand PinCommand { get; }
    [DataMember] public AsyncCommand CopyCommand { get; }
    [DataMember] public AsyncCommand AppendToHandoffCommand { get; }
    [DataMember] public AsyncCommand OpenCommand { get; }
}
