using System;
using System.Collections.Generic;

namespace ContextRelay.Core.Handoff;

public sealed class HandoffGenerationResult
{
    public string OutputDirectory { get; set; } = string.Empty;

    public string WorkspaceRoot { get; set; } = string.Empty;

    public string PlanPath { get; set; } = string.Empty;

    public string TasksPath { get; set; } = string.Empty;

    public string TestPlanPath { get; set; } = string.Empty;

    public string? HandoffPath { get; set; }

    public IReadOnlyList<string> WrittenFiles { get; set; } = Array.Empty<string>();
}
