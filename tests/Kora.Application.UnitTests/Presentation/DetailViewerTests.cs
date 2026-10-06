using System.Diagnostics;
using AwesomeAssertions;
using Kora.Application.Presentation;
using Kora.Core.Hosting;
using Kora.Core.Presentation;

namespace Kora.Application.UnitTests.Presentation;

[Collection("Host tracing")]
public sealed class DetailViewerTests
{
    [Fact]
    public void Reopening_exact_reference_reuses_state_but_new_revision_is_separate()
    {
        var registry = new DetailViewerRegistry();
        var item = Item();
        var first = registry.Open(item, true);
        registry.Open(item, true).Should().BeSameAs(first);
        var revision = Item(reference: new(item.Reference.ItemId, 2), source: "new answer");
        var second = registry.Open(revision, true);
        second.Should().NotBeSameAs(first);
        first.Content.Should().BeSameAs(item);
        second.Content.Should().BeSameAs(revision);
        first.Reference.Should().Be(item.Reference);
    }

    [Fact]
    public void Same_reference_with_changed_source_fails_instead_of_replacing()
    {
        var registry = new DetailViewerRegistry();
        var item = Item();
        registry.Open(item, true);
        var replace = () => registry.Open(Item(reference: item.Reference, source: "replacement"), true);
        replace.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void Privacy_unknown_and_closed_host_gate_fail_closed_even_for_existing_reference()
    {
        var registry = new DetailViewerRegistry();
        var item = Item();
        registry.Open(item, true);
        var open = () => registry.Open(item, false);
        open.Should().Throw<InvalidOperationException>();
        var missing = () => registry.Open(null!, true);
        missing.Should().Throw<ArgumentNullException>();
        var missingState = () => new DetailViewerState(null!);
        missingState.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Open_viewers_have_a_measured_bound_without_rejecting_activation()
    {
        var registry = new DetailViewerRegistry();
        var first = Item();
        registry.Open(first, true);
        for (var index = 1; index < NativeDetailProfile.MaximumOpenViewers; index++)
        {
            registry.Open(Item(), true);
        }
        registry.Open(first, true).Content.Should().BeSameAs(first);
        var overflow = () => registry.Open(Item(), true);
        overflow.Should().Throw<InvalidOperationException>().WithMessage("*Eight*");
        registry.Close(first.Reference);
        registry.Open(Item(), true);
    }

    [Fact]
    public void Closing_is_presentation_only_and_late_render_cannot_restore_content()
    {
        var registry = new DetailViewerRegistry();
        var item = Item();
        var viewer = registry.Open(item, true);
        var generation = viewer.Generation;
        viewer.CompleteRender(generation, "semantic text", "Rendered.", false).Should().BeTrue();
        registry.Close(item.Reference);
        registry.Close(item.Reference);
        viewer.CompleteRender(generation, "late private answer", "Rendered.", false).Should().BeFalse();
        viewer.Content.Should().BeNull();
        viewer.ActiveText.Should().BeEmpty();
        viewer.RenderState.Should().Be(DetailRenderState.Closed);
        viewer.Generation.Should().BeGreaterThan(generation);
        viewer.Status.Should().Contain("work were not changed");
        item.Source.Should().Be("source");
        registry.Open(item, true).Should().NotBeSameAs(viewer);
    }

    [Fact]
    public void Explicit_privacy_cleanup_forgets_all_private_sources_search_and_render_generations()
    {
        var registry = new DetailViewerRegistry();
        var first = registry.Open(Item(sensitivity: DetailSensitivity.Private), true);
        var second = registry.Open(Item(), true);
        first.Search("source").Should().BeTrue();
        registry.ClearForPrivacy();
        registry.ClearForPrivacy();
        first.ActiveText.Should().BeEmpty();
        first.SearchQuery.Should().BeEmpty();
        first.MatchStart.Should().Be(-1);
        first.MatchLength.Should().Be(0);
        second.Content.Should().BeNull();
        first.Search("source").Should().BeFalse();
        first.SetSource(true);
        first.TryGetCopySource(true, true, out var text).Should().BeFalse();
        text.Should().BeNull();
    }

    [Fact]
    public void Externally_closed_state_cannot_be_reused_as_live_source()
    {
        var registry = new DetailViewerRegistry();
        var item = Item();
        registry.Open(item, true).Close();
        var open = () => registry.Open(item, true);
        open.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void Render_result_is_exact_generation_bound_and_source_toggle_does_not_change_identity()
    {
        var item = Item(source: "# Heading\r\n\r\n**text**");
        var viewer = new DetailViewerState(item);
        var changes = new List<string?>();
        viewer.PropertyChanged += (_, args) => changes.Add(args.PropertyName);
        viewer.RenderState.Should().Be(DetailRenderState.Rendering);
        viewer.ActiveText.Should().Be(item.Source);
        viewer.IsSource.Should().BeFalse();
        viewer.CompleteRender(2, "late", "Late.", false).Should().BeFalse();
        viewer.CompleteRender(viewer.Generation, "Heading\n\ntext", "Native rendering.", false).Should().BeTrue();
        viewer.RenderState.Should().Be(DetailRenderState.Rendered);
        viewer.ActiveText.Should().Be("Heading\n\ntext");
        viewer.SetSource(true);
        viewer.IsSource.Should().BeTrue();
        viewer.ActiveText.Should().Be(item.Source);
        viewer.SetSource(false);
        viewer.ActiveText.Should().Be("Heading\n\ntext");
        viewer.Content.Should().BeSameAs(item);
        changes.Should().Contain(nameof(DetailViewerState.ActiveText));
        changes.Should().Contain(nameof(DetailViewerState.RenderState));
    }

    [Fact]
    public void Renderer_failure_or_missing_renderer_exposes_complete_labelled_source()
    {
        var viewer = new DetailViewerState(Item(source: "<script>approve()</script>"));
        viewer.CompleteRender(viewer.Generation, string.Empty,
            "Renderer unavailable; exact source fallback.", true).Should().BeTrue();
        viewer.RenderState.Should().Be(DetailRenderState.SourceFallback);
        viewer.Status.Should().Contain("unavailable");
        viewer.ActiveText.Should().Be("<script>approve()</script>");
        viewer.SetSource(true);
        viewer.ActiveText.Should().Be("<script>approve()</script>");
    }

    [Fact]
    public void Semantic_projection_cannot_create_an_unbounded_text_buffer()
    {
        var viewer = new DetailViewerState(Item());
        viewer.CompleteRender(1, new('s', NativeDetailProfile.MaximumUtf8Bytes), "At bound.", false);
        var oversized = () => viewer.CompleteRender(1, new('s', NativeDetailProfile.MaximumUtf8Bytes + 1), "Too large.", false);
        oversized.Should().Throw<InvalidDataException>();
        var oversizedUtf8 = () => viewer.CompleteRender(1, new('é', NativeDetailProfile.MaximumUtf8Bytes / 2 + 1), "Too large.", false);
        oversizedUtf8.Should().Throw<InvalidDataException>();
        var emptySemantic = () => viewer.CompleteRender(1, string.Empty, "Empty semantic result.", false);
        emptySemantic.Should().Throw<InvalidDataException>();
        var nullText = () => viewer.CompleteRender(1, null!, "Error.", true);
        nullText.Should().Throw<ArgumentNullException>();
        var emptyMessage = () => viewer.CompleteRender(1, "text", "", false);
        emptyMessage.Should().Throw<ArgumentException>();
        var emptyStatus = () => viewer.ReportStatus("");
        emptyStatus.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Forward_backward_search_wraps_and_respects_source_and_case_without_activity_changes()
    {
        var viewer = new DetailViewerState(Item(source: "a SOURCE b source"));
        viewer.Search("source").Should().BeTrue();
        viewer.MatchStart.Should().Be(2);
        viewer.MatchLength.Should().Be(6);
        viewer.Search("source").Should().BeTrue();
        viewer.MatchStart.Should().Be(11);
        viewer.Search("source").Should().BeTrue();
        viewer.MatchStart.Should().Be(2);
        viewer.Search("source", backwards: true).Should().BeTrue();
        viewer.MatchStart.Should().Be(11);
        viewer.Search("source", backwards: true).Should().BeTrue();
        viewer.MatchStart.Should().Be(2);
        viewer.Search("missing", backwards: true).Should().BeFalse();
        viewer.Search("missing").Should().BeFalse();
        viewer.Status.Should().Be("No search match.");
        viewer.Search("").Should().BeFalse();
        viewer.Search("a", backwards: true).Should().BeTrue();
        viewer.MatchStart.Should().Be(0);
        viewer.Search("a", backwards: true).Should().BeTrue();
        viewer.MatchStart.Should().Be(0);
        viewer.Content!.Source.Should().Be("a SOURCE b source");
    }

    [Fact]
    public void Search_rejects_overlength_query_visibly_and_keeps_current_selection()
    {
        var viewer = new DetailViewerState(Item(source: new('s', 256)));
        viewer.Search(new('s', 256)).Should().BeTrue();
        viewer.Search(new('s', 257)).Should().BeFalse();
        viewer.Status.Should().Contain("nothing was truncated");
        viewer.MatchLength.Should().Be(256);
        var nullQuery = () => viewer.Search(null!);
        nullQuery.Should().Throw<ArgumentNullException>();
        viewer.CompleteRender(1, "Rendered semantic text.", "Rendered.", false);
        viewer.Search("absent").Should().BeFalse();
    }

    [Theory]
    [InlineData(DetailSensitivity.Public, false, true)]
    [InlineData(DetailSensitivity.Private, false, true)]
    [InlineData(DetailSensitivity.DisclosureConfirmationRequired, false, false)]
    [InlineData(DetailSensitivity.DisclosureConfirmationRequired, true, true)]
    public void Native_copy_revalidates_access_and_additional_disclosure_without_touching_clipboard(
        DetailSensitivity sensitivity, bool confirmed, bool permitted)
    {
        var item = Item(source: "exact\ttext\r\n", sensitivity: sensitivity);
        var viewer = new DetailViewerState(item);
        viewer.TryGetCopySource(true, confirmed, out var text).Should().Be(permitted);
        text.Should().Be(permitted ? item.Source : null);
        viewer.TryGetCopySource(false, true, out text).Should().BeFalse();
        text.Should().BeNull();
        viewer.Status.Should().Contain("blocked");
        viewer.Content.Should().BeSameAs(item);
    }

    [Fact]
    public void Native_selection_copy_uses_exact_current_representation_and_rejects_empty_stale_or_invalid_ranges()
    {
        var viewer = new DetailViewerState(Item(source: "a\tb\r\nc"));
        viewer.TryGetCopySelection(true, false, 1, 4, out var copied).Should().BeTrue();
        copied.Should().Be("\tb\r\n");
        viewer.CompleteRender(1, "semantic\ntext", "Rendered.", false);
        viewer.TryGetCopySelection(true, false, 0, 8, out copied).Should().BeTrue();
        copied.Should().Be("semantic");
        viewer.TryGetCopySelection(false, true, 0, 1, out copied).Should().BeFalse();
        copied.Should().BeNull();
        foreach (var (start, length) in new[] { (-1, 1), (0, 0), (0, -1), (100, 1), (0, 100), (int.MaxValue, int.MaxValue) })
        {
            viewer.TryGetCopySelection(true, false, start, length, out copied).Should().BeFalse();
            copied.Should().BeNull();
            viewer.Status.Should().Contain("nonempty range");
        }
        viewer.SetSource(true);
        viewer.TryGetCopySelection(true, false, 0, viewer.ActiveText.Length, out copied).Should().BeTrue();
        copied.Should().Be("a\tb\r\nc");
        viewer.Close();
        viewer.TryGetCopySelection(true, true, 0, 1, out copied).Should().BeFalse();
    }

    [Fact]
    public void Passive_viewing_does_not_mutate_existing_host_task_or_authorization_identity()
    {
        var request = HostRequest.Create(RequestOrigin.LocalUi);
        var task = new HostTaskRecord(request, new(1), HostTaskState.Succeeded);
        var source = new DetailSessionSource(request.SessionId, request.RequestId, request.TaskId);
        var item = new AdmittedDetailContent(new(new(Guid.NewGuid()), 1), DetailContentKind.PlainText,
            DetailContentOrigin.FinalizedResponse, DetailSensitivity.Private, "Result", "Host finalized result", "text", source);
        var registry = new DetailViewerRegistry();
        var viewer = registry.Open(item, true);
        viewer.Search("text");
        viewer.SetSource(true);
        viewer.TryGetCopySource(true, false, out _);
        registry.Close(item.Reference);
        task.State.Should().Be(HostTaskState.Succeeded);
        task.Revision.Value.Should().Be(1);
        task.Request.Should().BeSameAs(request);
    }

    [Fact]
    public void Presentation_trace_is_truthful_and_does_not_leak_source_or_claim_session_authority()
    {
        var stopped = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => string.Equals(source.Name, "Kora.Application", StringComparison.Ordinal),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = stopped.Add,
        };
        ActivitySource.AddActivityListener(listener);
        var registry = new DetailViewerRegistry();
        var item = Item(source: "private body");
        registry.Open(item, true);
        registry.Open(item, true);
        var blocked = () => registry.Open(item, false);
        blocked.Should().Throw<InvalidOperationException>();
        stopped.Select(activity => activity.Status).Should().Equal(
            ActivityStatusCode.Ok, ActivityStatusCode.Ok, ActivityStatusCode.Error);
        stopped.Should().OnlyContain(activity => activity.OperationName == "presentation.open");
        stopped.SelectMany(activity => activity.TagObjects).Should().NotContain(tag =>
            tag.Key == "kora.session.id" || Equals(tag.Value, "private body"));
    }

    private static AdmittedDetailContent Item(DetailContentReference? reference = null,
        string source = "source", DetailSensitivity sensitivity = DetailSensitivity.Public) =>
        new(reference ?? new(new(Guid.NewGuid()), 1), DetailContentKind.Markdown,
            DetailContentOrigin.EmbeddedDocument, sensitivity, "Guide page", "Embedded guide", source);
}
