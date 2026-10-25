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
        Item = item;
        Title = item.Title;
        Snippet = item.Snippet;
        Source = ContextSourceSchema.ToSchemaValue(item.Source);
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

    /// <summary>
    /// The original result, kept in-process and never serialized, so pin, copy, and handoff act on the
    /// full item (metadata and extracts included) instead of a lossy reconstruction from display fields.
    /// </summary>
    internal ContextItem Item { get; }

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
