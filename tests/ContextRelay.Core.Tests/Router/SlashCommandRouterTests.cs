using ContextRelay.Core.Router;
using Xunit;

namespace ContextRelay.Core.Tests.Router;

public sealed class SlashCommandRouterTests
{
    [Fact]
    public void Parse_WithoutSlashCommand_RoutesToAllSources()
    {
        var result = SlashCommandRouter.Parse("  architecture   decisions  ");

        Assert.Equal(RouteTarget.All, result.Target);
        Assert.Equal("architecture decisions", result.Query);
        Assert.False(result.IsEmpty);
        Assert.Equal(4, result.TargetSources.Count);
    }

    [Fact]
    public void Parse_KnownSlashCommand_RoutesToSpecificSource()
    {
        var result = SlashCommandRouter.Parse("/mail from:alice subject:budget");

        Assert.Equal(RouteTarget.Mail, result.Target);
        Assert.Equal("/mail", result.SlashCommandName);
        Assert.Equal("from:alice subject:budget", result.Query);
        Assert.Single(result.TargetSources);
        Assert.Equal(ContextSource.Mail, result.TargetSources[0]);
    }

    [Fact]
    public void Parse_AskCommand_PreservesInstructionShape()
    {
        var result = SlashCommandRouter.Parse("/ask  Summarize this\nas markdown  ");

        Assert.Equal(RouteTarget.Ask, result.Target);
        Assert.Equal("Summarize this\nas markdown", result.Query);
        Assert.True(result.TargetSources.Count == 0);
    }

    [Fact]
    public void Parse_ClearCommand_IsNeverEmpty()
    {
        var result = SlashCommandRouter.Parse("/clear");

        Assert.Equal(RouteTarget.Clear, result.Target);
        Assert.False(result.IsEmpty);
        Assert.Equal(string.Empty, result.Query);
    }

    [Fact]
    public void Parse_UnknownSlashCommand_FallsBackToAll()
    {
        var result = SlashCommandRouter.Parse("/unknown test value");

        Assert.Equal(RouteTarget.All, result.Target);
        Assert.Equal("/unknown test value", result.Query);
        Assert.False(result.IsEmpty);
    }

    [Fact]
    public void GetHelpText_ReturnsCommandSpecificExamples()
    {
        var help = SlashCommandRouter.GetHelpText("/teams");

        Assert.Contains("/teams sprint review", help);
    }
}
