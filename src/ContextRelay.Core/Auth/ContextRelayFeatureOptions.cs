namespace ContextRelay.Core.Auth;

public sealed class ContextRelayFeatureOptions
{
    public bool MailEnabled { get; set; } = true;

    public bool TeamsEnabled { get; set; } = true;

    public bool SharePointEnabled { get; set; } = true;

    public bool OneDriveEnabled { get; set; } = true;

    public bool ConnectorsEnabled { get; set; }

    public bool OneNoteEnabled { get; set; }

    public bool PlannerEnabled { get; set; }

    public bool TodoEnabled { get; set; }

    public bool ChatPreviewEnabled { get; set; } = true;
}
