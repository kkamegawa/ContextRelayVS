using System.Runtime.Serialization;
using Microsoft.VisualStudio.Extensibility.UI;

namespace ContextRelay.VSExtension.ToolWindows;

/// <summary>
/// One row of the inline SharePoint Embedded source selector. Only the display name is shown; the
/// container type ID stays in-process. Rows are rebuilt as new instances when the highlight moves,
/// because Remote UI does not re-send in-place changes on plain data objects.
/// </summary>
[DataContract]
internal sealed class EmbeddedSourceOptionViewModel
{
    internal EmbeddedSourceOptionViewModel(string name, string containerTypeId, bool isHighlighted, AsyncCommand selectCommand)
    {
        Name = name;
        ContainerTypeId = containerTypeId;
        IsHighlighted = isHighlighted;
        SelectCommand = selectCommand;
    }

    internal string ContainerTypeId { get; }

    [DataMember] public string Name { get; private set; }
    [DataMember] public bool IsHighlighted { get; private set; }
    [DataMember] public AsyncCommand SelectCommand { get; }
}
