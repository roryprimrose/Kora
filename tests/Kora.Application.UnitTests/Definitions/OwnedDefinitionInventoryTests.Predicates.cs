using System.Globalization;
using System.Text;

using AwesomeAssertions;

using Kora.Application.Definitions;
using Kora.Core.Artifacts;

namespace Kora.Application.UnitTests.Definitions;

public sealed partial class OwnedDefinitionInventoryTests
{
    private static string Utc(DateTimeOffset value) => value.ToString("O", CultureInfo.InvariantCulture);

    [Theory]
    [InlineData(ArtifactKind.Instruction)]
    [InlineData(ArtifactKind.Prompt)]
    public void ExactRevisionAndInclusiveSnapshotTimeAndEveryExistingPredicateComposeWithoutInferringUnknowns(ArtifactKind kind)
    {
        var inventory = Inventory(kind, ["enabled", "held", "unknown", "unmapped"],
            Candidate(kind, "enabled", enabled: true), Candidate(kind, "held"), Candidate(kind, "unknown", revision: false));
        foreach (var use in Enum.GetValues<OwnedDefinitionUseFilter>())
        {
            var expected = inventory.Page("", use).Rows.Where(row => string.Equals(row.NativeRevision, NativeRevision, StringComparison.Ordinal)).ToArray();
            inventory.Page("", use, revision: NativeRevision).Rows.Should().Equal(expected);
            foreach (var row in expected)
            {
                foreach (var query in new[] { row.CanonicalFileName, row.DisplayName!, row.ArtifactId! })
                {
                    inventory.Page(query, use, digest: row.Digest!, revision: NativeRevision,
                        observedFromUtc: Utc(Observed), observedUntilUtc: Utc(Observed)).Rows.Should().ContainSingle().Which.Should().BeSameAs(row);
                }
            }
            inventory.Page("", use, revision: NativeRevision, observedFromUtc: Utc(Observed.AddTicks(1))).Rows.Should().BeEmpty();
            inventory.Page("", use, revision: NativeRevision, observedUntilUtc: Utc(Observed.AddTicks(-1))).Rows.Should().BeEmpty();
            inventory.Page("", use, revision: NativeRevision, observedFromUtc: Utc(Observed.AddTicks(-1)),
                observedUntilUtc: Utc(Observed.AddTicks(1))).Rows.Should().Equal(expected);
            inventory.Page("missing", use, revision: NativeRevision).Rows.Should().BeEmpty();
            inventory.Page("", use, digest: new string('f', 64), revision: NativeRevision).Rows.Should().BeEmpty();
            inventory.Page("", use, revision: NativeRevision.Replace('b', 'e')).Rows.Should().BeEmpty();
        }
        inventory.Page("", OwnedDefinitionUseFilter.All, observedFromUtc: Utc(Observed), observedUntilUtc: Utc(Observed))
            .Rows.Should().HaveCount(4, "observation time includes unknown/unmapped rows without conferring revision or use authority");
        inventory.Rows.Where(row => row.NativeRevision is null).Select(row => row.Name).Should().Equal("unknown", "unmapped");
        inventory.Page("Private", OwnedDefinitionUseFilter.All, revision: NativeRevision).Rows.Should().BeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    [InlineData("2026-10-12T00:00:00Z")]
    [InlineData("2026-10-12T00:00:00.0000000Z")]
    [InlineData("2026-10-12T01:00:00.0000000+01:00")]
    [InlineData("2026-10-12T00:00:00.0000000+00:00 ")]
    [InlineData("2026-02-30T00:00:00.0000000+00:00")]
    public void InvalidTimesFailBeforeAnEmptyOrExcludingSearch(string? text)
    {
        foreach (var inventory in new[] { Inventory(ArtifactKind.Instruction, []),
            Inventory(ArtifactKind.Instruction, ["enabled"], Candidate(ArtifactKind.Instruction, "enabled", enabled: true)) })
        {
            Action from = () => inventory.Page("missing", OwnedDefinitionUseFilter.ReviewRequired, observedFromUtc: text!);
            Action until = () => inventory.Page("missing", OwnedDefinitionUseFilter.ReviewRequired, observedUntilUtc: text!);
            if (text is null)
            {
                from.Should().Throw<ArgumentNullException>();
                until.Should().Throw<ArgumentNullException>();
            }
            else
            {
                from.Should().Throw<InvalidDataException>();
                until.Should().Throw<InvalidDataException>();
            }
        }
    }

    [Fact]
    public void InvalidRevisionRangeOrCandidateCannotHideBehindAnyPredicateOrOffset()
    {
        var empty = Inventory(ArtifactKind.Instruction, []);
        ((Action)(() => empty.Page("missing", OwnedDefinitionUseFilter.All, revision: null!))).Should().Throw<ArgumentNullException>();
        ((Action)(() => empty.Page("missing", OwnedDefinitionUseFilter.All, revision: " "))).Should().Throw<InvalidDataException>();
        ((Action)(() => empty.Page("missing", OwnedDefinitionUseFilter.All, observedFromUtc: Utc(Observed.AddTicks(1)),
            observedUntilUtc: Utc(Observed)))).Should().Throw<InvalidDataException>();
        ((Action)(() => _ = new OwnedDefinitionInventory(ArtifactKind.Instruction, [], Array.AsReadOnly(Array.Empty<ArtifactDefinition>()),
            0, Observed.ToOffset(TimeSpan.FromHours(1))))).Should().Throw<InvalidDataException>();
        foreach (var invalid in new[] { "", " ", NativeRevision.ToUpperInvariant(), NativeRevision[..^1], NativeRevision + "a",
            new string('a', 64), NativeRevision.Replace('.', '-'), NativeRevision + "\n" })
        {
            var inventory = Inventory(ArtifactKind.Instruction, ["enabled", "held"], Candidate(ArtifactKind.Instruction, "enabled", enabled: true),
                Candidate(ArtifactKind.Instruction, "held") with { OwnedInstructionRevision = invalid });
            foreach (var use in Enum.GetValues<OwnedDefinitionUseFilter>())
            {
                ((Action)(() => inventory.Page("missing", use, 1, new string('f', 64), NativeRevision,
                    Utc(Observed.AddTicks(1)), Utc(Observed.AddTicks(2))))).Should().Throw<InvalidDataException>();
            }
        }
    }

    [Theory]
    [InlineData(ArtifactKind.Instruction)]
    [InlineData(ArtifactKind.Prompt)]
    public void EveryPredicateBindsContinuationAndCompleteUtf8PagesRemainBounded(ArtifactKind kind)
    {
        var names = Enumerable.Range(0, 128).Select(index => index.ToString("D3", CultureInfo.InvariantCulture) + new string('界', 125)).ToArray();
        var candidates = names.Select((name, index) => new ArtifactDefinition("owned." + index.ToString(CultureInfo.InvariantCulture), kind,
            new string('界', 128), "Hidden", "cmd-" + index.ToString(CultureInfo.InvariantCulture), ["name"], "kora-profile", "1", new string('a', 64), "Hidden")
            { OwnedInstructionName = name, OwnedInstructionRevision = NativeRevision, IsUseEnabled = true }).ToArray();
        var inventory = Inventory(kind, names, candidates);
        var first = inventory.Page("界", OwnedDefinitionUseFilter.UseEnabled, digest: new string('a', 64), revision: NativeRevision,
            observedFromUtc: Utc(Observed), observedUntilUtc: Utc(Observed));
        first.NextOffset.Should().NotBeNull();
        foreach (var change in new[]
        {
            (Revision: "", From: Utc(Observed), Until: Utc(Observed)),
            (Revision: NativeRevision.Replace('b', 'e'), From: Utc(Observed), Until: Utc(Observed)),
            (Revision: NativeRevision, From: "", Until: Utc(Observed)),
            (Revision: NativeRevision, From: Utc(Observed.AddTicks(-1)), Until: Utc(Observed)),
            (Revision: NativeRevision, From: Utc(Observed), Until: ""),
            (Revision: NativeRevision, From: Utc(Observed), Until: Utc(Observed.AddTicks(1))),
        })
        {
            ((Action)(() => inventory.Continue(first, "界", OwnedDefinitionUseFilter.UseEnabled, new string('a', 64),
                change.Revision, change.From, change.Until))).Should().Throw<InvalidOperationException>();
        }
        var page = first;
        var seen = new List<OwnedDefinitionInventoryRow>();
        while (true)
        {
            page.Rows.Should().NotBeEmpty();
            var measured = page.Rows.Sum(row => Encoding.UTF8.GetByteCount(row.ToString()) + Encoding.UTF8.GetByteCount(row.Details));
            page.DisplayUtf8Bytes.Should().Be(measured).And.BeLessThanOrEqualTo(64 * 1024);
            seen.AddRange(page.Rows);
            if (page.NextOffset is null) { break; }
            page = inventory.Continue(page, "界", OwnedDefinitionUseFilter.UseEnabled, new string('a', 64), NativeRevision, Utc(Observed), Utc(Observed));
        }
        seen.Should().Equal(inventory.Rows);
    }
}
