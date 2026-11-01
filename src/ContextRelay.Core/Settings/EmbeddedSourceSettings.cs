namespace ContextRelay.Core.Settings;

/// <summary>
/// Represents one configured SharePoint Embedded container type that /embedded can search.
/// </summary>
public sealed class EmbeddedSourceSettings
{
    /// <summary>
    /// Gets or sets the display name shown in the source selector.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the container type identifier.
    /// </summary>
    public string ContainerTypeId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether this source can be selected.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Creates a copy of this source.
    /// </summary>
    /// <returns>The copied source.</returns>
    public EmbeddedSourceSettings Clone()
    {
        return new EmbeddedSourceSettings
        {
            Name = Name,
            ContainerTypeId = ContainerTypeId,
            Enabled = Enabled,
        };
    }
}
