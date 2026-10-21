using System.Collections.Immutable;
using System.Globalization;

using AwesomeAssertions;

using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Core.UnitTests.Storage;

public sealed class SessionListPageOrderingTests
{
    private static SessionWorkspaceEntry Row(int id, string? name, long generation = 1)
    {
        var identity = new HostId<SessionIdentity>(Guid.Parse(
            "00000000-0000-0000-0000-" + id.ToString("d12", CultureInfo.InvariantCulture)));
        return new(new(identity, new(generation), true),
            name is null ? null : new(identity, new(1), new(name)));
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("tr-TR")]
    [InlineData("sv-SE")]
    public void EveryOrderUsesOrdinalNamesUnnamedLastAndCanonicalAscendingTies(string culture)
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
        try
        {
            var a = Row(2, "I", 2);
            var b = Row(1, "I", 2);
            var c = Row(3, "i", 1);
            var d = Row(5, null, 3);
            var e = Row(4, null, 3);
            var f = Row(6, "ı", long.MaxValue);
            ImmutableArray<SessionWorkspaceEntry> source = [d, a, f, c, e, b];
            SessionListPageOrdering.Apply(source, SessionListPageOrder.Source).Should().Equal(source);
            SessionListPageOrdering.Apply(source, SessionListPageOrder.Source).Equals(source).Should().BeTrue();
            AssertOrder(SessionListPageOrder.NameAscending, [b, a, c, f, e, d]);
            AssertOrder(SessionListPageOrder.NameDescending, [f, c, b, a, e, d]);
            AssertOrder(SessionListPageOrder.GenerationAscending, [c, b, a, e, d, f]);
            AssertOrder(SessionListPageOrder.GenerationDescending, [f, e, d, b, a, c]);
            source.Should().Equal(d, a, f, c, e, b);

            void AssertOrder(SessionListPageOrder order, SessionWorkspaceEntry[] expected)
            {
                var result = SessionListPageOrdering.Apply(source, order);
                result.Should().Equal(expected);
                for (var i = 0; i < result.Length; i++)
                { result[i].Should().BeSameAs(expected[i]); }
            }
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Fact]
    public void EmptyAndExactMaximumPagesAreValidForEveryOrder()
    {
        var page = Enumerable.Range(1, SessionPage<SessionWorkspaceEntry>.MaximumRecords)
            .Select(id => Row(id, id % 2 == 0 ? null : "name")).ToImmutableArray();
        foreach (var order in Enum.GetValues<SessionListPageOrder>())
        {
            SessionListPageOrdering.Apply([], order).Should().BeEmpty();
            SessionListPageOrdering.Apply(page, order).Should().HaveCount(page.Length);
        }
    }

    [Fact]
    public void DefaultOversizeDuplicateAndUnknownOrderRejectBeforePublication()
    {
        var row = Row(1, "name");
        var invalid = new[]
        {
            default(ImmutableArray<SessionWorkspaceEntry>),
            Enumerable.Range(1, SessionPage<SessionWorkspaceEntry>.MaximumRecords + 1)
                .Select(id => Row(id, null)).ToImmutableArray(),
            ImmutableArray.Create(row, row),
        };
        foreach (var page in invalid)
        foreach (var order in Enum.GetValues<SessionListPageOrder>())
        {
            var apply = () => SessionListPageOrdering.Apply(page, order);
            apply.Should().Throw<InvalidDataException>();
        }
        var unknown = () => SessionListPageOrdering.Apply([row], (SessionListPageOrder)999);
        unknown.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void CorruptionInLastUnnamedOrNamedRowRejectsTheWholePageEvenForSource()
    {
        var valid = Row(1, "valid");
        var other = Row(2, null);
        SessionWorkspaceEntry[] invalid =
        [
            null!,
            new(null!, null),
            new(new(default, new(1), true), null),
            new(other.Authority with { Generation = default }, null),
            new(other.Authority, new(valid.Authority.SessionId, new(1), new("name"))),
            new(other.Authority, new(other.Authority.SessionId, default, new("name"))),
            new(other.Authority, new(other.Authority.SessionId, new(1), null!)),
        ];
        foreach (var row in invalid)
        foreach (var order in Enum.GetValues<SessionListPageOrder>())
        {
            var apply = () => SessionListPageOrdering.Apply([valid, row], order);
            apply.Should().Throw<Exception>();
        }
    }
}
