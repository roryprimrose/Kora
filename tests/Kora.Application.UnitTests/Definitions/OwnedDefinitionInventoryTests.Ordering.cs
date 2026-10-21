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


}
