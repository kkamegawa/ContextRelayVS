namespace ContextRelay.Core.Models;

/// <summary>
/// Metadata keys stored on SharePoint Embedded results and pins. The names match upstream ContextRelay.
/// </summary>
public static class EmbeddedMetadataKeys
{
    public const string SourceName = "sourceName";
    public const string ContainerTypeId = "containerTypeId";
    public const string Extracts = "extracts";
    public const string SensitivityLabelDisplayName = "sensitivityLabelDisplayName";
    public const string SensitivityLabelId = "sensitivityLabelId";
}
