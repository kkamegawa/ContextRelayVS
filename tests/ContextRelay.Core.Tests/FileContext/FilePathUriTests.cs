using System;
using ContextRelay.Core.FileContext;
using Xunit;

namespace ContextRelay.Core.Tests.FileContext;

public sealed class FilePathUriTests
{
    [Fact]
    public void FromPath_ConvertsWindowsDriveLetterPath()
    {
        Assert.Equal("file:///C:/workspace/docs/plan.md", FilePathUri.FromPath(@"C:\workspace\docs\plan.md"));
    }

    [Fact]
    public void FromPath_ConvertsUnixPathWithoutRelativeUriFailure()
    {
        Assert.Equal("file:///tmp/workspace/file.md", FilePathUri.FromPath("/tmp/workspace/file.md"));
    }

    [Fact]
    public void FromPath_ConvertsUncPathToHostAndShare()
    {
        Assert.Equal("file://server/share/file.md", FilePathUri.FromPath(@"\\server\share\file.md"));
    }

    [Fact]
    public void FromPath_EscapesSpacesInPathSegments()
    {
        Assert.Equal("file:///tmp/design%20notes.md", FilePathUri.FromPath("/tmp/design notes.md"));
    }

    [Fact]
    public void FromPath_RejectsEmptyPath()
    {
        Assert.Throws<ArgumentException>(() => FilePathUri.FromPath("  "));
    }
}
