using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Xunit;

namespace ContextRelay.Core.Tests.ToolWindows;

public sealed class SafeMarkdownParserTests
{
    private const string MarkdownNamespace = "ContextRelay.VSExtension.ToolWindows.Markdown.";

    [Fact]
    public void Parse_FlattensHeadingsParagraphsAndRules()
    {
        var (blocks, _) = Parse("# Title\n\n#### Deep\n\nSome **bold** and *em* text.\n\n---\n");

        Assert.Equal(new[] { "Heading", "Heading", "Paragraph", "Rule" }, blocks.Select(block => block.Kind));
        Assert.Equal(1, blocks[0].Level);
        Assert.Equal("Title", blocks[0].Text);
        Assert.Equal(3, blocks[1].Level);
        Assert.Equal("Some bold and em text.", blocks[2].Text);
    }

    [Fact]
    public void Parse_KeepsListMarkersAndCapsNestingAtThreeLevels()
    {
        var (blocks, _) = Parse("- one\n  - two\n    - three\n      - four\n\n3. third\n4. fourth\n");

        Assert.All(blocks, block => Assert.Equal("ListItem", block.Kind));
        Assert.Equal(new[] { 0, 1, 2, 2, 0, 0 }, blocks.Select(block => block.ListDepth));
        Assert.Equal(new[] { "•", "•", "•", "•", "3.", "4." }, blocks.Select(block => block.ListMarker));
        Assert.Equal("four", blocks[3].Text);
    }

    [Fact]
    public void Parse_KeepsBlocksInsideQuotesWithTheirDepth()
    {
        var (blocks, _) = Parse("> quoted\n>\n> > nested\n>\n> - item\n");

        Assert.Equal(new[] { 1, 2, 1 }, blocks.Select(block => block.QuoteDepth));
        Assert.Equal(new[] { "Paragraph", "Paragraph", "ListItem" }, blocks.Select(block => block.Kind));
        Assert.Equal("nested", blocks[1].Text);
    }

    [Fact]
    public void Parse_RendersPipeTablesAsAlignedText()
    {
        var (blocks, _) = Parse("| Name | Count |\n| --- | --- |\n| alpha | 1 |\n| b | 22 |\n");

        var table = Assert.Single(blocks);
        Assert.Equal("Table", table.Kind);
        Assert.Equal(
            "| Name  | Count |\n| ----- | ----- |\n| alpha | 1     |\n| b     | 22    |",
            table.Text);
    }

    [Fact]
    public void Parse_KeepsCodeVerbatimWithItsLanguage()
    {
        var (blocks, _) = Parse("```csharp\nvar list = new List<string>();\n  <b>kept</b>\n```\n");

        var code = Assert.Single(blocks);
        Assert.Equal("Code", code.Kind);
        Assert.Equal("csharp", code.Language);
        Assert.Equal("var list = new List<string>();\n  <b>kept</b>", code.Text);
    }

    [Fact]
    public void Parse_TreatsAnUnclosedFenceAsCodeInProgress()
    {
        var (blocks, _) = Parse("Intro\n\n```python\nprint('hi')\n");

        Assert.Equal(new[] { "Paragraph", "Code" }, blocks.Select(block => block.Kind));
        Assert.Equal("print('hi')", blocks[1].Text);
        Assert.Equal("python", blocks[1].Language);
    }

