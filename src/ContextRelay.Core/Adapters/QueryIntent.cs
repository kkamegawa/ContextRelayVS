using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace ContextRelay.Core.Adapters;

public sealed class QueryIntent
{
    public bool IncludeOneNoteHierarchy { get; set; }

    public bool IncludePlannerMetadata { get; set; }

    public bool IncludePlannerComments { get; set; }

    public IReadOnlyList<string> SearchTerms { get; set; } = Array.Empty<string>();
}

public static class QueryIntentParser
{
    private static readonly HashSet<string> FillerWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "an", "and", "as", "at", "for", "from", "in", "into", "me", "my", "of", "on",
        "or", "please", "search", "show", "the", "to", "with"
    };

    private static readonly HashSet<string> OneNoteHierarchyWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "section", "sections", "notebook", "notebooks"
    };

    private static readonly HashSet<string> PlannerMetadataWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "assigned", "assignment", "assignments", "bucket", "buckets", "metadata", "meta", "status"
    };

    private static readonly HashSet<string> PlannerCommentWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "comment", "comments", "conversation", "thread", "threads"
    };

    public static QueryIntent Parse(string query)
    {
        var words = NormalizeWords(query);
        var includeOneNoteHierarchy = false;
        var includePlannerMetadata = false;
        var includePlannerComments = false;

        foreach (var word in words)
        {
            if (OneNoteHierarchyWords.Contains(word))
            {
                includeOneNoteHierarchy = true;
            }

            if (PlannerMetadataWords.Contains(word))
            {
                includePlannerMetadata = true;
            }

            if (PlannerCommentWords.Contains(word))
            {
                includePlannerComments = true;
            }
        }

        var searchTerms = new List<string>();
        foreach (var word in words)
        {
            if (word.Length >= 2 &&
                !FillerWords.Contains(word) &&
                !OneNoteHierarchyWords.Contains(word) &&
                !PlannerMetadataWords.Contains(word) &&
                !PlannerCommentWords.Contains(word))
            {
                searchTerms.Add(word);
            }
        }

        return new QueryIntent
        {
            IncludeOneNoteHierarchy = includeOneNoteHierarchy,
            IncludePlannerMetadata = includePlannerMetadata,
            IncludePlannerComments = includePlannerComments,
            SearchTerms = searchTerms
        };
    }

    public static int ScoreMatches(string text, IReadOnlyList<string> terms)
    {
        if (terms.Count == 0)
        {
            return 0;
        }

        var haystack = NormalizeText(text);
        var score = 0;
        foreach (var term in terms)
        {
            if (haystack.Contains(term, StringComparison.OrdinalIgnoreCase))
            {
                score++;
            }
        }

        return score;
    }

    public static string NormalizeText(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var result = value.ToLower(CultureInfo.InvariantCulture);
        result = Regex.Replace(result, @"[_-]+", " ");
        result = Regex.Replace(result, @"[^\p{L}\p{N}\s]+", " ");
        result = Regex.Replace(result, @"\s+", " ");
        return result.Trim();
    }

    private static string[] NormalizeWords(string query)
    {
        var normalized = NormalizeText(query);
        if (normalized.Length == 0)
        {
            return Array.Empty<string>();
        }

        return normalized.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
    }
}
