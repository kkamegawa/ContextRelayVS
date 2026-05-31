using System;
using System.Runtime.Serialization;
using Microsoft.VisualStudio.Extensibility.UI;

namespace ContextRelay.VSExtension.ToolWindows;

[DataContract]
public sealed class SlashCommandSuggestion
{
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
