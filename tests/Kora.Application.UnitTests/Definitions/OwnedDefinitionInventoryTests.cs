using System.Text;

using AwesomeAssertions;

using Kora.Application.Definitions;
using Kora.Core.Artifacts;

namespace Kora.Application.UnitTests.Definitions;

public sealed partial class OwnedDefinitionInventoryTests
{
    [Fact]
    public void InventoryRejectsNonUtcObservationAndUndefinedOrderBeforeExcludingRows()
    {
        var nonUtc = () => new OwnedDefinitionInventory(ArtifactKind.Instruction, [],
            Array.AsReadOnly(Array.Empty<ArtifactDefinition>()), 0, Observed.ToOffset(TimeSpan.FromHours(1)));
        nonUtc.Should().Throw<InvalidDataException>();
        var inventory = Inventory(ArtifactKind.Instruction, ["example"], Candidate(ArtifactKind.Instruction, "example"));
        var invalidOrder = () => inventory.Page("missing", OwnedDefinitionUseFilter.All, order: (OwnedDefinitionInventoryOrder)99);
        invalidOrder.Should().Throw<ArgumentOutOfRangeException>();
    }
    private static readonly DateTimeOffset Observed = new(2026, 10, 12, 0, 0, 0, TimeSpan.Zero);
    private static readonly string NativeRevision = new string('a', 64) + "." + new string('b', 48) + "." + new string('c', 48) + "." + new string('d', 64);

    private static ArtifactDefinition Candidate(ArtifactKind kind, string name, bool enabled = false, bool revision = true, string? displayName = null) =>
        new("owned." + name, kind, displayName ?? "Display " + name, "Private description must not project", "cmd-" + name,
            ["spoken " + name], "kora-profile", "1", new string('a', 64), "Private body must not project")
        { OwnedInstructionName = name, OwnedInstructionRevision = revision ? NativeRevision : null,
            IsUseEnabled = enabled };

    private static OwnedDefinitionInventory Inventory(ArtifactKind kind, IReadOnlyList<string> names,
        params ArtifactDefinition[] candidates) => new(kind, names, Array.AsReadOnly(candidates), 0, Observed);

    [Theory]
    [InlineData(ArtifactKind.Instruction)]
    [InlineData(ArtifactKind.Prompt)]
    public void CompleteValidationPrecedesEveryExcludingPredicateAndOffset(ArtifactKind kind)
    {
        var oversized = Inventory(kind, ["example"], Candidate(kind, "example")
            with { OwnedInstructionRevision = new string('a', 64 * 1024) });
        foreach (var filter in Enum.GetValues<OwnedDefinitionUseFilter>())
        {
            ((Action)(() => oversized.Page("missing", filter))).Should().Throw<InvalidDataException>();
        }
        var invalidUnicode = Inventory(kind, ["example"], Candidate(kind, "example") with { OwnedInstructionRevision = "\ud800" });
        ((Action)(() => invalidUnicode.Page("missing", OwnedDefinitionUseFilter.UseEnabled)))
            .Should().Throw<EncoderFallbackException>();
        ((Action)(() => Inventory(kind, ["../invalid"], Candidate(kind, "example")))).Should().Throw<InvalidDataException>();
        ((Action)(() => Inventory(kind, ["example"], Candidate(kind, "example"), Candidate(kind, "example"))))
            .Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData(ArtifactKind.Instruction)]
    [InlineData(ArtifactKind.Prompt)]
    public void FiltersOnlyObservedExactOwnedKindAndSourceWithoutExposingContent(ArtifactKind kind)
    {
        var enabled = Candidate(kind, "enabled", enabled: true);
        var held = Candidate(kind, "held");
        var unknown = Candidate(kind, "unknown", enabled: true, revision: false);
        var otherKind = kind == ArtifactKind.Instruction ? ArtifactKind.Prompt : ArtifactKind.Instruction;
        // A same-kind profile candidate must never map to a host-owned filename.
        var inventory = Inventory(kind, ["unmapped", "unknown", "profile", "held", "enabled", "other"],
            enabled, held, unknown,
            new ArtifactDefinition("profile.test", kind, "Profile", "Private", "profile", ["profile"], "vscode-profile", "1", new string('b', 64), "Private")
                { OwnedInstructionName = "profile", OwnedInstructionRevision = held.OwnedInstructionRevision },
            Candidate(otherKind, "other"));
        inventory.Kind.Should().Be(kind);
        inventory.Rows.Select(row => row.Name).Should().Equal("enabled", "held", "other", "profile", "unknown", "unmapped");
        inventory.Page("", OwnedDefinitionUseFilter.All).Rows.Should().HaveCount(6);
        inventory.Page("", OwnedDefinitionUseFilter.UseEnabled).Rows.Should().ContainSingle().Which.Name.Should().Be("enabled");
        inventory.Page("", OwnedDefinitionUseFilter.ReviewRequired).Rows.Should().ContainSingle().Which.Name.Should().Be("held");
        inventory.Page("", OwnedDefinitionUseFilter.UnavailableOrUnknown).Rows.Select(row => row.Name)
            .Should().Equal("other", "profile", "unknown", "unmapped");
        inventory.Page("not-found", OwnedDefinitionUseFilter.All).Rows.Should().BeEmpty();
        foreach (var row in inventory.Rows)
        {
            row.Kind.Should().Be(kind);
            row.Source.Should().Be("kora-profile");
            row.CanonicalFileName.Should().Be(row.Name + OwnedDefinitionKind.Extension(kind));
            row.ObservedUtc.Should().Be(Observed);
            row.Details.Should().NotContain("Private").And.NotContain("description").And.NotContain("body");
        }
        inventory.Rows[0].ArtifactId.Should().Be(enabled.Id);
        inventory.Rows[0].Digest.Should().Be(enabled.Digest);
        inventory.Rows[0].NativeRevision.Should().Be(enabled.OwnedInstructionRevision);
        inventory.Rows[0].DisplayName.Should().Be(enabled.Name);
        inventory.Rows[0].UseState.Should().Be("Use enabled (observed)");
        inventory.Rows[1].UseState.Should().Be("Review required (observed)");
        inventory.Rows[^1].UseState.Should().Be("Unavailable or unknown");
    }

