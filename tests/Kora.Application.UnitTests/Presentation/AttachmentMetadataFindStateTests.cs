using AwesomeAssertions;

using Kora.Application.Presentation;
using Kora.Core.Context;

namespace Kora.Application.UnitTests.Presentation;

public sealed class AttachmentMetadataFindStateTests
{
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

    [Fact]
    public void RevocationDuringFindCannotPublishComputedMatch()
    {
        var calls = 0;
        using var state = new AdmittedMetadataFindState("private", () => ++calls < 3);
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
