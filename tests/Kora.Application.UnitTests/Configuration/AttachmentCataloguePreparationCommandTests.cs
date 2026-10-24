using AwesomeAssertions;
using Kora.Application.Configuration;

namespace Kora.Application.UnitTests.Configuration;

public sealed class AttachmentCataloguePreparationCommandTests
{
    private const string Id = "12345678-abcd-1234-abcd-123456789abc";

    [Theory]
    [InlineData("", "Kora", false)]
    [InlineData("Kora, ", "Kora", true)]
    [InlineData("Nova Prime: ", "Nova Prime", true)]
    [InlineData("界, ", "界", true)]
    [InlineData("", "prepare", false)]
    [InlineData("", "prepare attachment", false)]
    [InlineData("", "attachment", false)]
    public void CanonicalPreparationIsExactAndNeverCarriesAuthority(string prefix, string name, bool activated)
    {
        var result = AttachmentCataloguePreparationCommand.Parse(prefix + "prepare attachment catalogue " + Id, name)!;
        result.SessionId.Should().Be(Guid.Parse(Id));
        result.IsHelp.Should().BeFalse();
        result.IsActivated.Should().Be(activated);
        AttachmentCataloguePreparationCommand.DiscoveryPhrases.Should().HaveCount(2);
    }

    [Theory]
    [InlineData("attachment catalogue help", "attachment")]
    [InlineData("attachment catalogue help", "attachment catalogue")]
    [InlineData("attachment catalogue, attachment catalogue help", "attachment catalogue")]
    [InlineData("Kora, ATTACHMENT CATALOGUE HELP", "Kora")]
    public void HelpContainsNoTarget(string text, string name)
    {
        var result = AttachmentCataloguePreparationCommand.Parse(text, name)!;
        result.IsHelp.Should().BeTrue();
        result.SessionId.Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("current")]
    [InlineData("latest")]
    [InlineData("all")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("12345678-ABCD-1234-abcd-123456789abc")]
    [InlineData("{12345678-abcd-1234-abcd-123456789abc}")]
    [InlineData("12345678abcd1234abcd123456789abc")]
    [InlineData("12345678-abcd-1234-abcd-123456789abc extra")]
    [InlineData("12345678-abcd-1234-abcd-123456789abc\n")]
    [InlineData("12345678-abcd-1234-abcd-123456789abc\u202e")]
    [InlineData("１２３４５６７８-abcd-1234-abcd-123456789abc")]
    [InlineData("\"12345678-abcd-1234-abcd-123456789abc\"")]
    public void MalformedTargetIsReservedWithoutRepair(string target)
    {
        var result = AttachmentCataloguePreparationCommand.Parse("prepare attachment catalogue " + target, "Kora")!;
        result.Should().NotBeNull();
        result.SessionId.Should().BeNull();
        result.IsHelp.Should().BeFalse();
    }

    [Theory]
    [InlineData("prepare attachment")]
    [InlineData("prepare attachment catalogue")]
    [InlineData("prepare  attachment catalogue " + Id)]
    [InlineData("prepare\tattachment catalogue " + Id)]
    [InlineData("prepare\0attachment catalogue " + Id)]
    [InlineData("pre\u200bpare attachment catalogue " + Id)]
    [InlineData("prepare-attachment catalogue " + Id)]
    [InlineData("attachment_catalogue help")]
    [InlineData("attachment\u202ecatalogue help")]
    [InlineData("\u00a0prepare attachment catalogue " + Id)]
    [InlineData("prepare attachment catalogue  " + Id)]
    [InlineData("attachment catalogue search text")]
    [InlineData("attachment catalogue help extra")]
    [InlineData("attachment catalogue\nhelp")]
    public void MalformedFamilyDoesNotBecomeAQuestion(string text)
    {
        var result = AttachmentCataloguePreparationCommand.Parse(text, "Kora")!;
        result.SessionId.Should().BeNull();
        result.IsHelp.Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("help")]
    [InlineData("prepare file search")]
    [InlineData("open all work")]
    [InlineData("Kora, tell me about attachments")]
    [InlineData("Nova, prepare attachment catalogue " + Id)]
    public void UnrelatedInputIsNotClaimed(string text) =>
        AttachmentCataloguePreparationCommand.Parse(text, "Kora").Should().BeNull();

    [Fact]
    public void EncodedInputIsBoundedBeforeAdmission()
    {
        var result = AttachmentCataloguePreparationCommand.Parse("prepare attachment catalogue " + new string('a', 300), "Kora")!;
        result.SessionId.Should().BeNull();
        result.IsHelp.Should().BeFalse();
    }
}
