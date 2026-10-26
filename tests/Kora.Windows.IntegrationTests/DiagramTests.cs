using System.Diagnostics.CodeAnalysis;
using System.Text;

using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;

using AwesomeAssertions;

using Kora.Application.Presentation;
using Kora.Controls;
using Kora.Core.Hosting;
using Kora.Core.Presentation;
using Kora.Diagrams;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed class DiagramTests
{
    [Theory]
    [InlineData("flowchart TD\nA[Start] --> B[Finish]\n", 2, 1)]
    [InlineData("graph LR; A -->|next| B; B -.-> C", 3, 2)]
    [InlineData("sequenceDiagram\nparticipant A as Alice\nparticipant B as Bob\nA->>B: Hello\nB-->>A: Reply", 2, 4)]
    [InlineData("classDiagram\nclass Animal {\n+name\n+Speak()\n}\nAnimal <|-- Dog", 2, 1)]
    public async Task RealNativeRendererProducesOnlyBoundedInertDrawingAndSanitizedSvg(string source, int boxes, int lines)
    {
        var result = await Qualified().RenderAsync(source, TestContext.Current.CancellationToken);
        result.Failure.Should().Be(MermaidFailure.None);
        result.Drawing!.Boxes.Should().HaveCount(boxes);
        result.Drawing.Lines.Should().HaveCount(lines);
        MermaidSvg.IsSafe(result.Svg!, () => { }).Should().BeTrue();
        result.Svg.Should().NotContain("script").And.NotContain("href").And.NotContain("foreignObject");
        Encoding.UTF8.GetByteCount(result.Svg!).Should().BeLessThanOrEqualTo(MermaidProfile.MaximumSvgBytes);
    }

    [Theory]
    [InlineData("%%{init: {'securityLevel': 'loose'}}%%\nflowchart TD\nA-->B")]
    [InlineData("flowchart TD\nclick A \"https://example.invalid\"")]
    [InlineData("flowchart TD\nA[<img src='https://example.invalid'>] --> B")]
    [InlineData("flowchart TD\nA[<script>alert()</script>] --> B")]
    [InlineData("flowchart TD\nA[&lt;script&gt;] --> B")]
    [InlineData("flowchart TD\nstyle A fill:url(https://example.invalid)")]
    [InlineData("flowchart TD\nA@{img: 'file:///private'}")]
    [InlineData("flowchart TD\nlinkStyle 0 stroke:red")]
    [InlineData("sequenceDiagram\nlinks A: {\"Website\":\"https://example.invalid\"}")]
    [InlineData("sequenceDiagram\nA->>B: <b>HTML</b>")]
    [InlineData("classDiagram\nclass A {\n<img>\n}")]
    [InlineData("pie\n\"secret\" : 1")]
    [InlineData("flowchart TD\nA --> B --> C")]
    [InlineData("flowchart TD\nsubgraph secret")]
    [InlineData("classDiagram\nclass A {\n+method()")]
    public async Task UnsupportedAndHostileSyntaxHasNoPartialOutputOrDocumentPolicy(string source)
    {
        var result = await Qualified().RenderAsync(source, TestContext.Current.CancellationToken);
        result.Failure.Should().Be(MermaidFailure.UnsupportedSyntax);
        result.Drawing.Should().BeNull();
        result.Svg.Should().BeNull();
    }

    [Theory]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("<foreignObject />")]
    [InlineData("<image href='https://example.invalid/x' />")]
    [InlineData("<text onclick='host()'>secret</text>")]
    [InlineData("<text style='font-family:url(file:///private)'>secret</text>")]
    [InlineData("<a href='javascript:host()'><text>secret</text></a>")]
    [InlineData("<line x1='NaN' />")]
    [InlineData("<line x1='Infinity' />")]
    [InlineData("<svg />")]
    [InlineData("<!--hidden-->")]
    [InlineData("<?host secret?>")]
    public void SvgSanitizerRejectsTheEntireUnsafeProjection(string fragment) =>
        MermaidSvg.IsSafe($"<svg xmlns='http://www.w3.org/2000/svg'>{fragment}</svg>", () => { }).Should().BeFalse();

    [Fact]
    public void SvgSanitizerRejectsDtdNamespacesMalformedAndOversizeOutput()
    {
        MermaidSvg.IsSafe("<!DOCTYPE svg [<!ENTITY x SYSTEM 'file:///private'>]><svg>&x;</svg>", () => { }).Should().BeFalse();
        MermaidSvg.IsSafe("<svg xmlns='http://evil.invalid' />", () => { }).Should().BeFalse();
        MermaidSvg.IsSafe("<svg xmlns='http://www.w3.org/2000/svg'><text>", () => { }).Should().BeFalse();
        MermaidSvg.IsSafe(new string('x', MermaidProfile.MaximumSvgBytes + 1), () => { }).Should().BeFalse();
    }

    [Fact]
    public async Task ExactDiagramUtf8BoundaryIsInclusiveAndNeverTruncated()
    {
        const string prefix = "flowchart TD\nA\n";
        var source = prefix + new string('\u00a0', (MermaidProfile.MaximumDiagramBytes - prefix.Length) / 2)
            + new string(' ', (MermaidProfile.MaximumDiagramBytes - prefix.Length) % 2);
        Encoding.UTF8.GetByteCount(source).Should().Be(MermaidProfile.MaximumDiagramBytes);
        (await Qualified().RenderAsync(source, TestContext.Current.CancellationToken)).Failure.Should().Be(MermaidFailure.None);
        var exceeded = await Qualified().RenderAsync(source + " ", TestContext.Current.CancellationToken);
        exceeded.Failure.Should().Be(MermaidFailure.ByteLimit);
        exceeded.Drawing.Should().BeNull();
        exceeded.Svg.Should().BeNull();
    }

    [Fact]
    public async Task ExactNodeAndEdgeBoundsAreInclusive()
    {
        var nodes = "flowchart TD\n" + string.Join('\n', Enumerable.Range(0, 500).Select(index => $"N{index}"));
        var exactNodes = await Qualified().RenderAsync(nodes, TestContext.Current.CancellationToken);
        exactNodes.Failure.Should().Be(MermaidFailure.None);
        exactNodes.Drawing!.Boxes.Should().HaveCount(500);
        (await Qualified().RenderAsync(nodes + "\nN500", TestContext.Current.CancellationToken)).Failure.Should().Be(MermaidFailure.NodeLimit);
        var edges = "flowchart TD\n" + string.Join('\n', Enumerable.Repeat("A-->B", 1000));
        var exactEdges = await Qualified().RenderAsync(edges, TestContext.Current.CancellationToken);
        exactEdges.Failure.Should().Be(MermaidFailure.None);
        exactEdges.Drawing!.Lines.Should().HaveCount(1000);
        (await Qualified().RenderAsync(edges + "\nA-->B", TestContext.Current.CancellationToken)).Failure.Should().Be(MermaidFailure.EdgeLimit);
    }

    [Theory]
    [InlineData("TD", false)]
    [InlineData("LR", true)]
    public async Task FlowDirectionAndFiveHundredNodeBoundaryRemainTruthful(string direction, bool horizontal)
    {
        var source = $"flowchart {direction}\n" + string.Join('\n', Enumerable.Range(0, 500).Select(index => $"N{index}"));
        var result = await Qualified().RenderAsync(source, TestContext.Current.CancellationToken);
        result.Failure.Should().Be(MermaidFailure.None);
        var boxes = result.Drawing!.Boxes;
        boxes.Should().HaveCount(500);
        if (horizontal)
        {
            boxes[1].Y.Should().Be(boxes[0].Y);
            boxes[1].X.Should().BeGreaterThan(boxes[0].X);
        }
        else
        {
            boxes[1].X.Should().Be(boxes[0].X);
            boxes[1].Y.Should().BeGreaterThan(boxes[0].Y);
        }
    }

    [Theory]
    [InlineData("flowchart TD\nA-->A", 3)]
    [InlineData("sequenceDiagram\nA->>A: Think", 4)]
    public async Task SelfMessagesAreRealLoopsRatherThanInvisibleZeroLengthLines(string source, int lines)
    {
        var result = await Qualified().RenderAsync(source, TestContext.Current.CancellationToken);
        result.Failure.Should().Be(MermaidFailure.None);
        result.Drawing!.Lines.Should().HaveCount(lines);
        result.Drawing.Lines.Should().OnlyContain(line => line.X1 != line.X2 || line.Y1 != line.Y2);
        result.Drawing.Lines[^1].Arrow.Should().BeTrue();
    }

    [Fact]
    public async Task TwoSecondDeadlineAndCancellationCannotPublishSuccess()
    {
        var clock = new SteppingClock(TimeSpan.FromSeconds(2));
        var result = await new MermaidRenderer(true, clock).RenderAsync("flowchart TD\nA-->B", TestContext.Current.CancellationToken);
        result.Failure.Should().Be(MermaidFailure.TimeLimit);
        result.Drawing.Should().BeNull();
        var cancelled = () => Qualified().RenderAsync("flowchart TD\nA-->B", new CancellationToken(true));
        await cancelled.Should().ThrowAsync<OperationCanceledException>();
        var unavailable = await new MermaidRenderer().RenderAsync("flowchart TD\nA-->B", TestContext.Current.CancellationToken);
        unavailable.Failure.Should().Be(MermaidFailure.Unavailable);
        unavailable.Drawing.Should().BeNull();
    }

    [Fact]
    public async Task RealWorkerObservesCancellationInsideParsingWithoutAPartialDrawing()
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var renderer = new MermaidRenderer(true, new CancellingClock(cancellation));
        var render = () => renderer.RenderAsync("flowchart TD\nA-->B\nB-->C", cancellation.Token);
        await render.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public Task DefaultDetailsActionTruthfullyReportsUnavailableAndLeavesDocumentUntouched() => HeadlessSession.RunAsync(async () =>
    {
        var content = Content("```mermaid\nflowchart TD\nA-->B\n```");
        var state = new DetailViewerState(content);
        var fallback = new NativeDetailRenderer(NullLogger<NativeDetailRenderer>.Instance).Render(content.Source);
        state.CompleteRender(state.Generation, fallback.SemanticText, fallback.Status, true);
        var view = new DetailContentView(state, fallback, (_, _, _) => Task.CompletedTask);
        var diagram = (MermaidDetailView)view.FindControl<ContentControl>("MermaidContent")!.Content!;
        await diagram.RenderSelectedAsync();
        diagram.Children.OfType<NamedTextBlock>().Single().Text.Should().Contain("Unavailable").And.Contain("fallback");
        state.ActiveText.Should().Be(content.Source);
        view.ClearPresentation();
    });

    [Fact]
    public Task NativeDiagramUsesSharedDynamicThemeBrushesWithoutChangingItsSource() => HeadlessSession.RunAsync(() =>
    {
        var drawing = new MermaidDrawing(300, 200, [new(20, 20, 180, 80, "Exact label")],
            [new(20, 100, 200, 160, "edge", false, true)]);
        var canvas = (Canvas)MermaidNativeDrawing.Create(drawing);
        canvas.Resources["KoraForegroundBrush"] = Brushes.Blue;
        canvas.Resources["KoraWindowBackgroundBrush"] = Brushes.White;
        var box = canvas.Children.OfType<Border>().Single();
        box.BorderBrush.Should().BeSameAs(Brushes.Blue);
        box.Background.Should().BeSameAs(Brushes.White);
        canvas.Children.OfType<TextBlock>().Should().OnlyContain(text => ReferenceEquals(text.Foreground, Brushes.Blue));
        canvas.Resources["KoraForegroundBrush"] = Brushes.White;
        canvas.Resources["KoraWindowBackgroundBrush"] = Brushes.Black;
        box.BorderBrush.Should().BeSameAs(Brushes.White);
        box.Background.Should().BeSameAs(Brushes.Black);
        canvas.Children.OfType<TextBlock>().Should().OnlyContain(text => ReferenceEquals(text.Foreground, Brushes.White));
        drawing.Boxes[0].Text.Should().Be("Exact label");
    });

    [Fact]
    public async Task UriLikeLabelTextCannotBecomeAnExternalResourceOrLink()
    {
        const string text = "https://example.invalid/file.svg";
        var result = await Qualified().RenderAsync($"flowchart TD\nA[{text}]", TestContext.Current.CancellationToken);
        result.Failure.Should().Be(MermaidFailure.None);
        result.Drawing!.Boxes[0].Text.Should().Be(text);
        result.Svg.Should().Contain(text).And.NotContain("href").And.NotContain("image");
    }

    [Fact]
    public void OnlyCompleteTopLevelMermaidFencesAreOfferedAndSourceOffsetsStayExact()
    {
        const string source = "Before\r\n\r\n```mermaid\r\nflowchart TD\r\nA-->B\r\n```\r\n\r\nAfter";
        var content = Content(source);
        var block = MermaidFences.Read(content).Should().ContainSingle().Subject;
        block.Text.Should().Be("flowchart TD\r\nA-->B\r\n");
        content.Source.Substring(block.Start, block.Length).Should().Be(block.Text);
        MermaidFences.Read(Content("```mermaid\nflowchart TD\nA-->B")).Should().BeEmpty();
        MermaidFences.Read(Content("```mermaid title\nflowchart TD\nA-->B\n```")).Should().BeEmpty();
        MermaidFences.Read(Content("```text\nflowchart TD\nA-->B\n```")).Should().BeEmpty();
        MermaidFences.Read(Content("> ```mermaid\n> flowchart TD\n> A-->B\n> ```")).Should().BeEmpty();
        MermaidFences.Read(Content(string.Join("\n\n", Enumerable.Repeat("```mermaid\nflowchart TD\nA-->B\n```", 9)))).Should().BeEmpty();
    }

    [Fact]
    public void CompleteDocumentAdmissionRetainsItsExact256KiBUtf8Limit()
    {
        var source = new string('x', NativeDetailProfile.MaximumUtf8Bytes);
        Content(source).Utf8Bytes.Should().Be(256 * 1024);
        var exceeded = () => Content(source + "x");
        exceeded.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public async Task CancelAndRetireRetainActualWorkerOwnershipUntilJoinedAndFenceLateSuccess()
    {
        var renderer = new HeldRenderer();
        await using var session = new MermaidRenderSession(renderer);
        var publishChecks = 0;
        var task = session.StartAsync("flowchart TD\nA-->B", () => { publishChecks++; return true; });
        session.IsRunning.Should().BeTrue();
        session.Cancel();
        renderer.Token.IsCancellationRequested.Should().BeTrue();
        session.IsRunning.Should().BeTrue("a cancellation request does not prove worker termination");
        Action repeat = () => _ = session.StartAsync("flowchart TD\nA", () => true);
        repeat.Should().Throw<InvalidOperationException>();
        renderer.Release();
        (await task).Should().BeNull();
        session.IsRunning.Should().BeFalse();
        publishChecks.Should().Be(0);
        var restarted = session.StartAsync("flowchart TD\nA", () => false);
        (await restarted).Should().BeNull("live admission is revalidated at publication");
        session.Retire();
        repeat.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public Task ActualDetailsShellRendersOnlyOnExplicitActionAndRetainsSourceCopyAndFindPolicy() => HeadlessSession.RunAsync(async () =>
    {
        var content = Content("Before\n\n```mermaid\nflowchart TD\nA[Start] --> B[Finish]\n```\n\nAfter");
        var state = new DetailViewerState(content);
        var fallback = new NativeDetailRenderer(NullLogger<NativeDetailRenderer>.Instance).Render(content.Source);
        fallback.IsFallback.Should().BeTrue();
        state.CompleteRender(state.Generation, fallback.SemanticText, fallback.Status, fallback.IsFallback).Should().BeTrue();
        var renderer = new CountingRenderer();
        var view = new DetailContentView(state, fallback, (_, _, _) => Task.CompletedTask, mermaidRenderer: renderer);
        var diagrams = (MermaidDetailView)view.FindControl<ContentControl>("MermaidContent")!.Content!;
        renderer.Calls.Should().Be(0);
        await diagrams.RenderSelectedAsync();
        renderer.Calls.Should().Be(1);
        diagrams.GetVisualDescendants().OfType<SelectableTextBlock>().Should().BeEmpty("no clipboard policy bypass");
        diagrams.Children.OfType<NamedTextBlock>().Single().Text.Should().Contain("Rendered");
        state.ActiveText.Should().Be(content.Source);
        state.Find("Start", Kora.Core.Context.SnapshotFindDirection.First).Should().BeTrue();
        state.TryGetCopySource(true, false, out var exact).Should().BeTrue();
        exact.Should().Be(content.Source);
        view.ClearPresentation();
        await view.MermaidQuiescence;
        state.ActiveText.Should().BeEmpty();
        diagrams.Children.Should().BeEmpty();
    });

    [Fact]
    [SuppressMessage("Usage", "VSTHRD003", Justification = "Awaits only the owned finite worker retirement evidence.")]
    public Task PrivacyClosureClearsNativeControlsBeforeAnUncooperativeWorkerCanFinish() => HeadlessSession.RunAsync(async () =>
    {
        var content = Content("```mermaid\nflowchart TD\nA-->B\n```");
        var state = new DetailViewerState(content);
        var fallback = new NativeDetailRenderer(NullLogger<NativeDetailRenderer>.Instance).Render(content.Source);
        var renderer = new HeldRenderer();
        var view = new DetailContentView(state, fallback, (_, _, _) => Task.CompletedTask, mermaidRenderer: renderer);
        var diagrams = (MermaidDetailView)view.FindControl<ContentControl>("MermaidContent")!.Content!;
        var rendering = diagrams.RenderSelectedAsync();
        view.ClearPresentation();
        renderer.Token.IsCancellationRequested.Should().BeTrue();
        view.MermaidQuiescence.IsCompleted.Should().BeFalse();
        view.FindControl<ContentControl>("MermaidContent")!.Content.Should().BeNull();
        diagrams.Children.Should().BeEmpty();
        renderer.Release();
        await rendering;
        await view.MermaidQuiescence;
        diagrams.Children.Should().BeEmpty();
        state.ActiveText.Should().BeEmpty();
    });

    private static MermaidRenderer Qualified() => new(true, new SteppingClock(TimeSpan.Zero));
    private static AdmittedDetailContent Content(string source) => new(
        new(new HostId<EvidenceIdentity>(Guid.NewGuid()), 1), DetailContentKind.Markdown, DetailContentOrigin.EmbeddedDocument,
        DetailSensitivity.Public, "Diagram test", "Build-pinned test source", source);

    private sealed class SteppingClock(TimeSpan step) : TimeProvider
    {
        private long timestamp;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => Interlocked.Add(ref timestamp, step.Ticks);
    }

    private sealed class CancellingClock(CancellationTokenSource cancellation) : TimeProvider
    {
        private int calls;
        public override long GetTimestamp()
        {
            if (Interlocked.Increment(ref calls) == 4) { cancellation.Cancel(); }
            return 0;
        }
    }

    private sealed class HeldRenderer : IMermaidRenderer
    {
        private readonly TaskCompletionSource<MermaidResult> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal CancellationToken Token { get; private set; }
        public Task<MermaidResult> RenderAsync(string source, CancellationToken token)
        {
            Token = token;
            return completion.Task;
        }
        internal void Release() => completion.SetResult(new(MermaidFailure.None, new(100, 100, [], []), "<unused />"));
    }

    private sealed class CountingRenderer : IMermaidRenderer
    {
        internal int Calls { get; private set; }
        public Task<MermaidResult> RenderAsync(string source, CancellationToken token)
        {
            Calls++;
            return Qualified().RenderAsync(source, token);
        }
    }
}
