using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Xml;

namespace Kora.Core.Presentation;

public static class StaticHtmlParser
{
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private static readonly ActivitySource ActivitySource =
        new("Kora.Core", typeof(StaticHtmlParser).Assembly.GetName().Version!.ToString());

    public static StaticHtmlDocument Parse(string source, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        using var activity = ActivitySource.StartActivity("presentation.static-html.parse");
        activity?.SetTag("kora.presentation.profile", StaticHtmlProfile.Name);
        activity?.SetStatus(ActivityStatusCode.Error);
        cancellationToken.ThrowIfCancellationRequested();
        var state = new ParseState(source);
        StaticHtmlFailure failure;
        try
        {
            failure = source.Length > StaticHtmlProfile.MaximumUtf8Bytes
                || Utf8.GetByteCount(source) > StaticHtmlProfile.MaximumUtf8Bytes
                ? StaticHtmlFailure.ByteLimit : Preflight(source, state, cancellationToken);
        }
        catch (EncoderFallbackException)
        {
            failure = StaticHtmlFailure.InvalidUnicode;
        }
        if (failure == StaticHtmlFailure.None)
        {
            var settings = new XmlReaderSettings
            {
                ConformanceLevel = ConformanceLevel.Fragment,
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = StaticHtmlProfile.MaximumUtf8Bytes,
                MaxCharactersFromEntities = StaticHtmlProfile.MaximumSemanticUtf8Bytes,
                IgnoreWhitespace = false,
                IgnoreComments = false,
            };
            using var input = new StringReader(source);
            using var reader = XmlReader.Create(input, settings);
            try
            {
                while (reader.Read())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    failure = state.Read(reader);
                    if (failure != StaticHtmlFailure.None) { break; }
                }
            }
            catch (XmlException)
            {
                // The exception can contain source text. Only the typed failure leaves this boundary.
                failure = StaticHtmlFailure.MalformedSource;
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        var result = state.Complete(failure);
        activity?.SetTag("kora.presentation.outcome", result.Failure.ToString());
        activity?.SetTag("kora.presentation.nodes", result.NodeCount);
        activity?.SetTag("kora.presentation.depth", result.Depth);
        activity?.SetTag("kora.presentation.entities", result.EntityReferenceCount);
        if (!result.IsFallback) { activity?.SetStatus(ActivityStatusCode.Ok); }
        return result;
    }

    private static StaticHtmlFailure Preflight(string source, ParseState state, CancellationToken token)
    {
        foreach (var character in source)
        {
            token.ThrowIfCancellationRequested();
            // References coalesce into a text node in XmlReader. Count their lexical
            // introducers independently before decoding, including malformed candidates.
            if (character == '&' && ++state.EntityReferences > StaticHtmlProfile.MaximumEntityReferences)
            {
                return StaticHtmlFailure.EntityLimit;
            }
        }
        return StaticHtmlFailure.None;
    }

    private sealed class ParseState(string source)
    {
        private readonly List<Frame> stack = [];
        private readonly List<StaticHtmlSection> sections = [];
        private readonly StringBuilder text = new();
        private int blocks;
        private int nodes;
        private int depth;
        private int semanticBytes;
        internal int EntityReferences { get; set; }

        internal StaticHtmlFailure Read(XmlReader reader)
        {
            if (reader.NodeType != XmlNodeType.EndElement && ++nodes > StaticHtmlProfile.MaximumNodes)
            { return StaticHtmlFailure.NodeLimit; }
            switch (reader.NodeType)
            {
                case XmlNodeType.Element:
                    return Open(reader);
                case XmlNodeType.EndElement:
                    if (IsBlock(reader.Name) && !Flush()) { return StaticHtmlFailure.SemanticLimit; }
                    stack.RemoveAt(stack.Count - 1);
                    break;
                case XmlNodeType.Text:
                case XmlNodeType.Whitespace:
                case XmlNodeType.SignificantWhitespace:
                    if (stack.Count > 0 && stack[^1].Name is "ul" or "ol")
                    {
                        if (!string.IsNullOrWhiteSpace(reader.Value)) { return StaticHtmlFailure.UnsupportedMarkup; }
                        break;
                    }
                    Append(reader.Value, stack.Any(frame => frame.Name is "pre"));
                    break;
                default:
                    return StaticHtmlFailure.UnsupportedMarkup;
            }
            return StaticHtmlFailure.None;
        }

        private StaticHtmlFailure Open(XmlReader reader)
        {
            var name = reader.Name;
            if (reader.AttributeCount > StaticHtmlProfile.MaximumAttributes || !IsAllowed(name)
                || !ValidParent(name) || (name is "br" or "hr") != reader.IsEmptyElement)
            {
                return StaticHtmlFailure.UnsupportedMarkup;
            }
            depth = Math.Max(depth, stack.Count + 1);
            if (depth > StaticHtmlProfile.MaximumDepth) { return StaticHtmlFailure.DepthLimit; }
            if (IsBlock(name))
            {
                if (++blocks > StaticHtmlProfile.MaximumBlocks) { return StaticHtmlFailure.BlockLimit; }
                if (!Flush()) { return StaticHtmlFailure.SemanticLimit; }
            }
            if (name is "br") { text.Append('\n'); }
            else if (name is "hr")
            {
                if (!AddSection("Horizontal rule.", StaticHtmlSectionKind.Divider))
                { return StaticHtmlFailure.SemanticLimit; }
            }
            else
            {
                var prefix = name is "li"
                    ? stack[^1].Name is "ol"
                        ? (++stack[^1].ItemCount).ToString(CultureInfo.InvariantCulture) + ". "
                        : "- "
                    : string.Empty;
                stack.Add(new(name, prefix));
            }
            return StaticHtmlFailure.None;
        }

        private bool ValidParent(string name)
        {
            var parent = stack.Count == 0 ? null : stack[^1].Name;
            if (name is "li") { return parent is "ul" or "ol"; }
            if (parent is "ul" or "ol") { return false; }
            return !IsBlock(name) || !stack.Any(frame => IsPhrasingContainer(frame.Name));
        }

        private static bool IsPhrasingContainer(string name) =>
            name is "p" or "h1" or "h2" or "h3" or "h4" or "h5" or "h6" or "pre"
                or "span" or "strong" or "em" or "b" or "i" or "code";

        private static bool IsAllowed(string name) => IsBlock(name)
            || name is "span" or "strong" or "em" or "b" or "i" or "code" or "br";

        private static bool IsBlock(string name) =>
            name is "div" or "p" or "h1" or "h2" or "h3" or "h4" or "h5" or "h6"
                or "ul" or "ol" or "li" or "blockquote" or "pre" or "hr";

        private void Append(string value, bool preserve)
        {
            foreach (var character in value)
            {
                if (preserve || character is not (' ' or '\t' or '\r' or '\n')) { text.Append(character); }
                else if (text.Length > 0 && text[^1] is not (' ' or '\n')) { text.Append(' '); }
            }
        }

        private bool Flush()
        {
            var kind = StaticHtmlSectionKind.Paragraph;
            for (var i = stack.Count - 1; i >= 0; i--)
            {
                kind = stack[i].Name switch
                {
                    "h1" => StaticHtmlSectionKind.Heading1,
                    "h2" => StaticHtmlSectionKind.Heading2,
                    "h3" => StaticHtmlSectionKind.Heading3,
                    "h4" => StaticHtmlSectionKind.Heading4,
                    "h5" => StaticHtmlSectionKind.Heading5,
                    "h6" => StaticHtmlSectionKind.Heading6,
                    "pre" => StaticHtmlSectionKind.Code,
                    "li" => StaticHtmlSectionKind.ListItem,
                    "blockquote" => StaticHtmlSectionKind.Quote,
                    _ => StaticHtmlSectionKind.Paragraph,
                };
                if (kind != StaticHtmlSectionKind.Paragraph) { break; }
            }
            var value = kind == StaticHtmlSectionKind.Code ? text.ToString() : text.ToString().Trim(' ', '\t', '\r', '\n');
            text.Clear();
            if (value.Length == 0) { return true; }
            var list = stack.LastOrDefault(frame => frame.Name is "li");
            if (list is not null && !list.PrefixUsed)
            {
                value = list.Prefix + value;
                list.PrefixUsed = true;
            }
            if (stack.Any(frame => frame.Name is "blockquote")) { value = "> " + value; }
            return AddSection(value, kind);
        }

        private bool AddSection(string value, StaticHtmlSectionKind kind)
        {
            semanticBytes += Utf8.GetByteCount(value) + (sections.Count == 0 ? 0 : 2);
            if (semanticBytes > StaticHtmlProfile.MaximumSemanticUtf8Bytes) { return false; }
            sections.Add(new(value, kind));
            return true;
        }

        internal StaticHtmlDocument Complete(StaticHtmlFailure failure)
        {
            if (failure == StaticHtmlFailure.None && !Flush()) { failure = StaticHtmlFailure.SemanticLimit; }
            if (failure == StaticHtmlFailure.None && (sections.Count == 0 || sections.All(section => string.IsNullOrWhiteSpace(section.Text))))
            { failure = StaticHtmlFailure.EmptyProjection; }
            return failure == StaticHtmlFailure.None
                ? new(source, string.Join("\n\n", sections.Select(section => section.Text)), [.. sections],
                    failure, blocks, nodes, depth, EntityReferences)
                : new(source, source, [], failure, blocks, nodes, depth, EntityReferences);
        }

        private sealed class Frame(string name, string prefix)
        {
            internal string Name { get; } = name;
            internal string Prefix { get; } = prefix;
            internal bool PrefixUsed { get; set; }
            internal int ItemCount { get; set; }
        }
    }
}
