using System.Diagnostics;

using AwesomeAssertions;

using Kora.Core.Artifacts;
using Kora.Core.Customizations;
using Kora.Windows.Artifacts;

namespace Kora.Windows.IntegrationTests.ArtifactDiscovery;

public sealed partial class WindowsOwnedInstructionStoreTests
{
    private static Task<OwnedInstructionSnapshot> CommitRename(Func<OwnedInstructionSnapshot> operation) => Task.FromResult(operation());

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RenameMovesTheExactOriginalAndPublishesOnlyReviewedNameBytes(bool bom)
    {
        var store = Create();
        var source = (await store.Write("example", null, Document(bom: bom), () => true, TestContext.Current.CancellationToken)).Current!;
        var unrelated = Path.Combine(OwnedDirectory, "unrelated.txt");
        File.WriteAllBytes(unrelated, [1, 2, 3, 0, 4]);
        var proposal = new OwnedInstructionRename(source, "renamed");
        var saved = await store.Rename(proposal, () => true, CommitRename, TestContext.Current.CancellationToken);
        saved.Name.Should().Be("renamed");
        saved.Document.CopyBytes().Should().Equal(proposal.Proposed.CopyBytes());
        saved.FileIdentity.Should().NotBe(source.FileIdentity);
        saved.DirectoryIdentity.Should().Be(source.DirectoryIdentity);
        saved.RootBindingIdentity.Should().Be(source.RootBindingIdentity);
        (await store.Read("example", TestContext.Current.CancellationToken)).Should().BeNull();
        (await store.List(TestContext.Current.CancellationToken)).Should().Equal("renamed");
        (await store.ReadRenameRecovery(TestContext.Current.CancellationToken)).Should().BeNull();
        File.ReadAllBytes(unrelated).Should().Equal([1, 2, 3, 0, 4]);
        Directory.GetFiles(OwnedDirectory, ".instruction-rename-*").Should().BeEmpty();
    }

    [Theory]
    [InlineData(OwnedInstructionRenamePhase.Prepared)]
    [InlineData(OwnedInstructionRenamePhase.OriginalHeld)]
    [InlineData(OwnedInstructionRenamePhase.Published)]
    [InlineData(OwnedInstructionRenamePhase.CleanupPending)]
    public async Task RestartNeverReplaysAndExplicitExactRecoveryJoinsEveryDurablePhase(OwnedInstructionRenamePhase phase)
    {
        var store = Create();
        var source = (await store.Write("example", null, Document(bom: true), () => true, TestContext.Current.CancellationToken)).Current!;
        var proposal = new OwnedInstructionRename(source, "renamed");
        var customizations = new WindowsCustomizationStore(new Paths(root), Path.Combine(root, "no-profile"), Path.Combine(root, "no-user"));
        var selected = (await customizations.List(TestContext.Current.CancellationToken)).Single(item =>
            item.Kind == CustomizationKind.Instruction && item.Scope == CustomizationScope.Kora);
        var original = await customizations.Read(selected, TestContext.Current.CancellationToken);
        store.AfterRenamePhase = observed => { if (observed == phase) { throw new IOException("Injected interruption"); } };
        var rename = () => store.Rename(proposal, () => true, CommitRename, TestContext.Current.CancellationToken);
        await rename.Should().ThrowAsync<InstructionRenameUnconfirmedException>();
        var restarted = new WindowsOwnedInstructionStore(new Paths(root));
        var list = () => restarted.List(TestContext.Current.CancellationToken);
        await list.Should().ThrowAsync<InvalidOperationException>();
        var read = () => restarted.Read("renamed", TestContext.Current.CancellationToken);
        await read.Should().ThrowAsync<InvalidOperationException>();
        var write = () => restarted.Write("example", source, Document("Must not write"), () => true, TestContext.Current.CancellationToken);
        await write.Should().ThrowAsync<InvalidOperationException>();
        var otherRead = () => customizations.Read(selected, TestContext.Current.CancellationToken);
        await otherRead.Should().ThrowAsync<InvalidOperationException>();
        var metadata = () => customizations.ReadMetadata(selected, TestContext.Current.CancellationToken);
        await metadata.Should().ThrowAsync<InvalidOperationException>();
        var otherWrite = () => customizations.Save(selected, original, original.Files, () => true,
            operation => Task.FromResult(operation()), TestContext.Current.CancellationToken);
        await otherWrite.Should().ThrowAsync<InvalidOperationException>();
        var recovery = await restarted.ReadRenameRecovery(TestContext.Current.CancellationToken);
        recovery!.Phase.Should().Be(phase);
        recovery.Proposal.Source.Document.CopyBytes().Should().Equal(source.Document.CopyBytes());
        var saved = await restarted.RecoverRename(recovery, () => true, CommitRename, TestContext.Current.CancellationToken);
        saved.Document.CopyBytes().Should().Equal(proposal.Proposed.CopyBytes());
        (await restarted.List(TestContext.Current.CancellationToken)).Should().Equal("renamed");
        (await restarted.ReadRenameRecovery(TestContext.Current.CancellationToken)).Should().BeNull();
    }

