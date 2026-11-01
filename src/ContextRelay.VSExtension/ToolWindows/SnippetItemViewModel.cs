using System.Linq;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using ContextRelay.Core.Embedded;
using ContextRelay.Core.Models;
using ContextRelay.Core.SharedStore;
using Microsoft.VisualStudio.Extensibility.UI;

namespace ContextRelay.VSExtension.ToolWindows;

/// <summary>A pinned snippet card; its preview toggles in place like a result card.</summary>
[DataContract]
internal sealed class SnippetItemViewModel : NotifyPropertyChangedObject
{
    private bool isPreviewExpanded;

    internal SnippetItemViewModel(SharedSnippetItem item, ContextRelayWindowViewModel parent, bool isPreviewExpanded = false)
    {
        Id = item.Id;
        Name = item.Name;
        Snippet = item.Snippet;
        Source = item.Source;
        IsEmbedded = EmbeddedSnippetMetadata.IsEmbedded(item);
        var embeddedSourceName = EmbeddedSnippetMetadata.TryGetString(item, EmbeddedMetadataKeys.SourceName);
        SourceLabel = IsEmbedded && !string.IsNullOrWhiteSpace(embeddedSourceName)
            ? embeddedSourceName!
            : SourcePresentation.GetSourceLabel(Source);
        SourceIcon = SourcePresentation.GetSourceIcon(Source);
        EmbeddedPreviewBadgeText = ContextRelayLocalizedStrings.EmbeddedPreviewBadgeText;
        var sensitivityLabel = EmbeddedSnippetMetadata.TryGetString(item, EmbeddedMetadataKeys.SensitivityLabelDisplayName);
        HasSensitivityLabel = !string.IsNullOrWhiteSpace(sensitivityLabel);
        SensitivityLabelText = HasSensitivityLabel ? ContextRelayLocalizedStrings.GetSensitivityLabelText(sensitivityLabel!) : string.Empty;
        CanOpen = SafeLinks.TryGetOpenableUrl(item.SourceUrl, out var openableUrl);
        SourceUrl = openableUrl;
        var extracts = EmbeddedSnippetMetadata.ReadExtracts(item)
            .Select(extract => extract.Text.Trim())
            .Where(text => text.Length > 0)
            .ToArray();
        PreviewText = extracts.Length > 0 ? string.Join("\n\n", extracts) : item.Snippet;
        HasPreview = !string.IsNullOrWhiteSpace(PreviewText);
        this.isPreviewExpanded = isPreviewExpanded;
        OpenButtonText = ContextRelayLocalizedStrings.OpenButtonText;
        DeleteButtonText = ContextRelayLocalizedStrings.DeleteButtonText;
        CopyPreviewButtonText = ContextRelayLocalizedStrings.CopyPreviewButtonText;
        OpenCommand = new AsyncCommand((_, _) => { parent.OpenUrl(SourceUrl); return Task.CompletedTask; }) { CanExecute = CanOpen };
        DeleteCommand = new AsyncCommand(async (_, ct) => await parent.DeleteSnippetAsync(Id, ct).ConfigureAwait(false));
        CopyPreviewCommand = new AsyncCommand(async (_, ct) => await parent.CopyPreviewTextAsync(PreviewText, ct).ConfigureAwait(false));
        TogglePreviewCommand = new AsyncCommand((_, _) => { parent.SetSnippetPreviewExpanded(this, !IsPreviewExpanded); return Task.CompletedTask; });
    }

    [DataMember] public string Id { get; private set; }
    [DataMember] public string Name { get; private set; }
    [DataMember] public string Snippet { get; private set; }
    [DataMember] public string Source { get; private set; }
    [DataMember] public string SourceLabel { get; private set; }
    [DataMember] public string SourceIcon { get; private set; }
    [DataMember] public bool IsEmbedded { get; private set; }
    [DataMember] public string EmbeddedPreviewBadgeText { get; private set; }
    [DataMember] public bool HasSensitivityLabel { get; private set; }
    [DataMember] public string SensitivityLabelText { get; private set; }

    /// <summary>The URL to open, empty unless it is an https link that passed the safety check.</summary>
    [DataMember] public string SourceUrl { get; private set; }
    [DataMember] public bool CanOpen { get; private set; }
    [DataMember] public string PreviewText { get; private set; }
    [DataMember] public bool HasPreview { get; private set; }

    [DataMember]
    public bool IsPreviewExpanded
    {
        get => isPreviewExpanded;
        private set
        {
            if (isPreviewExpanded != value)
            {
                isPreviewExpanded = value;
                RaiseNotifyPropertyChangedEvent(nameof(IsPreviewExpanded));
                RaiseNotifyPropertyChangedEvent(nameof(PreviewButtonText));
            }
        }
    }

    [DataMember] public string PreviewButtonText => isPreviewExpanded ? ContextRelayLocalizedStrings.HidePreviewButtonText : ContextRelayLocalizedStrings.ShowPreviewButtonText;
    [DataMember] public string OpenButtonText { get; private set; }
    [DataMember] public string DeleteButtonText { get; private set; }
    [DataMember] public string CopyPreviewButtonText { get; private set; }
    [DataMember] public AsyncCommand OpenCommand { get; }
    [DataMember] public AsyncCommand DeleteCommand { get; }
    [DataMember] public AsyncCommand CopyPreviewCommand { get; }
    [DataMember] public AsyncCommand TogglePreviewCommand { get; }

    internal void SetPreviewExpanded(bool value) => IsPreviewExpanded = value;
}
