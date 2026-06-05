using System.Collections.Generic;
using ContextRelay.Core.Router;
using Xunit;

namespace ContextRelay.Core.Tests.Router;

public sealed class SlashCommandRouterTests
{
    [Fact]
    public void Parse_WithoutSlashCommand_RoutesToChat()
    {
        var result = SlashCommandRouter.Parse("  architecture   decisions  ");

        Assert.Equal(RouteTarget.Chat, result.Target);
        Assert.Equal("architecture decisions", result.Query);
        Assert.False(result.IsEmpty);
        Assert.Empty(result.TargetSources);
    }

    [Fact]
    public void Parse_AllCommand_RoutesToAllSources()
    {
        var result = SlashCommandRouter.Parse("/all architecture decisions");

        Assert.Equal(RouteTarget.All, result.Target);
        Assert.Equal("architecture decisions", result.Query);
        Assert.False(result.IsEmpty);
        Assert.Equal(7, result.TargetSources.Count);
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
    public void Parse_WorkIqCommand_PreservesInstructionShape()
    {
        var result = SlashCommandRouter.Parse("/workiq  Summarize this\nfor today  ");

        Assert.Equal(RouteTarget.WorkIq, result.Target);
        Assert.Equal("Summarize this\nfor today", result.Query);
        Assert.True(result.TargetSources.Count == 0);
    }

    [Fact]
    public void Parse_ConnectorsCommand_RoutesToConnectorSource()
    {
        var result = SlashCommandRouter.Parse("/connectors incident tracker");

        Assert.Equal(RouteTarget.Connectors, result.Target);
        Assert.Equal("/connectors", result.SlashCommandName);
        Assert.Equal("incident tracker", result.Query);
        Assert.Single(result.TargetSources);
        Assert.Equal(ContextSource.Connectors, result.TargetSources[0]);
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
    public void Parse_UnknownSlashCommand_FallsBackToAllSearch()
    {
        var result = SlashCommandRouter.Parse("/unknown test value");

        Assert.Equal(RouteTarget.All, result.Target);
        Assert.Equal("/all", result.SlashCommandName);
        Assert.Equal("/unknown test value", result.Query);
        Assert.False(result.IsEmpty);
        Assert.Equal(SearchScope.All, result.SearchScope);
        Assert.Equal(7, result.TargetSources.Count);
    }

    [Fact]
    public void GetHelpText_ReturnsCommandSpecificExamples()
    {
        var help = SlashCommandRouter.GetHelpText("/teams");

        Assert.Contains("/teams sprint review", help);
    }

    [Fact]
    public void GetSupportedCommands_IncludesConnectorsCommand()
    {
        var commands = SlashCommandRouter.GetSupportedCommands();

        Assert.Contains("/connectors", commands);
        Assert.Contains("/workiq", commands);
    }

    [Fact]
    public void Parse_OneNoteCommand_RoutesToOneNoteSource()
    {
        var result = SlashCommandRouter.Parse("/onenote architecture decision log");

        Assert.Equal(RouteTarget.OneNote, result.Target);
        Assert.Equal("/onenote", result.SlashCommandName);
        Assert.Equal("architecture decision log", result.Query);
        Assert.Single(result.TargetSources);
        Assert.Equal(ContextSource.OneNote, result.TargetSources[0]);
    }

    [Fact]
    public void Parse_TaskCommand_RoutesToPlannerAndTodoSources()
    {
        var result = SlashCommandRouter.Parse("/task release checklist");

        Assert.Equal(RouteTarget.Task, result.Target);
        Assert.Equal("/task", result.SlashCommandName);
        Assert.Equal("release checklist", result.Query);
        Assert.Equal(2, result.TargetSources.Count);
        Assert.Contains(ContextSource.Planner, result.TargetSources);
        Assert.Contains(ContextSource.Todo, result.TargetSources);
    }

    [Fact]
    public void GetSupportedCommands_IncludesOneNoteAndTaskCommands()
    {
        var commands = SlashCommandRouter.GetSupportedCommands();

        Assert.Contains("/onenote", commands);
        Assert.Contains("/task", commands);
    }

    [Fact]
    public void GetHelpText_ReturnsOneNoteExamples()
    {
        var help = SlashCommandRouter.GetHelpText("/onenote");

        Assert.Contains("/onenote architecture decision log", help);
    }

    [Fact]
    public void GetHelpText_ReturnsTaskExamples()
    {
        var help = SlashCommandRouter.GetHelpText("/task");

        Assert.Contains("/task release checklist", help);
    }

    [Fact]
    public void GetHelpText_ReturnsWorkIqExamples()
    {
        var help = SlashCommandRouter.GetHelpText("/workiq");

        Assert.Contains("/workiq Summarize my recent emails from Alice", help);
    }

    [Fact]
    public void GetSupportedCommands_DoesNotExposeMutableArrayState()
    {
        var commands = SlashCommandRouter.GetSupportedCommands();

        Assert.False(commands is string[]);

        if (commands is IList<string> mutableCommands && !mutableCommands.IsReadOnly)
        {
            mutableCommands[0] = "/mutated";
        }

        Assert.Contains("/mail", SlashCommandRouter.GetSupportedCommands());
        Assert.DoesNotContain("/mutated", SlashCommandRouter.GetSupportedCommands());
    }

    [Fact]
    public void Parse_CombinableSourceCommands_RoutesToExplicitSources()
    {
        var result = SlashCommandRouter.Parse("/mail /onedrive architecture decisions");

        Assert.Equal(RouteTarget.All, result.Target);
        Assert.Equal("architecture decisions", result.Query);
        Assert.Equal(SearchScope.Scoped, result.SearchScope);
        Assert.Equal(new[] { "/mail", "/onedrive" }, result.SourceCommandNames);
        Assert.Equal(new[] { ContextSource.Mail, ContextSource.OneDrive }, result.TargetSources);
    }

    [Fact]
    public void Parse_AllCombinedWithExplicitSource_FallsBackToAllSearch()
    {
        var result = SlashCommandRouter.Parse("/all /mail architecture decisions");

        Assert.Equal(RouteTarget.All, result.Target);
        Assert.Equal("/all /mail architecture decisions", result.Query);
        Assert.Equal(SearchScope.All, result.SearchScope);
        Assert.Empty(result.SourceCommandNames);
    }
}
