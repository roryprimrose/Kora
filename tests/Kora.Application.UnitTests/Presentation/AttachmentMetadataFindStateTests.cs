using AwesomeAssertions;

using Kora.Application.Presentation;
using Kora.Core.Context;

namespace Kora.Application.UnitTests.Presentation;

public sealed class AttachmentMetadataFindStateTests
{
    [Fact]
    public void CountedModeUsesCompleteForwardNonoverlappingSequenceAndErasesAllPrivateState()
    {
        var current = true;
        using var state = new AdmittedMetadataFindState("😀 aaaaa Aa aa", () => current, counted: true);
        state.Count.Should().Be(0);
        state.Ordinal.Should().Be(0);
        state.Edit("aa");
        state.Find(SnapshotFindDirection.First);
        state.Count.Should().Be(3);
        state.Ordinal.Should().Be(1);
        state.Match.Should().Be(new SnapshotLiteralSpan(3, 2));
        state.Status.Should().Contain("3 nonoverlapping matches").And.Contain("selected 1 of 3");
        state.Find(SnapshotFindDirection.Previous);
        state.Match.Should().BeNull();
        state.Count.Should().Be(3);
        state.Ordinal.Should().Be(1);
        state.Status.Should().Contain("no displayed match").And.Contain("no wrap");
        state.Find(SnapshotFindDirection.Next);
        state.Match.Should().Be(new SnapshotLiteralSpan(5, 2));
        state.Ordinal.Should().Be(2);
        state.Find(SnapshotFindDirection.Previous);
        state.Match.Should().Be(new SnapshotLiteralSpan(3, 2));
        state.Find(SnapshotFindDirection.Next);
        state.Find(SnapshotFindDirection.Next);
        state.Ordinal.Should().Be(3);
        state.Find(SnapshotFindDirection.Next);
        state.Match.Should().BeNull();
        state.Count.Should().Be(3);
        state.Ordinal.Should().Be(3);
        state.Status.Should().Contain("no displayed match");
        state.ReportFocusUnavailable();
        state.Count.Should().Be(0);
        state.Ordinal.Should().Be(0);
        state.CanNavigate.Should().BeFalse();
        state.Find(SnapshotFindDirection.Next);
        state.Status.Should().Contain("Find first");
        state.Find(SnapshotFindDirection.First);
        state.Clear();
        state.Count.Should().Be(0);
        state.Ordinal.Should().Be(0);
        state.Edit("missing");
        state.Find(SnapshotFindDirection.First);
        state.Count.Should().Be(0);
        state.Ordinal.Should().Be(0);
        state.Status.Should().Contain("No literal match").And.Contain("0 nonoverlapping matches");
        state.Edit("\n");
        state.Find(SnapshotFindDirection.First);
        state.Count.Should().Be(0);
        state.Ordinal.Should().Be(0);
        state.Status.Should().StartWith("Invalid");
        state.Edit("aa");
        state.Find(SnapshotFindDirection.First);
        state.Edit("Aa");
        state.Count.Should().Be(0);
        state.Ordinal.Should().Be(0);
        state.Find(SnapshotFindDirection.First);
        state.Count.Should().Be(1);
        state.Ordinal.Should().Be(1);
        current = false;
        state.Count.Should().Be(0);
        state.Ordinal.Should().Be(0);
        state.Query.Should().BeEmpty();
        current = true;
        state.Find(SnapshotFindDirection.First);
        state.Count.Should().Be(0);
        state.Ordinal.Should().Be(0);
    }

