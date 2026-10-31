using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AwesomeAssertions;
using Kora.Application.KnowledgeSources;
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

public sealed partial class WindowsKnowledgeSourceTests
{
    [Fact]
    public async Task DescendantCaptureRequiresOptInAndSurvivesRestartWithExactCitationsAndRevokeFirstRefresh()
    {
        using var listener = Listen();
        using var host = HostActivity.BeginOperation(HostActivityLayer.Application, HostOperation.Request, RequestOrigin.LocalUi);
        using var fixture = new OwnedStorageFixture();
        var root = NewSourceRoot();
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "A"));
            Directory.CreateDirectory(Path.Combine(root, "B"));
            Directory.CreateDirectory(Path.Combine(root, "Empty"));
            var first = Path.Combine(root, "A", "notes.md");
            var second = Path.Combine(root, "B", "notes.md");
            var original = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes("alpha nested first\r\n")).ToArray();
            await File.WriteAllBytesAsync(first, original, Token);
            await File.WriteAllTextAsync(second, "alpha nested second", Token);
            var stamp = File.GetLastWriteTimeUtc(first);
            var store = new WindowsKnowledgeSourceStore(fixture);
            using var manager = new KnowledgeSourceManager(new WindowsLocalFileInspector(fixture), store, new Audit(), TimeProvider.System);
            await FluentActions.Awaiting(() => manager.ReviewAsync("nested", root, null, () => true, Token))
                .Should().ThrowAsync<InvalidDataException>();
            var review = await manager.ReviewAsync("nested", root, null, KnowledgeSourceScope.BoundedDescendants, () => true, Token);
            review.Metadata.Directories.Count.Should().Be(4);
            manager.Candidate.Should().BeNull();
            (await store.ListAsync(Token)).Should().BeEmpty();
            var captured = await manager.CaptureAsync(review.ReviewId, () => true, Token);
            (await store.ListAsync(Token)).Should().BeEmpty();
            var saved = await manager.PublishAsync(captured, () => true, Token);
            var reopenedStore = new WindowsKnowledgeSourceStore(fixture);
            using var reopened = new KnowledgeSourceManager(new WindowsLocalFileInspector(fixture), reopenedStore, new Audit(), TimeProvider.System);
            var source = (await reopened.ListAsync(() => true, Token)).Single();
            source.Folder.Scope.Should().Be(KnowledgeSourceScope.BoundedDescendants);
            var snapshot = await reopened.OpenAsync(source, () => true, Token);
            snapshot.Reference.Should().Be(captured.Reference);
            snapshot.Files[0].Reference.Should().Be(captured.Files[0].Reference);
            snapshot.Files[0].Digest.Should().Be(LocalFilePolicy.Digest(original));
            var found = new LocalFileLexicalRetrieval().Search(snapshot, snapshot.Reference, "alpha", DateTimeOffset.UtcNow, Token);
            found.Citations.Select(citation => citation.DisplayIdentity).Should().Equal(@"A\notes.md", @"B\notes.md");
            snapshot.ResolveCitation(found.Citations[1]).Text.Should().Be("alpha nested second");
            (await File.ReadAllBytesAsync(first, Token)).Should().Equal(original);
            File.GetLastWriteTimeUtc(first).Should().Be(stamp);

            var extra = Path.Combine(root, "Empty", "new.txt");
            await File.WriteAllTextAsync(extra, "not in retained inventory", Token);
            await FluentActions.Awaiting(() => reopened.OpenAsync(source, () => true, Token)).Should().ThrowAsync<InvalidDataException>();
            File.Delete(extra);
            (await reopened.OpenAsync(source, () => true, Token)).Reference.Should().Be(snapshot.Reference);
            var oldEmpty = Path.Combine(fixture.LocalRoot, "retained-empty");
            Directory.Move(Path.Combine(root, "Empty"), oldEmpty);
            Directory.CreateDirectory(Path.Combine(root, "Empty"));
            await FluentActions.Awaiting(() => reopened.OpenAsync(source, () => true, Token)).Should().ThrowAsync<InvalidDataException>();
            Directory.Delete(Path.Combine(root, "Empty"));
            Directory.Move(oldEmpty, Path.Combine(root, "Empty"));
            await FluentActions.Awaiting(() => reopened.ReviewAsync(source.Name, root, source,
                KnowledgeSourceScope.ImmediateFiles, () => true, Token)).Should().ThrowAsync<InvalidDataException>();
            (await reopenedStore.ListAsync(Token)).Single().Version.Should().Be(source.Version);
            await reopened.ReviewAsync(source.Name, root, source, () => true, Token);
            (await reopenedStore.ListAsync(Token)).Single().State.Should().Be(KnowledgeSourceState.Unavailable);
            await FluentActions.Awaiting(() => reopenedStore.ReadAsync(saved, Token)).Should().ThrowAsync<InvalidOperationException>();
            reopened.Dispose(); // Cancelling a reviewed refresh never resurrects the old revision.
            var unavailable = (await reopenedStore.ListAsync(Token)).Single();
            using var refresh = new KnowledgeSourceManager(new WindowsLocalFileInspector(fixture), reopenedStore, new Audit(), TimeProvider.System);
            await File.WriteAllTextAsync(second, "replacement nested content", Token);
            var nextReview = await refresh.ReviewAsync(source.Name, root, unavailable, () => true, Token);
            nextReview.Metadata.Scope.Should().Be(KnowledgeSourceScope.BoundedDescendants);
            var next = await refresh.CaptureAsync(nextReview.ReviewId, () => true, Token);
            var published = await refresh.PublishAsync(next, () => true, Token);
            (await refresh.OpenAsync(published, () => true, Token)).Files[1].Text.Should().Be("replacement nested content");
            await refresh.DisableAsync(published, () => true, Token);
            var disabled = (await reopenedStore.ListAsync(Token)).Single();
            await FluentActions.Awaiting(() => refresh.OpenAsync(disabled, () => true, Token)).Should().ThrowAsync<InvalidOperationException>();
            await refresh.RemoveAsync(disabled, DateTimeOffset.UtcNow, () => true, Token);
            (await reopenedStore.ListAsync(Token)).Should().BeEmpty();
            (await File.ReadAllBytesAsync(first, Token)).Should().Equal(original);
            File.GetLastWriteTimeUtc(first).Should().Be(stamp);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Theory]
    [InlineData(NativeDenial.Depth)]
    [InlineData(NativeDenial.Directories)]
    [InlineData(NativeDenial.Files)]
    [InlineData(NativeDenial.Aggregate)]
    [InlineData(NativeDenial.FileBytes)]
    [InlineData(NativeDenial.Unsupported)]
    [InlineData(NativeDenial.Generated)]
    [InlineData(NativeDenial.Hidden)]
    [InlineData(NativeDenial.HardLink)]
    public async Task EntireTreeRejectsEveryForbiddenOrOverBoundMember(NativeDenial denial)
    {
        using var listener = Listen();
        using var host = HostActivity.BeginOperation(HostActivityLayer.Application, HostOperation.Request, RequestOrigin.LocalUi);
        using var fixture = new OwnedStorageFixture();
        var root = NewSourceRoot();
        try
        {
            var original = Path.Combine(root, "notes.md");
            await File.WriteAllTextAsync(original, "synthetic", Token);
            switch (denial)
            {
                case NativeDenial.Depth: Directory.CreateDirectory(Path.Combine(root, "a", "b", "c", "d", "e")); break;
                case NativeDenial.Directories:
                    for (var index = 0; index < 32; index++) { Directory.CreateDirectory(Path.Combine(root, $"d{index:D2}")); } break;
                case NativeDenial.Files:
                    for (var index = 0; index < 32; index++) { await File.WriteAllTextAsync(Path.Combine(root, $"f{index:D2}.md"), "", Token); } break;
                case NativeDenial.Aggregate:
                    for (var index = 0; index < 4; index++)
                    { await File.WriteAllBytesAsync(Path.Combine(root, $"f{index}.md"), new byte[LocalFilePolicy.MaximumBytes], Token); } break;
                case NativeDenial.FileBytes: await File.WriteAllBytesAsync(original, new byte[LocalFilePolicy.MaximumBytes + 1], Token); break;
                case NativeDenial.Unsupported: await File.WriteAllTextAsync(Path.Combine(root, "binary.exe"), "", Token); break;
                case NativeDenial.Generated: Directory.CreateDirectory(Path.Combine(root, "obj")); break;
                case NativeDenial.Hidden: File.SetAttributes(original, FileAttributes.Hidden); break;
                case NativeDenial.HardLink: CreateHardLink(Path.Combine(root, "alias.md"), original, nint.Zero).Should().BeTrue(); break;
            }
            await FluentActions.Awaiting(() => new WindowsLocalFileInspector(fixture)
                .InspectSourceAsync(root, KnowledgeSourceScope.BoundedDescendants, Token)).Should().ThrowAsync<InvalidDataException>();
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task EveryRetainedDirectoryMembershipIsRevalidatedAndReadsRemainOneShot()
    {
        using var listener = Listen();
        using var host = HostActivity.BeginOperation(HostActivityLayer.Application, HostOperation.Request, RequestOrigin.LocalUi);
        using var fixture = new OwnedStorageFixture();
        var root = NewSourceRoot();
        try
        {
            var nested = Directory.CreateDirectory(Path.Combine(root, "nested")).FullName;
            var original = Path.Combine(nested, "notes.md");
            await File.WriteAllTextAsync(original, "synthetic", Token);
            var inspector = new WindowsLocalFileInspector(fixture);
            using (var selection = await inspector.InspectSourceAsync(root, KnowledgeSourceScope.BoundedDescendants, Token))
            {
                await FluentActions.Awaiting(() => selection.ReadAsync(selection.Metadata.Files[0] with { FileIdentity = "foreign" }, Token))
                    .Should().ThrowAsync<InvalidDataException>();
                (await selection.ReadAsync(selection.Metadata.Files[0], Token)).Should().Equal(Encoding.UTF8.GetBytes("synthetic"));
                await FluentActions.Awaiting(() => selection.ReadAsync(selection.Metadata.Files[0], Token)).Should().ThrowAsync<InvalidOperationException>();
                var added = Path.Combine(nested, "new.md");
                await File.WriteAllTextAsync(added, "changed", Token);
                await FluentActions.Awaiting(() => selection.ValidateAsync(Token)).Should().ThrowAsync<InvalidDataException>();
                File.Delete(added);
                await selection.ValidateAsync(Token);
                FluentActions.Invoking(() => Directory.Move(nested, nested + "-replaced")).Should().Throw<IOException>();
                selection.Dispose();
                await FluentActions.Awaiting(() => selection.ValidateAsync(Token)).Should().ThrowAsync<ObjectDisposedException>();
                await FluentActions.Awaiting(() => selection.ReadAsync(selection.Metadata.Files[0], Token)).Should().ThrowAsync<ObjectDisposedException>();
            }
            await File.WriteAllBytesAsync(original, [0xff], Token);
            using var manager = new KnowledgeSourceManager(inspector, new WindowsKnowledgeSourceStore(fixture), new Audit(), TimeProvider.System);
            var review = await manager.ReviewAsync("bad UTF8", root, null, KnowledgeSourceScope.BoundedDescendants, () => true, Token);
            await FluentActions.Awaiting(() => manager.CaptureAsync(review.ReviewId, () => true, Token))
                .Should().ThrowAsync<DecoderFallbackException>();
            manager.Candidate.Should().BeNull();
            (await manager.ListAsync(() => true, Token)).Should().BeEmpty();
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task LegacyVersionOnePreservesImmediateConsentBytesAndItemIdentities()
    {
        using var fixture = new OwnedStorageFixture();
        var store = new WindowsKnowledgeSourceStore(fixture);
        var input = Snapshot();
        var source = await store.PublishAsync(null, "legacy", input.Snapshot, input.Bytes, Token);
        var file = input.Snapshot.Files[0];
        var plaintext = JsonSerializer.SerializeToUtf8Bytes(new
        {
            Version = 1, Id = source.Id, input.Snapshot.Review.ReviewId, Path = source.Folder.CanonicalPath,
            Identity = source.Folder.DirectoryIdentity, input.Snapshot.Review.Request,
            Files = new[] { new { Metadata = file.Review.Metadata, file.Reference, file.AdmittedAt, Bytes = input.Bytes[0].ToArray() } },
        });
        var key = RandomNumberGenerator.GetBytes(32);
        try
        {
            var nonce = RandomNumberGenerator.GetBytes(12);
            var tag = new byte[16];
            var ciphertext = new byte[plaintext.Length];
            using var aes = new AesGcm(key, 16);
            aes.Encrypt(nonce, plaintext, ciphertext, tag, source.Id.ToByteArray());
            var snapshot = JsonSerializer.SerializeToUtf8Bytes(new { Version = 1, Key = CurrentUserKeyProtection.Protect(key), Nonce = nonce, Tag = tag, Ciphertext = ciphertext });
            RewriteStorage(fixture, info =>
            {
                info.Remove("FormatVersion"); info.Remove("Scope"); info.Remove("Directories");
                info["StoredBytes"] = snapshot.Length;
            }, snapshot);
            var reopened = new WindowsKnowledgeSourceStore(fixture);
            var legacy = (await reopened.ListAsync(Token)).Single();
            legacy.Folder.Scope.Should().Be(KnowledgeSourceScope.ImmediateFiles);
            legacy.Folder.Directories.Should().ContainSingle();
            var restored = await reopened.ReadAsync(legacy, Token);
            restored.Files[0].Reference.Should().Be(file.Reference);
            restored.Files[0].Text.Should().Be(file.Text);
            var pending = await reopened.BeginRefreshAsync(legacy, Token);
            pending.Folder.Scope.Should().Be(KnowledgeSourceScope.ImmediateFiles);
            await reopened.PublishAsync(pending, "legacy", input.Snapshot, input.Bytes, Token);
        }
        finally { CryptographicOperations.ZeroMemory(key); CryptographicOperations.ZeroMemory(plaintext); }
    }

    [Theory]
    [InlineData("FormatVersion")]
    [InlineData("Scope")]
    [InlineData("Directories")]
    public async Task UnknownOrIncompleteVersionedMetadataNeverOpensAsEmptyFreshStore(string property)
    {
        using var fixture = new OwnedStorageFixture();
        var store = new WindowsKnowledgeSourceStore(fixture);
        var input = Snapshot();
        await store.PublishAsync(null, "notes", input.Snapshot, input.Bytes, Token);
        RewriteStorage(fixture, info => info[property] = string.Equals(property, "Directories", StringComparison.Ordinal) ? null : JsonValue.Create(99));
        await FluentActions.Awaiting(() => new WindowsKnowledgeSourceStore(fixture).ListAsync(Token)).Should().ThrowAsync<InvalidDataException>();
        await FluentActions.Awaiting(() => new WindowsKnowledgeSourceStore(fixture).PublishAsync(null, "new", input.Snapshot, input.Bytes, Token))
            .Should().ThrowAsync<InvalidDataException>();
    }

    private static void RewriteStorage(OwnedStorageFixture fixture, Action<JsonObject> change, byte[]? snapshot = null)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(fixture.LocalRoot, "KnowledgeSourcesV1", "sources.db"), Pooling = false,
        }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode=PERSIST";
        command.ExecuteNonQuery();
        command.CommandText = "SELECT info FROM knowledge_sources";
        var info = JsonNode.Parse((string)command.ExecuteScalar()!)!.AsObject();
        change(info);
        command.CommandText = snapshot is null ? "UPDATE knowledge_sources SET info=$info"
            : "UPDATE knowledge_sources SET info=$info,snapshot=$snapshot";
        command.Parameters.AddWithValue("$info", info.ToJsonString());
        if (snapshot is not null) { command.Parameters.AddWithValue("$snapshot", snapshot); }
        command.ExecuteNonQuery();
    }

    private static string NewSourceRoot()
    {
        var root = Path.Combine(OwnedStorageFixture.ScratchParent(), ".recursive-source-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static ActivityListener Listen()
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
        };
        ActivitySource.AddActivityListener(listener);
        return listener;
    }

    public enum NativeDenial { Depth, Directories, Files, Aggregate, FileBytes, Unsupported, Generated, Hidden, HardLink }
}
