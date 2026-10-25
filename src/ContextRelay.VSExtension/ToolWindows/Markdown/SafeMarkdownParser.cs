using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Markdig;
using Markdig.Extensions.Tables;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace ContextRelay.VSExtension.ToolWindows.Markdown;

internal enum MarkdownBlockKind
{
    Heading,
    Paragraph,
    ListItem,
    Code,
    Rule,
    Table
}

/// <summary>
/// One flattened display block. Remote UI cannot use recursive templates, so nesting is carried as
/// depths (list indent, quote depth) that the template turns into margins and a quote rule.
/// </summary>
internal sealed class MarkdownBlock
{
    public MarkdownBlockKind Kind { get; init; }

    /// <summary>Heading level, clamped to 1-3.</summary>
    public int Level { get; init; }

    public string Text { get; init; } = string.Empty;

    /// <summary>List nesting depth, 0-2 (three levels; deeper items stay at the last level).</summary>
    public int ListDepth { get; init; }

    public string ListMarker { get; init; } = string.Empty;

    /// <summary>Block quote depth, 0-3.</summary>
    public int QuoteDepth { get; init; }

    /// <summary>The fenced code language, if any.</summary>
    public string Language { get; init; } = string.Empty;
}

internal sealed class MarkdownLink
{
    public string Text { get; init; } = string.Empty;

    public string Url { get; init; } = string.Empty;
}

internal sealed class MarkdownDocumentModel
{
    public IReadOnlyList<MarkdownBlock> Blocks { get; init; } = Array.Empty<MarkdownBlock>();

    /// <summary>Distinct https links that passed the safety check, in order of appearance.</summary>
    public IReadOnlyList<MarkdownLink> Links { get; init; } = Array.Empty<MarkdownLink>();
}

/// <summary>
/// Turns an answer into plain-text display blocks. HTML is disabled and tags are dropped, images keep
/// only their alt text (nothing is fetched), emphasis becomes plain text, and link bodies keep only
/// their text while safe https targets are collected for separate open buttons.
/// </summary>
internal static class SafeMarkdownParser
{
    internal const int MaxListDepth = 2;
    internal const int MaxQuoteDepth = 3;

    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UsePipeTables()
        .DisableHtml()
        .Build();

    // With HTML disabled, tags arrive as literal text; drop them so markup never shows up as content.
    private static readonly Regex HtmlTagPattern = new(@"</?[A-Za-z][A-Za-z0-9-]*(\s[^<>]*)?/?>", RegexOptions.CultureInvariant);

    internal static MarkdownDocumentModel Parse(string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return new MarkdownDocumentModel();
        }

        var document = Markdig.Markdown.Parse(markdown!, Pipeline);
        var context = new ParseContext();
        foreach (var block in document)
        {
            AddBlock(block, context, listDepth: -1, quoteDepth: 0);
        }

