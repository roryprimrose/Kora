using System.Diagnostics;

using Avalonia.Controls;

using AwesomeAssertions;

using Kora.Application.Documentation;
using Kora.Application.Presentation;
using Kora.Core.Hosting;
using Kora.Core.Presentation;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

[Collection("Host tracing")]
public sealed class StaticHtmlDetailRendererTests
{
    [Fact]
    public void NativeRenderAndParserShareParentageHostIdsAndTruthfulTerminalStatus()
    {
        var stopped = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name is "Kora.Core" or "Kora.Desktop",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = stopped.Add,
        };
        ActivitySource.AddActivityListener(listener);
        using var parent = new Activity("test-request").Start();
        var content = Content("<p>private-source-marker</p>");
        Qualified().Render(content, TestContext.Current.CancellationToken);
        var render = stopped.Single(activity => activity.OperationName is "presentation.static-html.render");
        var parse = stopped.Single(activity => activity.OperationName is "presentation.static-html.parse");
        render.ParentId.Should().Be(parent.Id);
        parse.ParentId.Should().Be(render.Id);
        render.Status.Should().Be(ActivityStatusCode.Ok);
        parse.Status.Should().Be(ActivityStatusCode.Ok);
        render.GetTagItem("kora.session.id").Should().Be(content.SessionSource!.SessionId.Value);
        render.GetTagItem("kora.request.id").Should().Be(content.SessionSource.RequestId.Value);
        render.GetTagItem("kora.task.id").Should().Be(content.SessionSource.TaskId.Value);
        stopped.SelectMany(activity => activity.TagObjects).Should().NotContain(pair =>
            pair.Value != null && pair.Value.ToString()!.Contains("private-source-marker", StringComparison.Ordinal));
        stopped.Should().OnlyContain(activity => !activity.Baggage.Any());
        stopped.Clear();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        listener.ActivityStarted = activity =>
        {
            if (activity.OperationName is "presentation.static-html.parse") { cancellation.Cancel(); }
        };
        var cancelled = () => Qualified().Render(content, cancellation.Token);
        cancelled.Should().Throw<OperationCanceledException>();
        stopped.Where(activity => activity.OperationName is "presentation.static-html.render" or "presentation.static-html.parse")
            .Should().OnlyContain(activity => activity.Status == ActivityStatusCode.Error);
    }
    [Fact]
    public void DefaultUnqualifiedAndUnknownProfilesReturnTheEntireAdmittedSource()
    {
        var content = Content("<h1>Exact &amp; source</h1>\r\n");
        var unavailable = new StaticHtmlDetailRenderer(NullLogger<StaticHtmlDetailRenderer>.Instance);
        var result = unavailable.Render(content, TestContext.Current.CancellationToken);
        result.IsFallback.Should().BeTrue();
        result.Source.Should().Be(content.Source);
        result.SemanticText.Should().Be(content.Source);
        result.Status.Should().Contain("qualification unavailable");
        result.StaticHtml.Should().BeNull();
        var unknown = new StaticHtmlDetailRenderer(NullLogger<StaticHtmlDetailRenderer>.Instance, true, "unknown");
        unknown.Render(content, TestContext.Current.CancellationToken).Status.Should().Contain("profile unknown");
    }

    [Fact]
    public void QualifiedSeamUsesRealParserAndDoesNotAdmitOtherKindsOrIgnoreCancellation()
    {
        var renderer = Qualified();
        var content = Content("<h3>Heading</h3><p>Text &amp; source</p>");
        var result = renderer.Render(content, TestContext.Current.CancellationToken);
        result.IsFallback.Should().BeFalse();
        result.Source.Should().Be(content.Source);
        result.SemanticText.Should().Be("Heading\n\nText & source");
        result.StaticHtml.Should().NotBeNull();
        result.StaticHtml!.Sections[0].Kind.Should().Be(StaticHtmlSectionKind.Heading3);
        result.Sections.Should().BeEmpty("static HTML does not masquerade as the existing native-text-v1 result");
        var wrong = new AdmittedDetailContent(content.Reference, DetailContentKind.Markdown, content.Origin,
            content.Sensitivity, content.Title, content.Provenance, content.Source, sessionSource: content.SessionSource);
        var reclassify = () => renderer.Render(wrong, TestContext.Current.CancellationToken);
        reclassify.Should().Throw<InvalidDataException>();
        var cancelled = () => renderer.Render(content, new CancellationToken(true));
        cancelled.Should().Throw<OperationCanceledException>();
    }

    [Theory]
    [InlineData("<p>Before</p><script>hostile()</script><p>After</p>", "UnsupportedMarkup")]
    [InlineData("<p>unclosed", "MalformedSource")]
    public void InvalidContentNeverPublishesAPartialProjection(string source, string failure)
    {
        var result = Qualified().Render(Content(source), TestContext.Current.CancellationToken);
        result.IsFallback.Should().BeTrue();
        result.Source.Should().Be(source);
        result.SemanticText.Should().Be(source);
        result.Status.Should().Contain(failure).And.Contain("Nothing was stripped or truncated");
        result.StaticHtml!.Sections.Should().BeEmpty();
    }

    [Theory]
    [InlineData(StaticHtmlProfile.MaximumUtf8Bytes + 1)]
    [InlineData(NativeDetailProfile.MaximumUtf8Bytes)]
    public void OversizeProfileFallsBackToWholeSourceWithinExistingAdmissionEnvelope(int bytes)
    {
        var content = Content(new string('x', bytes));
        var result = Qualified().Render(content, TestContext.Current.CancellationToken);
        result.IsFallback.Should().BeTrue();
        result.Status.Should().Contain("ByteLimit");
        result.Source.Length.Should().Be(bytes);
        result.SemanticText.Should().Be(content.Source);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ActualControllerRoutesAdmittedHtmlSeparatelyAndRetiresItsExactPrivateGeneration(bool qualified)
    {
        var views = new List<FakeView>();
        var results = new List<NativeDocumentResult>();
        var clipboard = new NoClipboard();
        var sourceLive = true;
        using var controller = new DetailWindowController(new NoDocuments(), () => true,
            new(NullLogger<NativeDetailRenderer>.Instance, available: false), clipboard,
            (state, result, _) =>
            {
                results.Add(result);
                var view = new FakeView(state);
                views.Add(view);
                return view;
            }, NullLogger<DetailWindowController>.Instance,
            new StaticHtmlDetailRenderer(NullLogger<StaticHtmlDetailRenderer>.Instance, qualified));
        var content = Content("<h2>Exact result</h2><p>Private source</p>");
        controller.OpenCurrentResponse(content, null, () => sourceLive).Should().BeTrue();
        var view = views.Single();
        results.Single().IsFallback.Should().Be(!qualified);
        view.State.ActiveText.Should().Be(qualified ? "Exact result\n\nPrivate source" : content.Source);
        controller.OpenCurrentResponse(content, null, () => sourceLive).Should().BeTrue();
        views.Should().ContainSingle();
        var foreign = new AdmittedDetailContent(content.Reference, content.Kind, content.Origin, content.Sensitivity,
            content.Title, content.Provenance, "<p>foreign</p>", sessionSource: content.SessionSource);
        controller.OpenCurrentResponse(foreign, null, () => true).Should().BeFalse();
        var generation = view.State.Generation;
        view.State.SetSource(true);
        view.State.ActiveText.Should().Be(content.Source);
        await controller.CopyAsync(content.Reference, generation, true);
        clipboard.Writes.Should().Be(0, "current-response details grant no new clipboard authority");
        sourceLive = false;
        controller.RetireUnavailableCurrentResponses();
        view.Cleared.Should().BeTrue();
        view.State.ActiveText.Should().BeEmpty();
        view.State.CompleteRender(generation, "late private text", "Late", false).Should().BeFalse();
        controller.OpenCurrentResponse(content, null, () => sourceLive).Should().BeFalse();
        sourceLive = true;
        controller.OpenCurrentResponse(Content("<p>New exact source</p>"), null, () => sourceLive).Should().BeTrue();
        controller.ClearForPrivacy();
        views.Should().OnlyContain(item => item.Cleared);
        views.Should().OnlyContain(item => item.State.Content == null);
    }

    private static StaticHtmlDetailRenderer Qualified() => new(NullLogger<StaticHtmlDetailRenderer>.Instance, true);

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task HtmlAndChecklistRoutesKeepIndependentQualificationAndExactPrivateLifetimes(
        bool htmlQualified, bool checklistQualified)
    {
        var views = new List<FakeView>();
        var htmlLive = true;
        var clipboard = new NoClipboard();
        using var controller = new DetailWindowController(new NoDocuments(), () => true,
            new(NullLogger<NativeDetailRenderer>.Instance, pipeTablesAvailable: true,
                checklistsAvailable: checklistQualified), clipboard,
            (state, _, _) =>
            {
                var view = new FakeView(state);
                views.Add(view);
                return view;
            }, NullLogger<DetailWindowController>.Instance,
            new(NullLogger<StaticHtmlDetailRenderer>.Instance, htmlQualified));
        var html = Content("<h2>Private HTML</h2>");
        var request = HostRequest.Create(RequestOrigin.LocalUi);
        const string checklistSource = "- [ ] Private checklist\r\n";
        var checklist = new AdmittedDetailContent(new(new(Guid.NewGuid()), 1), DetailContentKind.Markdown,
            DetailContentOrigin.FinalizedResponse, DetailSensitivity.DisclosureConfirmationRequired,
            "Exact assistant answer", "Host-issued immutable test answer", checklistSource,
            new(request.SessionId, request.RequestId, request.TaskId));
        controller.OpenCurrentResponse(html, null, () => htmlLive).Should().BeTrue();
        controller.OpenCurrentResponse(checklist, null, () => true).Should().BeTrue();
        views[0].State.ActiveText.Should().Be(htmlQualified ? "Private HTML" : html.Source);
        views[0].State.Status.Should().Contain(StaticHtmlProfile.Name);
        views[1].State.RenderState.Should().Be(checklistQualified ? DetailRenderState.Rendered : DetailRenderState.SourceFallback);
        views[1].State.Status.Should().Contain(NativeMarkdownChecklistProfile.Name);
        views[1].State.ActiveText.Should().Contain(checklistQualified ? "Unchecked: Private checklist" : checklistSource);
        views[1].State.SetSource(true);
        views[1].State.ActiveText.Should().Be(checklistSource);
        var htmlGeneration = views[0].State.Generation;
        htmlLive = false;
        controller.RetireUnavailableCurrentResponses();
        views[0].Cleared.Should().BeTrue();
        views[0].State.ActiveText.Should().BeEmpty();
        views[0].State.CompleteRender(htmlGeneration, "late HTML", "Late", false).Should().BeFalse();
        views[1].Cleared.Should().BeFalse();
        views[1].State.ActiveText.Should().Be(checklistSource);
        await controller.CopyAsync(checklist.Reference, views[1].State.Generation, true);
        clipboard.Writes.Should().Be(0);
        controller.ClearForPrivacy();
        views.Should().OnlyContain(view => view.Cleared && view.State.Content == null);
    }

    private static AdmittedDetailContent Content(string source)
    {
        var request = HostRequest.Create(RequestOrigin.LocalUi);
        return new(new(new(Guid.NewGuid()), 1), DetailContentKind.StaticHtml,
            DetailContentOrigin.FinalizedResponse, DetailSensitivity.DisclosureConfirmationRequired,
            "Static result", "Exact host snapshot", source,
            sessionSource: new(request.SessionId, request.RequestId, request.TaskId));
    }

    private sealed class NoDocuments : IUserDocumentationProvider
    {
        public IReadOnlyList<UserDocumentationPage> GetPages() => [];
        public UserDocumentationPage GetStartPage() => throw new InvalidOperationException("No guide source is admitted by this result test.");
    }

    private sealed class NoClipboard : IDetailClipboard
    {
        public int Writes { get; private set; }
        public Task WriteAsync(IDetailView view, string source, string? htmlFragment)
        {
            Writes++;
            throw new InvalidOperationException("A static result must not acquire clipboard authority.");
        }
    }

    private sealed class FakeView(DetailViewerState state) : IDetailView
    {
        public DetailViewerState State { get; } = state;
        public bool Cleared { get; private set; }
        public event EventHandler? Closed;
        public void ShowOwned(Window? owner) { }
        public void Activate() { }
        public void ClearAndClose()
        {
            Cleared = true;
            State.Close();
            Closed?.Invoke(this, EventArgs.Empty);
        }
    }
}