    [Theory]
    [InlineData("occupied")]
    [InlineData("occupied-alias")]
    [InlineData("occupied-directory")]
    [InlineData("source-bytes")]
    [InlineData("source-identity")]
    [InlineData("source-case")]
    [InlineData("root")]
    [InlineData("parent-binding")]
    [InlineData("eligibility")]
    [InlineData("cancel")]
    public async Task RenameRefusesChangedSourceParentOrDestinationWithoutLosingOriginal(string change)
    {
        var store = Create();
        var source = (await store.Write("example", null, Document(), () => true, TestContext.Current.CancellationToken)).Current!;
        var destination = Path.Combine(OwnedDirectory, "renamed.instructions.md");
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        switch (change)
        {
            case "occupied": WriteOwnedFile(destination, Document("Occupied").CopyBytes()); break;
            case "occupied-alias": WriteOwnedFile(Path.Combine(OwnedDirectory, "RENAMED.instructions.md"), Document("Occupied").CopyBytes()); break;
            case "occupied-directory": Directory.CreateDirectory(destination); break;
            case "source-bytes": File.WriteAllBytes(Target, Document("Changed").CopyBytes()); break;
            case "source-identity": File.Delete(Target); WriteOwnedFile(Target, Document().CopyBytes()); break;
            case "source-case": File.Move(Target, Path.Combine(OwnedDirectory, "Example.instructions.md")); break;
            case "root":
                Directory.Move(OwnedDirectory, OwnedDirectory + "-old");
                Directory.CreateDirectory(OwnedDirectory);
                WriteOwnedFile(Target, Document().CopyBytes());
                break;
            case "parent-binding": source = source with { RootBindingIdentity = new string('a', 64) }; break;
            case "cancel": cancel.Cancel(); break;
        }
        var operation = () => store.Rename(new(source, "renamed"), () => change is not "eligibility", CommitRename, cancel.Token);
        await operation.Should().ThrowAsync<Exception>();
        File.Exists(change is "source-case" ? Path.Combine(OwnedDirectory, "Example.instructions.md") : Target).Should().BeTrue();
        (await store.ReadRenameRecovery(TestContext.Current.CancellationToken)).Should().BeNull();
        Directory.GetFiles(OwnedDirectory, ".instruction-rename-*").Should().BeEmpty();
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("privacy")]
    [InlineData("changed-destination")]
    [InlineData("stale-recovery")]
    public async Task UncertainOrStaleRecoveryNeverOverwritesOrRollsBack(string change)
    {
        var store = Create();
        var source = (await store.Write("example", null, Document(), () => true, TestContext.Current.CancellationToken)).Current!;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var eligible = true;
        store.AfterRenamePhase = phase =>
        {
            if (phase != OwnedInstructionRenamePhase.OriginalHeld) { return; }
            if (change is "cancel") { cancellation.Cancel(); }
            else if (change is "privacy") { eligible = false; }
            else { throw new IOException("Interrupted before new publication"); }
        };
        var rename = () => store.Rename(new(source, "renamed"), () => eligible, CommitRename, cancellation.Token);
        await rename.Should().ThrowAsync<InstructionRenameUnconfirmedException>();
        var recovery = await store.ReadRenameRecovery(TestContext.Current.CancellationToken);
        recovery!.Phase.Should().Be(OwnedInstructionRenamePhase.OriginalHeld);
        store.AfterRenamePhase = null;
        eligible = true;
        if (change is "changed-destination")
        { WriteOwnedFile(Path.Combine(OwnedDirectory, "renamed.instructions.md"), Document("External content").CopyBytes()); }
        if (change is "stale-recovery") { recovery = recovery with { ObservationRevision = "stale" }; }
        if (change is "changed-destination" or "stale-recovery")
        {
            var resume = () => store.RecoverRename(recovery, () => true, CommitRename, TestContext.Current.CancellationToken);
            await resume.Should().ThrowAsync<Exception>();
            Directory.GetFiles(OwnedDirectory, "*.original").Should().ContainSingle();
            if (change is "changed-destination")
            { File.ReadAllBytes(Path.Combine(OwnedDirectory, "renamed.instructions.md")).Should().Equal(Document("External content").CopyBytes()); }
        }
        else
        {
            var saved = await store.RecoverRename(recovery, () => true, CommitRename, TestContext.Current.CancellationToken);
            saved.Document.CopyBytes().Should().Equal(new OwnedInstructionRename(source, "renamed").Proposed.CopyBytes());
        }
    }

