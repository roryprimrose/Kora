using System.Diagnostics;
using System.Globalization;
using System.Text;

using Kora.Core.Presentation;

using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

using Microsoft.Extensions.Logging;

namespace Kora;

internal sealed partial class NativeDetailRenderer(ILogger<NativeDetailRenderer> logger, bool available = true)
{
    private static readonly ActivitySource ActivitySource =
        new("Kora.Desktop", typeof(NativeDetailRenderer).Assembly.GetName().Version!.ToString());
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder
    {
        MaximumNestingDepth = NativeDetailProfile.MaximumNesting,
    }.Build();
    private static readonly HashSet<Type> SupportedTypes =
    [
        typeof(MarkdownDocument), typeof(HeadingBlock), typeof(ParagraphBlock), typeof(ListBlock),
        typeof(ListItemBlock), typeof(QuoteBlock), typeof(CodeBlock), typeof(FencedCodeBlock),
        typeof(ThematicBreakBlock), typeof(LinkReferenceDefinition), typeof(LinkReferenceDefinitionGroup),
        typeof(LiteralInline), typeof(CodeInline), typeof(LineBreakInline),
        typeof(AutolinkInline), typeof(LinkInline), typeof(EmphasisInline), typeof(ContainerInline),
    ];

    // Supported: CommonMark headings, paragraphs, lists, quotes, breaks, literal/code text,
    // emphasis (plain semantic text), fenced/indented code, and inert labelled links.
    // No extensions, HTML conversion, resource resolver, browser, or actionable links.
    internal NativeDocumentResult Render(
        string source,
        DetailContentKind kind = DetailContentKind.Markdown,
        DetailContentReference? reference = null)
    {
        using var render = ActivitySource.StartActivity("presentation.render");
        render?.SetStatus(ActivityStatusCode.Error);
        render?.SetTag("kora.item.id", reference?.ItemId.Value);
        render?.SetTag("kora.item.revision", reference?.Revision);
        render?.SetTag("kora.presentation.profile", NativeDetailProfile.Name);
        ArgumentNullException.ThrowIfNull(source);
        int byteCount;
        try { byteCount = Utf8.GetByteCount(source); }
        catch (EncoderFallbackException)
        {
            return Fallback(string.Empty, "InvalidUnicode", "Invalid Unicode source; rendering refused.", reference);
        }
        if (byteCount > NativeDetailProfile.MaximumUtf8Bytes)
        {
            // Unadmitted callers (guide/grants facade) cannot create an oversized source control.
            return Fallback(string.Empty, "ByteLimit",
                "Source exceeds native-text-v1 256 KiB UTF-8; rendering refused. Nothing was truncated.", reference);
        }
        if (string.IsNullOrWhiteSpace(source))
        {
            return Fallback(source, "EmptySource", "Source has no readable text; rendering refused.", reference);
        }
        if (!available)
        {
            return Fallback(source, "Unavailable", "Renderer unavailable; exact source fallback.", reference);
        }
        if (!Enum.IsDefined(kind))
        {
            return Fallback(source, "UnsupportedKind", "Unsupported content kind; exact source fallback.", reference);
        }
        if (kind == DetailContentKind.PlainText)
        {
            render?.SetStatus(ActivityStatusCode.Ok);
            return Success(source, [new(source, NativeTextStyle.Paragraph)], 0, 1, 1);
        }
        var preflight = Preflight(source);
        if (preflight is not null)
        {
            return Fallback(source, "SourcePreflight", preflight, reference);
        }

        MarkdownDocument document;
        try { document = Markdown.Parse(source, Pipeline); }
        catch (InvalidOperationException)
        {
            // Markdig's nesting guard throws InvalidOperationException. Never log its source-bearing message.
            return Fallback(source, "ParserGuard", "Parser nesting/error guard; exact source fallback.", reference);
        }

        var validation = Validate(document);
        if (validation.Error is not null)
        {
            return Fallback(source, "Structure", validation.Error, reference,
                validation.Blocks, validation.Nodes, validation.Depth);
        }
        List<NativeTextSection> sections;
        try { sections = Flatten(document); }
        catch (InvalidDataException)
        {
            return Fallback(source, "SemanticLimit", "Semantic text limit exceeded; exact source fallback.", reference);
        }
        catch (InvalidOperationException)
        {
            return Fallback(source, "RenderGuard", "Native rendering guard failed; exact source fallback.", reference,
                validation.Blocks, validation.Nodes, validation.Depth);
        }
        // Native controls are one per section, never a recursive control tree.
        if (sections.Count > NativeDetailProfile.MaximumNodes)
        {
            return Fallback(source, "ControlLimit", "Native control limit exceeded; exact source fallback.", reference);
        }
        var semantic = string.Join(Environment.NewLine + Environment.NewLine, sections.Select(section => section.Text));
        if (string.IsNullOrWhiteSpace(semantic))
        {
            return Fallback(source, "EmptyProjection", "No readable native projection; exact source fallback.", reference);
        }
        render?.SetStatus(ActivityStatusCode.Ok);
        return new(source, semantic,
            "Rendered native-text-v1 CommonMark text subset. Links are inert; emphasis is plain text.",
            false, sections, validation.Blocks, validation.Nodes, validation.Depth);
    }

