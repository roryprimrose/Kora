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
    public void ExactModesUseOnlyAuthoritativeObservedIdentitiesAndComposeEveryPredicate(ArtifactKind kind)
    {
        var inventory = Inventory(kind, ["alpha", "alpha-more", "unknown", "unmapped", "Alpha", "é", "con"],
            Candidate(kind, "alpha", enabled: true), Candidate(kind, "alpha-more"),
            Candidate(kind, "unknown", revision: false));
        var expected = inventory.Rows.Single(row => string.Equals(row.Name, "alpha", StringComparison.Ordinal));
        foreach (var mode in Enum.GetValues<OwnedDefinitionInventoryQueryMode>())
        {
            var query = mode switch
            {
                OwnedDefinitionInventoryQueryMode.Literal => expected.CanonicalFileName,
                OwnedDefinitionInventoryQueryMode.ExactOwnedId => "alpha",
                _ => expected.ArtifactId!,
            };
            foreach (var use in Enum.GetValues<OwnedDefinitionUseFilter>())
            {
                var page = inventory.Page(query, use, mode: mode);
                var upper = inventory.Rows.Single(row => string.Equals(row.Name, "Alpha", StringComparison.Ordinal));
                OwnedDefinitionInventoryRow[] matching = (mode, use) switch
                {
                    (OwnedDefinitionInventoryQueryMode.Literal, OwnedDefinitionUseFilter.All) => [upper, expected],
                    (OwnedDefinitionInventoryQueryMode.Literal, OwnedDefinitionUseFilter.UnavailableOrUnknown) => [upper],
                    (_, OwnedDefinitionUseFilter.All or OwnedDefinitionUseFilter.UseEnabled) => [expected],
                    _ => [],
                };
                page.Rows.Should().Equal(matching);
            }
        }
        inventory.Page("alpha", OwnedDefinitionUseFilter.All).Rows.Select(row => row.Name).Should().Equal("Alpha", "alpha", "alpha-more");
        inventory.Page("alpha", OwnedDefinitionUseFilter.All, mode: OwnedDefinitionInventoryQueryMode.ExactOwnedId)
            .Rows.Should().ContainSingle().Which.Should().BeSameAs(expected);
        inventory.Page("unmapped", OwnedDefinitionUseFilter.All, mode: OwnedDefinitionInventoryQueryMode.ExactOwnedId).Rows.Should().ContainSingle();
        inventory.Page("owned.unknown", OwnedDefinitionUseFilter.All, mode: OwnedDefinitionInventoryQueryMode.ExactArtifactId).Rows.Should().BeEmpty();
        inventory.Page("owned.unmapped", OwnedDefinitionUseFilter.All, mode: OwnedDefinitionInventoryQueryMode.ExactArtifactId).Rows.Should().BeEmpty();
        inventory.Page("alpha", OwnedDefinitionUseFilter.All, mode: OwnedDefinitionInventoryQueryMode.ExactArtifactId).Rows.Should().BeEmpty();
        inventory.Page("owned.alpha", OwnedDefinitionUseFilter.All, mode: OwnedDefinitionInventoryQueryMode.ExactArtifactId)
            .Rows.Should().ContainSingle().Which.Should().BeSameAs(expected);
        inventory.Page("é", OwnedDefinitionUseFilter.All).Rows.Should().ContainSingle();
        inventory.Page("display alpha", OwnedDefinitionUseFilter.All).Rows.Should().HaveCount(2);
        inventory.Page("private", OwnedDefinitionUseFilter.All).Rows.Should().BeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("Alpha")]
    [InlineData("é")]
    [InlineData("e\u0301")]
    [InlineData("alpha.instructions.md")]
    [InlineData("alpha.prompt.md")]
    [InlineData("../alpha")]
    [InlineData("alpha/child")]
    [InlineData("alpha\\child")]
    [InlineData("alpha ")]
    [InlineData("con")]
    [InlineData("com1")]
    [InlineData("-alpha")]
    [InlineData("alpha-")]
    [InlineData("display alpha")]
    public void ExactOwnedIdRejectsNoncanonicalNamesEvenForEmptyInventory(string query)
    {
        ((Action)(() => Inventory(ArtifactKind.Instruction, []).Page(query, OwnedDefinitionUseFilter.All,
            mode: OwnedDefinitionInventoryQueryMode.ExactOwnedId))).Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("OWNED.alpha")]
    [InlineData("é")]
    [InlineData("../alpha")]
    [InlineData("owned.alpha ")]
    [InlineData("display alpha")]
    public void ExactArtifactIdReusesArtifactIdentityAdmissionWithoutTrimmingOrCaseFolding(string query)
    {
        ((Action)(() => Inventory(ArtifactKind.Prompt, []).Page(query, OwnedDefinitionUseFilter.All,
            mode: OwnedDefinitionInventoryQueryMode.ExactArtifactId))).Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ExactModeBoundsAndCompleteValidationPrecedeExclusionAndBindContinuation()
    {
        OwnedDefinitionInventory.ValidateQuery(new string('a', 64), OwnedDefinitionInventoryQueryMode.ExactOwnedId);
        OwnedDefinitionInventory.ValidateQuery(new string('a', 128), OwnedDefinitionInventoryQueryMode.ExactArtifactId);
        ((Action)(() => OwnedDefinitionInventory.ValidateQuery(new string('a', 65), OwnedDefinitionInventoryQueryMode.ExactOwnedId)))
            .Should().Throw<InvalidDataException>();
        ((Action)(() => OwnedDefinitionInventory.ValidateQuery(new string('a', 129), OwnedDefinitionInventoryQueryMode.ExactArtifactId)))
            .Should().Throw<ArgumentException>();
        ((Action)(() => OwnedDefinitionInventory.ValidateQuery("alpha", (OwnedDefinitionInventoryQueryMode)99)))
            .Should().Throw<ArgumentOutOfRangeException>();
        var kind = ArtifactKind.Instruction;
        foreach (var mode in new[] { OwnedDefinitionInventoryQueryMode.ExactOwnedId, OwnedDefinitionInventoryQueryMode.ExactArtifactId })
        {
            foreach (var invalid in new[] { new string('a', 64 * 1024), "\ud800", "invalid-revision" })
            {
                var inventory = Inventory(kind, ["alpha"], Candidate(kind, "alpha") with { OwnedInstructionRevision = invalid });
                Action invalidPage = () => inventory.Page("missing", OwnedDefinitionUseFilter.UseEnabled, 1, mode);
                if (invalid is "\ud800") { invalidPage.Should().Throw<EncoderFallbackException>(); }
                else { invalidPage.Should().Throw<InvalidDataException>(); }
            }
        }
        var names = Enumerable.Range(0, 128).Select(index => "alpha-" + index.ToString("D3", System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        var candidates = names.Select(name => Candidate(kind, name)).ToArray();
        var snapshot = Inventory(kind, names, candidates);
        var first = snapshot.Page("alpha", OwnedDefinitionUseFilter.All);
        first.NextOffset.Should().NotBeNull();
        foreach (var mode in new[] { OwnedDefinitionInventoryQueryMode.ExactOwnedId, OwnedDefinitionInventoryQueryMode.ExactArtifactId })
        {
            ((Action)(() => snapshot.Continue(first, "alpha", OwnedDefinitionUseFilter.All, mode: mode)))
                .Should().Throw<InvalidOperationException>();
        }
        var exact = snapshot.Page("alpha-000", OwnedDefinitionUseFilter.All, mode: OwnedDefinitionInventoryQueryMode.ExactOwnedId);
        ((Action)(() => snapshot.Continue(exact, "alpha-000", OwnedDefinitionUseFilter.All,
            mode: OwnedDefinitionInventoryQueryMode.ExactOwnedId))).Should().Throw<InvalidOperationException>();
        var seen = new List<OwnedDefinitionInventoryRow>();
        var page = first;
        while (true)
        {
            page.DisplayUtf8Bytes.Should().BeLessThanOrEqualTo(64 * 1024);
            seen.AddRange(page.Rows);
            if (page.NextOffset is null) { break; }
            page = snapshot.Continue(page, "alpha", OwnedDefinitionUseFilter.All, mode: OwnedDefinitionInventoryQueryMode.Literal);
        }
        seen.Should().Equal(snapshot.Rows);
    }
}
