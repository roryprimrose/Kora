using AwesomeAssertions;

using Kora.Core.Artifacts;
using Kora.Core.Authorization;
using Kora.Core.Hosting;

namespace Kora.Core.UnitTests.ArtifactInvocation;

public sealed class OwnedInstructionRenameTests
{
    [Theory]
    [InlineData(true, "\r\n", "name :  'Old Name'  \r\n")]
    [InlineData(false, "\n", "name: \"Old Name\"\n")]
    [InlineData(true, "\r", "name: old\r")]
    [InlineData(false, "\n", "")]
    public void RenamePreservesEveryByteExceptTheDeclaredName(bool bom, string newline, string nameLine)
    {
        var text = "---" + newline + "kora-format: 1" + newline + nameLine
            + "description: Exact description." + newline + "attribution: Original author." + newline + "---" + newline
            + "Literal body with  spaces.\r\nOther endings.\nNo final newline";
        var document = ArtifactFileDocument.Parse(ArtifactFileDocument.Encode(text, bom), ArtifactKind.Instruction, "kora-profile", "original");
        var source = new OwnedInstructionSnapshot("original", "directory", "file", document);
        var rename = new OwnedInstructionRename(source, "renamed");
        var expected = nameLine.Length == 0 ? text : text.Replace(
            nameLine.Contains("Old Name", StringComparison.Ordinal) ? "Old Name" : "old", "renamed", StringComparison.Ordinal);
        rename.Source.Should().BeSameAs(source);
        rename.DestinationName.Should().Be("renamed");
        rename.Proposed.CopyBytes().Should().Equal(ArtifactFileDocument.Encode(expected, bom));
        source.Document.CopyBytes().Should().Equal(ArtifactFileDocument.Encode(text, bom));
        source.RootBindingIdentity.Should().BeEmpty();
        (source with { RootBindingIdentity = "binding" }).RootBindingIdentity.Should().Be("binding");
        rename.Proposed.Definition.Id.Should().NotBe(source.Document.Definition.Id);
        rename.Proposed.Body.Should().Be(document.Body);
    }

