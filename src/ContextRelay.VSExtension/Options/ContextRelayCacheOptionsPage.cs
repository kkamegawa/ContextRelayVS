using System.ComponentModel;
using Microsoft.VisualStudio.Shell;

namespace ContextRelay.VSExtension.Options;

public sealed class ContextRelayCacheOptionsPage : DialogPage
{
    [Category("Cache")]
    [DisplayName("TTL seconds")]
    [Description("Number of seconds before cached search results expire.")]
    public int TtlSeconds { get; set; } = 300;

    [Category("Cache")]
    [DisplayName("Max entries")]
    [Description("Maximum number of search result entries kept in the LRU cache.")]
    public int MaxEntries { get; set; } = 200;

    [Category("Cache")]
    [DisplayName("Persist workspace state")]
    [Description("Persist the search cache under the current solution's .vs directory.")]
    public bool PersistWorkspaceState { get; set; } = true;
}