    [Fact]
    public async Task NewlyHardlinkedOriginalCannotBeMovedOrLoseEitherAlias()
    {
        var store = Create();
        var source = (await store.Write("example", null, Document(), () => true, TestContext.Current.CancellationToken)).Current!;
        var alias = Path.Combine(root, "alias.instructions.md");
        CreateHardLink(alias, Target, nint.Zero).Should().BeTrue();
        var rename = () => store.Rename(new(source, "renamed"), () => true, CommitRename, TestContext.Current.CancellationToken);
        await rename.Should().ThrowAsync<InvalidDataException>();
        File.ReadAllBytes(alias).Should().Equal(source.Document.CopyBytes());
        File.ReadAllBytes(Target).Should().Equal(source.Document.CopyBytes());
        (await store.ReadRenameRecovery(TestContext.Current.CancellationToken)).Should().BeNull();
    }

    [Fact]
    public async Task SameChildIdentityUnderAReplacedParentCannotBeRenamed()
    {
        Directory.CreateDirectory(root);
        var roaming = Path.Combine(root, "roaming");
        var store = new WindowsOwnedInstructionStore(new Paths(roaming));
        var source = (await store.Write("example", null, Document(), () => true, TestContext.Current.CancellationToken)).Current!;
        var prior = Path.Combine(root, "prior");
        Directory.Move(roaming, prior);
        Directory.CreateDirectory(roaming);
        Directory.Move(Path.Combine(prior, "Instructions"), Path.Combine(roaming, "Instructions"));
        var restarted = new WindowsOwnedInstructionStore(new Paths(roaming));
        var current = await restarted.Read("example", TestContext.Current.CancellationToken);
        current!.DirectoryIdentity.Should().Be(source.DirectoryIdentity);
        current.FileIdentity.Should().Be(source.FileIdentity);
        current.RootBindingIdentity.Should().NotBe(source.RootBindingIdentity);
        var rename = () => restarted.Rename(new(source, "renamed"), () => true, CommitRename, TestContext.Current.CancellationToken);
        await rename.Should().ThrowAsync<InvalidOperationException>();
        File.ReadAllBytes(Path.Combine(roaming, "Instructions", "example.instructions.md")).Should().Equal(source.Document.CopyBytes());
    }