    [Theory]
    [InlineData("original")]
    [InlineData("ORIGINAL")]
    [InlineData("CON")]
    [InlineData("con")]
    [InlineData("help")]
    [InlineData("instructions")]
    [InlineData("instruction")]
    [InlineData("skill")]
    [InlineData("skills")]
    [InlineData("prompt")]
    [InlineData("prompts")]
    [InlineData("agent")]
    [InlineData("agents")]
    [InlineData("definitions")]
    [InlineData("exit")]
    [InlineData("settings")]
    [InlineData("../new")]
    public void InvalidOrReservedDestinationIsNotRenameAuthority(string destination)
    {
        var source = Source();
        ((Action)(() => _ = new OwnedInstructionRename(source, destination))).Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void UnsupportedOrNonCanonicalSourcesCannotBecomeRenameProposals()
    {
        ((Action)(() => _ = new OwnedInstructionRename(null!, "new"))).Should().Throw<ArgumentNullException>();
        ((Action)(() => _ = new OwnedInstructionRename(Source() with { Name = "Legacy.Name" }, "new"))).Should().Throw<InvalidDataException>();
        ((Action)(() => _ = new OwnedInstructionRename(Source() with { CanonicalFileName = "Original.instructions.md" }, "new"))).Should().Throw<InvalidDataException>();
        ((Action)(() => _ = new OwnedInstructionRename(Source(ArtifactKind.Prompt), "new"))).Should().Throw<InvalidDataException>();
        var legacy = Source() with
        {
            Document = ArtifactFileDocument.Parse(ArtifactFileDocument.Encode(
                "---\nname: original\ndescription: Test\n---\nBody", false), ArtifactKind.Instruction, "kora-profile", "original"),
        };
        ((Action)(() => _ = new OwnedInstructionRename(legacy, "new"))).Should().Throw<InvalidDataException>();
        var alreadyNamed = Source() with
        {
            Document = ArtifactFileDocument.Parse(ArtifactFileDocument.Encode(
                "---\nkora-format: 1\nname: new\ndescription: Test\n---\nBody", false), ArtifactKind.Instruction, "kora-profile", "original"),
        };
        ((Action)(() => _ = new OwnedInstructionRename(alreadyNamed, "new"))).Should().Throw<InvalidDataException>();
        var failure = new InstructionRenameUnconfirmedException(new IOException("Held"));
        failure.InnerException.Should().BeOfType<IOException>();
        var recovery = new OwnedInstructionRenameRecovery(new(Source(), "new"), "observation", OwnedInstructionRenamePhase.Prepared);
        recovery.Proposal.DestinationName.Should().Be("new");
        recovery.ObservationRevision.Should().Be("observation");
        recovery.Phase.Should().Be(OwnedInstructionRenamePhase.Prepared);
    }

    [Theory]
    [InlineData("2", "disk.kora-profile.instruction.original, disk.kora-profile.instruction.other", 2)]
    [InlineData("4", "disk.kora-profile.instruction.original", 1)]
    [InlineData("1", "none", 0)]
    [InlineData("3", "shared-source", 0)]
    [InlineData("2", "", 0)]
    public void DeclaredReferenceObservationDoesNotResolveOrEnableSources(string format, string references, int count)
    {
        var document = ArtifactFileDocument.Parse(ArtifactFileDocument.Encode(
            $"---\nname: agent\ndescription: Test\nkora-agent-format: {format}\nreferences: {references}\n---\nBody",
            false), ArtifactKind.Agent, "user-profile", "agent");
        document.Definition.DeclaredInstructionReferences.Should().HaveCount(count);
    }

    [Fact]
    public void PriorExactGrantCannotFollowANameOnlyRenameWithUnchangedSourceBytes()
    {
        var source = new OwnedInstructionSnapshot("original", "directory", "file",
            ArtifactFileDocument.Parse(ArtifactFileDocument.Encode(
                "---\nkora-format: 1\ndescription: Test\n---\nBody", false), ArtifactKind.Instruction, "kora-profile", "original"));
        var rename = new OwnedInstructionRename(source, "renamed");
        rename.Proposed.CopyBytes().Should().Equal(source.Document.CopyBytes());
        static ExactOperationBinding Binding(ArtifactDefinition definition) =>
            new("bounded.read", definition.Source, definition.Id, definition.Digest, definition.Digest, definition.Digest,
                definition.Digest, definition.Digest, definition.Digest, definition.Digest, definition.Digest, definition.Digest, new(1));
        var before = Binding(source.Document.Definition);
        var after = Binding(rename.Proposed.Definition);
        var request = new HostRequest(new(Guid.NewGuid()), new(Guid.NewGuid()), new(Guid.NewGuid()), RequestOrigin.LocalUi, new(Guid.NewGuid()));
        var grant = new OperationGrant(new(Guid.NewGuid()), new(1),
            new(request, new(Guid.NewGuid()), new(1), before, HostOperationEffect.BoundedRead, DateTimeOffset.UtcNow.AddMinutes(2)),
            OperationGrantScope.Perpetual, new(1), RequestOrigin.LocalUi, DateTimeOffset.UtcNow);
        grant.Validate();
        grant.ApprovedProposal.Binding.Equals(after).Should().BeFalse();
        before.IsSameDefinition(after).Should().BeFalse();
        grant.Status.Should().Be(OperationGrantStatus.Active);
        grant.ApprovedProposal.Binding.Should().BeSameAs(before);
    }

    private static OwnedInstructionSnapshot Source(ArtifactKind kind = ArtifactKind.Instruction) =>
        new("original", "directory", "file", ArtifactFileDocument.Parse(ArtifactFileDocument.Encode(
            "---\nkora-format: 1\nname: original\ndescription: Test\n---\nBody", false), kind, "kora-profile", "original"));
}
