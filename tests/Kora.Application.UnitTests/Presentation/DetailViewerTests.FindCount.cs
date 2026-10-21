using AwesomeAssertions;

using Kora.Application.Presentation;
using Kora.Core.Context;
using Kora.Core.Presentation;

namespace Kora.Application.UnitTests.Presentation;

public sealed partial class DetailViewerTests
{
    [Theory]
    [InlineData("aaaaa", "aa", 0, 2)]
    [InlineData("é e\u0301 é", "é", 0, 5)]
    [InlineData("😀x😀", "😀", 0, 3)]
    [InlineData("a SOURCE b source source", "source", 11, 18)]
    [InlineData(" a  a ", " a ", 0, 3)]
    public void Counted_navigation_uses_complete_exact_nonoverlapping_sequence_and_retains_boundary_ordinal(
        string text, string query, int first, int second)
    {
        var viewer = new DetailViewerState(Item(source: text));
        viewer.Find("missing", SnapshotFindDirection.First).Should().BeFalse();
        viewer.MatchCount.Should().Be(0);
        viewer.MatchOrdinal.Should().BeNull();
        viewer.FindSummary.Should().Be("0 matches in this exact admitted text.");
        viewer.Find(query, SnapshotFindDirection.First).Should().BeTrue();
        AssertCount(viewer, 2, 1, first);
        viewer.Find(query, SnapshotFindDirection.Previous).Should().BeFalse();
        AssertCount(viewer, 2, 1, -1);
        viewer.Find(query, SnapshotFindDirection.Next).Should().BeTrue();
        AssertCount(viewer, 2, 2, second);
        viewer.Find(query, SnapshotFindDirection.Next).Should().BeFalse();
        AssertCount(viewer, 2, 2, -1);
        viewer.Find(query, SnapshotFindDirection.Next).Should().BeFalse();
        AssertCount(viewer, 2, 2, -1);
        viewer.Find(query, SnapshotFindDirection.Previous).Should().BeTrue();
        AssertCount(viewer, 2, 1, first);
        viewer.Find(query, SnapshotFindDirection.First).Should().BeTrue();
        AssertCount(viewer, 2, 1, first);
        viewer.Content!.Source.Should().Be(text);
    }

    [Fact]
    public void Count_is_complete_at_reader_bound_and_query_character_and_byte_bounds()
    {
        var viewer = new DetailViewerState(Item(source: new('a', NativeDetailProfile.MaximumUtf8Bytes)));
        viewer.Find("a", SnapshotFindDirection.First).Should().BeTrue();
        AssertCount(viewer, NativeDetailProfile.MaximumUtf8Bytes, 1, 0);
        viewer.Find(new('a', 256), SnapshotFindDirection.First).Should().BeTrue();
        AssertCount(viewer, NativeDetailProfile.MaximumUtf8Bytes / 256, 1, 0);
        var unicode = new DetailViewerState(Item(source: new('é', 512)));
        unicode.Find(new('é', 256), SnapshotFindDirection.First).Should().BeTrue();
        AssertCount(unicode, 2, 1, 0);
    }

    [Theory]
    [InlineData("")]
    [InlineData("a\nb")]
    [InlineData("\u202ealpha")]
    [InlineData("surrogate")]
    [InlineData("characters")]
    [InlineData("bytes")]
    public void Rejected_queries_never_leave_a_successful_count(string input)
    {
        var query = input switch
        {
            "surrogate" => new string('\ud800', 1),
            "characters" => new string('a', 257),
            "bytes" => new string('界', 171),
            _ => input,
        };
        var viewer = new DetailViewerState(Item(source: "alpha alpha"));
        viewer.Find("alpha", SnapshotFindDirection.First).Should().BeTrue();
        viewer.Find(query, SnapshotFindDirection.First).Should().BeFalse();
        AssertCountUnavailable(viewer);
        viewer.SearchQuery.Should().Be(query);
        viewer.Status.Should().Contain("rejected");
    }

