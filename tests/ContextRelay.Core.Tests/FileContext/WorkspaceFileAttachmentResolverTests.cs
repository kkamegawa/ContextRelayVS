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

    [Fact]
    public void TryResolve_PreservesDriveRootWorkspace()
    {
        var root = Path.GetPathRoot(Path.GetTempPath());
        Assert.False(string.IsNullOrWhiteSpace(root));
        var path = Path.Combine(Path.GetTempPath(), "workspace-root-" + Guid.NewGuid().ToString("N") + ".md");
        File.WriteAllText(path, "drive root file");
        try
        {
            Assert.True(WorkspaceFileAttachmentResolver.TryResolve(path, new[] { root! }, out var attachment));
            Assert.Equal(root, attachment!.WorkspaceRoot, ignoreCase: true);
            Assert.Contains("drive root file", File.ReadAllText(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ReadTextAsync_RejectsPathReplacedBySymlinkToOutsideWorkspace()
    {
        var root = Path.Combine(Path.GetTempPath(), "workspace-" + Guid.NewGuid().ToString("N"));
        var outsideRoot = Path.Combine(Path.GetTempPath(), "outside-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(outsideRoot);
        try
        {
            var path = Path.Combine(root, "file.md");
            var outsidePath = Path.Combine(outsideRoot, "secret.md");
            File.WriteAllText(path, "inside");
            File.WriteAllText(outsidePath, "outside");
            Assert.True(WorkspaceFileAttachmentResolver.TryResolve(path, new[] { root }, out var attachment));

            File.Delete(path);
            try
            {
                File.CreateSymbolicLink(path, outsidePath);
            }
            catch (IOException ex) when (OperatingSystem.IsWindows())
            {
                Assert.Skip($"Creating a symbolic link requires Windows Developer Mode or SeCreateSymbolicLinkPrivilege: {ex.Message}");
            }

            Assert.Null(await WorkspaceFileAttachmentResolver.ReadTextAsync(
                attachment!,
                TestContext.Current.CancellationToken));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
            Directory.Delete(outsideRoot, recursive: true);
        }
    }

    [Fact]
    public async Task ReadTextAsync_RejectsAttachmentRetargetedOutsideWorkspace()
    {
        var root = Path.Combine(Path.GetTempPath(), "workspace-" + Guid.NewGuid().ToString("N"));
        var outsideRoot = Path.Combine(Path.GetTempPath(), "outside-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(outsideRoot);
        try
        {
            var path = Path.Combine(root, "file.md");
            var outsidePath = Path.Combine(outsideRoot, "secret.md");
            File.WriteAllText(path, "inside");
            File.WriteAllText(outsidePath, "outside");
            Assert.True(WorkspaceFileAttachmentResolver.TryResolve(path, new[] { root }, out var attachment));

            attachment!.AbsolutePath = outsidePath;

            Assert.Null(await WorkspaceFileAttachmentResolver.ReadTextAsync(
                attachment,
                TestContext.Current.CancellationToken));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
            Directory.Delete(outsideRoot, recursive: true);
        }
    }

    [Fact]
    public async Task ReadTextAsync_ReadsOnlySelectedLines()
    {
        var root = Path.Combine(Path.GetTempPath(), "workspace-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "file.md");
            File.WriteAllText(path, "one\ntwo\nthree\nfour");
            Assert.True(WorkspaceFileAttachmentResolver.TryResolve(path, new[] { root }, out var attachment));
            attachment!.SelectionStartLine = 2;
            attachment.SelectionEndLine = 3;

            var text = await WorkspaceFileAttachmentResolver.ReadTextAsync(attachment, TestContext.Current.CancellationToken);

            Assert.Equal("two\nthree", text);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