    [Theory]
    [InlineData(ArtifactKind.Instruction, 0)]
    [InlineData(ArtifactKind.Instruction, 1)]
    [InlineData(ArtifactKind.Instruction, 128)]
    [InlineData(ArtifactKind.Prompt, 0)]
    [InlineData(ArtifactKind.Prompt, 1)]
    [InlineData(ArtifactKind.Prompt, 128)]
    public void EmptyDefaultAndLiteralSearchPreserveBoundedOrdinalFilenameOrder(ArtifactKind kind, int count)
    {
        var names = Enumerable.Range(0, count).Select(index => "file-" + index.ToString("D3", System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        var inventory = Inventory(kind, names.Reverse().ToArray(), names.Select(name => Candidate(kind, name)).ToArray());
        var page = inventory.Page("", OwnedDefinitionUseFilter.All);
        page.TotalMatches.Should().Be(count);
        page.Rows.Select(row => row.Name).Should().Equal(names.Take(page.Rows.Count));
        if (page.Rows.Count == count) { page.NextOffset.Should().BeNull(); }
        else { page.NextOffset.Should().Be(page.Rows.Count); }
        page.Offset.Should().Be(0);
        page.DisplayUtf8Bytes.Should().Be(page.Rows.Sum(row => Encoding.UTF8.GetByteCount(row.ToString()) + Encoding.UTF8.GetByteCount(row.Details)));
        page.DisplayUtf8Bytes.Should().BeLessThanOrEqualTo(OwnedDefinitionInventory.MaximumDisplayUtf8Bytes);
        inventory.Page("DOES-NOT-EXIST", OwnedDefinitionUseFilter.All).TotalMatches.Should().Be(0);
        inventory.Page("file-", OwnedDefinitionUseFilter.All).TotalMatches.Should().Be(count);
        if (count > 0)
        {
            inventory.Page("OWNED.FILE-000", OwnedDefinitionUseFilter.All).Rows.Should().ContainSingle();
            inventory.Page("Display file-000", OwnedDefinitionUseFilter.All).Rows.Should().ContainSingle();
            inventory.Page("file-000" + OwnedDefinitionKind.Extension(kind), OwnedDefinitionUseFilter.All).Rows.Should().ContainSingle();
            inventory.Page("*", OwnedDefinitionUseFilter.All).Rows.Should().BeEmpty();
            inventory.Page("../file-000", OwnedDefinitionUseFilter.All).Rows.Should().BeEmpty();
            inventory.Page(" file-000 ", OwnedDefinitionUseFilter.All).Rows.Should().BeEmpty();
        }
    }

    [Theory]
    [InlineData(ArtifactKind.Instruction)]
    [InlineData(ArtifactKind.Prompt)]
    public void WorstCaseCompleteMetadataUsesExplicitDeterministicPagesRatherThanTruncation(ArtifactKind kind)
    {
        var names = Enumerable.Range(0, 128).Select(index => index.ToString("D3", System.Globalization.CultureInfo.InvariantCulture) + new string('界', 125)).ToArray();
        var candidates = names.Select((name, index) => new ArtifactDefinition(new string('a', 125) + index.ToString("D3", System.Globalization.CultureInfo.InvariantCulture),
            kind, new string('界', 128), "Private", "cmd-" + index.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["spoken " + index.ToString(System.Globalization.CultureInfo.InvariantCulture)], "kora-profile", "1", new string('a', 64), "Private")
            { OwnedInstructionName = name, OwnedInstructionRevision = NativeRevision }).ToArray();
        var inventory = Inventory(kind, names, candidates);
        var seen = new List<OwnedDefinitionInventoryRow>();
        var page = inventory.Page("", OwnedDefinitionUseFilter.All);
        var exactFirst = inventory.Page("", OwnedDefinitionUseFilter.All);
        exactFirst.Rows.Should().Equal(page.Rows);
        exactFirst.TotalMatches.Should().Be(128);
        inventory.Continue(exactFirst, "", OwnedDefinitionUseFilter.All).Offset.Should().Be(exactFirst.NextOffset!.Value);
        ((Action)(() => inventory.Continue(exactFirst, "changed", OwnedDefinitionUseFilter.All))).Should().Throw<InvalidOperationException>();
        ((Action)(() => inventory.Continue(exactFirst, "", OwnedDefinitionUseFilter.ReviewRequired))).Should().Throw<InvalidOperationException>();
        ((Action)(() => Inventory(kind, names, candidates).Continue(exactFirst, "", OwnedDefinitionUseFilter.All))).Should().Throw<InvalidOperationException>();
        ((Action)(() => inventory.Continue(null!, "", OwnedDefinitionUseFilter.All))).Should().Throw<ArgumentNullException>();
        page.NextOffset.Should().NotBeNull();
        while (true)
        {
            page.Rows.Should().NotBeEmpty();
            var measured = page.Rows.Sum(row => Encoding.UTF8.GetByteCount(row.Details) + Encoding.UTF8.GetByteCount(row.ToString()));
            page.DisplayUtf8Bytes.Should().Be(measured);
            measured.Should().BeLessThanOrEqualTo(64 * 1024);
            seen.AddRange(page.Rows);
            if (page.NextOffset is not { } next) { break; }
            var firstOmitted = inventory.Rows[next];
            (measured + Encoding.UTF8.GetByteCount(firstOmitted.Details) + Encoding.UTF8.GetByteCount(firstOmitted.ToString()))
                .Should().BeGreaterThan(64 * 1024);
            page = inventory.Page("", OwnedDefinitionUseFilter.All, next);
        }
        seen.Should().Equal(inventory.Rows);
        inventory.Page("cmd-does-not-search-commands", OwnedDefinitionUseFilter.All).Rows.Should().BeEmpty();
        inventory.Page("", OwnedDefinitionUseFilter.All, 128).Rows.Should().BeEmpty();
        var last = inventory.Page("", OwnedDefinitionUseFilter.All, 128);
        ((Action)(() => inventory.Continue(last, "", OwnedDefinitionUseFilter.All))).Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("literal*?[]+../")]
    [InlineData(" with spaces ")]
    [InlineData("é漢字")]
    public void AcceptsUnmodifiedLiteralQueries(string query)
    {
        ((Action)(() => OwnedDefinitionInventory.ValidateQuery(query))).Should().NotThrow();
        OwnedDefinitionInventory.ValidateQuery(new string('a', 256));
        OwnedDefinitionInventory.ValidateQuery(new string('é', 256));
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\0")]
    [InlineData("\u200b")]
    [InlineData("\u2028")]
    [InlineData("\u2029")]
    public void RejectsInvalidUnicodeControlsAndOversizeQueriesWithoutTrimming(string query)
    {
        ((Action)(() => OwnedDefinitionInventory.ValidateQuery(query))).Should().Throw<InvalidDataException>();
        ((Action)(() => OwnedDefinitionInventory.ValidateQuery(new string('a', 257)))).Should().Throw<InvalidDataException>();
        ((Action)(() => OwnedDefinitionInventory.ValidateQuery(new string('界', 171)))).Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void InvalidInventoryQueryFilterOffsetAndIndividualProjectionAreExplicitFailures()
    {
        ((Action)(() => Inventory(ArtifactKind.Skill, []))).Should().Throw<InvalidDataException>();
        ((Action)(() => Inventory(ArtifactKind.Instruction, ["same", "same"]))).Should().Throw<InvalidDataException>();
        ((Action)(() => Inventory(ArtifactKind.Prompt, Enumerable.Range(0, 129).Select(index => index.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToArray())))
            .Should().Throw<InvalidDataException>();
        ((Action)(() => Inventory(ArtifactKind.Instruction, ["../path"]))).Should().Throw<InvalidDataException>();
        ((Action)(() => OwnedDefinitionInventory.ValidateQuery(null!))).Should().Throw<ArgumentNullException>();
        ((Action)(() => OwnedDefinitionInventory.ValidateQuery(new string('\ud800', 1)))).Should().Throw<InvalidDataException>();
        var inventory = Inventory(ArtifactKind.Instruction, ["example"], Candidate(ArtifactKind.Instruction, "example"));
        ((Action)(() => inventory.Page("", (OwnedDefinitionUseFilter)99))).Should().Throw<ArgumentOutOfRangeException>();
        ((Action)(() => inventory.Page("", OwnedDefinitionUseFilter.All, -1))).Should().Throw<ArgumentOutOfRangeException>();
        ((Action)(() => inventory.Page("", OwnedDefinitionUseFilter.All, 2))).Should().Throw<ArgumentOutOfRangeException>();
        var oversized = Inventory(ArtifactKind.Instruction, ["example"],
            Candidate(ArtifactKind.Instruction, "example") with { OwnedInstructionRevision = new string('a', 64 * 1024) });
        ((Action)(() => oversized.Page("", OwnedDefinitionUseFilter.All))).Should().Throw<InvalidDataException>();
    }
}
