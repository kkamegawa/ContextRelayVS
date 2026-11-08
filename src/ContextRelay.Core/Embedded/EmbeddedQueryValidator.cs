using System;
using ContextRelay.Core.Adapters;
using ContextRelay.Core.Settings;

namespace ContextRelay.Core.Embedded;

/// <summary>
/// Why an /embedded request was rejected before any authentication or network call.
/// </summary>
public enum EmbeddedQueryError
{
    None,

    /// <summary>The activation conditions are not met; see <see cref="EmbeddedQueryValidationResult.ActivationStatus"/>.</summary>
    NotActive,

    /// <summary>Several sources are valid and none is selected.</summary>
    SourceNotSelected,

    /// <summary>The query is empty after trimming.</summary>
    QueryEmpty,

    /// <summary>The trimmed query is longer than <see cref="RetrievalClient.MaxQueryLength"/>; it is never truncated.</summary>
    QueryTooLong
}

public sealed class EmbeddedQueryValidationResult
{
    internal EmbeddedQueryValidationResult(
        EmbeddedQueryError error,
        EmbeddedActivationStatus activationStatus,
        EmbeddedSourceSettings? source,
        string query,
        int maxResults)
    {
        Error = error;
        ActivationStatus = activationStatus;
        Source = source;
        Query = query;
        MaxResults = maxResults;
    }

    public EmbeddedQueryError Error { get; }

    public bool IsValid => Error == EmbeddedQueryError.None;

    public EmbeddedActivationStatus ActivationStatus { get; }

    public EmbeddedSourceSettings? Source { get; }

    /// <summary>The trimmed query.</summary>
    public string Query { get; }

    /// <summary>The result count clamped to 1-25.</summary>
    public int MaxResults { get; }
}

/// <summary>
/// Pre-validates /embedded in order: activation, source selection, then query length.
/// </summary>
public static class EmbeddedQueryValidator
{
    public static EmbeddedQueryValidationResult Validate(
        EmbeddedActivationResult activation,
        string? selectedContainerTypeId,
        string? query,
        int maxResults)
    {
        if (activation is null)
        {
            throw new ArgumentNullException(nameof(activation));
        }

        var trimmed = (query ?? string.Empty).Trim();
        var count = Math.Min(Math.Max(maxResults, 1), RetrievalClient.MaxResultsLimit);

        if (!activation.IsActive)
        {
            return new EmbeddedQueryValidationResult(EmbeddedQueryError.NotActive, activation.Status, null, trimmed, count);
        }

        var source = activation.ResolveSource(selectedContainerTypeId);
        if (source is null)
        {
            return new EmbeddedQueryValidationResult(EmbeddedQueryError.SourceNotSelected, activation.Status, null, trimmed, count);
        }

        if (trimmed.Length == 0)
        {
            return new EmbeddedQueryValidationResult(EmbeddedQueryError.QueryEmpty, activation.Status, source, trimmed, count);
        }

        if (trimmed.Length > RetrievalClient.MaxQueryLength)
        {
            return new EmbeddedQueryValidationResult(EmbeddedQueryError.QueryTooLong, activation.Status, source, trimmed, count);
        }

        return new EmbeddedQueryValidationResult(EmbeddedQueryError.None, activation.Status, source, trimmed, count);
    }
}
