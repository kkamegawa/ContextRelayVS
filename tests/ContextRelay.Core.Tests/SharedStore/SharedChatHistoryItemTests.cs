using System.Text.Json;
using ContextRelay.Core.SharedStore;
using Xunit;

namespace ContextRelay.Core.Tests.SharedStore;

public sealed class SharedChatHistoryItemTests
{
    [Fact]
    public void ContextLabels_ExtractsOnlyNonEmptyStrings()
    {
        var item = new SharedChatHistoryItem
        {
            Metadata = new()
            {
                ["contextLabels"] = JsonSerializer.SerializeToElement(new object?[] { "Pinned doc", "", null, 123, "Search summary" })
            }
        };

        Assert.Equal(new[] { "Pinned doc", "Search summary" }, item.ContextLabels);
        Assert.True(item.HasContextLabels);
        Assert.Equal("Pinned doc, Search summary", item.ContextLabelsJoinedDisplay);
    }

    [Fact]
    public void ContextLabels_ReturnsEmptyWhenMetadataIsMissing()
    {
        var item = new SharedChatHistoryItem();

        Assert.Empty(item.ContextLabels);
        Assert.False(item.HasContextLabels);
        Assert.Equal(string.Empty, item.ContextLabelsJoinedDisplay);
    }
}
