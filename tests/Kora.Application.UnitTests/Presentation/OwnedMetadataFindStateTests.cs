using AwesomeAssertions;

using Kora.Application.Presentation;
using Kora.Core.Context;

namespace Kora.Application.UnitTests.Presentation;

public sealed class OwnedMetadataFindStateTests
{
    [Fact]
    public void CountedMetadataUsesCompleteNonoverlappingSequenceAndBoundaryOrdinalWithoutWrap()
    {
        using var state = new AdmittedMetadataFindState("😀 aaaaa aa Aa e\u0301 é", () => true, counted: true);
        state.Count.Should().Be(0);
        state.Ordinal.Should().Be(0);
        state.Edit("aa");
        state.Find(SnapshotFindDirection.Previous);
        state.Count.Should().Be(0);
        state.Find(SnapshotFindDirection.First);
        state.Match.Should().Be(new SnapshotLiteralSpan(3, 2));
        state.Count.Should().Be(3);
        state.Ordinal.Should().Be(1);
        state.Find(SnapshotFindDirection.Next);
        state.Match.Should().Be(new SnapshotLiteralSpan(5, 2));
        state.Ordinal.Should().Be(2);
        state.Find(SnapshotFindDirection.Next);
        state.Match.Should().Be(new SnapshotLiteralSpan(9, 2));
        state.Ordinal.Should().Be(3);
        state.Find(SnapshotFindDirection.Next);
        state.Match.Should().BeNull();
        state.Count.Should().Be(3);
        state.Ordinal.Should().Be(3);
        state.Status.Should().Contain("no wrap").And.Contain("no displayed match");
        state.Find(SnapshotFindDirection.Previous);
        state.Match.Should().Be(new SnapshotLiteralSpan(5, 2));
        state.Ordinal.Should().Be(2);
        state.Find(SnapshotFindDirection.Previous);
        state.Find(SnapshotFindDirection.Previous);
        state.Match.Should().BeNull();
        state.Ordinal.Should().Be(1);
        state.Find(SnapshotFindDirection.First);
        state.ReportFocusUnavailable();
        state.Count.Should().Be(0);
        state.Ordinal.Should().Be(0);
        state.Match.Should().BeNull();
        state.Find(SnapshotFindDirection.First);
        state.Clear();
        state.Count.Should().Be(0);
        state.Ordinal.Should().Be(0);
        state.Edit("missing");
        state.Find(SnapshotFindDirection.First);
        state.Count.Should().Be(0);
        state.Ordinal.Should().Be(0);
        state.Status.Should().Contain("0 nonoverlapping matches").And.Contain("no displayed match");
        state.Edit("\n");
        state.Find(SnapshotFindDirection.First);
        state.Count.Should().Be(0);
        state.Dispose();
        state.Ordinal.Should().Be(0);
    }

    [Fact]
    public void ReentrantRetirementCannotResurrectAnOldAdmission()
    {
        AdmittedMetadataFindState? state = null;
        state = new("private", () => { state!.Dispose(); return true; }, counted: true);
        using (state)
        {
            state.CheckAdmission().Should().BeFalse();
            state.Available.Should().BeFalse();
            state.Query.Should().BeEmpty();
            state.Count.Should().Be(0);
        }
    }

    [Fact]
    public void CountAndOrdinalAreCachedReadsWithoutAdmissionCallbacksOrScanning()
    {
        var calls = 0;
        using var state = new AdmittedMetadataFindState("aa aa", () => { calls++; return true; }, counted: true);
        state.Edit("aa");
        state.Find(SnapshotFindDirection.First);
        var before = calls;
        for (var index = 0; index < 10; index++)
        {
            state.Count.Should().Be(2);
            state.Ordinal.Should().Be(1);
            state.Available.Should().BeTrue();
            state.Query.Should().Be("aa");
            state.Status.Should().Contain("1 of 2");
            state.Match.Should().Be(new SnapshotLiteralSpan(0, 2));
            state.ExactMatch.Should().Be("aa");
            state.CanNavigate.Should().BeTrue();
        }
        calls.Should().Be(before);
    }
}