    [Fact]
    public void Parse_CollectsOnlySafeHttpsLinksAndKeepsLinkText()
    {
        var (blocks, links) = Parse(
            "See [docs](https://example.com/docs), [again](https://example.com/docs), [plain](http://example.com) and [bad](javascript:alert(1)).");

        Assert.Equal("See docs, again, plain and bad.", Assert.Single(blocks).Text);
        var link = Assert.Single(links);
        Assert.Equal("docs", link.Text);
        Assert.StartsWith("https://example.com/docs", link.Url, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_DropsHtmlAndKeepsOnlyImageAltText()
    {
        var (blocks, links) = Parse("<div onclick=\"x\">Hello</div>\n\nText with <b>tags</b> and ![a chart](https://example.com/chart.png).\n");

        Assert.Equal(new[] { "Hello", "Text with tags and a chart." }, blocks.Select(block => block.Text));
        Assert.Empty(links);
        Assert.DoesNotContain(blocks, block => block.Text.Contains('<', StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \n  ")]
    public void Parse_ReturnsNoBlocksForEmptyInput(string? markdown)
    {
        var (blocks, links) = Parse(markdown);

        Assert.Empty(blocks);
        Assert.Empty(links);
    }

    [Fact]
    public void BlockList_UpdatesExistingBlocksInPlaceAndTrimsTheRest()
    {
        var assembly = LoadBuiltExtensionAssembly();
        var blockViewModelType = assembly.GetType(MarkdownNamespace + "MarkdownBlockViewModel", throwOnError: true)!;
        var update = assembly.GetType(MarkdownNamespace + "MarkdownBlockList", throwOnError: true)!
            .GetMethod("Update", BindingFlags.Static | BindingFlags.NonPublic)!;
        // The test project does not reference the extensibility SDK, so take the list type from the signature.
        var target = (IList)Activator.CreateInstance(update.GetParameters()[0].ParameterType)!;

        update.Invoke(null, new[] { target, ParseRaw(assembly, "First\n\nSecond\n\nThird") });
        Assert.Equal(3, target.Count);
        var first = target[0];

        update.Invoke(null, new[] { target, ParseRaw(assembly, "# First grew") });

        Assert.Equal(1, target.Count);
        Assert.Same(first, target[0]);
        Assert.Equal("First grew", blockViewModelType.GetProperty("Text")!.GetValue(first));
        Assert.Equal(true, blockViewModelType.GetProperty("IsHeading1")!.GetValue(first));
    }

    private static (IReadOnlyList<BlockView> Blocks, IReadOnlyList<LinkView> Links) Parse(string? markdown)
    {
        var assembly = LoadBuiltExtensionAssembly();
        var document = ParseDocument(assembly, markdown);
        var blocks = ((IEnumerable)document.GetType().GetProperty("Blocks")!.GetValue(document)!)
            .Cast<object>()
            .Select(block => new BlockView(
                Read<object>(block, "Kind").ToString()!,
                Read<int>(block, "Level"),
                Read<string>(block, "Text"),
                Read<int>(block, "ListDepth"),
                Read<string>(block, "ListMarker"),
                Read<int>(block, "QuoteDepth"),
                Read<string>(block, "Language")))
            .ToList();
        var links = ((IEnumerable)document.GetType().GetProperty("Links")!.GetValue(document)!)
            .Cast<object>()
            .Select(link => new LinkView(Read<string>(link, "Text"), Read<string>(link, "Url")))
            .ToList();
        return (blocks, links);
    }

    private static object ParseRaw(Assembly assembly, string markdown)
    {
        var document = ParseDocument(assembly, markdown);
        return document.GetType().GetProperty("Blocks")!.GetValue(document)!;
    }

    private static object ParseDocument(Assembly assembly, string? markdown)
    {
        var parser = assembly.GetType(MarkdownNamespace + "SafeMarkdownParser", throwOnError: true)!;
        var method = parser.GetMethod("Parse", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return method!.Invoke(null, new object?[] { markdown })!;
    }

    private static T Read<T>(object instance, string propertyName)
    {
        return (T)instance.GetType().GetProperty(propertyName)!.GetValue(instance)!;
    }

    private static Assembly LoadBuiltExtensionAssembly()
    {
        return Assembly.LoadFrom(BuiltExtensionArtifactLocator.ResolveExtensionArtifactPath("ContextRelay.VSExtension.dll"));
    }

    private sealed record BlockView(string Kind, int Level, string Text, int ListDepth, string ListMarker, int QuoteDepth, string Language);

    private sealed record LinkView(string Text, string Url);
}
