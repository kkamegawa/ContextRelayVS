using System.Runtime.Serialization;

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
}
