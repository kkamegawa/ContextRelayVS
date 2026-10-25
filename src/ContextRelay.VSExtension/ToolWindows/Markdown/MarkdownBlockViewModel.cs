using System.Runtime.Serialization;
using Microsoft.VisualStudio.Extensibility.UI;

namespace ContextRelay.VSExtension.ToolWindows.Markdown;

/// <summary>
/// A display block of an answer. While a response streams, existing blocks are updated in place with
/// <see cref="CopyFrom"/> instead of being replaced, so the rendered text does not flicker. The template
/// cannot use converters, so every visual choice is a precomputed flag.
/// </summary>
[DataContract]
internal sealed class MarkdownBlockViewModel : NotifyPropertyChangedObject
{
    private MarkdownBlock block = new();

    internal MarkdownBlockViewModel(MarkdownBlock block)
    {
        this.block = block;
    }

    [DataMember] public string Text => block.Text;
    [DataMember] public bool IsText => block.Kind is MarkdownBlockKind.Heading or MarkdownBlockKind.Paragraph or MarkdownBlockKind.ListItem;
    [DataMember] public bool IsHeading1 => block.Kind == MarkdownBlockKind.Heading && block.Level == 1;
    [DataMember] public bool IsHeading2 => block.Kind == MarkdownBlockKind.Heading && block.Level == 2;
    [DataMember] public bool IsHeading3 => block.Kind == MarkdownBlockKind.Heading && block.Level >= 3;
    [DataMember] public bool IsListItem => block.Kind == MarkdownBlockKind.ListItem;
    [DataMember] public string ListMarker => block.ListMarker;
    [DataMember] public bool IsListDepth1 => block.ListDepth == 1;
    [DataMember] public bool IsListDepth2 => block.ListDepth >= 2;
    [DataMember] public bool IsCode => block.Kind == MarkdownBlockKind.Code;
    [DataMember] public string Language => block.Language;
    [DataMember] public bool HasLanguage => block.Kind == MarkdownBlockKind.Code && block.Language.Length > 0;
    [DataMember] public bool IsTable => block.Kind == MarkdownBlockKind.Table;
    [DataMember] public bool IsRule => block.Kind == MarkdownBlockKind.Rule;
    [DataMember] public bool IsQuote => block.QuoteDepth > 0;
    [DataMember] public bool IsQuoteDepth2 => block.QuoteDepth == 2;
    [DataMember] public bool IsQuoteDepth3 => block.QuoteDepth >= 3;

    internal MarkdownBlockKind Kind => block.Kind;

    /// <summary>Replaces the content and raises change notifications only for properties that changed.</summary>
    internal void CopyFrom(MarkdownBlock next)
    {
        var previous = block;
        block = next;
        if (previous.Text != next.Text)
        {
            RaiseNotifyPropertyChangedEvent(nameof(Text));
        }

        if (previous.Kind != next.Kind || previous.Level != next.Level)
        {
            RaiseNotifyPropertyChangedEvent(nameof(IsText));
            RaiseNotifyPropertyChangedEvent(nameof(IsHeading1));
            RaiseNotifyPropertyChangedEvent(nameof(IsHeading2));
            RaiseNotifyPropertyChangedEvent(nameof(IsHeading3));
            RaiseNotifyPropertyChangedEvent(nameof(IsListItem));
            RaiseNotifyPropertyChangedEvent(nameof(IsCode));
            RaiseNotifyPropertyChangedEvent(nameof(IsTable));
            RaiseNotifyPropertyChangedEvent(nameof(IsRule));
            RaiseNotifyPropertyChangedEvent(nameof(HasLanguage));
        }

        if (previous.ListMarker != next.ListMarker)
        {
            RaiseNotifyPropertyChangedEvent(nameof(ListMarker));
        }

        if (previous.ListDepth != next.ListDepth)
        {
            RaiseNotifyPropertyChangedEvent(nameof(IsListDepth1));
            RaiseNotifyPropertyChangedEvent(nameof(IsListDepth2));
        }

        if (previous.Language != next.Language)
        {
            RaiseNotifyPropertyChangedEvent(nameof(Language));
            RaiseNotifyPropertyChangedEvent(nameof(HasLanguage));
        }

        if (previous.QuoteDepth != next.QuoteDepth)
        {
            RaiseNotifyPropertyChangedEvent(nameof(IsQuote));
            RaiseNotifyPropertyChangedEvent(nameof(IsQuoteDepth2));
            RaiseNotifyPropertyChangedEvent(nameof(IsQuoteDepth3));
        }
    }
}
