using System.Diagnostics;
using System.Text.Json.Nodes;
using AwesomeAssertions;
using Kora.Application.Configuration;
using Kora.Application.KnowledgeSources;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.KnowledgeSources;
using Kora.Windows.Context;
using Kora.Windows.IntegrationTests.Storage;
using Kora.Windows.KnowledgeSources;

namespace Kora.Windows.IntegrationTests.KnowledgeSources;

public sealed partial class WindowsKnowledgeSourceTests
{
    [Theory]
    [InlineData(2, 5, 10, true)]
    [InlineData(3, 5, 15, false)]
    [InlineData(2, 6, 12, false)]
    [InlineData(2, 5, 9, false)]
    public async Task NativeCaptureRejectsEntireSourceAboveEachInclusiveLoweredThreshold(int count, int bytes, int total, bool accepted)
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
        };
        ActivitySource.AddActivityListener(listener);
        using var host = HostActivity.BeginOperation(HostActivityLayer.Application, HostOperation.Request, RequestOrigin.LocalUi);
        using var fixture = new OwnedStorageFixture();
        var root = Path.Combine(OwnedStorageFixture.ScratchParent(), ".capture-limits-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            for (var i = 0; i < count; i++) { await File.WriteAllTextAsync(Path.Combine(root, $"{i}.md"), new string('a', bytes), Token); }
            var inspector = new WindowsLocalFileInspector(fixture);
            var limits = new KnowledgeSourceCaptureLimits(2, 5, total);
            if (!accepted)
            {
                await FluentActions.Awaiting(() => inspector.InspectSourceAsync(root, KnowledgeSourceScope.ImmediateFiles, limits, Token))
                    .Should().ThrowAsync<InvalidDataException>();
            }
            else
            {
                using var selection = await inspector.InspectSourceAsync(root, KnowledgeSourceScope.ImmediateFiles, limits, Token);
                selection.Metadata.CaptureLimits.Should().Be(limits);
                selection.Metadata.Files.Should().HaveCount(count);
                selection.Metadata.CombinedBytes.Should().Be(total);
                await selection.ValidateAsync(Token);
                foreach (var item in selection.Metadata.Files) { (await selection.ReadAsync(item, Token)).Should().HaveCount(bytes); }
            }
            for (var i = 0; i < count; i++)
            {
                using var writable = new FileStream(Path.Combine(root, $"{i}.md"), FileMode.Open, FileAccess.Write, FileShare.None);
                writable.Length.Should().Be(bytes);
            }
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task NewRegistryAndIndexedSnapshotPersistSameImmutableTupleWithoutChangingIndexAuthority()
    {
        using var fixture = new OwnedStorageFixture();
        var limits = new KnowledgeSourceCaptureLimits(2, 100, 200);
        var input = Snapshot(limits: limits);
        var store = new WindowsKnowledgeSourceStore(fixture);
        var source = await store.PublishAsync(null, "lowered", input.Snapshot, input.Bytes, Token);
        var reopened = new WindowsKnowledgeSourceStore(fixture);
        var exact = (await reopened.ListAsync(Token)).Single();
        exact.Folder.CaptureLimits.Should().Be(limits);
        var snapshot = await reopened.ReadAsync(exact, Token);
        snapshot.Review.Metadata.CaptureLimits.Should().Be(limits);
        snapshot.Reference.Should().Be(input.Snapshot.Reference);
        snapshot.Files.Single().Reference.Should().Be(input.Snapshot.Files.Single().Reference);
        snapshot.Indexes.Should().ContainSingle();
        var wrong = source with { Folder = new(source.Folder.CanonicalPath, source.Folder.DirectoryIdentity,
            source.Folder.Scope, source.Folder.Directories, source.Folder.Files) };
        await FluentActions.Awaiting(() => reopened.ReadAsync(wrong, Token)).Should().ThrowAsync<InvalidOperationException>();
        var refresh = await reopened.BeginRefreshAsync(exact, Token);
        var changed = Snapshot(exact.Id, limits: new(1, 100, 100));
        var published = await reopened.PublishAsync(refresh, "lowered", changed.Snapshot, changed.Bytes, Token);
        reopened.ReusedIndexEntries.Should().Be(1);
        published.Folder.CaptureLimits.Should().Be(new KnowledgeSourceCaptureLimits(1, 100, 100));
        input.Snapshot.Review.Metadata.CaptureLimits.Should().Be(limits);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("invalid")]
    [InlineData("increased")]
    [InlineData("unknown")]
    public async Task MalformedNewLimitsRegistryNeverBecomesLegacyDefaults(string scenario)
    {
        using var fixture = new OwnedStorageFixture();
        var input = Snapshot();
        var store = new WindowsKnowledgeSourceStore(fixture);
        await store.PublishAsync(null, "lowered", input.Snapshot, input.Bytes, Token);
        RewriteStorage(fixture, info =>
        {
            switch (scenario)
            {
                case "missing": info.Remove("CaptureLimits"); break;
                case "invalid": info["CaptureLimits"]!["MaximumFiles"] = 0; break;
                case "increased": info["CaptureLimits"]!["MaximumFileBytes"] = 262145; break;
                case "unknown": info["CaptureLimits"]!["Other"] = 1; break;
            }
        });
        await FluentActions.Awaiting(() => new WindowsKnowledgeSourceStore(fixture).ListAsync(Token))
            .Should().ThrowAsync<InvalidDataException>();
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("disagree")]
    [InlineData("invalid")]
    [InlineData("legacy-with-limits")]
    public async Task SnapshotLimitsMustBeKnownValidAndAgreeWithRegistry(string scenario)
    {
        using var fixture = new OwnedStorageFixture();
        var input = Snapshot();
        var store = new WindowsKnowledgeSourceStore(fixture);
        await store.PublishAsync(null, "lowered", input.Snapshot, input.Bytes, Token);
        RewriteIndexedSnapshot(fixture, data =>
        {
            switch (scenario)
            {
                case "missing": data.Remove("CaptureLimits"); break;
                case "disagree": data["CaptureLimits"]!["MaximumFiles"] = 1; break;
                case "invalid": data["CaptureLimits"]!["MaximumCombinedBytes"] = 0; break;
                case "legacy-with-limits": data["Version"] = 3; break;
            }
        }, 2);
        var reopened = new WindowsKnowledgeSourceStore(fixture);
        var exact = (await reopened.ListAsync(Token)).Single();
        await FluentActions.Awaiting(() => reopened.ReadAsync(exact, Token)).Should().ThrowAsync<InvalidDataException>();
    }

    [Fact]
    public async Task LegacyDirectoryRegistryAndIndexedSnapshotReadAtHistoricalHardMaxima()
    {
        using var fixture = new OwnedStorageFixture();
        var input = Snapshot();
        var store = new WindowsKnowledgeSourceStore(fixture);
        await store.PublishAsync(null, "legacy", input.Snapshot, input.Bytes, Token);
        RewriteIndexedSnapshot(fixture, data => { data["Version"] = 3; data.Remove("CaptureLimits"); }, 2);
        RewriteStorage(fixture, info => { info["FormatVersion"] = 2; info.Remove("CaptureLimits"); });
        var reopened = new WindowsKnowledgeSourceStore(fixture);
        var exact = (await reopened.ListAsync(Token)).Single();
        exact.Folder.CaptureLimits.Should().Be(KnowledgeSourceCaptureLimits.HardMaximum);
        var restored = await reopened.ReadAsync(exact, Token);
        restored.Indexes.Should().ContainSingle();
        restored.Files.Single().Text.Should().Be("alpha notes");
        restored.Review.Metadata.CaptureLimits.Should().Be(KnowledgeSourceCaptureLimits.HardMaximum);
    }

    [Fact]
    public async Task FailedNumericCleanupLeavesRemovingInventoryAndReopenedRetryCleansOnlyExactGuid()
    {
        using var fixture = new OwnedStorageFixture();
        var preferences = new LocalKnowledgeSourceCaptureLimitsPreferences(fixture);
        var failing = new CleanupPreferences(preferences) { FailRemove = true };
        var store = new WindowsKnowledgeSourceStore(fixture, failing);
        var input = Snapshot();
        var source = await store.PublishAsync(null, "lowered", input.Snapshot, input.Bytes, Token);
        var other = Guid.NewGuid();
        preferences.Save(source.Id, new(2, 100, 200));
        preferences.Save(other, new(1, 1, 1));
        await FluentActions.Awaiting(() => store.RemoveAsync(source, Token)).Should().ThrowAsync<IOException>();
        var removing = (await store.ListAsync(Token)).Single();
        removing.State.Should().Be(KnowledgeSourceState.Removing);
        preferences.Load(source.Id).Should().NotBeNull();
        await FluentActions.Awaiting(() => store.ReadAsync(removing, Token)).Should().ThrowAsync<InvalidOperationException>();
        var reopened = new WindowsKnowledgeSourceStore(fixture, new LocalKnowledgeSourceCaptureLimitsPreferences(fixture));
        await reopened.RemoveAsync(removing, Token);
        (await reopened.ListAsync(Token)).Should().BeEmpty();
        preferences.Load(source.Id).Should().BeNull();
        preferences.Load(other).Should().Be(new KnowledgeSourceCaptureLimits(1, 1, 1));
    }

    [Fact]
    public async Task PreferenceChangesDoNotRefreshButNextExplicitNativeRefreshRejectsExcessBeforeAnyBodyCapture()
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
        };
        ActivitySource.AddActivityListener(listener);
        using var host = HostActivity.BeginOperation(HostActivityLayer.Application, HostOperation.Request, RequestOrigin.LocalUi);
        using var fixture = new OwnedStorageFixture();
        var root = Path.Combine(OwnedStorageFixture.ScratchParent(), ".capture-refresh-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "a.md"), "alpha", Token);
            var preferences = new LocalKnowledgeSourceCaptureLimitsPreferences(fixture);
            var store = new WindowsKnowledgeSourceStore(fixture, preferences);
            using var manager = new KnowledgeSourceManager(new WindowsLocalFileInspector(fixture), store, new Audit(), TimeProvider.System, preferences);
            var review = await manager.ReviewAsync("Team", root, null, () => true, Token);
            var snapshot = await manager.CaptureAsync(review.ReviewId, () => true, Token);
            var source = await manager.PublishAsync(snapshot, () => true, Token);
            preferences.Save(source.Id, new(1, 1, 1));
            (await store.ListAsync(Token)).Single().Version.Should().Be(source.Version);
            (await manager.OpenAsync(source, () => true, Token)).Review.Metadata.CaptureLimits.Should().Be(KnowledgeSourceCaptureLimits.HardMaximum);
            await FluentActions.Awaiting(() => manager.ReviewAsync("Team", root, source, () => true, Token))
                .Should().ThrowAsync<InvalidDataException>();
            manager.Review.Should().BeNull();
            manager.Candidate.Should().BeNull();
            (await store.ListAsync(Token)).Single().State.Should().Be(KnowledgeSourceState.Unavailable);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private sealed class CleanupPreferences(IKnowledgeSourceCaptureLimitsPreferences inner) : IKnowledgeSourceCaptureLimitsPreferences
    {
        internal bool FailRemove;
        public KnowledgeSourceCaptureLimits? Load(Guid sourceId) => inner.Load(sourceId);
        public KnowledgeSourceCaptureLimits? ReadBack(Guid sourceId) => inner.ReadBack(sourceId);
        public void BeginWrite(Guid sourceId) => inner.BeginWrite(sourceId);
        public void Save(Guid sourceId, KnowledgeSourceCaptureLimits? limits) => inner.Save(sourceId, limits);
        public void ConfirmWrite(Guid sourceId) => inner.ConfirmWrite(sourceId);
        public void Remove(Guid sourceId)
        {
            if (FailRemove) { throw new IOException("Synthetic failed numeric cleanup."); }
            inner.Remove(sourceId);
        }
    }
}
