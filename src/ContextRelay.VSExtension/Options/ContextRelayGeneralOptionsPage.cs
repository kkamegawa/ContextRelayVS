using System.ComponentModel;
using Microsoft.VisualStudio.Shell;

namespace ContextRelay.VSExtension.Options;

public sealed class ContextRelayGeneralOptionsPage : DialogPage
{
    [Category("General")]
    [DisplayName("Max results")]
    [Description("Maximum number of results returned per source search.")]
    public int MaxResults { get; set; } = 10;

    [Category("General")]
    [DisplayName("Output directory")]
    [Description("Relative or absolute directory used for generated handoff documents.")]
    public string OutputDirectory { get; set; } = ".contextrelay";

    [Category("General")]
    [DisplayName("Enable chat preview")]
    [Description("Enable /ask against the Microsoft 365 Copilot chat preview API.")]
    public bool EnableChatPreview { get; set; } = true;

    [Category("General")]
    [DisplayName("Enable Graph debug logging")]
    [Description("Write Graph request/response summaries to the ContextRelay Debug output pane.")]
    public bool EnableGraphDebugLogging { get; set; }
}
