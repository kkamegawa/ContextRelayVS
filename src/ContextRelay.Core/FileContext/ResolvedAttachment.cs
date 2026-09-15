using System;

namespace ContextRelay.Core.FileContext;

/// <summary>
/// A workspace-confined file selected as explicit chat context.
/// </summary>
public sealed class ResolvedAttachment
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string AbsolutePath { get; set; } = string.Empty;

    public string WorkspaceRoot { get; set; } = string.Empty;

    public string RelativePath { get; set; } = string.Empty;

    public string? DisplayName { get; set; }

    public int? SelectionStartLine { get; set; }

    public int? SelectionEndLine { get; set; }

    public string Label => string.IsNullOrWhiteSpace(DisplayName) ? RelativePath : DisplayName!;

    public ResolvedAttachment Clone() => new()
    {
        Id = Id,
        AbsolutePath = AbsolutePath,
        WorkspaceRoot = WorkspaceRoot,
        RelativePath = RelativePath,
        DisplayName = DisplayName,
        SelectionStartLine = SelectionStartLine,
        SelectionEndLine = SelectionEndLine
    };
}
