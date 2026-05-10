using System.Runtime.Serialization;
using System.Threading.Tasks;
using ContextRelay.Core.SharedStore;
using Microsoft.VisualStudio.Extensibility.UI;

namespace ContextRelay.VSExtension.ToolWindows;

[DataContract]
internal sealed class SnippetItemViewModel
{
    internal SnippetItemViewModel(SharedSnippetItem item, ContextRelayWindowViewModel parent)
    {
        Id = item.Id;
        Name = item.Name;
        Snippet = item.Snippet;
        Source = item.Source;
        SourceLabel = SourcePresentation.GetSourceLabel(Source);
        SourceIcon = SourcePresentation.GetSourceIcon(Source);
        SourceUrl = item.SourceUrl ?? string.Empty;
        OpenButtonText = ContextRelayLocalizedStrings.OpenButtonText;
        DeleteButtonText = ContextRelayLocalizedStrings.DeleteButtonText;
        OpenCommand = new AsyncCommand((_, _) => { parent.OpenUrl(SourceUrl); return Task.CompletedTask; });
        DeleteCommand = new AsyncCommand(async (_, ct) => await parent.DeleteSnippetAsync(Id, ct).ConfigureAwait(false));
    }

    [DataMember] public string Id { get; private set; }
    [DataMember] public string Name { get; private set; }
    [DataMember] public string Snippet { get; private set; }
    [DataMember] public string Source { get; private set; }
    [DataMember] public string SourceLabel { get; private set; }
    [DataMember] public string SourceIcon { get; private set; }
    [DataMember] public string SourceUrl { get; private set; }
    [DataMember] public string OpenButtonText { get; private set; }
    [DataMember] public string DeleteButtonText { get; private set; }
    [DataMember] public AsyncCommand OpenCommand { get; }
    [DataMember] public AsyncCommand DeleteCommand { get; }
}
