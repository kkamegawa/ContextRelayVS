using System;
using System.IO;
using System.Threading.Tasks;
using ContextRelay.Core.FileContext;
using Xunit;

namespace ContextRelay.Core.Tests.FileContext;

public sealed class WorkspaceFileAttachmentResolverTests
{
    [Fact]
    public void TryResolve_RejectsOutsideWorkspaceAndPathPrefixCollision()
    {
        var root = Path.Combine(Path.GetTempPath(), "workspace-" + Guid.NewGuid().ToString("N"));
        var sibling = root + "-other";
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(sibling);
        try
        {
            var outside = Path.Combine(sibling, "file.md");
            File.WriteAllText(outside, "secret");
            Assert.False(WorkspaceFileAttachmentResolver.TryResolve(outside, new[] { root }, out _));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
            Directory.Delete(sibling, recursive: true);
        }
    }

    [Fact]
    public async Task ReadTextAsync_RevalidatesAttachmentAtSendTime()
    {
        var root = Path.Combine(Path.GetTempPath(), "workspace-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "file.md");
            File.WriteAllText(path, "before");
            Assert.True(WorkspaceFileAttachmentResolver.TryResolve(path, new[] { root }, out var attachment));
            File.Delete(path);
            Assert.Null(await WorkspaceFileAttachmentResolver.ReadTextAsync(
                attachment!,
                TestContext.Current.CancellationToken));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
