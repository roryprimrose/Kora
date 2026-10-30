using AwesomeAssertions;
using Kora.Core.Skills;
using Kora.Windows.IntegrationTests.Storage;
using Kora.Windows.Skills;
using Kora.Windows.Storage;

namespace Kora.Windows.IntegrationTests.Skills;

public sealed class OwnedSkillRenameTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static OwnedDeclarativeSkillDocument Document(string id = "example", string version = "1.0.0") =>
        OwnedDeclarativeSkillDocument.Create(id, version, "Readable name", "Original attribution", "Exact literal\r\nid: example\n");
    private static Task<OwnedDeclarativeSkillSnapshot> Save(WindowsOwnedDeclarativeSkillStore store, OwnedDeclarativeSkillDocument document) =>
        store.Publish(document, static () => true, operation => Task.FromResult(operation()), Token);
    private static Task<OwnedDeclarativeSkillSnapshot> Rename(WindowsOwnedDeclarativeSkillStore store, OwnedSkillRename proposal,
        bool recovery = false, Func<bool>? eligible = null) =>
        store.Rename(proposal, recovery, eligible ?? (static () => true), operation => Task.FromResult(operation()), Token);

    [Theory]
    [InlineData(OwnedSkillToolSelection.None, false)]
    [InlineData(OwnedSkillToolSelection.WebPageGet, false)]
    [InlineData(OwnedSkillToolSelection.None, true)]
    public async Task CompleteNativeRenameReadbackRestartAndUnrelatedVersionsPreserveExactContentAndNoAuthority(
        OwnedSkillToolSelection tool, bool inputs)
    {
        using var paths = new OwnedStorageFixture();
        var store = new WindowsOwnedDeclarativeSkillStore(paths);
        var document = OwnedDeclarativeSkillDocument.Create("example", "1.0.0", "Readable", "Attribution",
            inputs ? "Literal ${input:topic}\r\n" : "Literal\r\n", tool,
            inputs ? OwnedSkillGuidanceFormat.RequiredLiteralInputs : OwnedSkillGuidanceFormat.Literal);
        var original = await Save(store, document);
        var unrelatedVersion = await Save(store, Document(version: "2.0.0"));
        var unrelated = await Save(store, Document("unrelated"));
        var proposal = new OwnedSkillRename(original, "renamed");
        var renamed = await Rename(store, proposal);
        renamed.Document.ManifestBytes.ToArray().Should().Equal(proposal.Renamed.ManifestBytes.ToArray());
        renamed.Document.InstructionBytes.ToArray().Should().Equal(document.InstructionBytes.ToArray());
        renamed.Document.IsAvailableForInvocation.Should().BeFalse();
        var restarted = new WindowsOwnedDeclarativeSkillStore(paths);
        (await restarted.Read("example", "1.0.0", Token)).Should().BeNull();
        (await restarted.Read("renamed", "1.0.0", Token))!.IsSameRevision(renamed).Should().BeTrue();
        (await restarted.Read("example", "2.0.0", Token))!.IsSameRevision(unrelatedVersion).Should().BeTrue();
        (await restarted.Read("unrelated", "1.0.0", Token))!.IsSameRevision(unrelated).Should().BeTrue();
        (await restarted.PendingRenames(Token)).Should().BeEmpty();
        (await restarted.List(Token)).Should().HaveCount(3);
    }

    [Theory]
    [InlineData("rename-intent-durable")]
    [InlineData("rename-destination-verified")]
    [InlineData("rename-component-retired")]
    [InlineData("rename-source-retired")]
    public async Task EveryRetainedPhaseRequiresExplicitExactRecoveryAcrossRestartWithoutLosingCompleteDestination(string fault)
    {
        using var paths = new OwnedStorageFixture();
        var store = new WindowsOwnedDeclarativeSkillStore(paths);
        var original = await Save(store, Document());
        var unrelated = await Save(store, Document("unrelated"));
        var proposal = new OwnedSkillRename(original, "renamed");
        store.Checkpoint = point => { if (string.Equals(point, fault, StringComparison.Ordinal)) { throw new IOException("injected boundary"); } };
        var run = () => Rename(store, proposal);
        await run.Should().ThrowAsync<OwnedSkillRenameUnconfirmedException>();
        var restarted = new WindowsOwnedDeclarativeSkillStore(paths);
        var pending = (await restarted.PendingRenames(Token)).Should().ContainSingle().Subject;
        pending.Original.IsSameRevision(original).Should().BeTrue();
        pending.Renamed.RevisionDigest.Should().Be(proposal.Renamed.RevisionDigest);
        await restarted.Invoking(value => value.Read("example", "1.0.0", Token)).Should().ThrowAsync<InvalidOperationException>();
        await restarted.Invoking(value => value.Read("renamed", "1.0.0", Token)).Should().ThrowAsync<InvalidOperationException>();
        await restarted.Invoking(value => Save(value, proposal.Renamed)).Should().ThrowAsync<InvalidOperationException>();
        await restarted.Invoking(value => Rename(value, proposal)).Should().ThrowAsync<InvalidOperationException>();
        (await restarted.List(Token)).Should().ContainSingle().Which.IsSameRevision(unrelated).Should().BeTrue();
        var recovered = await Rename(restarted, pending, recovery: true);
        recovered.Document.RevisionDigest.Should().Be(proposal.Renamed.RevisionDigest);
        (await restarted.Read("example", "1.0.0", Token)).Should().BeNull();
        (await restarted.PendingRenames(Token)).Should().BeEmpty();
        (await restarted.Read("renamed", "1.0.0", Token))!.IsSameRevision(recovered).Should().BeTrue();
    }

    [Theory]
    [InlineData("occupied")]
    [InlineData("alias")]
    [InlineData("changed")]
    [InlineData("member")]
    [InlineData("revoked")]
    public async Task CollisionAliasesChangedSourceExtraMembersAndRevocationNeverDeleteOrOverwrite(string boundary)
    {
        using var paths = new OwnedStorageFixture();
        var store = new WindowsOwnedDeclarativeSkillStore(paths);
        var original = await Save(store, Document());
        var proposal = new OwnedSkillRename(original, "renamed");
        OwnedDeclarativeSkillSnapshot? collision = null;
        if (boundary is "occupied") { collision = await Save(store, Document("renamed")); }
        if (boundary is "alias") { Directory.CreateDirectory(Path.Combine(paths.RoamingRoot, "Skills", "RENAMED")); }
        if (boundary is "changed") { await File.AppendAllTextAsync(Path.Combine(store.Destination(original.Document), "instructions.md"), "external", Token); }
        if (boundary is "member") { await File.WriteAllTextAsync(Path.Combine(store.Destination(original.Document), "external.md"), "retain", Token); }
        var run = () => Rename(store, proposal, eligible: () => boundary is not "revoked");
        if (boundary is "alias" or "member") { await run.Should().ThrowAsync<InvalidDataException>(); }
        else { await run.Should().ThrowAsync<InvalidOperationException>(); }
        Directory.Exists(store.Destination(original.Document)).Should().BeTrue();
        File.Exists(Path.Combine(store.Destination(original.Document), "manifest.yaml")).Should().BeTrue();
        (await store.PendingRenames(Token)).Should().BeEmpty();
        if (collision is not null) { (await store.Read("renamed", "1.0.0", Token))!.IsSameRevision(collision).Should().BeTrue(); }
    }

    [Theory]
    [InlineData("source")]
    [InlineData("destination")]
    [InlineData("destination-parent")]
    [InlineData("destination-permissions")]
    [InlineData("intent")]
    public async Task RecoveryRefusesChangedIdentitiesBytesAndIntentWithoutDeletingSubstitutions(string boundary)
    {
        using var paths = new OwnedStorageFixture();
        var store = new WindowsOwnedDeclarativeSkillStore(paths);
        var original = await Save(store, Document());
        var proposal = new OwnedSkillRename(original, "renamed");
        store.Checkpoint = point => { if (point is "rename-destination-verified") { throw new IOException("injected boundary"); } };
        await store.Invoking(value => Rename(value, proposal)).Should().ThrowAsync<OwnedSkillRenameUnconfirmedException>();
        var restart = new WindowsOwnedDeclarativeSkillStore(paths);
        var pending = (await restart.PendingRenames(Token)).Single();
        var source = store.Destination(original.Document);
        var destination = store.Destination(proposal.Renamed);
        if (boundary is "source" or "destination")
        { await File.AppendAllTextAsync(Path.Combine(boundary is "source" ? source : destination, "instructions.md"), "external", Token); }
        if (boundary is "destination-parent" or "destination-permissions")
        {
            var parent = Path.GetDirectoryName(destination)!;
            Directory.Move(parent, parent + "-external");
            if (boundary is "destination-parent") { RestrictedStorageDirectory.ForOwnedSkills(paths).CreateOwnedSkillDirectory(parent); }
            else { Directory.CreateDirectory(parent); }
        }
        if (boundary is "intent")
        { await File.WriteAllTextAsync(Directory.GetFiles(Path.Combine(paths.LocalRoot, "OwnedSkillRenames")).Single(), "{}", Token); }
        if (boundary is "source" or "destination")
        { await restart.Invoking(value => Rename(value, pending, recovery: true)).Should().ThrowAsync<OwnedSkillRenameUnconfirmedException>(); }
        else if (boundary is "destination-permissions")
        { await restart.Invoking(value => Rename(value, pending, recovery: true)).Should().ThrowAsync<UnauthorizedAccessException>(); }
        else { await restart.Invoking(value => Rename(value, pending, recovery: true)).Should().ThrowAsync<InvalidDataException>(); }
        Directory.Exists(source).Should().BeTrue();
        if (boundary is not ("destination-parent" or "destination-permissions")) { Directory.Exists(destination).Should().BeTrue(); }
    }

    [Fact]
    public async Task FinalSourceChangeRevocationAndPostReadbackFailureAreNotRolledBackOrReportedAsSuccess()
    {
        using var paths = new OwnedStorageFixture();
        var store = new WindowsOwnedDeclarativeSkillStore(paths);
        var original = await Save(store, Document());
        var proposal = new OwnedSkillRename(original, "renamed");
        var current = true;
        store.Checkpoint = point => { if (point is "rename-destination-verified") { current = false; } };
        await store.Invoking(value => Rename(value, proposal, eligible: () => current)).Should().ThrowAsync<OwnedSkillRenameUnconfirmedException>();
        (await store.PendingRenames(Token)).Should().ContainSingle();
        store.Checkpoint = point => { if (point is "rename-intent-retired") { throw new IOException("terminal boundary"); } };
        var recovery = () => Rename(store, proposal, recovery: true);
        var failure = await recovery.Should().ThrowAsync<OwnedSkillRenameUnconfirmedException>();
        failure.Which.Effect.Should().Be(OwnedSkillRenameEffect.NativeReadbackVerified);
        (await store.PendingRenames(Token)).Should().BeEmpty();
        (await store.Read("example", "1.0.0", Token)).Should().BeNull();
        (await store.Read("renamed", "1.0.0", Token))!.Document.RevisionDigest.Should().Be(proposal.Renamed.RevisionDigest);
    }

    [Fact]
    public async Task ObservationWorkflowRenamePreservesClosedStepsAndReopensWithoutRunningAnyStep()
    {
        using var paths = new OwnedStorageFixture();
        var store = new WindowsOwnedDeclarativeSkillStore(paths);
        var document = OwnedDeclarativeSkillDocument.CreateObservationWorkflow("inspection", "1.0.0", "Inspection", "Description",
            "{\"steps\":[{\"tool\":\"application.get_version\",\"input\":{}}]}");
        var saved = await Save(store, document);
        var renamed = await Rename(store, new(saved, "renamed"));
        var reopened = await new WindowsOwnedDeclarativeSkillStore(paths).Read("renamed", "1.0.0", Token);
        reopened!.IsSameRevision(renamed).Should().BeTrue();
        reopened.Document.Guidance.Should().Be(document.Guidance);
        reopened.Document.ObservationWorkflow!.Steps.Should().ContainSingle();
        reopened.Document.IsAvailableForInvocation.Should().BeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationJoinsOwnedNativeWorkAndRetainsOnlyExplicitPostPublicationRecovery(bool afterPublication)
    {
        using var paths = new OwnedStorageFixture();
        var store = new WindowsOwnedDeclarativeSkillStore(paths);
        var saved = await Save(store, Document());
        var proposal = new OwnedSkillRename(saved, "renamed");
        using var cancel = new CancellationTokenSource();
        store.Checkpoint = point =>
        {
            if (string.Equals(point, afterPublication ? "rename-destination-verified" : "before-commit", StringComparison.Ordinal))
            { cancel.Cancel(); }
        };
        var run = () => store.Rename(proposal, false, static () => true, operation => Task.FromResult(operation()), cancel.Token);
        if (afterPublication) { await run.Should().ThrowAsync<OwnedSkillRenameUnconfirmedException>(); }
        else { await run.Should().ThrowAsync<OperationCanceledException>(); }
        store.Checkpoint = null;
        (await store.PendingRenames(Token)).Should().HaveCount(afterPublication ? 1 : 0);
        Directory.GetDirectories(Path.Combine(paths.RoamingRoot, "Skills", "renamed"), ".owned-skill-*").Should().BeEmpty();
        if (afterPublication) { await Rename(store, proposal, recovery: true); }
        else { (await store.Read("example", "1.0.0", Token))!.IsSameRevision(saved).Should().BeTrue(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FinalComparisonRejectsSourceChangesAndNativeParentPinsPreventEscapes(bool changeSource)
    {
        using var paths = new OwnedStorageFixture();
        var store = new WindowsOwnedDeclarativeSkillStore(paths);
        var saved = await Save(store, Document());
        var proposal = new OwnedSkillRename(saved, "renamed");
        var attempts = 0;
        store.Checkpoint = point =>
        {
            if (point is not "before-commit") { return; }
            if (changeSource)
            { File.AppendAllText(Path.Combine(store.Destination(saved.Document), "instructions.md"), "external"); }
            else
            {
                foreach (var path in new[] { Path.Combine(paths.RoamingRoot, "Skills"),
                    Path.Combine(paths.RoamingRoot, "Skills", "example"), Path.Combine(paths.RoamingRoot, "Skills", "renamed") })
                {
                    var move = () => Directory.Move(path, path + "-escape");
                    move.Should().Throw<IOException>();
                    attempts++;
                }
            }
        };
        if (changeSource)
        {
            await store.Invoking(value => Rename(value, proposal)).Should().ThrowAsync<InvalidOperationException>();
            (await store.PendingRenames(Token)).Should().BeEmpty();
            (await store.Read("renamed", "1.0.0", Token)).Should().BeNull();
        }
        else
        {
            await Rename(store, proposal);
            attempts.Should().Be(3);
        }
    }

    [Theory]
    [InlineData("intent")]
    [InlineData("directory")]
    [InlineData("replacement")]
    public async Task MissingPreviouslyObservedPrivateRecoveryStateCannotReadmitOldOrNewUse(string boundary)
    {
        using var paths = new OwnedStorageFixture();
        var store = new WindowsOwnedDeclarativeSkillStore(paths);
        var original = await Save(store, Document());
        var proposal = new OwnedSkillRename(original, "renamed");
        store.Checkpoint = point => { if (point is "rename-destination-verified") { throw new IOException("injected boundary"); } };
        await store.Invoking(value => Rename(value, proposal)).Should().ThrowAsync<OwnedSkillRenameUnconfirmedException>();
        var root = Path.Combine(paths.LocalRoot, "OwnedSkillRenames");
        if (boundary is "intent") { File.Move(Directory.GetFiles(root).Single(), Path.Combine(paths.LocalRoot, "external-intent.json")); }
        else
        {
            Directory.Move(root, root + "-external");
            if (boundary is "replacement") { RestrictedStorageDirectory.ForOwnedSkillRenames(paths).CreateNew(); }
        }
        await store.Invoking(value => value.Read("example", "1.0.0", Token)).Should().ThrowAsync<InvalidDataException>();
        await store.Invoking(value => value.Read("renamed", "1.0.0", Token)).Should().ThrowAsync<InvalidDataException>();
        Directory.Exists(store.Destination(original.Document)).Should().BeTrue();
        Directory.Exists(store.Destination(proposal.Renamed)).Should().BeTrue();
    }
}
