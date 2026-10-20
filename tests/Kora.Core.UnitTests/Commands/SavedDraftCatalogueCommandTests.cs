using AwesomeAssertions;

using Kora.Core.Commands;

namespace Kora.Core.UnitTests.Commands;

public sealed class SavedDraftCatalogueCommandTests
{
    private const string Id = "abcdef12-3456-7890-abcd-ef1234567890";

    [Theory]
    [InlineData("Nova Prime")]
    [InlineData("Prepare")]
    [InlineData("Prepare Saved Draft")]
    [InlineData("Saved Draft Catalogue")]
    public void FixedFamilyWinsNameCollisionsAndCurrentActivationIsSupported(string name)
    {
        var phrase = SavedDraftCatalogueCommand.PreparePhrase + " " + Id;
        SavedDraftCatalogueCommand.Parse(phrase, name)!.SessionId.Should().Be(Guid.Parse(Id));
        SavedDraftCatalogueCommand.Parse(phrase, name)!.IsActivated.Should().BeFalse();
        SavedDraftCatalogueCommand.Parse(name + ", " + phrase, name)!.SessionId.Should().Be(Guid.Parse(Id));
        SavedDraftCatalogueCommand.Parse(name + ", " + phrase, name)!.IsActivated.Should().BeTrue();
        SavedDraftCatalogueCommand.Parse(name + " " + phrase, name)!.IsActivated.Should().BeTrue();
        SavedDraftCatalogueCommand.Parse(SavedDraftCatalogueCommand.HelpPhrase, name)!.IsHelp.Should().BeTrue();
        SavedDraftCatalogueCommand.Parse("Kora, " + phrase, "Nova").Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("Nova")]
    [InlineData("\0")]
    [InlineData("open saved conversation drafts")]
    [InlineData("prepare attachment catalogue")]
    [InlineData("find saved draft metadata")]
    [InlineData("please prepare saved draft catalogue")]
    public void UnownedInputIsNotPreparation(string input) =>
        SavedDraftCatalogueCommand.Parse(input, "Nova").Should().BeNull();

    [Theory]
    [InlineData("")]
    [InlineData("current")]
    [InlineData("latest")]
    [InlineData("my session")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("ABCDEF12-3456-7890-ABCD-EF1234567890")]
    [InlineData("{abcdef12-3456-7890-abcd-ef1234567890}")]
    [InlineData("abcdef1234567890abcdef1234567890")]
    [InlineData("abcdef12-3456-7890-abcd-ef1234567890 body")]
    [InlineData("abcdef12-3456-7890-abcd-ef1234567890 ")]
    [InlineData("abcdef12-3456-7890-abcd-ef1234567890\u00a0")]
    [InlineData("abcdef12-3456-7890-abcd-ef1234567890\n")]
    [InlineData("abcdef12-3456-7890-abcd-ef1234567890\0")]
    [InlineData("\u200babcdef12-3456-7890-abcd-ef1234567890")]
    [InlineData("ａｂｃｄｅｆ12-3456-7890-abcd-ef1234567890")]
    public void IdentifierIsNeverRepairedOrInterpretedAsName(string selector)
    {
        var parsed = SavedDraftCatalogueCommand.Parse("Nova, prepare saved draft catalogue " + selector, "Nova");
        parsed.Should().NotBeNull();
        parsed!.SessionId.Should().BeNull();
        parsed.IsHelp.Should().BeFalse();
    }

    [Theory]
    [InlineData("prepare saved draft")]
    [InlineData("prepare saved drafts catalogue")]
    [InlineData("prepare saved draft catalogue")]
    [InlineData("prepare-saved draft catalogue")]
    [InlineData("prepare\u200bsaved draft catalogue")]
    [InlineData("prepare\tsaved draft catalogue")]
    [InlineData("saved draft catalogue")]
    [InlineData("saved draft catalogue help body")]
    [InlineData("saved draft catalogue help!")]
    [InlineData("saved draft catalogue help\n")]
    [InlineData("Nova\u200b, prepare saved draft catalogue latest")]
    [InlineData("Nova\tprepare saved draft catalogue latest")]
    public void ReservedMalformedFamiliesCannotBecomeModelRequests(string text)
    {
        var parsed = SavedDraftCatalogueCommand.Parse(text, "Nova");
        parsed.Should().NotBeNull();
        parsed!.SessionId.Should().BeNull();
        parsed.IsHelp.Should().BeFalse();
    }

    [Fact]
    public void BoundsAndConstantHelpAreIndependentOfCurrentIdentity()
    {
        SavedDraftCatalogueCommand.Parse("PREPARE SAVED DRAFT CATALOGUE ffffffff-ffff-ffff-ffff-ffffffffffff", "Nova")!
            .SessionId.Should().Be(Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"));
        SavedDraftCatalogueCommand.Parse("prepare saved draft catalogue 00000000-0000-0000-0000-000000000001", "Nova")!
            .SessionId.Should().Be(Guid.Parse("00000000-0000-0000-0000-000000000001"));
        SavedDraftCatalogueCommand.Parse("saved draft catalogue " + new string('x', 10000), "Nova")!.IsHelp.Should().BeFalse();
        SavedDraftCatalogueCommand.Parse("Nova, SAVED DRAFT CATALOGUE HELP", "Nova")!.IsHelp.Should().BeTrue();
        SavedDraftCatalogueCommand.Help.Should().Contain("AND-composed").And.Contain("explicitly Search")
            .And.NotContain(Id);
    }
}