    [Fact]
    public void Query_cancel_clear_projection_render_and_close_retire_count_without_changing_copy_authority()
    {
        var content = Item(source: "source source", sensitivity: DetailSensitivity.DisclosureConfirmationRequired);
        var viewer = new DetailViewerState(content);
        AssertCountUnavailable(viewer);
        viewer.Find("source", SnapshotFindDirection.First).Should().BeTrue();
        viewer.EditFindQuery("source");
        AssertCountUnavailable(viewer);
        viewer.Find("source", SnapshotFindDirection.Next).Should().BeFalse();
        AssertCountUnavailable(viewer);
        viewer.Find("source", SnapshotFindDirection.First).Should().BeTrue();
        viewer.CancelFind();
        AssertCountUnavailable(viewer);
        viewer.SearchQuery.Should().Be("source");
        viewer.Find("source", SnapshotFindDirection.First).Should().BeTrue();
        viewer.CancelFind(clearQuery: true);
        AssertCountUnavailable(viewer);
        viewer.SearchQuery.Should().BeEmpty();
        viewer.Find("source", SnapshotFindDirection.First).Should().BeTrue();
        viewer.CompleteRender(viewer.Generation, "rendered rendered rendered", "Rendered.", false).Should().BeTrue();
        AssertCountUnavailable(viewer);
        viewer.Find("source", SnapshotFindDirection.First).Should().BeFalse();
        viewer.MatchCount.Should().Be(0);
        viewer.Find("rendered", SnapshotFindDirection.First).Should().BeTrue();
        AssertCount(viewer, 3, 1, 0);
        viewer.SetSource(true);
        AssertCountUnavailable(viewer);
        viewer.Find("source", SnapshotFindDirection.First).Should().BeTrue();
        AssertCount(viewer, 2, 1, 0);
        viewer.TryGetCopySource(true, false, out var denied).Should().BeFalse();
        denied.Should().BeNull();
        viewer.TryGetCopySource(true, true, out var exact).Should().BeTrue();
        exact.Should().Be(content.Source);
        viewer.CompleteRender(viewer.Generation, "", "Fallback.", true).Should().BeTrue();
        AssertCountUnavailable(viewer);
        viewer.Find("source", SnapshotFindDirection.First).Should().BeTrue();
        viewer.Close();
        AssertCountUnavailable(viewer);
        viewer.FindSummary.Should().Be("Literal Find unavailable.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Admission_loss_retires_counts_for_read_and_find_without_source_reads(bool findOnly)
    {
        var admitted = true;
        var viewer = new DetailViewerState(Item(source: "source source"),
            sourceReadAdmission: findOnly ? null : () => admitted,
            findAdmission: findOnly ? () => admitted : null);
        viewer.Find("source", SnapshotFindDirection.First).Should().BeTrue();
        admitted = false;
        AssertCountUnavailable(viewer);
        viewer.Content.Should().BeNull();
        viewer.FindSummary.Should().Be("Literal Find unavailable.");
        viewer.Find("source", SnapshotFindDirection.Next).Should().BeFalse();
    }

    [Theory]
    [InlineData("query")]
    [InlineData("close")]
    public void Reentrant_query_admission_changes_cannot_publish_counts(string change)
    {
        var viewer = new DetailViewerState(Item(source: "source source"));
        var changed = false;
        viewer.PropertyChanged += (_, args) =>
        {
            if (changed || !string.Equals(args.PropertyName, nameof(DetailViewerState.SearchQuery), StringComparison.Ordinal)) { return; }
            changed = true;
            if (string.Equals(change, "close", StringComparison.Ordinal)) { viewer.Close(); }
            else { viewer.EditFindQuery("different"); }
        };
        viewer.Find("source", SnapshotFindDirection.First).Should().BeFalse();
        AssertCountUnavailable(viewer);
    }

    [Fact]
    public void Reentrant_projection_change_at_final_admission_cannot_publish_stale_count()
    {
        DetailViewerState? viewer = null;
        var admissionCalls = 0;
        var changeAt = int.MaxValue;
        viewer = new DetailViewerState(Item(source: "source source"), findAdmission: () =>
        {
            admissionCalls++;
            if (admissionCalls == changeAt) { viewer!.SetSource(true); }
            return true;
        });
        viewer.EditFindQuery("source");
        // Find's initial, query and final publication checks must retain the same projection.
        changeAt = admissionCalls + 3;
        viewer.Find("source", SnapshotFindDirection.First).Should().BeFalse();
        viewer.IsSource.Should().BeTrue();
        AssertCountUnavailable(viewer);
    }

    [Fact]
    public void Final_admission_denial_does_not_publish_a_computed_count()
    {
        var calls = 0;
        var denyAt = int.MaxValue;
        var viewer = new DetailViewerState(Item(source: "source source"),
            findAdmission: () => ++calls != denyAt);
        viewer.EditFindQuery("source");
        denyAt = calls + 3;
        viewer.Find("source", SnapshotFindDirection.First).Should().BeFalse();
        AssertCountUnavailable(viewer);
        viewer.Content.Should().BeNull();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Reentrant_retirement_after_successful_count_publication_retires_all_results(bool close)
    {
        var viewer = new DetailViewerState(Item(source: "source source"));
        viewer.PropertyChanged += (_, args) =>
        {
            if (string.Equals(args.PropertyName, nameof(DetailViewerState.MatchCount), StringComparison.Ordinal) && viewer.MatchCount is not null)
            {
                if (close) { viewer.Close(); }
                else { viewer.CancelFind(); }
            }
        };
        viewer.Find("source", SnapshotFindDirection.First).Should().BeFalse();
        AssertCountUnavailable(viewer);
        if (close) { viewer.Content.Should().BeNull(); }
        else { viewer.Content.Should().NotBeNull(); }
    }

    private static void AssertCount(DetailViewerState viewer, int count, int ordinal, int start)
    {
        viewer.MatchCount.Should().Be(count);
        viewer.MatchOrdinal.Should().Be(ordinal);
        viewer.MatchStart.Should().Be(start);
        viewer.FindSummary.Should().Be($"Match {ordinal} of {count} in this exact admitted text.");
    }

    private static void AssertCountUnavailable(DetailViewerState viewer)
    {
        viewer.MatchCount.Should().BeNull();
        viewer.MatchOrdinal.Should().BeNull();
        viewer.MatchStart.Should().Be(-1);
        viewer.MatchLength.Should().Be(0);
        viewer.FindSummary.Should().Contain("unavailable");
    }
}
