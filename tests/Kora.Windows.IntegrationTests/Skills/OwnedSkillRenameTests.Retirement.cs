using AwesomeAssertions;
using Kora.Core.Skills;
using Kora.Windows.Skills;
using Kora.Windows.Storage;
using Kora.Windows.IntegrationTests.Storage;

namespace Kora.Windows.IntegrationTests.Skills;

public sealed partial class OwnedSkillRenameTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RetirementHoldsExactSourceAndDestinationAgainstSameVolumeMovesAndWritesUntilDisposition(bool revoke)
    {
        using var paths = new OwnedStorageFixture();
        var store = new WindowsOwnedDeclarativeSkillStore(paths);
        var original = await Save(store, Document());
        var proposal = new OwnedSkillRename(original, "renamed");
        var outside = Path.Combine(paths.LocalRoot, "outside-owned-skills");
        Directory.CreateDirectory(outside);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        var current = true;
        store.Checkpoint = point =>
        {
            if (point is "rename-before-retirement") { entered.SetResult(); release.Wait(Token); }
        };
        var operation = Rename(store, proposal, eligible: () => current);
        try
        {
            (await Task.WhenAny(entered.Task, operation)).Should().BeSameAs(entered.Task);
            await entered.Task;
            foreach (var document in new[] { original.Document, proposal.Renamed })
            {
                var package = store.Destination(document);
                var moveDirectory = () => Directory.Move(package, Path.Combine(outside, document.Id));
                moveDirectory.Should().Throw<IOException>();
                foreach (var name in new[] { "manifest.yaml", "instructions.md" })
                {
                    var path = Path.Combine(package, name);
                    var moveFile = () => File.Move(path, Path.Combine(outside, document.Id + "-" + name));
                    moveFile.Should().Throw<IOException>();
                    var write = () => File.AppendAllText(path, "external");
                    write.Should().Throw<IOException>();
                    File.Exists(path).Should().BeTrue();
                }
            }
            Directory.GetFileSystemEntries(outside).Should().BeEmpty();
        }
        finally
        {
            current = !revoke;
            release.Set();
            if (revoke)
            {
                var run = () => operation;
                var failure = await run.Should().ThrowAsync<OwnedSkillRenameUnconfirmedException>();
                failure.Which.Effect.Should().Be(OwnedSkillRenameEffect.SourceRetirementStarted);
            }
            else { (await operation).Document.RevisionDigest.Should().Be(proposal.Renamed.RevisionDigest); }
        }
        if (revoke)
        {
            File.ReadAllBytes(Path.Combine(store.Destination(original.Document), "manifest.yaml"))
                .Should().Equal(original.Document.ManifestBytes.ToArray());
            File.ReadAllBytes(Path.Combine(store.Destination(original.Document), "instructions.md"))
                .Should().Equal(original.Document.InstructionBytes.ToArray());
            (await store.PendingRenames(Token)).Should().ContainSingle();
            await store.Invoking(value => value.Read("example", "1.0.0", Token)).Should().ThrowAsync<InvalidOperationException>();
            await store.Invoking(value => value.Read("renamed", "1.0.0", Token)).Should().ThrowAsync<InvalidOperationException>();
            store.Checkpoint = null;
            await Rename(store, proposal, recovery: true);
        }
        Directory.GetFileSystemEntries(outside).Should().BeEmpty();
        (await store.PendingRenames(Token)).Should().BeEmpty();
        (await store.Read("example", "1.0.0", Token)).Should().BeNull();
        (await store.Read("renamed", "1.0.0", Token))!.Document.RevisionDigest.Should().Be(proposal.Renamed.RevisionDigest);
    }

    [Theory]
    [InlineData("source-file")]
    [InlineData("source-directory")]
    [InlineData("destination-file")]
    [InlineData("destination-directory")]
    public async Task ChangedPermissionsAtTheHeldRetirementBoundaryNeverRetireSourceOrClaimSuccess(string boundary)
    {
        using var paths = new OwnedStorageFixture();
        var store = new WindowsOwnedDeclarativeSkillStore(paths);
        var original = await Save(store, Document());
        var proposal = new OwnedSkillRename(original, "renamed");
        var permissionsChanged = false;
        store.Checkpoint = point =>
        {
            if (point is not "rename-before-retirement") { return; }
            var package = store.Destination(boundary.StartsWith("source", StringComparison.Ordinal) ? original.Document : proposal.Renamed);
            if (boundary.EndsWith("file", StringComparison.Ordinal))
            {
                var file = new FileInfo(Path.Combine(package, "instructions.md"));
                var security = file.GetAccessControl();
                security.SetAccessRuleProtection(isProtected: false, preserveInheritance: true);
                file.SetAccessControl(security);
                permissionsChanged = !file.GetAccessControl().AreAccessRulesProtected;
            }
            else
            {
                var folder = new DirectoryInfo(package);
                var security = folder.GetAccessControl();
                security.SetAccessRuleProtection(isProtected: false, preserveInheritance: true);
                folder.SetAccessControl(security);
                permissionsChanged = !folder.GetAccessControl().AreAccessRulesProtected;
            }
        };
        var failure = await store.Invoking(value => Rename(value, proposal)).Should().ThrowAsync<OwnedSkillRenameUnconfirmedException>();
        permissionsChanged.Should().BeTrue("the actual test-owned ACL change must precede the guarded retirement");
        failure.Which.Effect.Should().Be(OwnedSkillRenameEffect.SourceRetirementStarted);
        failure.Which.InnerException.Should().BeOfType<UnauthorizedAccessException>();
        (await store.PendingRenames(Token)).Should().ContainSingle();
        foreach (var document in new[] { original.Document, proposal.Renamed })
        {
            File.ReadAllBytes(Path.Combine(store.Destination(document), "manifest.yaml")).Should().Equal(document.ManifestBytes.ToArray());
            File.ReadAllBytes(Path.Combine(store.Destination(document), "instructions.md")).Should().Equal(document.InstructionBytes.ToArray());
            await store.Invoking(value => value.Read(document.Id, document.Version, Token)).Should().ThrowAsync<InvalidOperationException>();
        }
    }

    [Fact]
    public async Task FilesMovedOutsideBeforeRetirementHoldsAreAcquiredAreNeverDeletedEvenDuringExplicitRecovery()
    {
        using var paths = new OwnedStorageFixture();
        var store = new WindowsOwnedDeclarativeSkillStore(paths);
        var original = await Save(store, Document());
        var proposal = new OwnedSkillRename(original, "renamed");
        var outside = Path.Combine(paths.LocalRoot, "outside-owned-skills");
        Directory.CreateDirectory(outside);
        store.Checkpoint = point =>
        {
            if (point is not "rename-destination-verified") { return; }
            foreach (var name in new[] { "manifest.yaml", "instructions.md" })
            { File.Move(Path.Combine(store.Destination(original.Document), name), Path.Combine(outside, name)); }
        };
        var failure = await store.Invoking(value => Rename(value, proposal)).Should().ThrowAsync<OwnedSkillRenameUnconfirmedException>();
        failure.Which.Effect.Should().Be(OwnedSkillRenameEffect.DestinationPublished);
        (await store.PendingRenames(Token)).Should().ContainSingle();
        Directory.Exists(store.Destination(original.Document)).Should().BeTrue();
        store.Checkpoint = null;
        await Rename(store, proposal, recovery: true);
        using var manifest = OwnedSkillPackagePublication.OpenFile(Path.Combine(outside, "manifest.yaml"));
        using var instructions = OwnedSkillPackagePublication.OpenFile(Path.Combine(outside, "instructions.md"));
        WindowsFileIdentity.Read(manifest).Should().Be(original.ManifestIdentity);
        WindowsFileIdentity.Read(instructions).Should().Be(original.InstructionsIdentity);
        File.ReadAllBytes(Path.Combine(outside, "manifest.yaml")).Should().Equal(original.Document.ManifestBytes.ToArray());
        File.ReadAllBytes(Path.Combine(outside, "instructions.md")).Should().Equal(original.Document.InstructionBytes.ToArray());
        (await store.PendingRenames(Token)).Should().BeEmpty();
        (await store.Read("example", "1.0.0", Token)).Should().BeNull();
        (await store.Read("renamed", "1.0.0", Token))!.Document.RevisionDigest.Should().Be(proposal.Renamed.RevisionDigest);
    }
}
