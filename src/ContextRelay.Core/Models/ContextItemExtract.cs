namespace ContextRelay.Core.Models;

/// <summary>
/// One extract returned by the Retrieval API, kept in response order with its relevance score.
/// </summary>
public sealed class ContextItemExtract
{
    public string Text { get; set; } = string.Empty;

    public double? RelevanceScore { get; set; }

    public ContextItemExtract Clone()
    {
        return new ContextItemExtract
        {
            Text = Text,
            RelevanceScore = RelevanceScore
        };
    }
}
