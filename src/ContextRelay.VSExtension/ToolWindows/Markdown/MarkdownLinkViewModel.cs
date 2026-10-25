using System.Runtime.Serialization;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Extensibility.UI;

namespace ContextRelay.VSExtension.ToolWindows.Markdown;

/// <summary>An open button in a message's "Links" row; only safe https targets get one.</summary>
[DataContract]
internal sealed class MarkdownLinkViewModel
{
    internal MarkdownLinkViewModel(MarkdownLink link, ContextRelayWindowViewModel parent)
    {
        Text = link.Text;
        Url = link.Url;
        OpenCommand = new AsyncCommand((_, _) => { parent.OpenUrl(Url); return Task.CompletedTask; });
    }

    [DataMember] public string Text { get; private set; }
    [DataMember] public string Url { get; private set; }
    [DataMember] public AsyncCommand OpenCommand { get; }
}
