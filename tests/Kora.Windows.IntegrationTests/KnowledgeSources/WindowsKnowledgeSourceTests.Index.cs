using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AwesomeAssertions;
using Kora.Core.Context;
using Kora.Core.Hosting;
using Kora.Core.KnowledgeSources;
using Kora.Windows.IntegrationTests.Storage;
using Kora.Windows.KnowledgeSources;
using Kora.Windows.Storage;
using Microsoft.Data.Sqlite;

namespace Kora.Windows.IntegrationTests.KnowledgeSources;

public sealed partial class WindowsKnowledgeSourceTests
{
    [Fact]
    public async Task AtomicIndexRestartRefreshReusesOnlyUnchangedDigestsAndRemovalRetiresDerivedState()
    {
        using var fixture = new OwnedStorageFixture();
        var store = new WindowsKnowledgeSourceStore(fixture);
        var input = IndexedSnapshot();
        var source = await store.PublishAsync(null, "indexed", input.Snapshot, input.Bytes, Token);
        store.ReusedIndexEntries.Should().Be(0);
        var unrelated = Snapshot(text: "unrelated retained");
        var other = await store.PublishAsync(null, "other", unrelated.Snapshot, unrelated.Bytes, Token);
        var reopened = new WindowsKnowledgeSourceStore(fixture);
        var restored = await reopened.ReadAsync(source, Token);
        reopened.IndexReadValidations.Should().Be(1);
        (await reopened.ReadAsync(source, Token)).Should().BeSameAs(restored);
        reopened.IndexReadValidations.Should().Be(1, "repeated exact reads validate durable bytes without re-chunking or tokenizing");
        restored.Indexes.Should().HaveCount(3);
        var before = new LocalFileLexicalRetrieval().Search(restored, restored.Reference, "alpha", DateTimeOffset.UnixEpoch, Token);
        before.Citations.Should().HaveCount(3);
        foreach (var citation in before.Citations) { restored.ResolveCitation(citation).Text.Should().Be(citation.Excerpt); }
        var pending = await reopened.BeginRefreshAsync(source, Token);
        await FluentActions.Awaiting(() => reopened.ReadAsync(pending, Token)).Should().ThrowAsync<InvalidOperationException>();
        var replacement = IndexedSnapshot(source.Id, ["alpha unchanged", "beta changed"]);
        var refreshed = await reopened.PublishAsync(pending, "indexed", replacement.Snapshot, replacement.Bytes, Token);
        reopened.ReusedIndexEntries.Should().Be(1);
        reopened.IndexReadValidations.Should().Be(1, "explicit refresh reuses the previously validated unchanged entries");
        var current = await new WindowsKnowledgeSourceStore(fixture).ReadAsync(refreshed, Token);
        current.Indexes.Should().HaveCount(2);
        current.Indexes![0].Digest.Should().Be(restored.Indexes![0].Digest);
        var result = new LocalFileLexicalRetrieval().Search(current, current.Reference, "alpha", DateTimeOffset.UnixEpoch, Token);
        result.Citations.Should().ContainSingle().Which.Source.Should().Be(replacement.Snapshot.Files[0].Reference);
        await reopened.RemoveAsync(refreshed, Token);
        await FluentActions.Awaiting(() => reopened.ReadAsync(refreshed, Token)).Should().ThrowAsync<InvalidOperationException>();
        (await reopened.ListAsync(Token)).Should().ContainSingle().Which.Id.Should().Be(other.Id);
        (await reopened.ReadAsync(other, Token)).Files.Single().Text.Should().Be("unrelated retained");
        new FileInfo(Path.Combine(fixture.LocalRoot, "KnowledgeSourcesV1", "sources.db-journal")).Length.Should().Be(0);
    }

