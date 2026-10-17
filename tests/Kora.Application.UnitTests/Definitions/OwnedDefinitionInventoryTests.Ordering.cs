using System.Globalization;
using System.Text;

using AwesomeAssertions;

using Kora.Application.Definitions;
using Kora.Core.Artifacts;

namespace Kora.Application.UnitTests.Definitions;

public sealed partial class OwnedDefinitionInventoryTests
{
    [Theory]
    [InlineData(ArtifactKind.Instruction)]
    [InlineData(ArtifactKind.Prompt)]
    public void ExplicitOrderUsesOrdinalMetadataWithAbsentNamesLastAndAscendingCanonicalTies(ArtifactKind kind)
    {
        var inventory = Inventory(kind, ["z", "a", "A", "file-2", "file-10", "é", "e\u0301", "界"],
            Candidate(kind, "z", displayName: "same"),
            Candidate(kind, "a", displayName: "same"),
            Candidate(kind, "upper", displayName: "Z") with { OwnedInstructionName = "A" },
            Candidate(kind, "file-2", displayName: "é"),
            Candidate(kind, "file-10", displayName: "e\u0301"));
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            foreach (var culture in new[] { "en-US", "tr-TR", "sv-SE" })
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
                inventory.Page("", OwnedDefinitionUseFilter.All).Rows.Should().Equal(inventory.Rows);
                inventory.Page("", OwnedDefinitionUseFilter.All, order: OwnedDefinitionInventoryOrder.FileNameAscending)
                    .Rows.Select(row => row.Name).Should().Equal("A", "a", "e\u0301", "file-10", "file-2", "z", "é", "界");
                inventory.Page("", OwnedDefinitionUseFilter.All, order: OwnedDefinitionInventoryOrder.FileNameDescending)
                    .Rows.Select(row => row.Name).Should().Equal("界", "é", "z", "file-2", "file-10", "e\u0301", "a", "A");
                inventory.Page("", OwnedDefinitionUseFilter.All, order: OwnedDefinitionInventoryOrder.DisplayNameAscending)
                    .Rows.Select(row => row.Name).Should().Equal("A", "file-10", "a", "z", "file-2", "e\u0301", "é", "界");
                inventory.Page("", OwnedDefinitionUseFilter.All, order: OwnedDefinitionInventoryOrder.DisplayNameDescending)
                    .Rows.Select(row => row.Name).Should().Equal("file-2", "a", "z", "file-10", "A", "e\u0301", "é", "界");
            }
        }
        finally { CultureInfo.CurrentCulture = originalCulture; }
        inventory.Rows.Select(row => row.Name).Should().Equal("A", "a", "e\u0301", "file-10", "file-2", "z", "é", "界");
    }

    [Theory]
    [InlineData(ArtifactKind.Instruction)]
    [InlineData(ArtifactKind.Prompt)]
    public void FilenameOrderAndDisplayTiesPreserveExtensionlessIdentityPrefixCompatibility(ArtifactKind kind)
    {
        var inventory = Inventory(kind, ["alpha-more", "alpha", "Alpha"],
            Candidate(kind, "alpha-more", displayName: "Same"), Candidate(kind, "alpha", displayName: "Same"));
        inventory.Page("", OwnedDefinitionUseFilter.All).Rows.Should().Equal(inventory.Rows);
        inventory.Page("", OwnedDefinitionUseFilter.All, order: OwnedDefinitionInventoryOrder.FileNameAscending)
            .Rows.Select(row => row.Name).Should().Equal("Alpha", "alpha", "alpha-more");
        inventory.Page("", OwnedDefinitionUseFilter.All, order: OwnedDefinitionInventoryOrder.FileNameDescending)
            .Rows.Select(row => row.Name).Should().Equal("alpha-more", "alpha", "Alpha");
        foreach (var order in new[] { OwnedDefinitionInventoryOrder.DisplayNameAscending, OwnedDefinitionInventoryOrder.DisplayNameDescending })
        {
            inventory.Page("", OwnedDefinitionUseFilter.All, order: order).Rows.Select(row => row.Name)
                .Should().Equal("alpha", "alpha-more", "Alpha");
        }
    }

    [Theory]
    [InlineData(ArtifactKind.Instruction)]
    [InlineData(ArtifactKind.Prompt)]
    public void EveryOrderAndModeAndOptionalPredicateCombinationComposesWithEveryUseState(ArtifactKind kind)
    {
        var inventory = Inventory(kind, ["enabled", "held", "unknown", "unmapped"],
            Candidate(kind, "enabled", enabled: true, displayName: "Z"),
            Candidate(kind, "held", displayName: "A"), Candidate(kind, "unknown", revision: false));
        foreach (var order in Enum.GetValues<OwnedDefinitionInventoryOrder>())
        foreach (var mode in Enum.GetValues<OwnedDefinitionInventoryQueryMode>())
        foreach (var use in Enum.GetValues<OwnedDefinitionUseFilter>())
        foreach (var bits in Enumerable.Range(0, 16))
        {
            var query = mode switch
            {
                OwnedDefinitionInventoryQueryMode.Literal => "",
                OwnedDefinitionInventoryQueryMode.ExactOwnedId => "enabled",
                _ => "owned.enabled",
            };
            var digest = (bits & 1) == 0 ? "" : new string('a', 64);
            var revision = (bits & 2) == 0 ? "" : NativeRevision;
            var from = (bits & 4) == 0 ? "" : Utc(Observed);
            var until = (bits & 8) == 0 ? "" : Utc(Observed);
            var canonical = inventory.Page(query, use, digest: digest, revision: revision,
                observedFromUtc: from, observedUntilUtc: until, mode: mode).Rows;
            var ordered = inventory.Page(query, use, digest: digest, revision: revision,
                observedFromUtc: from, observedUntilUtc: until, mode: mode, order: order);
            ordered.Rows.Should().BeEquivalentTo(canonical);
            ordered.Rows.Should().Equal(inventory.Page("", OwnedDefinitionUseFilter.All, order: order).Rows.Where(canonical.Contains));
            inventory.Page(query, use, digest: new string('f', 64), revision: revision,
                observedFromUtc: from, observedUntilUtc: until, mode: mode, order: order).Rows.Should().BeEmpty();
            inventory.Page(query, use, revision: NativeRevision.Replace('b', 'e'), mode: mode, order: order).Rows.Should().BeEmpty();
            inventory.Page(query, use, observedFromUtc: Utc(Observed.AddTicks(1)), mode: mode, order: order).Rows.Should().BeEmpty();
            inventory.Page(query, use, observedUntilUtc: Utc(Observed.AddTicks(-1)), mode: mode, order: order).Rows.Should().BeEmpty();
        }
    }

    [Theory]
    [InlineData(ArtifactKind.Instruction)]
    [InlineData(ArtifactKind.Prompt)]
    public void OrderingPrecedesCompleteByteBudgetPagesAndBindsEveryContinuationInput(ArtifactKind kind)
    {
        var names = Enumerable.Range(0, 128).Select(index => index.ToString("D3", CultureInfo.InvariantCulture) + new string('界', 125)).ToArray();
        var candidates = names.Select((name, index) => new ArtifactDefinition("owned." + index.ToString(CultureInfo.InvariantCulture),
            kind, index < 64 ? "Same" : "Other", "Private", "cmd-" + index.ToString(CultureInfo.InvariantCulture),
            ["spoken"], "kora-profile", "1", new string('a', 64), "Private")
            { OwnedInstructionName = name, OwnedInstructionRevision = NativeRevision, IsUseEnabled = true }).ToArray();
        var inventory = Inventory(kind, names, candidates);
        foreach (var order in Enum.GetValues<OwnedDefinitionInventoryOrder>())
        {
            var canonical = inventory.Rows;
            var expected = order switch
            {
                OwnedDefinitionInventoryOrder.FileNameAscending => canonical.ToArray(),
                OwnedDefinitionInventoryOrder.FileNameDescending => canonical.Reverse().ToArray(),
                OwnedDefinitionInventoryOrder.DisplayNameAscending => canonical.Skip(64).Concat(canonical.Take(64)).ToArray(),
                _ => canonical.ToArray(),
            };
            OwnedDefinitionInventoryPage Start() => inventory.Page("界", OwnedDefinitionUseFilter.UseEnabled, digest: new string('a', 64),
                revision: NativeRevision, observedFromUtc: Utc(Observed), observedUntilUtc: Utc(Observed), order: order);
            var page = Start();
            page.NextOffset.Should().NotBeNull();
            foreach (var otherOrder in Enum.GetValues<OwnedDefinitionInventoryOrder>().Where(value => value != order))
            {
                ((Action)(() => inventory.Continue(page, "界", OwnedDefinitionUseFilter.UseEnabled, new string('a', 64),
                    NativeRevision, Utc(Observed), Utc(Observed), order: otherOrder))).Should().Throw<InvalidOperationException>();
            }
            foreach (var change in Enumerable.Range(0, 8))
            {
                ((Action)(() => (change == 0 ? Inventory(kind, names, candidates) : inventory).Continue(page,
                    change == 1 ? "changed" : "界", change == 2 ? OwnedDefinitionUseFilter.All : OwnedDefinitionUseFilter.UseEnabled,
                    change == 3 ? "" : new string('a', 64), change == 4 ? "" : NativeRevision,
                    change == 5 ? "" : Utc(Observed), change == 6 ? "" : Utc(Observed),
                    change == 7 ? OwnedDefinitionInventoryQueryMode.ExactOwnedId : OwnedDefinitionInventoryQueryMode.Literal, order)))
                    .Should().Throw<InvalidOperationException>();
            }
            var seen = new List<OwnedDefinitionInventoryRow>();
            while (true)
            {
                page.Rows.Should().NotBeEmpty();
                page.Rows.Should().Equal(expected.Skip(page.Offset).Take(page.Rows.Count));
                var measured = page.Rows.Sum(row => Encoding.UTF8.GetByteCount(row.Details) + Encoding.UTF8.GetByteCount(row.ToString()));
                page.DisplayUtf8Bytes.Should().Be(measured);
                measured.Should().BeLessThanOrEqualTo(64 * 1024);
                seen.AddRange(page.Rows);
                if (page.NextOffset is not { } next) { break; }
                var omitted = expected[next];
                (measured + Encoding.UTF8.GetByteCount(omitted.Details) + Encoding.UTF8.GetByteCount(omitted.ToString()))
                    .Should().BeGreaterThan(64 * 1024);
                page = inventory.Continue(page, "界", OwnedDefinitionUseFilter.UseEnabled, new string('a', 64),
                    NativeRevision, Utc(Observed), Utc(Observed), order: order);
            }
            seen.Should().Equal(expected);
            inventory.Rows.Should().Equal(canonical);
            ((Action)(() => inventory.Continue(page, "界", OwnedDefinitionUseFilter.UseEnabled, new string('a', 64),
                NativeRevision, Utc(Observed), Utc(Observed), order: order))).Should().Throw<InvalidOperationException>();
        }
    }

    [Fact]
    public void UndefinedOrderAndInvalidRowsFailBeforeExclusionOrOffsetInEveryOrderAndMode()
    {
        ((Action)(() => Inventory(ArtifactKind.Instruction, []).Page("missing", OwnedDefinitionUseFilter.All,
            order: (OwnedDefinitionInventoryOrder)99))).Should().Throw<ArgumentOutOfRangeException>();
        foreach (var order in Enum.GetValues<OwnedDefinitionInventoryOrder>())
        foreach (var mode in Enum.GetValues<OwnedDefinitionInventoryQueryMode>())
        foreach (var invalid in new[] { "invalid", new string('a', 64 * 1024), "\ud800" })
        {
            var names = Enumerable.Range(0, 128).Select(index => "file-" + index.ToString("D3", CultureInfo.InvariantCulture)).ToArray();
            var candidates = names.Select(name => Candidate(ArtifactKind.Instruction, name)).ToArray();
            candidates[^1] = candidates[^1] with { OwnedInstructionRevision = invalid };
            var inventory = Inventory(ArtifactKind.Instruction, names, candidates);
            Action read = () => inventory.Page("missing", OwnedDefinitionUseFilter.UseEnabled, 1, new string('f', 64),
                NativeRevision, Utc(Observed.AddTicks(1)), Utc(Observed.AddTicks(2)), mode, order);
            if (invalid is "\ud800") { read.Should().Throw<EncoderFallbackException>(); }
            else { read.Should().Throw<InvalidDataException>(); }
        }
    }
}
