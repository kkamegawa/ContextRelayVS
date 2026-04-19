using System.Collections.Generic;

namespace ContextRelay.Core.Router;

public sealed class SlashCommandParseResult
{
    public RouteTarget Target { get; set; }

    public string Query { get; set; } = string.Empty;

    public bool IsEmpty { get; set; }

    public string? SlashCommandName { get; set; }

    public IReadOnlyList<ContextSource> TargetSources { get; set; } = new ContextSource[0];
}