    [Fact]
    public void CountedPassivePresentationIsExactNonoverlappingAndDoesNotRetireOrScanOnRead()
    {
        var admitted = true;
        using var state = new AdmittedMetadataFindState("aaaaa Aa Aa aa 😀 e\u0301 é", () => admitted);
        state.ReadCurrent().Should().Be(new AdmittedMetadataFindSnapshot(true, "", "", null, false, "", 0, 0, false));
        state.Edit("aa");
        state.Find(SnapshotFindDirection.First);
        state.ReadCurrent().Count.Should().Be(3);
        state.ReadCurrent().HasCount.Should().BeTrue();
        state.ReadCurrent().Ordinal.Should().Be(1);
        state.ReadCurrent().Match.Should().Be(new SnapshotLiteralSpan(0, 2));
        state.Find(SnapshotFindDirection.Next);
        state.ReadCurrent().Ordinal.Should().Be(2);
        state.ReadCurrent().Match.Should().Be(new SnapshotLiteralSpan(2, 2));
        state.Find(SnapshotFindDirection.Previous);
        state.ReadCurrent().Match.Should().Be(new SnapshotLiteralSpan(0, 2));
        state.Find(SnapshotFindDirection.Previous);
        state.ReadCurrent().Match.Should().BeNull();
        state.ReadCurrent().Count.Should().Be(3);
        state.ReadCurrent().Ordinal.Should().Be(1);
        state.ReadCurrent().ExactMatch.Should().BeEmpty();
        state.Find(SnapshotFindDirection.Next);
        state.Find(SnapshotFindDirection.Next);
        state.Find(SnapshotFindDirection.Next);
        var end = state.ReadCurrent();
        end.Ordinal.Should().Be(3);
        end.Count.Should().Be(3);
        end.Match.Should().BeNull();
        end.Status.Should().Contain("no wrap");
        admitted = false;
        state.ReadCurrent().Should().Be(AdmittedMetadataFindSnapshot.Unavailable);
        admitted = true;
        state.ReadCurrent().Should().Be(end, "passive reads neither mutate nor notify; owners explicitly retire revoked lifetimes");
        state.ReportFocusUnavailable();
        state.ReadCurrent().Count.Should().Be(0);
        state.ReadCurrent().HasCount.Should().BeFalse();
        state.ReadCurrent().Ordinal.Should().Be(0);
        state.Edit("Aa");
        state.Find(SnapshotFindDirection.First);
        state.ReadCurrent().Count.Should().Be(2);
        state.ReadCurrent().ExactMatch.Should().Be("Aa");
        state.Clear();
        state.ReadCurrent().Count.Should().Be(0);
        state.Edit("\n");
        state.Find(SnapshotFindDirection.First);
        state.ReadCurrent().Count.Should().Be(0);
        var unsupported = () => state.Find((SnapshotFindDirection)1234);
        unsupported.Should().Throw<ArgumentOutOfRangeException>();
        state.Dispose();
        state.ReadCurrent().Should().Be(AdmittedMetadataFindSnapshot.Unavailable);
    }

    [Fact]
    public void FindNavigatesExactImmutableTextWithoutWrapAndRetainsOnlyBoundaryCursor()
    {
        using var state = new AdmittedMetadataFindState("😀 Aa Aa aa", () => true);
        state.Available.Should().BeTrue();
        state.Query.Should().BeEmpty();
        state.Status.Should().BeEmpty();
        state.Match.Should().BeNull();
        state.ExactMatch.Should().BeEmpty();
        state.CanNavigate.Should().BeFalse();
        state.Edit("Aa");
        state.Query.Should().Be("Aa");
        state.Find(SnapshotFindDirection.Next);
        state.Status.Should().Contain("Find first");
        state.Find(SnapshotFindDirection.First);
        state.Match.Should().Be(new SnapshotLiteralSpan(3, 2));
        state.ExactMatch.Should().Be("Aa");
        state.Status.Should().Contain("[3, 5)");
        state.CanNavigate.Should().BeTrue();
        state.Find(SnapshotFindDirection.Next);
        state.Match.Should().Be(new SnapshotLiteralSpan(6, 2));
        state.Find(SnapshotFindDirection.Next);
        state.Match.Should().BeNull();
        state.ExactMatch.Should().BeEmpty();
        state.Status.Should().Contain("forward").And.Contain("no wrap");
        state.CanNavigate.Should().BeTrue();
        state.Find(SnapshotFindDirection.Previous);
        state.Match.Should().Be(new SnapshotLiteralSpan(3, 2));
        state.Find(SnapshotFindDirection.Previous);
        state.Match.Should().BeNull();
        state.Status.Should().Contain("backward").And.Contain("no wrap");
        state.Find(SnapshotFindDirection.Next);
        state.Match.Should().Be(new SnapshotLiteralSpan(6, 2));
        state.Edit("AA");
        state.CanNavigate.Should().BeFalse();
        state.Status.Should().BeEmpty();
        state.Match.Should().BeNull();
        state.Find(SnapshotFindDirection.First);
        state.Status.Should().StartWith("No literal match");
        state.Clear();
        state.Query.Should().BeEmpty();
        state.Status.Should().BeEmpty();
        state.Match.Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("\n")]
    [InlineData("\t")]
    [InlineData("\u200b")]
    [InlineData("\u2028")]
    [InlineData("\u2029")]
    public void FindRejectsInvalidQueryExplicitly(string query)
    {
        using var state = new AdmittedMetadataFindState(query, () => true);
        state.Edit(query);
        state.Find(SnapshotFindDirection.First);
        state.Status.Should().StartWith("Invalid");
        state.Match.Should().BeNull();
        state.CanNavigate.Should().BeFalse();
    }

