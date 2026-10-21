using System.Collections.Immutable;
using System.Globalization;

using AwesomeAssertions;

using Kora.Core.Storage;

namespace Kora.Core.UnitTests.Storage;

public sealed class SavedDraftPageOrderingTests
{
    [Theory]
    [InlineData("")]
    [InlineData("tr-TR")]
    [InlineData("sv-SE")]
    public void CompletePageOrderingIsOrdinalDeterministicAndNeverChangesTheSourceOrContinuation(string culture)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            var a = Row("00000001", "z", long.MaxValue);
            var b = Row("00000100", "I", 1);
            var c = Row("80000000", "I", long.MaxValue);
            var d = Row("81000000", null, 1);
            var e = Row("82000000", null, long.MaxValue);
            var f = Row("83000000", "i", 1);
            var g = Row("ffffffff", "Unnamed", 1);
            var page = Page([a, b, c, d, e, f, g]);
            SavedDraftSummary[][] expected =
                [[a, b, c, d, e, f, g], [b, c, g, f, a, d, e], [a, f, g, b, c, d, e],
                    [b, d, f, g, a, c, e], [a, c, e, b, d, f, g]];
            SavedDraftPageOrdering.Apply(page).Should().Equal(page.Records);
            foreach (var order in Enum.GetValues<SavedDraftPageOrder>())
            {
                for (var repeat = 0; repeat < 3; repeat++)
                {
                    SavedDraftPageOrdering.Apply(page, order).Should().Equal(expected[(int)order]);
                    SavedDraftPageOrdering.Apply(Page([]), order).Should().BeEmpty();
                    SavedDraftPageOrdering.Apply(Page([d]), order).Should().Equal(d);
                    SavedDraftPageOrdering.Apply(page with { Records = [], Scanned = 0, AuthorityPending = true }, order)
                        .Should().BeEmpty();
                }
            }
            page.Records.Should().Equal(a, b, c, d, e, f, g);
            page.Next.Should().Be(Cursor);
            page.Scanned.Should().Be(50);
            page.AuditScanned.Should().Be(1);
            page.Validate();
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Fact]
    public void EveryCandidateIsValidatedBeforeOrderingEvenIfItsPositionOrPredicateWouldHideIt()
    {
        var first = Row("00000001", "a", 1);
        var last = Row("ffffffff", null, long.MaxValue);
        foreach (var order in Enum.GetValues<SavedDraftPageOrder>())
        {
            foreach (var invalid in new[]
            {
                Page([first, last with { DraftRevision = 0 }]),
                Page([first, last with { Utf8Bytes = 0 }]),
                Page([first, last with { Metadata = new(first.Session.SessionId, new(1), new("foreign")) }]),
                Page([first, last with { Source = Guid.NewGuid() }]),
                Page([first, first]),
                Page([last, first]),
                Page([first, last]) with { MetadataFilter = new(1, null, null) },
                Page(Enumerable.Repeat(first, SavedDraftPage.MaximumRecords + 1).ToImmutableArray()),
            })
            {
                var act = () => SavedDraftPageOrdering.Apply(invalid, order);
                act.Should().Throw<InvalidDataException>();
            }
            var maximum = Page(Enumerable.Range(1, SavedDraftPage.MaximumRecords)
                .Select(index => Row(index.ToString("x8", CultureInfo.InvariantCulture), null, 1)).ToImmutableArray());
            SavedDraftPageOrdering.Apply(maximum, order).Should().HaveCount(SavedDraftPage.MaximumRecords);
        }
        foreach (var order in new[] { (SavedDraftPageOrder)(-1), (SavedDraftPageOrder)5, (SavedDraftPageOrder)int.MaxValue })
        {
            foreach (var page in new[] { Page([]), Page([first]) })
            {
                var act = () => SavedDraftPageOrdering.Apply(page, order);
                act.Should().Throw<InvalidDataException>().WithMessage("*Unknown*Source order*");
            }
        }
        var missing = () => SavedDraftPageOrdering.Apply(null!);
        missing.Should().Throw<ArgumentNullException>();
    }

    private static readonly string Cursor = Convert.ToBase64String([1]) + "." + new string('a', 64);
    private static SavedDraftPage Page(ImmutableArray<SavedDraftSummary> rows) =>
        new(SessionListFilter.All, rows, Cursor, 50, 1, false);
    private static SavedDraftSummary Row(string prefix, string? name, long revision)
    {
        var id = new Kora.Core.Hosting.HostId<Kora.Core.Hosting.SessionIdentity>(Guid.Parse(prefix + "-0000-0000-0000-000000000001"));
        return new(new("10000000-0000-0000-0000-000000000001"),
            new(new Guid("20000000-0000-0000-0000-000000000001")), new(id, new(1), true),
            name is null ? null : new(id, new(1), new(name)), revision, 4, 1, 1, 1, new string('a', 64));
    }
}