    private static NativeDocumentResult Success(
        string source, IReadOnlyList<NativeTextSection> sections, int blocks, int nodes, int depth) =>
        new(source, source, "Rendered native-text-v1 plain text.", false, sections, blocks, nodes, depth);

    private NativeDocumentResult Fallback(
        string source, string reason, string message, DetailContentReference? reference,
        int blocks = 0, int nodes = 0, int depth = 0)
    {
        RenderFallback(logger, reason, reference?.ItemId.Value, reference?.Revision,
            NativeDetailProfile.Name, blocks, nodes, depth);
        return new(source, source, message, true, [], blocks, nodes, depth);
    }

    [LoggerMessage(310, LogLevel.Warning,
        "Native detail source fallback: {Reason}, ItemId {ItemId}, Revision {Revision}, Profile {Profile}, Blocks {Blocks}, Nodes {Nodes}, Depth {Depth}.")]
    private static partial void RenderFallback(
        ILogger logger, string reason, Guid? itemId, long? revision, string profile, int blocks, int nodes, int depth);

    private static string? Preflight(string source)
    {
        // Conservative lexical rejection complements Markdig's pre-parse nesting cap.
        // False positives stay readable as exact source rather than being partially rendered.
        var brackets = 0;
        var parentheses = 0;
        foreach (var line in source.AsSpan().EnumerateLines())
        {
            var indent = 0;
            var quotes = 0;
            foreach (var character in line)
            {
                if (character == ' ') { indent++; }
                else if (character == '\t') { indent += 4; }
                else { break; }
            }
            var prefix = line.TrimStart();
            while (!prefix.IsEmpty && prefix[0] == '>')
            {
                quotes++;
                prefix = prefix[1..].TrimStart();
            }
            if (quotes > NativeDetailProfile.MaximumNesting || indent / 2 > NativeDetailProfile.MaximumNesting)
            {
                return "Source nesting preflight exceeds depth 32; exact source fallback.";
            }
            foreach (var character in line)
            {
                switch (character)
                {
                    case '[': brackets++; break;
                    case ']': brackets = Math.Max(0, brackets - 1); break;
                    case '(': parentheses++; break;
                    case ')': parentheses = Math.Max(0, parentheses - 1); break;
                }
                if (brackets > NativeDetailProfile.MaximumNesting || parentheses > NativeDetailProfile.MaximumNesting)
                {
                    return "Source nesting preflight exceeds depth 32; exact source fallback.";
                }
            }
            var trimmed = line.Trim();
            if (trimmed.Contains("~~", StringComparison.Ordinal)
                || trimmed.StartsWith(":::", StringComparison.Ordinal)
                || trimmed.StartsWith("[^", StringComparison.Ordinal)
                || trimmed.Contains("[ ]", StringComparison.Ordinal)
                || trimmed.Contains("[x]", StringComparison.OrdinalIgnoreCase)
                || (trimmed.Contains('|') && IsTableSeparator(trimmed)))
            {
                return "Unsupported Markdown extension/table; exact source fallback.";
            }
        }
        return null;
    }

    private static bool IsTableSeparator(ReadOnlySpan<char> line)
    {
        var dash = false;
        foreach (var character in line)
        {
            if (character == '-') { dash = true; }
            else if (character is not ('|' or ':' or ' ' or '\t')) { return false; }
        }
        return dash;
    }

    internal static (string? Error, int Blocks, int Nodes, int Depth) Validate(MarkdownDocument document)
    {
        var pending = new Stack<(MarkdownObject Node, int Depth)>();
        pending.Push((document, 0));
        var blocks = 0;
        var nodes = 0;
        var depth = 0;
        string? unsupported = null;
        while (pending.TryPop(out var entry))
        {
            nodes++;
            if (entry.Node is Block and not MarkdownDocument) { blocks++; }
            depth = Math.Max(depth, entry.Depth);
            if (blocks > NativeDetailProfile.MaximumBlocks || nodes > NativeDetailProfile.MaximumNodes
                || depth > NativeDetailProfile.MaximumNesting)
            {
                return ("Structural limit exceeded (512 blocks / 4096 nodes / depth 32); exact source fallback.",
                    blocks, nodes, depth);
            }
            unsupported ??= Unsupported(entry.Node);
            // Advance siblings without recursively constructing or queuing a large visual tree.
            switch (entry.Node)
            {
                case ContainerBlock container:
                    for (var index = container.Count - 1; index >= 0; index--)
                    {
                        pending.Push((container[index], entry.Depth + 1));
                        if (pending.Count + nodes > NativeDetailProfile.MaximumNodes)
                        {
                            return ("Structural limit exceeded (4096 nodes); exact source fallback.",
                                blocks, pending.Count + nodes, depth);
                        }
                    }
                    break;
                case LeafBlock { Inline: { } inline }:
                    pending.Push((inline, entry.Depth + 1));
                    break;
                case ContainerInline container:
                    for (var child = container.LastChild; child is not null; child = child.PreviousSibling)
                    {
                        pending.Push((child, entry.Depth + 1));
                        if (pending.Count + nodes > NativeDetailProfile.MaximumNodes)
                        {
                            return ("Structural limit exceeded (4096 nodes); exact source fallback.",
                                blocks, pending.Count + nodes, depth);
                        }
                    }
                    break;
            }
        }
        return (unsupported, blocks, nodes, depth);
    }

