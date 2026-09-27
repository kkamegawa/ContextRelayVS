using System;
using System.Runtime.Serialization;
using Microsoft.VisualStudio.Extensibility.UI;

namespace ContextRelay.VSExtension.ToolWindows;

[DataContract]
public sealed class SlashCommandSuggestion : NotifyPropertyChangedObject
{
    private bool isSelected;

    [DataMember]
    public string Icon { get; set; } = string.Empty;

    [DataMember]
    public string Name { get; set; } = string.Empty;

    [DataMember]
    public string Description { get; set; } = string.Empty;

    [DataMember]
    public string CommittedQuery { get; set; } = string.Empty;

    [DataMember]
    public AsyncCommand? ApplyCommand { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether this suggestion is the keyboard-selected row.
    /// The popup highlight is driven by this serialized per-item state instead of the WPF
    /// <c>Selector</c> selection, because Remote UI cannot guarantee that a scalar selection
    /// index survives the item collection being replaced on every keystroke.
    /// The flag is stamped at construction time on per-window display clones (see
    /// <see cref="CreateDisplayClone"/>); it must never be mutated on an instance that has
    /// already been transmitted over the Remote UI boundary, because such in-place changes
    /// are not re-serialized for objects the channel already knows by identity.
    /// </summary>
    [DataMember]
    public bool IsSelected
    {
        get => isSelected;
        set
        {
            if (isSelected == value)
            {
                return;
            }

            isSelected = value;
            RaiseNotifyPropertyChangedEvent(nameof(IsSelected));
        }
    }

    /// <summary>
    /// Creates a brand-new display item from <paramref name="source"/> with the selection state
    /// baked in at construction. Remote UI de-duplicates already-transmitted objects by identity,
    /// so the visible window must be rebuilt from fresh instances on every selection change for
    /// the highlight to repaint; <paramref name="source"/> is never mutated.
    /// <see cref="ApplyCommand"/> is intentionally left <see langword="null"/> — the view model
    /// wires it after cloning so the command can target the master suggestion.
    /// </summary>
    /// <param name="source">The master suggestion to copy display data from.</param>
    /// <param name="isSelected">Whether the clone represents the keyboard-selected row.</param>
    /// <returns>A fresh <see cref="SlashCommandSuggestion"/> ready to be transmitted.</returns>
    internal static SlashCommandSuggestion CreateDisplayClone(SlashCommandSuggestion source, bool isSelected)
    {
        ArgumentNullException.ThrowIfNull(source);

        return new SlashCommandSuggestion
        {
            Icon = source.Icon,
            Name = source.Name,
            Description = source.Description,
            CommittedQuery = source.CommittedQuery,
            IsSelected = isSelected
        };
    }

    /// <summary>
    /// Builds the exact query text that should be committed when a popup suggestion is accepted.
    /// </summary>
    /// <param name="isPopupOpen">Whether the suggestion popup is currently active.</param>
    /// <param name="suggestion">The selected suggestion to commit.</param>
    /// <param name="committedQuery">The committed query text, including the trailing separator.</param>
    /// <returns><see langword="true"/> when the suggestion can be committed; otherwise, <see langword="false"/>.</returns>
    internal static bool TryBuildCommittedQuery(bool isPopupOpen, SlashCommandSuggestion? suggestion, out string committedQuery)
    {
        if (!isPopupOpen || suggestion is null || string.IsNullOrWhiteSpace(suggestion.Name))
        {
            committedQuery = string.Empty;
            return false;
        }

        var sourceText = string.IsNullOrWhiteSpace(suggestion.CommittedQuery)
            ? suggestion.Name
            : suggestion.CommittedQuery;
        committedQuery = sourceText.EndsWith(" ", StringComparison.Ordinal)
            ? sourceText
            : $"{sourceText} ";
        return true;
    }
}
