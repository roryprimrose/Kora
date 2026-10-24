using AwesomeAssertions;
using Kora.Core.Skills;
using Kora.Windows.IntegrationTests.Storage;
using Kora.Windows.Skills;
using Kora.Windows.Storage;

namespace Kora.Windows.IntegrationTests.Skills;

public sealed class OwnedSkillPackagePublicationTests
{
    [Fact]
    public void NativePopulatedDirectoryRenameUsesThePinnedParentWithoutPathPublication()
    {
        using var paths = new OwnedStorageFixture();
        var directory = RestrictedStorageDirectory.ForOwnedSkills(paths);
        Directory.CreateDirectory(paths.RoamingRoot);
        directory.CreateNew();
        var id = Path.Combine(directory.Root, "example");
        directory.CreateOwnedSkillDirectory(id);
        var stage = Path.Combine(id, ".owned-skill-" + Guid.NewGuid().ToString("N"));
        directory.CreateOwnedSkillDirectory(stage);
        using (var stream = directory.CreateNewFile(Path.Combine(stage, "manifest.yaml")))
        { stream.Write("complete"u8); stream.Flush(true); }
        using var pins = new OwnedSkillPackagePublication();
        var parent = pins.PinDirectory(id);
        var staged = pins.PinDirectory(stage, forRename: true);
        OwnedSkillPackagePublication.Commit(staged, parent, "1.0.0");
        Directory.Exists(Path.Combine(id, "1.0.0")).Should().BeTrue();
    }
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static OwnedDeclarativeSkillDocument Document(string version = "1.0.0") =>
        OwnedDeclarativeSkillDocument.Create("example", version, "Example", "Description", "literal\nowned skill save");
    private static Task<OwnedDeclarativeSkillSnapshot> Save(WindowsOwnedDeclarativeSkillStore store,
        OwnedDeclarativeSkillDocument document, CancellationToken token, Func<bool>? eligible = null) =>
        store.Publish(document, eligible ?? (static () => true), operation => Task.FromResult(operation()), token);

    [Fact]
    public async Task WholeDirectoryCommitDurableBothFileReadbackAndNewStoreReopenRemainInactive()
    {
        using var paths = new OwnedStorageFixture();
        var store = new WindowsOwnedDeclarativeSkillStore(paths);
        var document = Document();
        var saved = await Save(store, document, Token);
        saved.Document.ManifestBytes.ToArray().Should().Equal(document.ManifestBytes.ToArray());
        saved.Document.InstructionBytes.ToArray().Should().Equal(document.InstructionBytes.ToArray());
        Directory.GetFileSystemEntries(store.Destination(document)).Select(Path.GetFileName)
            .Should().BeEquivalentTo("manifest.yaml", "instructions.md");
        var reopened = await new WindowsOwnedDeclarativeSkillStore(paths).Read("example", "1.0.0", Token);
        reopened!.DirectoryIdentity.Should().Be(saved.DirectoryIdentity);
        reopened.ManifestIdentity.Should().Be(saved.ManifestIdentity);
        reopened.InstructionsIdentity.Should().Be(saved.InstructionsIdentity);
        reopened.Document.IsAvailableForInvocation.Should().BeFalse();
        (await store.List(Token)).Should().ContainSingle();
        var collision = () => Save(store, document, Token);
        await collision.Should().ThrowAsync<InvalidOperationException>();
        (await store.Read("example", "1.0.0", Token))!.ManifestIdentity.Should().Be(saved.ManifestIdentity);
        await Save(store, Document("1.0.1"), Token);
        (await store.List(Token)).Should().HaveCount(2);
    }

    [Theory]
    [InlineData("manifest-flushed")]
    [InlineData("instructions-flushed")]
    [InlineData("before-commit")]
    [InlineData("after-commit")]
    public async Task FaultsNeverExposePartialPackagesAndPostCommitFaultsAreExplicitlyUncertain(string point)
    {
        using var paths = new OwnedStorageFixture();
        var store = new WindowsOwnedDeclarativeSkillStore(paths)
        {
            Checkpoint = checkpoint => { if (string.Equals(point, checkpoint, StringComparison.Ordinal)) { throw new IOException("injected fault"); } },
        };
        var run = () => Save(store, Document(), Token);
        if (point is "after-commit") { await run.Should().ThrowAsync<OwnedSkillPublicationUnconfirmedException>(); }
        else { await run.Should().ThrowAsync<IOException>(); }
        store.Checkpoint = null;
        var catalogue = await store.List(Token);
        catalogue.Should().HaveCount(point is "after-commit" ? 1 : 0);
        if (catalogue.Count == 1)
        {
            catalogue[0].Document.ManifestText.Should().Be(Document().ManifestText);
            catalogue[0].Document.Guidance.Should().Be(Document().Guidance);
        }
        Directory.EnumerateDirectories(Path.Combine(paths.RoamingRoot, "Skills", "example"), ".owned-skill-*").Should().BeEmpty();
    }