        return new MarkdownDocumentModel { Blocks = context.Blocks, Links = context.Links };
    }

    private static void AddBlock(Block block, ParseContext context, int listDepth, int quoteDepth)
    {
        switch (block)
        {
            case HeadingBlock heading:
                context.Blocks.Add(new MarkdownBlock
                {
                    Kind = MarkdownBlockKind.Heading,
                    Level = Math.Clamp(heading.Level, 1, 3),
                    Text = RenderInlines(heading.Inline, context),
                    QuoteDepth = quoteDepth
                });
                break;
            case ParagraphBlock paragraph:
                AddText(context, RenderInlines(paragraph.Inline, context), listDepth, quoteDepth, marker: string.Empty);
                break;
            case FencedCodeBlock fenced:
                context.Blocks.Add(new MarkdownBlock
                {
                    Kind = MarkdownBlockKind.Code,
                    Text = GetCodeText(fenced),
                    Language = fenced.Info?.Trim() ?? string.Empty,
                    ListDepth = Math.Max(0, listDepth),
                    QuoteDepth = quoteDepth
                });
                break;
            case CodeBlock code:
                context.Blocks.Add(new MarkdownBlock
                {
                    Kind = MarkdownBlockKind.Code,
                    Text = GetCodeText(code),
                    ListDepth = Math.Max(0, listDepth),
                    QuoteDepth = quoteDepth
                });
                break;
            case ThematicBreakBlock:
                context.Blocks.Add(new MarkdownBlock { Kind = MarkdownBlockKind.Rule, QuoteDepth = quoteDepth });
                break;
            case Table table:
                context.Blocks.Add(new MarkdownBlock
                {
                    Kind = MarkdownBlockKind.Table,
                    Text = RenderTable(table, context),
                    QuoteDepth = quoteDepth
                });
                break;
            case QuoteBlock quote:
                foreach (var child in quote)
                {
                    AddBlock(child, context, listDepth, Math.Min(quoteDepth + 1, MaxQuoteDepth));
                }

                break;
            case ListBlock list:
                AddList(list, context, Math.Min(listDepth + 1, MaxListDepth), quoteDepth);
                break;
            case ContainerBlock container:
                foreach (var child in container)
                {
                    AddBlock(child, context, listDepth, quoteDepth);
                }

                break;
            case LeafBlock leaf:
                AddText(context, StripTags(GetLeafText(leaf)), listDepth, quoteDepth, marker: string.Empty);
                break;
        }
    }

    private static void AddList(ListBlock list, ParseContext context, int depth, int quoteDepth)
    {
        var number = 1;
        if (list.IsOrdered && int.TryParse(list.OrderedStart, NumberStyles.Integer, CultureInfo.InvariantCulture, out var start))
        {
            number = start;
        }

        foreach (var child in list)
        {
            if (child is not ListItemBlock item)
            {
                continue;
            }

            var marker = list.IsOrdered ? string.Create(CultureInfo.InvariantCulture, $"{number}.") : "•";
            number++;
            var markerUsed = false;
            foreach (var itemChild in item)
            {
                if (!markerUsed && itemChild is ParagraphBlock paragraph)
                {
                    AddText(context, RenderInlines(paragraph.Inline, context), depth, quoteDepth, marker);
                    markerUsed = true;
                    continue;
                }

                if (!markerUsed && itemChild is not ListBlock)
                {
                    // An item that starts with a code block or similar still shows its marker.
                    AddText(context, string.Empty, depth, quoteDepth, marker);
                    markerUsed = true;
                }

                AddBlock(itemChild, context, depth, quoteDepth);
            }

            if (!markerUsed)
            {
                AddText(context, string.Empty, depth, quoteDepth, marker);
            }
        }
    }

    private static void AddText(ParseContext context, string text, int listDepth, int quoteDepth, string marker)
    {
        var isListItem = listDepth >= 0;
        if (!isListItem && string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        context.Blocks.Add(new MarkdownBlock
        {
            Kind = isListItem ? MarkdownBlockKind.ListItem : MarkdownBlockKind.Paragraph,
            Text = text,
            ListDepth = Math.Max(0, listDepth),
            ListMarker = marker,
            QuoteDepth = quoteDepth
        });
    }

    private static string RenderInlines(ContainerInline? container, ParseContext context)
    {
        if (container is null)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        AppendInlines(container, builder, context);
        return StripTags(builder.ToString()).Trim();
    }

    private static void AppendInlines(ContainerInline container, StringBuilder builder, ParseContext context)
    {
        foreach (var inline in container)
        {
            switch (inline)
            {
                case LiteralInline literal:
                    builder.Append(literal.Content.ToString());
                    break;
                case CodeInline code:
                    builder.Append('`').Append(code.Content).Append('`');
                    break;
                case LineBreakInline:
                    builder.Append('\n');
                    break;
                case HtmlEntityInline entity:
                    builder.Append(entity.Transcoded.ToString());
                    break;
                case HtmlInline:
                    break;
                case AutolinkInline autolink:
                    builder.Append(autolink.Url);
                    if (!autolink.IsEmail)
                    {
                        context.AddLink(autolink.Url, autolink.Url);
                    }

                    break;
                case LinkInline link:
                    var linkText = new StringBuilder();
                    AppendInlines(link, linkText, context);
                    var text = linkText.ToString();
                    if (link.IsImage)
                    {
                        // Alt text only; the image is never fetched.
                        builder.Append(text);
                        break;
                    }

                    builder.Append(text.Length > 0 ? text : link.Url ?? string.Empty);
                    context.AddLink(text.Length > 0 ? text : link.Url ?? string.Empty, link.Url);
                    break;
                case ContainerInline nested:
                    AppendInlines(nested, builder, context);
                    break;
            }
        }
    }

    private static string RenderTable(Table table, ParseContext context)
    {
        var rows = new List<string[]>();
        var headerRows = 0;
        foreach (var rowBlock in table)
        {
            if (rowBlock is not TableRow row)
            {
                continue;
            }

            var cells = row
                .OfType<TableCell>()
                .Select(cell => string.Join(" ", cell.OfType<ParagraphBlock>().Select(paragraph => RenderInlines(paragraph.Inline, context))).Replace('\n', ' '))
                .ToArray();
            rows.Add(cells);
            if (row.IsHeader)
            {
                headerRows = rows.Count;
            }
        }

        if (rows.Count == 0)
        {
            return string.Empty;
        }

        var columnCount = rows.Max(row => row.Length);
        var widths = new int[columnCount];
        foreach (var row in rows)
        {
            for (var column = 0; column < row.Length; column++)
            {
                widths[column] = Math.Max(widths[column], row[column].Length);
            }
        }

        var builder = new StringBuilder();
        for (var index = 0; index < rows.Count; index++)
        {
            AppendTableRow(builder, rows[index], widths);
            if (index + 1 == headerRows)
            {
                AppendTableRow(builder, widths.Select(width => new string('-', Math.Max(width, 1))).ToArray(), widths);
            }
        }

        return builder.ToString().TrimEnd('\n');
    }

    private static void AppendTableRow(StringBuilder builder, IReadOnlyList<string> cells, IReadOnlyList<int> widths)
    {
        builder.Append("| ");
        for (var column = 0; column < widths.Count; column++)
        {
            var cell = column < cells.Count ? cells[column] : string.Empty;
            builder.Append(cell.PadRight(widths[column]));
            builder.Append(column + 1 < widths.Count ? " | " : " |");
        }

        builder.Append('\n');
    }

    private static string GetCodeText(LeafBlock block)
    {
        var lines = block.Lines.Lines?
            .Take(block.Lines.Count)
            .Select(line => line.Slice.ToString())
            ?? Enumerable.Empty<string>();
        return string.Join("\n", lines).TrimEnd('\n', '\r');
    }

    private static string GetLeafText(LeafBlock block)
    {
        return block.Inline is not null ? string.Concat(block.Inline.Select(inline => inline.ToString())) : GetCodeText(block);
    }

    private static string StripTags(string text) => HtmlTagPattern.Replace(text, string.Empty);

    private sealed class ParseContext
    {
        private readonly HashSet<string> linkUrls = new(StringComparer.Ordinal);

        public List<MarkdownBlock> Blocks { get; } = new();

        public List<MarkdownLink> Links { get; } = new();

        public void AddLink(string text, string? url)
        {
            if (SafeLinks.TryGetOpenableUrl(url, out var openableUrl) && linkUrls.Add(openableUrl))
            {
                Links.Add(new MarkdownLink { Text = string.IsNullOrWhiteSpace(text) ? openableUrl : text.Trim(), Url = openableUrl });
            }
        }
    }
}