    [Theory]
    [InlineData("version")]
    [InlineData("policy")]
    [InlineData("digest")]
    [InlineData("tokens")]
    [InlineData("span")]
    [InlineData("missing")]
    [InlineData("source")]
    [InlineData("reference")]
    [InlineData("envelope")]
    public async Task AuthenticatedButTamperedIndexNeverBecomesCurrentRetrieval(string defect)
    {
        using var fixture = new OwnedStorageFixture();
        var store = new WindowsKnowledgeSourceStore(fixture);
        var input = Snapshot();
        var source = await store.PublishAsync(null, "tampered", input.Snapshot, input.Bytes, Token);
        _ = await store.ReadAsync(source, Token);
        RewriteIndexedSnapshot(fixture, data =>
        {
            var index = data["Indexes"]![0]!;
            switch (defect)
            {
                case "version": index["Version"] = "unknown"; break;
                case "policy": index["PolicyVersion"] = "markdown-structure-v2"; break;
                case "digest": index["Digest"] = new string('A', 64); break;
                case "tokens": index["Chunks"]![0]!["Frequencies"]!["ALPHA"] = 16; break;
                case "span": index["Chunks"]![0]!["Length"] = 1; break;
                case "missing": data["Indexes"] = null; break;
                case "source": data["Id"] = Guid.NewGuid(); break;
                case "reference": data["Reference"]!["SourceId"] = Guid.NewGuid(); break;
            }
        }, string.Equals(defect, "envelope", StringComparison.Ordinal) ? 99 : 2);
        var reopened = new WindowsKnowledgeSourceStore(fixture);
        var exact = (await reopened.ListAsync(Token)).Single();
        await FluentActions.Awaiting(() => store.ReadAsync(exact, Token)).Should().ThrowAsync<InvalidDataException>();
        await FluentActions.Awaiting(() => reopened.ReadAsync(exact, Token)).Should().ThrowAsync<InvalidDataException>();
        var pending = await reopened.BeginRefreshAsync(exact, Token);
        await FluentActions.Awaiting(() => reopened.PublishAsync(pending, "tampered", input.Snapshot, input.Bytes, Token))
            .Should().ThrowAsync<InvalidDataException>();
        (await reopened.ListAsync(Token)).Single().State.Should().Be(KnowledgeSourceState.Unavailable);
        await reopened.RemoveAsync((await reopened.ListAsync(Token)).Single(), Token);
        (await reopened.ListAsync(Token)).Should().BeEmpty();
        source.Id.Should().Be(exact.Id);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task CancellationOrInterruptionBeforeCommitCannotPublishHalfAnIndex(bool refresh, bool cancel)
    {
        using var fixture = new OwnedStorageFixture();
        var store = new WindowsKnowledgeSourceStore(fixture);
        var input = IndexedSnapshot();
        KnowledgeSource? pending = null;
        if (refresh)
        { pending = await store.BeginRefreshAsync(await store.PublishAsync(null, "atomic", input.Snapshot, input.Bytes, Token), Token); }
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var interrupted = new WindowsKnowledgeSourceStore(fixture)
        { TransactionCheckpoint = cancel ? new CancelIndexCommit(cancellation) : new Interrupt(1) };
        var action = () => interrupted.PublishAsync(pending, "atomic", input.Snapshot, input.Bytes, cancellation.Token);
        if (cancel) { await action.Should().ThrowAsync<OperationCanceledException>(); }
        else { await action.Should().ThrowAsync<IOException>(); }
        var rows = await new WindowsKnowledgeSourceStore(fixture).ListAsync(Token);
        if (!refresh) { rows.Should().BeEmpty(); return; }
        var unavailable = rows.Should().ContainSingle().Which;
        unavailable.State.Should().Be(KnowledgeSourceState.Unavailable);
        await FluentActions.Awaiting(() => store.ReadAsync(unavailable, Token)).Should().ThrowAsync<InvalidOperationException>();
        var published = await store.PublishAsync(unavailable, "atomic", input.Snapshot, input.Bytes, Token);
        store.ReusedIndexEntries.Should().Be(3);
        (await store.ReadAsync(published, Token)).Indexes.Should().HaveCount(3);
    }

    [Fact]
    public async Task MaximumExistingSourceByteAndFileBudgetPublishesCompleteIndexedSnapshot()
    {
        using var fixture = new OwnedStorageFixture();
        var text = "alpha " + new string('x', 32762);
        var input = IndexedSnapshot(texts: Enumerable.Repeat(text, 32).ToArray());
        input.Snapshot.Review.Metadata.CombinedBytes.Should().Be(1024 * 1024);
        var store = new WindowsKnowledgeSourceStore(fixture);
        var saved = await store.PublishAsync(null, "maximum", input.Snapshot, input.Bytes, Token);
        saved.StoredBytes.Should().BeLessThanOrEqualTo(2 * 1024 * 1024);
        var revision = await new WindowsKnowledgeSourceStore(fixture).ReadAsync(saved, Token);
        revision.Indexes.Should().HaveCount(32);
        var result = new LocalFileLexicalRetrieval().Search(revision, revision.Reference, "alpha", DateTimeOffset.UnixEpoch, Token);
        result.Citations.Should().HaveCount(8);
        result.Truncated.Should().BeTrue();
    }

    private static (KnowledgeSourceRevision Snapshot, IReadOnlyList<ReadOnlyMemory<byte>> Bytes) IndexedSnapshot(
        Guid? id = null, string[]? texts = null)
    {
        texts ??= ["alpha unchanged", "alpha changed", "alpha deleted"];
        var bytes = texts.Select(Encoding.UTF8.GetBytes).ToArray();
        var files = bytes.Select((body, position) => new LocalFileMetadata($@"C:\Team\item{position:D2}.md",
            "physical-" + position, body.Length, DateTimeOffset.UnixEpoch)).ToArray();
        var review = new KnowledgeSourceReview(Guid.NewGuid(), id ?? Guid.NewGuid(), HostRequest.Create(RequestOrigin.LocalUi),
            new(@"C:\Team", "root", KnowledgeSourceScope.ImmediateFiles, [new(@"C:\Team", "root")], files));
        return (new(review, files.Select((file, position) => new LocalFileRevision(
            new(review.ReviewId, review.SourceId, review.Request, file), bytes[position], DateTimeOffset.UnixEpoch))),
            bytes.Select(body => (ReadOnlyMemory<byte>)body).ToArray());
    }

    private sealed class CancelIndexCommit(CancellationTokenSource cancellation) : ISqliteTransactionCheckpoint
    {
        public void BeforeWrite(SqliteConnection connection, SqliteTransaction transaction) { }
        public void BeforeCommit(SqliteConnection connection, SqliteTransaction transaction) => cancellation.Cancel();
    }

    private static void RewriteIndexedSnapshot(OwnedStorageFixture fixture, Action<JsonObject> change, int version)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        { DataSource = Path.Combine(fixture.LocalRoot, "KnowledgeSourcesV1", "sources.db"), Pooling = false }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT snapshot FROM knowledge_sources";
        var envelope = JsonNode.Parse((byte[])command.ExecuteScalar()!)!.AsObject();
        byte[] Field(string name) => Convert.FromBase64String(envelope[name]!.GetValue<string>());
        var key = CurrentUserKeyProtection.Unprotect(Field("Key"));
        var cipher = Field("Ciphertext");
        var compressed = new byte[cipher.Length];
        using var aes = new AesGcm(key, 16);
        try
        {
            command.CommandText = "SELECT id FROM knowledge_sources";
            var id = Guid.Parse((string)command.ExecuteScalar()!);
            aes.Decrypt(Field("Nonce"), cipher, Field("Tag"), compressed, id.ToByteArray());
            using var input = new MemoryStream(compressed);
            using var decoder = new BrotliStream(input, CompressionMode.Decompress);
            var data = JsonNode.Parse(decoder)!.AsObject();
            change(data);
            using var output = new MemoryStream();
            using (var encoder = new BrotliStream(output, CompressionLevel.Optimal, leaveOpen: true))
            { JsonSerializer.Serialize(encoder, data); }
            var updated = output.ToArray();
            var nonce = RandomNumberGenerator.GetBytes(12);
            var tag = new byte[16];
            var ciphertext = new byte[updated.Length];
            aes.Encrypt(nonce, updated, ciphertext, tag, id.ToByteArray());
            var snapshot = JsonSerializer.SerializeToUtf8Bytes(new
            { Version = version, Key = CurrentUserKeyProtection.Protect(key), Nonce = nonce, Tag = tag, Ciphertext = ciphertext });
            RewriteStorage(fixture, info => info["StoredBytes"] = snapshot.Length, snapshot);
            CryptographicOperations.ZeroMemory(updated);
        }
        finally { CryptographicOperations.ZeroMemory(key); CryptographicOperations.ZeroMemory(compressed); }
    }
}
