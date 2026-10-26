using System.Diagnostics;

namespace Kora.Diagrams;

internal sealed class MermaidRenderer(bool qualified = false, TimeProvider? timeProvider = null) : IMermaidRenderer
{
    private static readonly ActivitySource Activities =
        new("Kora.Desktop", typeof(MermaidRenderer).Assembly.GetName().Version!.ToString());
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    public Task<MermaidResult> RenderAsync(string source, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(source);
        token.ThrowIfCancellationRequested();
        // No document, fence, user preference or model field can enable the native profile.
        if (!qualified) { return Task.FromResult(new MermaidResult(MermaidFailure.Unavailable)); }
        var started = clock.GetTimestamp();
        return Task.Run(() => Render(source, started, token), token);
    }

    private MermaidResult Render(string source, long started, CancellationToken token)
    {
        using var activity = Activities.StartActivity("presentation.mermaid.render");
        activity?.SetTag("kora.presentation.profile", MermaidProfile.Name);
        activity?.SetStatus(ActivityStatusCode.Error);
        var parser = new MermaidParser(clock, started, token);
        try
        {
            var graph = parser.Parse(source);
            var drawing = Layout(graph, parser);
            var svg = MermaidSvg.Write(drawing, parser.Check);
            if (!MermaidSvg.IsSafe(svg, parser.Check)) { return new(MermaidFailure.UnsafeOutput); }
            parser.Check();
            activity?.SetStatus(ActivityStatusCode.Ok);
            return new(MermaidFailure.None, drawing, svg);
        }
        catch (MermaidRejectedException exception)
        {
            activity?.SetTag("kora.presentation.fallback", exception.Failure.ToString());
            return new(exception.Failure);
        }
    }

    private static MermaidDrawing Layout(MermaidGraph graph, MermaidParser parser)
    {
        List<MermaidBox> boxes = [];
        List<MermaidLine> lines = [];
        var columns = graph.Kind switch
        {
            MermaidKind.Sequence => graph.Nodes.Count,
            MermaidKind.Flowchart => graph.Horizontal ? graph.Nodes.Count : 1,
            _ => Math.Min(5, graph.Nodes.Count),
        };
        var rowHeight = graph.Kind == MermaidKind.Class
            ? Math.Max(100, graph.Nodes.Max(node => node.Label.Count(character => character == '\n') + 1) * 24 + 32)
            : 120;
        for (var i = 0; i < graph.Nodes.Count; i++)
        {
            parser.Check();
            boxes.Add(new(30 + i % columns * 240, 30 + i / columns * rowHeight, 180, rowHeight - 40, graph.Nodes[i].Label));
        }
        var height = 60 + (graph.Nodes.Count + columns - 1) / columns * rowHeight;
        if (graph.Kind == MermaidKind.Sequence)
        {
            height = 160 + graph.Edges.Count * 60;
            foreach (var box in boxes)
            {
                parser.Check();
                lines.Add(new(box.X + 90, 110, box.X + 90, height - 20, string.Empty, true, false));
            }
        }
        for (var i = 0; i < graph.Edges.Count; i++)
        {
            parser.Check();
            var edge = graph.Edges[i];
            var from = boxes[edge.From];
            var to = boxes[edge.To];
            if (edge.From == edge.To)
            {
                var x = graph.Kind == MermaidKind.Sequence ? from.X + 90 : from.X + from.Width;
                var y = graph.Kind == MermaidKind.Sequence ? 140 + i * 60 : from.Y + from.Height / 2;
                lines.Add(new(x, y, x + 30, y, edge.Label, edge.Dashed, false));
                lines.Add(new(x + 30, y, x + 30, y + 24, string.Empty, edge.Dashed, false));
                lines.Add(new(x + 30, y + 24, x, y + 24, string.Empty, edge.Dashed, edge.Arrow));
                continue;
            }
            if (graph.Kind == MermaidKind.Sequence)
            {
                var y = 140 + i * 60;
                lines.Add(new(from.X + 90, y, to.X + 90, y, edge.Label, edge.Dashed, edge.Arrow));
            }
            else
            {
                lines.Add(graph.Horizontal
                    ? new(from.X + from.Width, from.Y + from.Height / 2,
                        to.X, to.Y + to.Height / 2, edge.Label, edge.Dashed, edge.Arrow)
                    : new(from.X + from.Width / 2, from.Y + from.Height,
                        to.X + to.Width / 2, to.Y, edge.Label, edge.Dashed, edge.Arrow));
            }
        }
        parser.Check();
        return new(60 + columns * 240, height, boxes.AsReadOnly(), lines.AsReadOnly());
    }
}
