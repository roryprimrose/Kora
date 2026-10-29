using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Text;
using AwesomeAssertions;
using Kora.Application.KnowledgeSources;
using Kora.Core.Auditing;
using Kora.Core.Context;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.KnowledgeSources;
using Kora.Windows.Context;
using Kora.Windows.IntegrationTests.Storage;
using Kora.Windows.KnowledgeSources;
using Kora.Windows.Storage;
using Microsoft.Data.Sqlite;

namespace Kora.Windows.IntegrationTests.KnowledgeSources;

[Collection("Host tracing")]
public sealed partial class WindowsKnowledgeSourceTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task CompleteSnapshotSurvivesReopenWithOriginalByteDigestsAndExplicitLexicalRetrieval()
    {
        using var fixture = new OwnedStorageFixture();
        var store = new WindowsKnowledgeSourceStore(fixture);
        var (snapshot, bytes) = Snapshot();
        var source = await store.PublishAsync(null, "Team notes", snapshot, bytes, Token);
        source.State.Should().Be(KnowledgeSourceState.Enabled);
        var reopened = new WindowsKnowledgeSourceStore(fixture);
        var listed = (await reopened.ListAsync(Token)).Should().ContainSingle().Which;
        listed.Folder.Files.Should().Equal(snapshot.Review.Metadata.Files);
        var restored = await reopened.ReadAsync(listed, Token);
        restored.Files[0].Reference.Should().Be(snapshot.Files[0].Reference);
        restored.Files[0].Text.Should().Be("alpha notes");
        var found = new LocalFileLexicalRetrieval().Search(restored, restored.Reference, "alpha", DateTimeOffset.UtcNow, Token);
        found.Outcome.Should().Be(LocalFileSearchOutcome.Matched);
        found.Citations.Should().ContainSingle().Which.Source.Should().Be(snapshot.Files[0].Reference);
        var stored = await File.ReadAllBytesAsync(Path.Combine(fixture.LocalRoot, "KnowledgeSourcesV1", "sources.db"), Token);
        Encoding.UTF8.GetString(stored).Should().NotContain("alpha notes");
    }

    [Fact]
    public async Task RefreshRevokesOldUseBeforeNewContentAndCompleteReplacementDoesNotReuseOldMembership()
    {
        using var fixture = new OwnedStorageFixture();
        var store = new WindowsKnowledgeSourceStore(fixture);
        var (snapshot, bytes) = Snapshot();
        var source = await store.PublishAsync(null, "notes", snapshot, bytes, Token);
        var pending = await store.BeginRefreshAsync(source, Token);
        pending.State.Should().Be(KnowledgeSourceState.Unavailable);
        await FluentActions.Awaiting(() => store.ReadAsync(source, Token)).Should().ThrowAsync<InvalidOperationException>();
        await FluentActions.Awaiting(() => store.ReadAsync(pending, Token)).Should().ThrowAsync<InvalidOperationException>();
        var replacement = Snapshot(source.Id, "replacement notes");
        var refreshed = await store.PublishAsync(pending, "notes", replacement.Snapshot, replacement.Bytes, Token);
        refreshed.Version.Should().BeGreaterThan(pending.Version);
        (await store.ReadAsync(refreshed, Token)).Files[0].Text.Should().Be("replacement notes");
        await FluentActions.Awaiting(() => store.ReadAsync(pending, Token)).Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task DisableRetainsCopiesButBlocksUseAndRemovalClearsOnlyInventoriedKoraCopies()
    {
        using var fixture = new OwnedStorageFixture();
        var store = new WindowsKnowledgeSourceStore(fixture);
        var (snapshot, bytes) = Snapshot();
        var original = Path.Combine(fixture.LocalRoot, "unrelated-original.txt");
        await File.WriteAllTextAsync(original, "original must remain", Token);
        var source = await store.PublishAsync(null, "notes", snapshot, bytes, Token);
        await store.DisableAsync(source, Token);
        var disabled = (await store.ListAsync(Token)).Single();
        disabled.State.Should().Be(KnowledgeSourceState.Disabled);
        disabled.StoredBytes.Should().Be(source.StoredBytes);
        await FluentActions.Awaiting(() => store.ReadAsync(disabled, Token)).Should().ThrowAsync<InvalidOperationException>();
        await store.RemoveAsync(disabled, Token);
        (await store.ListAsync(Token)).Should().BeEmpty();
        new FileInfo(Path.Combine(fixture.LocalRoot, "KnowledgeSourcesV1", "sources.db-journal")).Length.Should().Be(0);
        (await File.ReadAllTextAsync(original, Token)).Should().Be("original must remain");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task MissingOrCorruptPrivateStorageNeverReinitializesAnAuthorizedSource(bool missing)
    {
        using var fixture = new OwnedStorageFixture();
        var store = new WindowsKnowledgeSourceStore(fixture);
        var input = Snapshot();
        await store.PublishAsync(null, "notes", input.Snapshot, input.Bytes, Token);
        var path = Path.Combine(fixture.LocalRoot, "KnowledgeSourcesV1", "sources.db");
        if (missing) { File.Delete(path); }
        else { await File.WriteAllBytesAsync(path, [1, 2, 3], Token); }
        await FluentActions.Awaiting(() => store.ListAsync(Token)).Should().ThrowAsync<InvalidDataException>();
        File.Exists(path).Should().Be(!missing);
    }

    [Fact]
    public async Task UnexpectedArtifactOrDirectoryHoldsCleanupAndDoesNotDeleteUnknownCopies()
    {
        using var fixture = new OwnedStorageFixture();
        var store = new WindowsKnowledgeSourceStore(fixture);
        var input = Snapshot();
        var source = await store.PublishAsync(null, "notes", input.Snapshot, input.Bytes, Token);
        var partition = Path.Combine(fixture.LocalRoot, "KnowledgeSourcesV1");
        var unknown = Path.Combine(partition, "Artifacts", "unknown.pending");
        await File.WriteAllTextAsync(unknown, "unknown retained work", Token);
        await FluentActions.Awaiting(() => store.RemoveAsync(source, Token)).Should().ThrowAsync<InvalidDataException>();
        File.Exists(unknown).Should().BeTrue();
        File.Delete(unknown);
        var directory = Path.Combine(partition, "unexpected");
        Directory.CreateDirectory(directory);
        await FluentActions.Awaiting(() => store.RemoveAsync(source, Token)).Should().ThrowAsync<InvalidDataException>();
        Directory.Exists(directory).Should().BeTrue();
        Directory.Delete(directory);
        await store.RemoveAsync(source, Token);
        (await store.ListAsync(Token)).Should().BeEmpty();
    }

    [Fact]
    public async Task HardLinkedStorageHoldsUseAndDeletionWithoutModifyingTheAlias()
    {
        using var fixture = new OwnedStorageFixture();
        var store = new WindowsKnowledgeSourceStore(fixture);
        var input = Snapshot();
        var source = await store.PublishAsync(null, "notes", input.Snapshot, input.Bytes, Token);
        var path = Path.Combine(fixture.LocalRoot, "KnowledgeSourcesV1", "sources.db");
        var alias = Path.Combine(fixture.LocalRoot, "original-alias.txt");
        CreateHardLink(alias, path, nint.Zero).Should().BeTrue();
        var before = await File.ReadAllBytesAsync(alias, Token);
        await FluentActions.Awaiting(() => store.RemoveAsync(source, Token)).Should().ThrowAsync<InvalidDataException>();
        (await File.ReadAllBytesAsync(alias, Token)).Should().Equal(before);
        File.Delete(alias);
        await store.RemoveAsync(source, Token);
    }

    [Fact]
    public async Task ChangedOrPartialPublicationAndCancelledRefreshNeverExposeTheOldRevisionAsCurrent()
    {
        using var fixture = new OwnedStorageFixture();
        var store = new WindowsKnowledgeSourceStore(fixture);
        var input = Snapshot();
        var source = await store.PublishAsync(null, "notes", input.Snapshot, input.Bytes, Token);
        var pending = await store.BeginRefreshAsync(source, Token);
        await FluentActions.Awaiting(() => store.PublishAsync(pending, "notes", input.Snapshot, [], Token))
            .Should().ThrowAsync<InvalidDataException>();
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await FluentActions.Awaiting(() => store.PublishAsync(pending, "notes", input.Snapshot, input.Bytes, cancelled.Token))
            .Should().ThrowAsync<OperationCanceledException>();
        var reopened = (await new WindowsKnowledgeSourceStore(fixture).ListAsync(Token)).Single();
        reopened.State.Should().Be(KnowledgeSourceState.Unavailable);
        await FluentActions.Awaiting(() => store.ReadAsync(reopened, Token)).Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task NativeReadConsentExactMembershipAndRemovalNeverWriteTheOriginals()
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
        };
        ActivitySource.AddActivityListener(listener);
        using var host = HostActivity.BeginOperation(HostActivityLayer.Application, HostOperation.Request, RequestOrigin.LocalUi);
        using var fixture = new OwnedStorageFixture();
        var originalDirectory = Path.Combine(OwnedStorageFixture.ScratchParent(), ".knowledge-source-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(originalDirectory);
        try
        {
            var original = Path.Combine(originalDirectory, "notes.md");
            await File.WriteAllTextAsync(original, "synthetic alpha", Token);
            var before = await File.ReadAllBytesAsync(original, Token);
            var writeTime = File.GetLastWriteTimeUtc(original);
            var store = new WindowsKnowledgeSourceStore(fixture);
            using var manager = new KnowledgeSourceManager(new WindowsLocalFileInspector(fixture), store, new Audit(), TimeProvider.System);
            var review = await manager.ReviewAsync("native notes", originalDirectory, null, () => true, Token);
            manager.Candidate.Should().BeNull();
            var snapshot = await manager.CaptureAsync(review.ReviewId, () => true, Token);
            (await store.ListAsync(Token)).Should().BeEmpty();
            var source = await manager.PublishAsync(snapshot, () => true, Token);
            (await manager.OpenAsync(source, () => true, Token)).Files[0].Text.Should().Be("synthetic alpha");
            await File.WriteAllTextAsync(original, "changed", Token);
            await FluentActions.Awaiting(() => manager.OpenAsync(source, () => true, Token))
                .Should().ThrowAsync<InvalidDataException>();
            var fresh = await manager.ReviewAsync("native notes", originalDirectory, source, () => true, Token);
            fresh.Metadata.Files[0].ByteLength.Should().Be(7);
            var replacement = await manager.CaptureAsync(fresh.ReviewId, () => true, Token);
            var refreshed = await manager.PublishAsync(replacement, () => true, Token);
            await manager.RemoveAsync(refreshed, DateTimeOffset.UtcNow, () => true, Token);
            (await File.ReadAllTextAsync(original, Token)).Should().Be("changed");
            // Restore the test-authored bytes to demonstrate removal never opens originals for writing.
            await File.WriteAllBytesAsync(original, before, Token);
            File.SetLastWriteTimeUtc(original, writeTime);
            var last = await manager.ReviewAsync("another", originalDirectory, null, () => true, Token);
            var lastSnapshot = await manager.CaptureAsync(last.ReviewId, () => true, Token);
            var lastSource = await manager.PublishAsync(lastSnapshot, () => true, Token);
            await manager.RemoveAsync(lastSource, DateTimeOffset.UtcNow, () => true, Token);
            (await File.ReadAllBytesAsync(original, Token)).Should().Equal(before);
            File.GetLastWriteTimeUtc(original).Should().Be(writeTime);
        }
        finally { Directory.Delete(originalDirectory, recursive: true); }
    }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task InterruptedPublicationCannotPublishPartialNewOrRefreshedSource(bool refresh)
        {
            using var fixture = new OwnedStorageFixture();
            var store = new WindowsKnowledgeSourceStore(fixture);
            var input = Snapshot();
            KnowledgeSource? pending = null;
            if (refresh)
            {
                var original = await store.PublishAsync(null, "notes", input.Snapshot, input.Bytes, Token);
                pending = await store.BeginRefreshAsync(original, Token);
            }
            var interrupted = new WindowsKnowledgeSourceStore(fixture) { TransactionCheckpoint = new Interrupt(1) };
            await FluentActions.Awaiting(() => interrupted.PublishAsync(pending, "notes", input.Snapshot, input.Bytes, Token))
                .Should().ThrowAsync<IOException>();
            var rows = await new WindowsKnowledgeSourceStore(fixture).ListAsync(Token);
            if (refresh)
            {
                rows.Should().ContainSingle().Which.State.Should().Be(KnowledgeSourceState.Unavailable);
                await FluentActions.Awaiting(() => store.ReadAsync(rows.Single(), Token)).Should().ThrowAsync<InvalidOperationException>();
            }
            else { rows.Should().BeEmpty(); }
        }

        [Fact]
        public async Task InterruptedRemovalPersistsRevocationAndInventoriedRetryCompletesCleanup()
        {
            using var fixture = new OwnedStorageFixture();
            var store = new WindowsKnowledgeSourceStore(fixture);
            var input = Snapshot();
            var original = await store.PublishAsync(null, "notes", input.Snapshot, input.Bytes, Token);
            var interrupted = new WindowsKnowledgeSourceStore(fixture) { TransactionCheckpoint = new Interrupt(2) };
            await FluentActions.Awaiting(() => interrupted.RemoveAsync(original, Token)).Should().ThrowAsync<IOException>();
            var pending = (await new WindowsKnowledgeSourceStore(fixture).ListAsync(Token)).Single();
            pending.State.Should().Be(KnowledgeSourceState.Removing);
            await FluentActions.Awaiting(() => store.ReadAsync(pending, Token)).Should().ThrowAsync<InvalidOperationException>();
            await FluentActions.Awaiting(() => store.BeginRefreshAsync(pending, Token)).Should().ThrowAsync<InvalidOperationException>();
            await FluentActions.Awaiting(() => store.DisableAsync(pending, Token)).Should().ThrowAsync<InvalidOperationException>();
            await store.RemoveAsync(pending, Token);
            (await store.ListAsync(Token)).Should().BeEmpty();
            new FileInfo(Path.Combine(fixture.LocalRoot, "KnowledgeSourcesV1", "sources.db-journal")).Length.Should().Be(0);
        }

        private sealed class Interrupt(int atCommit) : ISqliteTransactionCheckpoint
        {
            private int commits;
            public void BeforeWrite(SqliteConnection connection, SqliteTransaction transaction) { }
            public void BeforeCommit(SqliteConnection connection, SqliteTransaction transaction)
            {
                if (++commits == atCommit) { throw new IOException("Synthetic interruption before atomic commit."); }
            }
        }
    private sealed class Audit : ISecurityAuditLog
    {
        public void Write(SecurityAuditEvent auditEvent) { }
    }

    private static (KnowledgeSourceRevision Snapshot, IReadOnlyList<ReadOnlyMemory<byte>> Bytes) Snapshot(Guid? id = null,
        string text = "alpha notes")
    {
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(text)).ToArray();
        var metadata = new LocalFileMetadata(@"C:\Team\notes.md", "physical-file", bytes.Length, DateTimeOffset.UtcNow);
        var review = new KnowledgeSourceReview(Guid.NewGuid(), id ?? Guid.NewGuid(), HostRequest.Create(RequestOrigin.LocalUi),
            new(@"C:\Team", "physical-root", KnowledgeSourceScope.ImmediateFiles, [new(@"C:\Team", "physical-root")], [metadata]));
        var file = new LocalFileRevision(new(review.ReviewId, review.SourceId, review.Request, metadata), bytes, DateTimeOffset.UtcNow);
        return (new(review, [file]), new ReadOnlyMemory<byte>[] { bytes });
    }

    [LibraryImport("kernel32.dll", EntryPoint = "CreateHardLinkW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CreateHardLink(string alias, string original, nint reserved);
}