    [Fact]
    public async Task CancelledOrRevokedFinalCommitCannotPublishAndDoesNotDeleteExternalCollision()
    {
        using var paths = new OwnedStorageFixture();
        var store = new WindowsOwnedDeclarativeSkillStore(paths);
        var current = true;
        store.Checkpoint = point => { if (point is "before-commit") { current = false; } };
        var save = () => Save(store, Document(), Token, () => current);
        await save.Should().ThrowAsync<InvalidOperationException>();
        (await store.List(Token)).Should().BeEmpty();
        store.Checkpoint = null;
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        var cancelled = () => Save(store, Document(), cancel.Token);
        await cancelled.Should().ThrowAsync<OperationCanceledException>();
        (await store.List(Token)).Should().BeEmpty();
    }

    [Fact]
    public async Task DirectoryPinsRejectRootAndIdReplacementThroughCommit()
    {
        using var paths = new OwnedStorageFixture();
        var store = new WindowsOwnedDeclarativeSkillStore(paths);
        var attempts = 0;
        store.Checkpoint = point =>
        {
            if (point is not "before-commit") { return; }
            foreach (var path in new[] { Path.Combine(paths.RoamingRoot, "Skills"), Path.Combine(paths.RoamingRoot, "Skills", "example") })
            {
                var replace = () => Directory.Move(path, path + "-substituted");
                replace.Should().Throw<IOException>();
                attempts++;
            }
        };
        var saved = await Save(store, Document(), Token);
        attempts.Should().Be(2);
        saved.Document.Guidance.Should().Be(Document().Guidance);
    }

    [Fact]
    public async Task ExternalStagingReplacementIsRejectedAndNeverDeletedByCleanup()
    {
        using var paths = new OwnedStorageFixture();
        var store = new WindowsOwnedDeclarativeSkillStore(paths);
        string? replacement = null;
        store.Checkpoint = point =>
        {
            if (point is not "before-commit") { return; }
            var stage = Directory.GetDirectories(Path.Combine(paths.RoamingRoot, "Skills", "example"), ".owned-skill-*").Single();
            var manifest = Path.Combine(stage, "manifest.yaml");
            File.Move(manifest, Path.Combine(stage, "original.manifest"));
            replacement = manifest;
            using var external = RestrictedStorageDirectory.ForOwnedSkills(paths).CreateNewFile(manifest);
            external.Write("external data must survive"u8);
        };
        var run = () => Save(store, Document(), Token);
        await run.Should().ThrowAsync<InvalidDataException>();
        File.ReadAllText(replacement!).Should().Be("external data must survive");
        Directory.Exists(store.Destination(Document())).Should().BeFalse();
    }

    [Fact]
    public async Task ChangedPublishedBytesAreUncertainNotSuccessfulOrSilentlyDeleted()
    {
        using var paths = new OwnedStorageFixture();
        var store = new WindowsOwnedDeclarativeSkillStore(paths);
        store.Checkpoint = point =>
        {
            if (point is "after-commit") { File.WriteAllText(Path.Combine(store.Destination(Document()), "instructions.md"), "Changed"); }
        };
        var run = () => Save(store, Document(), Token);
        await run.Should().ThrowAsync<OwnedSkillPublicationUnconfirmedException>();
        File.ReadAllText(Path.Combine(store.Destination(Document()), "instructions.md")).Should().Be("Changed");
    }

    [Fact]
    public async Task CancellationRetainsOwnershipUntilIgnoringIoActuallyJoins()
    {
        using var paths = new OwnedStorageFixture();
        var store = new WindowsOwnedDeclarativeSkillStore(paths);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        using var cancelled = new CancellationTokenSource();
        store.Checkpoint = point =>
        {
            if (point is "before-commit") { entered.SetResult(); release.Wait(Token); }
        };
        var operation = Save(store, Document(), cancelled.Token);
        await entered.Task.WaitAsync(Token);
        cancelled.Cancel();
        operation.IsCompleted.Should().BeFalse();
        release.Set();
        var joined = () => operation;
        await joined.Should().ThrowAsync<OperationCanceledException>();
        (await store.List(Token)).Should().BeEmpty();
    }

    [Fact]
    public async Task TwoWritersNeverReplaceOneDestinationAndDefaultLocalStorageRemainsSeparate()
    {
        using var paths = new OwnedStorageFixture();
        var first = new WindowsOwnedDeclarativeSkillStore(paths);
        var second = new WindowsOwnedDeclarativeSkillStore(paths);
        await first.List(Token);
        var operations = new[] { Save(first, Document(), Token), Save(second, Document(), Token) };
        var joined = () => Task.WhenAll(operations);
        await joined.Should().ThrowAsync<InvalidOperationException>();
        operations.Count(t => t.IsCompletedSuccessfully).Should().Be(1);
        (await first.List(Token)).Should().ContainSingle();
        Directory.Exists(Path.Combine(paths.LocalRoot, "DurableStorageV1")).Should().BeFalse();
        Directory.Exists(Path.Combine(paths.RoamingRoot, "Skills", "Keys")).Should().BeFalse();
        Directory.Exists(Path.Combine(paths.RoamingRoot, "Skills", "Artifacts")).Should().BeFalse();
    }
}
