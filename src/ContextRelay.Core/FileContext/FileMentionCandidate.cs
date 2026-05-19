using System;
using System.Collections.Generic;

namespace ContextRelay.Core.FileContext;

/// <summary>
/// Identifies why a file mention resolution attempt failed.
/// </summary>
public enum FileMentionErrorCode
{
    /// <summary>Workspace roots could not be determined.</summary>
    WorkspaceUnavailable,

    /// <summary>The prompt referenced too many files.</summary>
    MentionLimitReached,

    /// <summary>The referenced file could not be found.</summary>
    NotFound,

    /// <summary>The referenced file resolves outside the current workspace.</summary>
    OutsideWorkspace,

    /// <summary>The referenced path is ambiguous across workspace roots.</summary>
    AmbiguousPath,

    /// <summary>The referenced file type is not supported for Copilot context.</summary>
    UnsupportedFileType
}

/// <summary>
/// Represents a structured failure from file mention resolution.
/// </summary>
public sealed class FileMentionResolutionError
{
    /// <summary>
    /// Gets or sets the failure code.
    /// </summary>
    public FileMentionErrorCode Code { get; set; }

    /// <summary>
    /// Gets or sets optional detail for the failure.
    /// </summary>
    public string? Detail { get; set; }
}

/// <summary>
/// Represents a raw <c>#file</c> mention token found in a user prompt.
/// </summary>
public sealed class FileMentionCandidate
{
    /// <summary>
    /// Gets or sets the raw path text from the mention token.
    /// </summary>
    public string RawPath { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the start index of the mention token to remove.
    /// </summary>
    public int RemoveStart { get; set; }

    /// <summary>
    /// Gets or sets the exclusive end index of the mention token to remove.
    /// </summary>
    public int RemoveEnd { get; set; }
}

/// <summary>
/// Represents a workspace-confined file resolved from a <c>#file</c> mention.
/// </summary>
public sealed class ResolvedFileMention
{
    /// <summary>
    /// Gets or sets the canonical absolute path.
    /// </summary>
    public string AbsolutePath { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the workspace root containing the file.
    /// </summary>
    public string WorkspaceRoot { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the display path relative to the workspace root.
    /// </summary>
    public string RelativePath { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the file URI passed to Copilot contextual resources.
    /// </summary>
    public string Uri { get; set; } = string.Empty;
}

/// <summary>
/// Contains the cleaned prompt and resolved files for a file mention pass.
/// </summary>
public sealed class FileMentionResolutionResult
{
    /// <summary>
    /// Gets or sets the prompt after mention tokens were removed.
    /// </summary>
    public string CleanedPrompt { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the resolved file mentions.
    /// </summary>
    public IReadOnlyList<ResolvedFileMention> Files { get; set; } = Array.Empty<ResolvedFileMention>();

    /// <summary>
    /// Gets or sets structured resolution errors.
    /// </summary>
    public IReadOnlyList<FileMentionResolutionError> Errors { get; set; } = Array.Empty<FileMentionResolutionError>();
}