    [Fact]
    public void FindUsesAuthoritativeUnicodeAndByteBoundsOverEntireSnapshot()
    {
        var maximum = new string('x', LocalFileRetrievalPolicy.MaximumQueryCharacters);
        var unicode = string.Concat(Enumerable.Repeat("😀", LocalFileRetrievalPolicy.MaximumQueryUtf8Bytes / 4));
        var suffix = maximum + unicode + " e\u0301 é";
        var text = new string('z', SessionPageBudget - suffix.Length) + suffix;
        text.Length.Should().Be(SessionPageBudget);
        using var state = new AdmittedMetadataFindState(text, () => true);
        foreach (var query in new[] { maximum, unicode, "e\u0301", "é", " " })
        {
            state.Edit(query);
            state.Find(SnapshotFindDirection.First);
            state.ExactMatch.Should().Be(query);
            state.Match!.Start.Should().Be(text.IndexOf(query, StringComparison.Ordinal));
        }
        foreach (var invalid in new[] { maximum + "x", unicode + "😀", new string('界', 171), new string('\ud800', 1) })
        {
            state.Edit(invalid);
            state.Find(SnapshotFindDirection.First);
            state.Status.Should().StartWith("Invalid");
            state.CanNavigate.Should().BeFalse();
        }
        state.Edit("zz");
        state.Find(SnapshotFindDirection.First);
        state.Find(SnapshotFindDirection.Next);
        state.Match!.Start.Should().Be(2);
    }

    [Fact]
    public void RevocationAndDisposalEraseSnapshotQueryMatchStatusAndDenyRetainedActions()
    {
        var admitted = true;
        using var state = new AdmittedMetadataFindState("private", () => admitted);
        state.Edit("private");
        state.Find(SnapshotFindDirection.First);
        state.ReportFocusUnavailable();
        state.Status.Should().Contain("focus is unavailable");
        state.Match.Should().BeNull();
        state.CanNavigate.Should().BeFalse();
        state.Find(SnapshotFindDirection.First);
        admitted = false;
        state.Available.Should().BeFalse();
        admitted = true;
        state.Edit("private");
        state.Find(SnapshotFindDirection.First);
        state.Clear();
        state.ReportFocusUnavailable();
        state.Dispose();
        state.Available.Should().BeFalse();
        state.Query.Should().BeEmpty();
        state.Match.Should().BeNull();
        state.Status.Should().BeEmpty();
        state.ExactMatch.Should().BeEmpty();
        state.CanNavigate.Should().BeFalse();
        var editNull = () => state.Edit(null!);
        editNull.Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RevocationDuringFindCannotPublishComputedMatch(bool counted)
    {
        var calls = 0;
        using var state = new AdmittedMetadataFindState("private", () => ++calls < 3, counted);
        state.Edit("private");
        state.Find(SnapshotFindDirection.First);
        state.Query.Should().BeEmpty();
        state.Status.Should().BeEmpty();
        state.Match.Should().BeNull();
    }

    [Fact]
    public void IndependentInspectorSnapshotsCannotRecoverRetiredIdentityThroughRetainedCallbacks()
    {
        var selected = true;
        using var oldIdentity = new AdmittedMetadataFindState("Source: already-displayed\\SKILL.md | Aa Aa", () => selected);
        oldIdentity.Edit("Aa");
        oldIdentity.Find(SnapshotFindDirection.First);
        oldIdentity.ExactMatch.Should().Be("Aa");
        selected = false;
        oldIdentity.Available.Should().BeFalse();
        selected = true;
        using var replacement = new AdmittedMetadataFindState("Replacement identity | Aa", () => selected);
        replacement.Edit("Aa");
        replacement.Find(SnapshotFindDirection.First);
        oldIdentity.Edit("already-displayed");
        oldIdentity.Find(SnapshotFindDirection.First);
        oldIdentity.ExactMatch.Should().BeEmpty();
        oldIdentity.Query.Should().BeEmpty();
        replacement.ExactMatch.Should().Be("Aa");
        replacement.Match.Should().Be(new SnapshotLiteralSpan(23, 2));
    }

    private const int SessionPageBudget = Kora.Core.Storage.SessionAttachmentPage.MaximumBytes;
}
