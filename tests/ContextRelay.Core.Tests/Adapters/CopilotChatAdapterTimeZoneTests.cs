using System.Reflection;
using ContextRelay.Core.Adapters;
using Xunit;

namespace ContextRelay.Core.Tests.Adapters;

public sealed class CopilotChatAdapterTimeZoneTests
{
    [Fact]
    public void ResolveTimeZone_ReturnsIanaFormattedValue()
    {
        var resolveMethod = typeof(CopilotChatAdapter).GetMethod("ResolveTimeZone", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(resolveMethod);

        var result = resolveMethod!.Invoke(null, null);
        var timeZone = Assert.IsType<string>(result);

        Assert.NotEmpty(timeZone);
        Assert.Contains("/", timeZone);
    }
}
