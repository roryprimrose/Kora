using AwesomeAssertions;

using Kora.Core.Artifacts;
using Kora.Core.Dependencies;
using Kora.Core.Customizations;
using Kora.Windows.Artifacts;

namespace Kora.Windows.IntegrationTests.ArtifactDiscovery;

public sealed class OwnedPromptRenameTests : IDisposable, IApplicationDataPaths
{
    public string LocalRoot { get; } = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..",
        ".net-test-artifacts", "prompt-rename", Guid.NewGuid().ToString("N")));
    public string RoamingRoot => LocalRoot;
    public OwnedPromptRenameTests() => Directory.CreateDirectory(LocalRoot);
    public void Dispose()
    {
        if (Directory.Exists(LocalRoot)) { Directory.Delete(LocalRoot, recursive: true); }
    }

    [Fact]
    public async Task FreshPromptRootHasNoUnfinishedRenameAndDoesNotInspectInstructionRecovery()
    {
        var store = new WindowsOwnedInstructionStore(this, ArtifactKind.Prompt);
        var document = ArtifactFileDocument.Parse(ArtifactFileDocument.Encode(
            "---\nkora-format: 1\nname: example\ndescription: Exact request\n---\nLiteral request", false),
            ArtifactKind.Prompt, "kora-profile", "example");
        var original = (await store.Write("example", null, document, () => true,
            operation => Task.FromResult(operation()), TestContext.Current.CancellationToken)).Current!;

        (await store.ReadRenameRecovery(TestContext.Current.CancellationToken)).Should().BeNull();
        (await store.Read("example", TestContext.Current.CancellationToken))!.Revision.Should().Be(original.Revision);
    }

    [Theory]
    [InlineData(OwnedInstructionRenamePhase.Prepared, "1")]
    [InlineData(OwnedInstructionRenamePhase.OriginalHeld, "2")]
    [InlineData(OwnedInstructionRenamePhase.Published, "2")]
    [InlineData(OwnedInstructionRenamePhase.CleanupPending, "1")]
    public async Task PromptRecoveryRequiresExactReviewAndJoinsEveryDurablePhaseWithoutTouchingInstructions(
        OwnedInstructionRenamePhase phase, string version)
    {
        var token = TestContext.Current.CancellationToken;
        var store = new WindowsOwnedInstructionStore(this, ArtifactKind.Prompt);
        var source = await CreateSource(store, version);
        var proposal = new OwnedPromptRename(source, "renamed");
        var instructions = new WindowsOwnedInstructionStore(this);
        var instructionDocument = ArtifactFileDocument.Parse(ArtifactFileDocument.Encode(
            "---\nkora-format: 1\nname: untouched\ndescription: Separate root\n---\nKeep exact", true),
            ArtifactKind.Instruction, "kora-profile", "untouched");
        var instruction = (await instructions.Write("untouched", null, instructionDocument, () => true,
            operation => Task.FromResult(operation()), token)).Current!;
        var customizations = new WindowsCustomizationStore(this, Path.Combine(LocalRoot, "no-profile"), Path.Combine(LocalRoot, "no-user"));
        var selected = (await customizations.List(token)).Single(item => item.Kind == CustomizationKind.Prompt && item.Scope == CustomizationScope.Kora);
        var original = await customizations.Read(selected, token);
        store.AfterRenamePhase = observed => { if (observed == phase) { throw new IOException("Injected durable interruption"); } };
        var rename = () => store.Rename(proposal, () => true, Commit, token);
        await rename.Should().ThrowAsync<InstructionRenameUnconfirmedException>();
        var restarted = new WindowsOwnedInstructionStore(this, ArtifactKind.Prompt);
        var recovery = (await restarted.ReadRenameRecovery(token))!;
        recovery.Phase.Should().Be(phase);
        recovery.Proposal.Should().BeOfType<OwnedPromptRename>();
        recovery.Proposal.Source.Document.CopyBytes().Should().Equal(source.Document.CopyBytes());
        var read = () => restarted.Read("example", token);
        await read.Should().ThrowAsync<InvalidOperationException>();
        var customizationRead = () => customizations.Read(selected, token);
        await customizationRead.Should().ThrowAsync<InvalidOperationException>();
        var customizationWrite = () => customizations.Save(selected, original, original.Files, () => true,
            operation => Task.FromResult(operation()), token);
        await customizationWrite.Should().ThrowAsync<InvalidOperationException>();
        (await instructions.ReadRenameRecovery(token)).Should().BeNull();
        (await instructions.Read("untouched", token))!.Revision.Should().Be(instruction.Revision);
        var wrongRoot = () => instructions.RecoverRename(recovery, () => true, Commit, token);
        await wrongRoot.Should().ThrowAsync<InvalidDataException>();
        var altered = () => restarted.RecoverRename(recovery with { ObservationRevision = "stale" }, () => true, Commit, token);
        await altered.Should().ThrowAsync<InvalidOperationException>();
        var saved = await restarted.RecoverRename(recovery, () => true, Commit, token);
        saved.Document.CopyBytes().Should().Equal(proposal.Proposed.CopyBytes());
        (await restarted.Read("example", token)).Should().BeNull();
        (await restarted.Read("renamed", token))!.Revision.Should().Be(saved.Revision);
        (await restarted.ReadRenameRecovery(token)).Should().BeNull();
        Directory.GetFiles(Path.Combine(RoamingRoot, "Prompts"), ".prompt-rename-*").Should().BeEmpty();
        File.ReadAllBytes(Path.Combine(RoamingRoot, "Instructions", "untouched.instructions.md")).Should().Equal(instructionDocument.CopyBytes());
    }

    [Theory]
    [InlineData("occupied")]
    [InlineData("alias")]
    [InlineData("foreign-source")]
    [InlineData("wrong-kind")]
    [InlineData("case-sensitive")]
    [InlineData("cancel")]
    [InlineData("privacy")]
    public async Task PromptRenameRefusesUncertainPathsOrAuthorityBeforeIntentWithoutChangingSources(string failure)
    {
        var token = TestContext.Current.CancellationToken;
        var store = new WindowsOwnedInstructionStore(this, ArtifactKind.Prompt);
        var source = await CreateSource(store, "2");
        var proposal = new OwnedPromptRename(source, "renamed");
        var foreign = Path.Combine(RoamingRoot, "Prompts", failure is "alias" ? "RENAMED.prompt.md" : "renamed.prompt.md");
        if (failure is "occupied" or "alias") { File.WriteAllBytes(foreign, [1, 0, 3]); }
        if (failure is "foreign-source") { proposal = new(source with { FileIdentity = new string('f', 48) }, "renamed"); }
        if (failure is "case-sensitive") { store.RenameDirectoryFlags = _ => 1; }
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        if (failure is "cancel") { cancellation.Cancel(); }
        var target = failure is "wrong-kind" ? new WindowsOwnedInstructionStore(this) : store;
        var rename = () => target.Rename(proposal, () => failure is not "privacy", Commit, cancellation.Token);
        await rename.Should().ThrowAsync<Exception>();
        File.ReadAllBytes(Path.Combine(RoamingRoot, "Prompts", "example.prompt.md")).Should().Equal(source.Document.CopyBytes());
        if (failure is "occupied" or "alias") { File.ReadAllBytes(foreign).Should().Equal([1, 0, 3]); }
        else { File.Exists(foreign).Should().BeFalse(); }
        (await store.ReadRenameRecovery(token)).Should().BeNull();
        Directory.GetFiles(Path.Combine(RoamingRoot, "Prompts"), ".prompt-rename-*").Should().BeEmpty();
    }

    [Theory]
    [InlineData("destination")]
    [InlineData("readback")]
    [InlineData("cancel")]
    [InlineData("privacy")]
    public async Task InterruptedPublicationRetainsOriginalAndCannotOverwriteOrClaimSuccess(string failure)
    {
        var token = TestContext.Current.CancellationToken;
        var store = new WindowsOwnedInstructionStore(this, ArtifactKind.Prompt);
        var source = await CreateSource(store, "2");
        var proposal = new OwnedPromptRename(source, "renamed");
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        var eligible = true;
        var destination = Path.Combine(RoamingRoot, "Prompts", "renamed.prompt.md");
        store.AfterRenamePhase = phase =>
        {
            if (phase == OwnedInstructionRenamePhase.OriginalHeld)
            {
                if (failure is "destination") { File.WriteAllBytes(destination, [1, 0, 3]); }
                if (failure is "readback")
                { File.WriteAllBytes(Directory.GetFiles(Path.Combine(RoamingRoot, "Prompts"), ".prompt-rename-*.proposed").Single(), [9, 0, 8]); }
                if (failure is "cancel") { cancellation.Cancel(); }
                if (failure is "privacy") { eligible = false; }
            }
        };
        var rename = () => store.Rename(proposal, () => eligible, Commit, cancellation.Token);
        await rename.Should().ThrowAsync<InstructionRenameUnconfirmedException>();
        var originals = Directory.GetFiles(Path.Combine(RoamingRoot, "Prompts"), ".prompt-rename-*.original");
        originals.Should().ContainSingle();
        File.ReadAllBytes(originals.Single()).Should().Equal(source.Document.CopyBytes());
        File.Exists(Path.Combine(LocalRoot, "OwnedPromptRename", "prompt-rename.json")).Should().BeTrue();
        if (failure is "destination" or "readback")
        {
            var recovery = () => new WindowsOwnedInstructionStore(this, ArtifactKind.Prompt).ReadRenameRecovery(token);
            await recovery.Should().ThrowAsync<InvalidDataException>();
            if (failure is "destination") { File.ReadAllBytes(destination).Should().Equal([1, 0, 3]); }
            else { File.Exists(destination).Should().BeFalse(); }
        }
        else
        {
            var recovery = (await store.ReadRenameRecovery(token))!;
            var saved = await store.RecoverRename(recovery, () => true, Commit, token);
            saved.Document.CopyBytes().Should().Equal(proposal.Proposed.CopyBytes());
        }
    }

    private async Task<OwnedInstructionSnapshot> CreateSource(WindowsOwnedInstructionStore store, string version)
    {
        var document = ArtifactFileDocument.Parse(ArtifactFileDocument.Encode(
            $"---\r\nkora-format: {version}\r\nname :  'example'  \r\ndescription: Exact request\r\nattribution: Original author.\r\n---\r\n"
                + (version is "2" ? "Literal ${input:second} then ${input:first}, ${input:second}." : "Literal saved request.")
                + "\n  Preserve  spacing.\r\n",
            true), ArtifactKind.Prompt, "kora-profile", "example");
        return (await store.Write("example", null, document, () => true, operation => Task.FromResult(operation()),
            TestContext.Current.CancellationToken)).Current!;
    }

    private static Task<OwnedInstructionSnapshot> Commit(Func<OwnedInstructionSnapshot> operation) => Task.FromResult(operation());
}
