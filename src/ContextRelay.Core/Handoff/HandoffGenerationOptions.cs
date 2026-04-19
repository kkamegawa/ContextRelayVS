namespace ContextRelay.Core.Handoff;

public sealed class HandoffGenerationOptions
{
    public string OutputDirectory { get; set; } = ".contextrelay";

    public string? WorkspaceRoot { get; set; }

    public string? FallbackRootDirectory { get; set; }

    public bool IncludeHandoffDocument { get; set; } = true;
}
