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
    public async Task ReadTextAsync_RejectsAttachmentRetargetedToUnsupportedFile()
    {
        var root = Path.Combine(Path.GetTempPath(), "workspace-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var supportedPath = Path.Combine(root, "file.md");
            var unsupportedPath = Path.Combine(root, "file.bin");
            File.WriteAllText(supportedPath, "supported");
            File.WriteAllText(unsupportedPath, "should not be sent");
            Assert.True(WorkspaceFileAttachmentResolver.TryResolve(supportedPath, new[] { root }, out var attachment));

            attachment!.AbsolutePath = unsupportedPath;

            Assert.Null(await WorkspaceFileAttachmentResolver.ReadTextAsync(
                attachment,
                TestContext.Current.CancellationToken));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ReadTextAsync_RejectsSupportedSymlinkNameWithUnsupportedTarget()
    {
        var root = Path.Combine(Path.GetTempPath(), "workspace-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var unsupportedPath = Path.Combine(root, "payload.bin");
            var supportedAlias = Path.Combine(root, "notes.md");
            File.WriteAllText(unsupportedPath, "should not be sent");
            try
            {
                File.CreateSymbolicLink(supportedAlias, unsupportedPath);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
            {
                Assert.Skip($"Creating a symbolic link is unavailable: {ex.Message}");
            }

            Assert.True(WorkspaceFileAttachmentResolver.TryResolve(supportedAlias, new[] { root }, out var attachment));
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

    [Fact]
    public async Task ReadTextAsync_BoundsSelectedLineWithoutAllocatingTheEntireLine()
    {
        var root = Path.Combine(Path.GetTempPath(), "workspace-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "file.md");
            var oversizedLine = new string('x', WorkspaceFileAttachmentResolver.MaxFileChars + 5000);
            File.WriteAllText(path, "first\r\n" + oversizedLine + "\r\nthird");
            Assert.True(WorkspaceFileAttachmentResolver.TryResolve(path, new[] { root }, out var attachment));
            attachment!.SelectionStartLine = 2;
            attachment.SelectionEndLine = 2;

            var text = await WorkspaceFileAttachmentResolver.ReadTextAsync(attachment, TestContext.Current.CancellationToken);

            Assert.NotNull(text);
            Assert.Equal(WorkspaceFileAttachmentResolver.MaxFileChars, text!.Length);
            Assert.Equal(new string('x', WorkspaceFileAttachmentResolver.MaxFileChars), text);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ReadTextAsync_StopsAtBudgetAcrossSelectedLines()
    {
        var root = Path.Combine(Path.GetTempPath(), "workspace-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "file.md");
            var trailingOversizedLine = new string('z', WorkspaceFileAttachmentResolver.MaxFileChars + 5000);
            File.WriteAllText(path, new string('a', 7000) + "\r\n" + new string('b', 6000) + "\r\n" + trailingOversizedLine);
            Assert.True(WorkspaceFileAttachmentResolver.TryResolve(path, new[] { root }, out var attachment));
            attachment!.SelectionStartLine = 1;
            attachment.SelectionEndLine = 3;

            var text = await WorkspaceFileAttachmentResolver.ReadTextAsync(attachment, TestContext.Current.CancellationToken);

            Assert.NotNull(text);
            Assert.Equal(WorkspaceFileAttachmentResolver.MaxFileChars, text!.Length);
            Assert.StartsWith(new string('a', 7000) + "\n", text, StringComparison.Ordinal);
            Assert.EndsWith(new string('b', 4999), text, StringComparison.Ordinal);
            Assert.DoesNotContain('z', text);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
