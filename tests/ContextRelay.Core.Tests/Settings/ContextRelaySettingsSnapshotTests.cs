using System.Text.Json;
using ContextRelay.Core.Settings;
using Xunit;

namespace ContextRelay.Core.Tests.Settings;

public sealed class ContextRelaySettingsSnapshotTests
{
    [Fact]
    public void DefaultsIncludeChatSettings()
    {
        var settings = new ContextRelaySettingsSnapshot();

        Assert.Equal(5, settings.ChatMaxAttachedFiles);
        Assert.False(settings.ChatAttachActiveEditor);
        Assert.True(settings.ChatStreamResponses);
    }

    [Fact]
    public void ChatMaxAttachedFilesClampsNegativeValues()
    {
        var settings = new ContextRelaySettingsSnapshot
        {
            ChatMaxAttachedFiles = -1
        };

        Assert.Equal(0, settings.ChatMaxAttachedFiles);
    }

    [Fact]
    public void MissingChatSettingsRemainBackwardCompatible()
    {
        var settings = JsonSerializer.Deserialize<ContextRelaySettingsSnapshot>("{\"MaxResults\":20}");

        Assert.NotNull(settings);
        Assert.Equal(20, settings!.MaxResults);
        Assert.Equal(5, settings.ChatMaxAttachedFiles);
        Assert.False(settings.ChatAttachActiveEditor);
        Assert.True(settings.ChatStreamResponses);
    }

    [Fact]
    public void NegativeChatMaxAttachedFilesFromJsonIsNormalized()
    {
        var settings = JsonSerializer.Deserialize<ContextRelaySettingsSnapshot>("{\"ChatMaxAttachedFiles\":-5}");

        Assert.NotNull(settings);
        Assert.Equal(0, settings!.ChatMaxAttachedFiles);
    }
}
