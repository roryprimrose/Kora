using AwesomeAssertions;
using Kora.Core.Skills;

namespace Kora.Core.UnitTests.Skills;

public sealed class OwnedSkillRenameTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RenamePreservesEveryByteExceptTheExactManifestIdAndTransfersNoAvailability(bool inputs)
    {
        var old = OwnedDeclarativeSkillDocument.Create("example", "1.0.0", "Readable original", "Attribution stays",
            inputs ? "Literal ${input:topic}\r\n" : "Literal\r\nid: example\n",
            guidanceFormat: inputs ? OwnedSkillGuidanceFormat.RequiredLiteralInputs : OwnedSkillGuidanceFormat.Literal);
        var snapshot = new OwnedDeclarativeSkillSnapshot(old, "directory", "manifest", "guidance");
        var proposal = new OwnedSkillRename(snapshot, "renamed");
        proposal.Original.Should().BeSameAs(snapshot);
        proposal.Renamed.Id.Should().Be("renamed");
        proposal.Renamed.Version.Should().Be(old.Version);
        proposal.Renamed.Name.Should().Be(old.Name);
        proposal.Renamed.ManifestText.Should().Be(old.ManifestText.Replace("\nid: example\n", "\nid: renamed\n", StringComparison.Ordinal));
        proposal.Renamed.InstructionBytes.ToArray().Should().Equal(old.InstructionBytes.ToArray());
        proposal.Renamed.RevisionDigest.Should().NotBe(old.RevisionDigest);
        proposal.Renamed.IsAvailableForInvocation.Should().BeFalse();
        proposal.ToString().Should().Be("example 1.0.0 -> renamed (recovery required)");
    }

    [Theory]
    [InlineData("example")]
    [InlineData("EXAMPLE")]
    [InlineData("../escape")]
    [InlineData("con")]
    [InlineData("")]
    public void InvalidOrUnchangedIdentityIsRefused(string id)
    {
        var document = OwnedDeclarativeSkillDocument.Create("example", "1.0.0", "Example", "Description", "Literal");
        var create = () => new OwnedSkillRename(new(document, "d", "m", "i"), id);
        create.Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData(OwnedSkillRenameEffect.IntentMayExist)]
    [InlineData(OwnedSkillRenameEffect.DestinationPublished)]
    [InlineData(OwnedSkillRenameEffect.SourceRetirementStarted)]
    [InlineData(OwnedSkillRenameEffect.NativeReadbackVerified)]
    public void UncertaintyRetainsTypedLastObservedEffectAndExplicitRecovery(OwnedSkillRenameEffect effect)
    {
        var inner = new IOException("injected");
        var exception = new OwnedSkillRenameUnconfirmedException(effect, inner);
        exception.Effect.Should().Be(effect);
        exception.InnerException.Should().BeSameAs(inner);
        exception.Message.Should().Contain("Do not retry");
    }
}
