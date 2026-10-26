using System.Text.RegularExpressions;

namespace Kora.Diagrams;

// This is a deliberately finite native grammar, not a JavaScript Mermaid engine.
internal sealed partial class MermaidParser(TimeProvider clock, long started, CancellationToken token)
{
    private static readonly string[] Arrows = ["-->", "---", "-.->", "<|--", "--|>", "--"];
    private readonly List<MermaidNode> nodes = [];
    private readonly List<MermaidEdge> edges = [];
    private readonly Dictionary<string, int> identities = new(StringComparer.Ordinal);
    private MermaidKind kind;
    private bool horizontal;

    internal void Check()
    {
        token.ThrowIfCancellationRequested();
        if (clock.GetElapsedTime(started) >= MermaidProfile.Budget)
        { throw new MermaidRejectedException(MermaidFailure.TimeLimit); }
    }

    internal MermaidGraph Parse(string source)
    {
        Check();
        if (MermaidProfile.Utf8.GetByteCount(source) > MermaidProfile.MaximumDiagramBytes)
        { throw new MermaidRejectedException(MermaidFailure.ByteLimit); }
        var statements = source.Replace("\r\n", "\n", StringComparison.Ordinal).Split(['\n', ';']);
        var header = Array.FindIndex(statements, line => !string.IsNullOrWhiteSpace(line));
        if (header < 0) { Reject(); }
        switch (statements[header].Trim())
        {
            case "flowchart TD": case "flowchart TB": case "graph TD": case "graph TB":
                kind = MermaidKind.Flowchart; break;
            case "flowchart LR": case "graph LR":
                kind = MermaidKind.Flowchart; horizontal = true; break;
            case "sequenceDiagram": kind = MermaidKind.Sequence; break;
            case "classDiagram": kind = MermaidKind.Class; break;
            default: Reject(); break;
        }
        string? classId = null;
        for (var i = header + 1; i < statements.Length; i++)
        {
            Check();
            var line = statements[i].Trim();
            if (line.Length == 0) { continue; }
            if (classId is not null)
            {
                if (line is "}") { classId = null; continue; }
                if (line.Length > 256 || !SafeText(line)) { Reject(); }
                var index = identities[classId];
                nodes[index] = nodes[index] with { Label = nodes[index].Label + "\n" + line };
                continue;
            }
            if (kind == MermaidKind.Sequence) { Sequence(line); }
            else if (kind == MermaidKind.Class && line.StartsWith("class ", StringComparison.Ordinal))
            {
                var declaration = line[6..].Trim();
                var body = declaration.EndsWith('{');
                var id = body ? declaration[..^1].TrimEnd() : declaration;
                Node(id);
                if (body) { classId = id; }
            }
            else { Connection(line); }
        }
        if (classId is not null || nodes.Count == 0) { Reject(); }
        Check();
        return new(kind, horizontal, nodes.AsReadOnly(), edges.AsReadOnly());
    }

    private void Sequence(string line)
    {
        if (line.StartsWith("participant ", StringComparison.Ordinal)
            || line.StartsWith("actor ", StringComparison.Ordinal))
        {
            var value = line[(line.IndexOf(' ', StringComparison.Ordinal) + 1)..];
            var alias = value.IndexOf(" as ", StringComparison.Ordinal);
            Node(alias < 0 ? value : value[..alias], alias < 0 ? null : value[(alias + 4)..]);
            return;
        }
        var match = MessagePattern().Match(line);
        if (!match.Success) { Reject(); }
        Edge(Node(match.Groups["from"].Value), Node(match.Groups["to"].Value),
            match.Groups["label"].Value, match.Groups["arrow"].Value.StartsWith("--", StringComparison.Ordinal), true);
    }

    private void Connection(string line)
    {
        var arrow = Arrows.FirstOrDefault(value => line.Contains(value, StringComparison.Ordinal));
        if (arrow is null) { NodeExpression(line); return; }
        if (kind == MermaidKind.Flowchart && arrow is "<|--" or "--|>" or "--") { Reject(); }
        var position = line.IndexOf(arrow, StringComparison.Ordinal);
        var from = NodeExpression(line[..position].Trim());
        var target = line[(position + arrow.Length)..].Trim();
        var label = string.Empty;
        if (target.StartsWith('|'))
        {
            var end = target.IndexOf('|', 1);
            if (end < 0) { Reject(); }
            label = target[1..end];
            target = target[(end + 1)..].Trim();
        }
        var to = NodeExpression(target);
        if (arrow is "<|--") { (from, to) = (to, from); }
        if (arrow is "<|--" or "--|>") { label = "inherits" + (label.Length == 0 ? string.Empty : ": " + label); }
        Edge(from, to, label, arrow is "-.->", arrow is not ("---" or "--"));
    }

    private int NodeExpression(string value)
    {
        var match = NodePattern().Match(value);
        if (!match.Success) { Reject(); }
        return Node(match.Groups["id"].Value, match.Groups["label"].Success ? match.Groups["label"].Value : null);
    }

    private int Node(string id, string? label = null)
    {
        Check();
        if (!IdPattern().IsMatch(id) || label is not null && (label.Length == 0 || !SafeText(label))) { Reject(); }
        if (identities.TryGetValue(id, out var existing))
        {
            if (label is not null && !string.Equals(nodes[existing].Label, id, StringComparison.Ordinal)
                && !string.Equals(nodes[existing].Label, label, StringComparison.Ordinal)) { Reject(); }
            if (label is not null) { nodes[existing] = new(id, label); }
            return existing;
        }
        if (nodes.Count == MermaidProfile.MaximumNodes)
        { throw new MermaidRejectedException(MermaidFailure.NodeLimit); }
        identities.Add(id, nodes.Count);
        nodes.Add(new(id, label ?? id));
        return nodes.Count - 1;
    }

    private void Edge(int from, int to, string label, bool dashed, bool arrow)
    {
        Check();
        if (!SafeText(label)) { Reject(); }
        if (edges.Count == MermaidProfile.MaximumEdges)
        { throw new MermaidRejectedException(MermaidFailure.EdgeLimit); }
        edges.Add(new(from, to, label, dashed, arrow));
    }

    private static bool SafeText(string text) => !text.Any(character =>
        char.IsControl(character) || character is '<' or '>' or '&' or '{' or '}' or '%' or '`' or '\\');

    private static void Reject() => throw new MermaidRejectedException(MermaidFailure.UnsupportedSyntax);

    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_]{0,63}$", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex IdPattern();
    [GeneratedRegex(@"^(?<id>[A-Za-z_][A-Za-z0-9_]{0,63})(?:\[(?<label>[^\[\]]{1,256})\])?$",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex NodePattern();
    [GeneratedRegex(@"^(?<from>[A-Za-z_][A-Za-z0-9_]{0,63})(?<arrow>--?>>?)(?<to>[A-Za-z_][A-Za-z0-9_]{0,63})\s*:\s*(?<label>.{0,256})$",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex MessagePattern();
}