    private static string? Unsupported(MarkdownObject node) =>
        node switch
        {
            HtmlBlock or HtmlInline => "Unsupported raw HTML; exact source fallback (no HTML/resource loading).",
            LinkInline { IsImage: true } => "Unsupported image; exact source fallback (no resource loading).",
            FencedCodeBlock code when code.Info?.TrimStart().StartsWith("mermaid", StringComparison.OrdinalIgnoreCase) == true =>
                "Unsupported Mermaid; exact source fallback (no diagram execution).",
            EmphasisInline { DelimiterCount: not (1 or 2) } =>
                "Unsupported emphasis extension; exact source fallback.",
            _ when SupportedTypes.Contains(node.GetType()) => null,
            _ => "Unsupported Markdown node/extension; exact source fallback.",
        };

    private static List<NativeTextSection> Flatten(MarkdownDocument document)
    {
        var sections = new List<NativeTextSection>();
        var semanticBudget = new BoundedNativeText();
        var pending = new Stack<(Block Block, string Prefix)>();
        for (var index = document.Count - 1; index >= 0; index--) { pending.Push((document[index], string.Empty)); }
        while (pending.TryPop(out var entry))
        {
            var block = entry.Block;
            // CommonMark definitions are counted metadata; each used destination is
            // projected by its link, while unused definitions remain in exact source.
            if (block is LinkReferenceDefinition) { continue; }
            if (block is ContainerBlock container)
            {
                var number = block is ListBlock list && int.TryParse(list.OrderedStart,
                    NumberStyles.Integer, CultureInfo.InvariantCulture, out var start) ? start : 1;
                for (var index = container.Count - 1; index >= 0; index--)
                {
                    var prefix = block switch
                    {
                        QuoteBlock => entry.Prefix + "> ",
                        ListBlock { IsOrdered: true } => entry.Prefix + (number + (long)index).ToString(CultureInfo.InvariantCulture) + ". ",
                        ListBlock => entry.Prefix + "- ",
                        _ => entry.Prefix,
                    };
                    pending.Push((container[index], prefix));
                }
                continue;
            }
            var text = block switch
            {
                CodeBlock code => code.Lines.ToString(),
                ThematicBreakBlock => "—",
                LeafBlock leaf => InlineText(leaf.Inline),
                _ => throw new InvalidOperationException("The validated native block is unsupported."),
            };
            var style = block switch
            {
                HeadingBlock { Level: 1 } => NativeTextStyle.Heading1,
                HeadingBlock { Level: 2 } => NativeTextStyle.Heading2,
                HeadingBlock => NativeTextStyle.Heading,
                CodeBlock => NativeTextStyle.Code,
                _ => NativeTextStyle.Paragraph,
            };
            if (sections.Count != 0) { semanticBudget.Append(Environment.NewLine + Environment.NewLine); }
            semanticBudget.Append(entry.Prefix);
            semanticBudget.Append(text);
            sections.Add(new(entry.Prefix + text, style));
        }
        return sections;
    }

    private static string InlineText(ContainerInline? root)
    {
        if (root is null) { return string.Empty; }
        var text = new BoundedNativeText();
        var pending = new Stack<(Inline Node, bool Exit)>();
        pending.Push((root, false));
        while (pending.TryPop(out var entry))
        {
            if (entry.Exit && entry.Node is LinkInline link)
            {
                text.Append(" (destination: ");
                text.Append(link.Url);
                text.Append(")");
                continue;
            }
            switch (entry.Node)
            {
                case LiteralInline literal: text.Append(literal.Content.ToString()); break;
                case CodeInline code: text.Append(code.Content); break;
                case LineBreakInline: text.Append(Environment.NewLine); break;
                case AutolinkInline autolink:
                    text.Append(autolink.Url);
                    text.Append(" (inert destination)");
                    break;
                case ContainerInline container:
                    if (container is LinkInline) { pending.Push((container, true)); }
                    for (var child = container.LastChild; child is not null; child = child.PreviousSibling)
                    {
                        pending.Push((child, false));
                    }
                    break;
                default: throw new InvalidOperationException("The validated native inline is unsupported.");
            }
        }
        return text.ToString();
    }
}
