using System.Runtime.Serialization;
using System.Threading.Tasks;
using ContextRelay.Core.Embedded;
using ContextRelay.Core.Models;
using ContextRelay.Core.Router;
using Microsoft.VisualStudio.Extensibility.UI;

namespace ContextRelay.VSExtension.ToolWindows;

/// <summary>
/// A search result card. Preview and pin state change in place (this is a
/// <see cref="NotifyPropertyChangedObject"/>), so toggling them keeps the list's scroll position.
/// </summary>
[DataContract]
internal sealed class ContextItemViewModel : NotifyPropertyChangedObject
{
    private bool isPinned;
    private bool isPreviewExpanded;

    internal ContextItemViewModel(ContextItem item, ContextRelayWindowViewModel parent, bool isPinned = false, bool isPreviewExpanded = false)
    {
        Item = item;
        Key = ContextItemKeys.Build(item);
        Title = item.Title;
        Snippet = item.Snippet;
        Source = ContextSourceSchema.ToSchemaValue(item.Source);
        IsEmbedded = item.Source == ContextSource.SharePointEmbedded;
        item.Metadata.TryGetValue(EmbeddedMetadataKeys.SourceName, out var embeddedSourceName);
        SourceLabel = IsEmbedded && !string.IsNullOrWhiteSpace(embeddedSourceName)
            ? embeddedSourceName!
            : SourcePresentation.GetSourceLabel(Source);
        SourceIcon = SourcePresentation.GetSourceIcon(Source);
        EmbeddedPreviewBadgeText = ContextRelayLocalizedStrings.EmbeddedPreviewBadgeText;
        item.Metadata.TryGetValue(EmbeddedMetadataKeys.SensitivityLabelDisplayName, out var sensitivityLabel);
        HasSensitivityLabel = !string.IsNullOrWhiteSpace(sensitivityLabel);
        SensitivityLabelText = HasSensitivityLabel ? ContextRelayLocalizedStrings.GetSensitivityLabelText(sensitivityLabel!) : string.Empty;
        Timestamp = item.Timestamp ?? string.Empty;
        CanOpen = SafeLinks.TryGetOpenableUrl(item.Url, out var openableUrl);
        Url = openableUrl;
        PreviewText = EmbeddedSnippetMetadata.BuildContextText(item);
        HasPreview = !string.IsNullOrWhiteSpace(PreviewText);
        OpenButtonText = ContextRelayLocalizedStrings.OpenButtonText;
        CopyMenuText = ContextRelayLocalizedStrings.CopyMenuText;
        CopyPreviewButtonText = ContextRelayLocalizedStrings.CopyPreviewButtonText;
        AppendToHandoffMenuText = ContextRelayLocalizedStrings.AppendToHandoffMenuText;
        OpenInBrowserMenuText = ContextRelayLocalizedStrings.OpenInBrowserMenuText;
        this.isPinned = isPinned;
        this.isPreviewExpanded = isPreviewExpanded;
        PinCommand = new AsyncCommand(async (_, ct) => await parent.PinResultAsync(this, ct).ConfigureAwait(false));
        CopyCommand = new AsyncCommand(async (_, ct) => await parent.CopyResultAsync(this, ct).ConfigureAwait(false));
        CopyPreviewCommand = new AsyncCommand(async (_, ct) => await parent.CopyPreviewTextAsync(PreviewText, ct).ConfigureAwait(false));
        AppendToHandoffCommand = new AsyncCommand(async (_, ct) => await parent.AppendResultAsync(this, ct).ConfigureAwait(false));
        OpenCommand = new AsyncCommand((_, _) => { parent.OpenUrl(Url); return Task.CompletedTask; }) { CanExecute = CanOpen };
        TogglePreviewCommand = new AsyncCommand((_, _) => { parent.SetResultPreviewExpanded(this, !IsPreviewExpanded); return Task.CompletedTask; });
    }

    /// <summary>
    /// The original result, kept in-process and never serialized, so pin, copy, and handoff act on the
    /// full item (metadata and extracts included) instead of a lossy reconstruction from display fields.
    /// </summary>
    internal ContextItem Item { get; }

    /// <summary>The identity shared with pins (<see cref="ContextItemKeys"/>), never serialized.</summary>
    internal string Key { get; }

    [DataMember] public string Title { get; private set; }
    [DataMember] public string Snippet { get; private set; }
    [DataMember] public string Source { get; private set; }
    [DataMember] public string SourceLabel { get; private set; }
    [DataMember] public string SourceIcon { get; private set; }
    [DataMember] public bool IsEmbedded { get; private set; }
    [DataMember] public string EmbeddedPreviewBadgeText { get; private set; }
    [DataMember] public bool HasSensitivityLabel { get; private set; }
    [DataMember] public string SensitivityLabelText { get; private set; }
    [DataMember] public string Timestamp { get; private set; }

    /// <summary>The URL to open, empty unless it is an https link that passed the safety check.</summary>
    [DataMember] public string Url { get; private set; }
    [DataMember] public bool CanOpen { get; private set; }

    /// <summary>Every extract in response order (the summary when there are none), shown selectable in the preview.</summary>
    [DataMember] public string PreviewText { get; private set; }
    [DataMember] public bool HasPreview { get; private set; }

    [DataMember]
    public bool IsPinned
    {
        get => isPinned;
        private set
        {
            if (isPinned != value)
            {
                isPinned = value;
                RaiseNotifyPropertyChangedEvent(nameof(IsPinned));
                RaiseNotifyPropertyChangedEvent(nameof(PinButtonText));
            }
        }
    }

    [DataMember] public string PinButtonText => isPinned ? ContextRelayLocalizedStrings.UnpinButtonText : ContextRelayLocalizedStrings.PinButtonText;

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
    [DataMember] public string CopyMenuText { get; private set; }
    [DataMember] public string CopyPreviewButtonText { get; private set; }
    [DataMember] public string AppendToHandoffMenuText { get; private set; }
    [DataMember] public string OpenInBrowserMenuText { get; private set; }
    [DataMember] public AsyncCommand PinCommand { get; }
    [DataMember] public AsyncCommand CopyCommand { get; }
    [DataMember] public AsyncCommand CopyPreviewCommand { get; }
    [DataMember] public AsyncCommand AppendToHandoffCommand { get; }
    [DataMember] public AsyncCommand OpenCommand { get; }
    [DataMember] public AsyncCommand TogglePreviewCommand { get; }

    internal void SetPinned(bool value) => IsPinned = value;

    internal void SetPreviewExpanded(bool value) => IsPreviewExpanded = value;
}