    [Fact]
    public async Task ReparseParentCannotRedirectTheRenameIntoAnotherFixtureDirectory()
    {
        var store = Create();
        var source = (await store.Write("example", null, Document(), () => true, TestContext.Current.CancellationToken)).Current!;
        var prior = Path.Combine(root, "prior");
        Directory.Move(OwnedDirectory, prior);
        var outside = Path.Combine(root, "outside");
        Directory.CreateDirectory(outside);
        using var process = Process.Start(new ProcessStartInfo("cmd.exe")
        {
            Arguments = "/c mklink /J \"" + OwnedDirectory + "\" \"" + outside + "\"",
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true,
        })!;
        await process.WaitForExitAsync(TestContext.Current.CancellationToken);
        process.ExitCode.Should().Be(0);
        try
        {
            var rename = () => store.Rename(new(source, "renamed"), () => true, CommitRename, TestContext.Current.CancellationToken);
            await rename.Should().ThrowAsync<InvalidDataException>();
            Directory.GetFiles(outside).Should().BeEmpty();
            File.ReadAllBytes(Path.Combine(prior, "example.instructions.md")).Should().Equal(source.Document.CopyBytes());
        }
        finally { Directory.Delete(OwnedDirectory); }
    }

    [Theory]
    [InlineData(1U)]
    [InlineData(2U)]
    public async Task CaseSensitiveOrUnknownNativeDirectorySemanticsRefuseRenameBeforeIntent(uint flags)
    {
        var store = Create();
        var source = (await store.Write("example", null, Document(), () => true, TestContext.Current.CancellationToken)).Current!;
        store.RenameDirectoryFlags = _ => flags;
        var rename = () => store.Rename(new(source, "renamed"), () => true, CommitRename, TestContext.Current.CancellationToken);
        await rename.Should().ThrowAsync<InvalidDataException>();
        File.ReadAllBytes(Target).Should().Equal(source.Document.CopyBytes());
        (await store.ReadRenameRecovery(TestContext.Current.CancellationToken)).Should().BeNull();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnconfirmedReadbackOrMalformedIntentCannotBecomeSuccessfulRecovery(bool corruptIntent)
    {
        var store = Create();
        var source = (await store.Write("example", null, Document(), () => true, TestContext.Current.CancellationToken)).Current!;
        store.AfterRenamePhase = phase =>
        {
            if (corruptIntent && phase == OwnedInstructionRenamePhase.Prepared) { throw new IOException("Interrupted"); }
            if (!corruptIntent && phase == OwnedInstructionRenamePhase.CleanupPending)
            { File.WriteAllBytes(Path.Combine(OwnedDirectory, "renamed.instructions.md"), Document("External after publication").CopyBytes()); }
        };
        var rename = () => store.Rename(new(source, "renamed"), () => true, CommitRename, TestContext.Current.CancellationToken);
        await rename.Should().ThrowAsync<InstructionRenameUnconfirmedException>();
        var intentPath = Path.Combine(root, "Local", "OwnedInstructionRename", "instruction-rename.json");
        File.Exists(intentPath).Should().BeTrue();
        if (corruptIntent) { File.WriteAllText(intentPath, "{\"Version\":123}"); }
        var recover = () => new WindowsOwnedInstructionStore(new Paths(root)).ReadRenameRecovery(TestContext.Current.CancellationToken);
        await recover.Should().ThrowAsync<InvalidDataException>();
        var list = () => store.List(TestContext.Current.CancellationToken);
        await list.Should().ThrowAsync<InvalidOperationException>();
        if (corruptIntent) { File.ReadAllBytes(Target).Should().Equal(source.Document.CopyBytes()); }
        else
        {
            var intent = System.Text.Json.JsonDocument.Parse(File.ReadAllBytes(intentPath));
            using (intent)
            { intent.RootElement.GetProperty("Original").GetBytesFromBase64().Should().Equal(source.Document.CopyBytes()); }
        }
    }
}
