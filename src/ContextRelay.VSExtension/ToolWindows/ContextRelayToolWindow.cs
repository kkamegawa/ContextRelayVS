using System;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.Shell;

namespace ContextRelay.VSExtension.ToolWindows;

[Guid("bc846b32-f3c7-47c1-a8cf-cae30cfc4a0e")]
public sealed class ContextRelayToolWindow : ToolWindowPane
{
    public ContextRelayToolWindow() : base(null)
    {
        Caption = "ContextRelay";
        var package = ContextRelayPackage.Instance ?? throw new InvalidOperationException("ContextRelayPackage is not initialized.");
        Content = new ContextRelayToolWindowControl(package.Host);
    }
}
