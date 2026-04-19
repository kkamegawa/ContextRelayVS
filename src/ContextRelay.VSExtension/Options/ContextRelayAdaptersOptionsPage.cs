using System.ComponentModel;
using Microsoft.VisualStudio.Shell;

namespace ContextRelay.VSExtension.Options;

public sealed class ContextRelayAdaptersOptionsPage : DialogPage
{
    [Category("Adapters")]
    [DisplayName("Mail")]
    [Description("Enable Exchange / Outlook search.")]
    public bool Mail { get; set; } = true;

    [Category("Adapters")]
    [DisplayName("Teams")]
    [Description("Enable Teams message search.")]
    public bool Teams { get; set; } = true;

    [Category("Adapters")]
    [DisplayName("SharePoint")]
    [Description("Enable SharePoint search.")]
    public bool SharePoint { get; set; } = true;

    [Category("Adapters")]
    [DisplayName("OneDrive")]
    [Description("Enable OneDrive search.")]
    public bool OneDrive { get; set; } = true;

    [Category("Adapters")]
    [DisplayName("Connectors")]
    [Description("Enable external-item retrieval for Graph connectors.")]
    public bool Connectors { get; set; }
}
