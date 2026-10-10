using System.Text;
using AwesomeAssertions;
using Kora.Core.Auditing;
using Kora.Core.Context;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;
using Kora.Tools.Files;
using Kora.Windows.Context;
using Kora.Windows.Storage;
using Kora.Windows.IntegrationTests.Audio;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Data.Sqlite;

namespace Kora.Windows.IntegrationTests.Storage;

[Collection(nameof(DurableStorageCompositionTestGroup))]
public sealed partial class WindowsSqliteSessionAttachmentTests
{
    [Theory]
    [InlineData("")]
    [InlineData("# exact café\r\n🙂 retained historical marker\n")]
    public async Task NativeCapturePersistsOriginalBomBytesReopensExactSourceAndCitationsThenRemovesAllOwnedDatabaseCopies(string text)
    {
        using var fixture = await Initialize();
        byte[] bytes = [0xef, 0xbb, 0xbf, .. Encoding.UTF8.GetBytes(text)];
        var repository = new DirectoryInfo(AppContext.BaseDirectory);
        while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "Kora.slnx")))
        {
            repository = repository.Parent;
        }
        var scratch = Path.Combine(repository?.FullName ?? throw new InvalidOperationException("Owned repository unavailable."),
            ".session-source-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        var path = Path.Combine(scratch, "source.md");
        try
        {
            await File.WriteAllBytesAsync(path, bytes, fixture.Token);
            var picker = new Picker(path);
            await using var action = new LocalSessionFileAttach(new WindowsLocalFileInspector(fixture.Paths),
                new Audit(), fixture.Time, NullLogger<LocalFilePreview>.Instance);
            using (var host = HostActivity.BeginRoot(fixture.Request, HostActivityLayer.Application, HostOperation.Request))
            {
                (await action.Select(picker, () => true, fixture.Token)).Should().Be(LocalFileOutcome.Reviewed);
                fixture.Count("session_file").Should().Be(0);
                var review = action.Review!;
                var write = () => File.Open(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite);
                write.Should().Throw<IOException>();
                (await action.Execute(review.ReviewId, () => true, async (file, originalBytes, cancellation) =>
                {
                    await fixture.Store.Attach(fixture.Request, new(1), file, originalBytes, () => true, cancellation);
                }, fixture.Token)).Should().Be(LocalFileOutcome.Admitted);
            }
            var retained = (await fixture.RunAsync(() => fixture.Store.ReadAttachment(fixture.Request.SessionId, fixture.Token)))!;
            retained.File.Text.Should().Be(text);
            retained.File.Digest.Should().Be(LocalFilePolicy.Digest(bytes));
            var retrieval = new LocalFileLexicalRetrieval();
            var citations = retrieval.Search(retained.File, retained.File.Reference, "café",
                fixture.Time.Now, fixture.Token);
            var clock = await fixture.Store.ReadRetentionAsync(fixture.Request.SessionId, fixture.Token);
            await File.WriteAllTextAsync(path, "changed live source; never implicitly refresh", fixture.Token);
            fixture.Reopen();
            await fixture.Store.InitializeAsync(fixture.Token);
            var after = (await fixture.RunAsync(() => fixture.Store.ReadAttachment(fixture.Request.SessionId, fixture.Token)))!;
            after.File.Reference.Should().Be(retained.File.Reference);
            after.File.Text.Should().Be(text);
            retrieval.Search(after.File, after.File.Reference, "café", fixture.Time.Now, fixture.Token)
                .Should().BeEquivalentTo(citations);
            (await fixture.Store.ReadRetentionAsync(fixture.Request.SessionId, fixture.Token)).Should().Be(clock);
            var audit = ReadString(fixture, "SELECT group_concat(envelope) FROM security_audit_events;");
            audit.Should().NotContain(text.Length == 0 ? "\uFEFF" : text).And.NotContain(path);
            var removal = await fixture.RunAsync(() => fixture.Store.PreviewRemoval(fixture.Request.SessionId, fixture.Token));
            await NewControl(fixture, fixture.Request.SessionId);
            await fixture.RunAsync(async () => await fixture.Store.Remove(fixture.Request, removal, () => true, fixture.Token));
            new FileInfo(fixture.DatabasePath + "-journal").Length.Should().Be(0);
            if (text.Length > 0)
            {
                Encoding.UTF8.GetString(File.ReadAllBytes(fixture.DatabasePath)).Should().NotContain(text);
            }
            (await File.ReadAllTextAsync(path, fixture.Token)).Should().Be("changed live source; never implicitly refresh");
            fixture.Reopen();
            await fixture.Store.InitializeAsync(fixture.Token);
            (await fixture.RunAsync(() => fixture.Store.ReadAttachment(fixture.Request.SessionId, fixture.Token))).Should().BeNull();
            ReadString(fixture, "SELECT descriptor FROM session_file;").Should().NotContain(path);
        }
        finally { Directory.Delete(scratch, recursive: true); }
    }

    [WindowsFact]
    public async Task SecondAttachmentCannotOverwriteAndProfileCapacityCannotEvict()
    {
        using var fixture = await Initialize();
        var first = await Attach(fixture, Encoding.UTF8.GetBytes("first immutable body"));
        await NewControl(fixture, fixture.Request.SessionId);
        var second = () => Attach(fixture, Encoding.UTF8.GetBytes("second body"));
        await second.Should().ThrowAsync<InvalidOperationException>().WithMessage("*existing attachment*");
        (await fixture.RunAsync(() => fixture.Store.ReadAttachment(first.Session, fixture.Token)))!.File.Reference.Should().Be(first.File.Reference);
        await FinishControl(fixture);
        for (var index = 1; index < SessionFileAttachment.MaximumRetainedFiles; index++)
        {
            await NewSession(fixture);
            await Attach(fixture, Encoding.UTF8.GetBytes("retained " + index));
        }
        await NewSession(fixture);
        var seventeenth = () => Attach(fixture, Encoding.UTF8.GetBytes("capacity overflow"));
        await seventeenth.Should().ThrowAsync<InvalidOperationException>().WithMessage("*sixteen*");
        fixture.Count("session_file").Should().Be(16);
        (await fixture.RunAsync(() => fixture.Store.ReadAttachment(first.Session, fixture.Token)))!.File.Reference.Should().Be(first.File.Reference);
    }

    [Theory]
    [InlineData("UPDATE session_file SET body=X'61';")]
    [InlineData("UPDATE session_file SET descriptor=json_set(descriptor,'$.Format',99);")]
    [InlineData("UPDATE session_file SET descriptor=json_set(descriptor,'$.Reference.Digest','aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa');")]
    [InlineData("UPDATE session_file SET descriptor=json_set(descriptor,'$.Profile.Value','11111111-1111-1111-1111-111111111111');")]
    [InlineData("UPDATE session_file SET descriptor=json_set(descriptor,'$.Request',NULL);")]
    [InlineData("UPDATE session_file SET descriptor=json_set(descriptor,'$.Reference',NULL);")]
    [InlineData("UPDATE session_file SET descriptor=json_set(descriptor,'$.MediaType','application/octet-stream');")]
    [InlineData("UPDATE session_file SET descriptor=json_set(descriptor,'$.Metadata',NULL);")]
    [InlineData("UPDATE session_file SET body=CAST(substr(body,1,length(body)-1)||X'80' AS BLOB);")]
    [InlineData("DELETE FROM session_file;")]
    [InlineData("PRAGMA user_version=99;")]
    public async Task CorruptBodyDigestScopeVersionOrMissingCommittedSnapshotFailsClosedWithoutReplacement(string mutation)
    {
        using var fixture = await Initialize();
        await Attach(fixture, Encoding.UTF8.GetBytes("exact source"));
        fixture.Mutate(mutation);
        var before = File.ReadAllBytes(fixture.DatabasePath);
        fixture.Reopen();
        var read = () => fixture.Store.InitializeAsync(fixture.Token).AsTask();
        await read.Should().ThrowAsync<InvalidDataException>();
        File.ReadAllBytes(fixture.DatabasePath).Should().Equal(before);
    }

    [WindowsFact]
    public async Task VersionSevenMigrationPreservesExistingAuthorityHashesAndRejectsAttachmentDowngrade()
    {
        using var fixture = await Initialize();
        var hashes = ReadString(fixture, "SELECT group_concat(hash) FROM security_audit_events;");
        fixture.Mutate("DROP TABLE session_file; PRAGMA user_version=7;");
        fixture.Reopen();
        await fixture.Store.InitializeAsync(fixture.Token);
        ReadString(fixture, "SELECT group_concat(hash) FROM security_audit_events;").Should().Be(hashes);
        await Attach(fixture, Encoding.UTF8.GetBytes("admitted"));
        fixture.Mutate("DROP TABLE session_file; PRAGMA user_version=7;");
        fixture.Reopen();
        var initialize = () => fixture.Store.InitializeAsync(fixture.Token).AsTask();
        await initialize.Should().ThrowAsync<InvalidDataException>().WithMessage("*downgraded*");
    }

    [WindowsFact]
    public async Task UninventoriedCopyAndChangedInventoryHoldRemovalWithoutDeletingBody()
    {
        using var fixture = await Initialize();
        var retained = await Attach(fixture, Encoding.UTF8.GetBytes("retained original"));
        var removal = await fixture.RunAsync(() => fixture.Store.PreviewRemoval(retained.Session, fixture.Token));
        var inventory = Path.Combine(fixture.Paths.LocalRoot, HostInteractionSchema.Partition, "Artifacts", "unknown.pending");
        await File.WriteAllTextAsync(inventory, "unknown independently owned work", fixture.Token);
        await NewControl(fixture, retained.Session);
        var remove = () => fixture.RunAsync(async () => await fixture.Store.Remove(fixture.Request, removal, () => true, fixture.Token));
        await remove.Should().ThrowAsync<InvalidDataException>().WithMessage("*Unregistered*");
        (await fixture.RunAsync(() => fixture.Store.ReadAttachment(retained.Session, fixture.Token)))!.File.Reference.Should().Be(retained.File.Reference);
        File.Exists(inventory).Should().BeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CurrentCommitGateAndOriginalNativeOriginCannotAdmitInvalidSnapshot(bool hostileOrigin)
    {
        using var fixture = await Initialize();
        if (hostileOrigin)
        {
            await FinishControl(fixture);
            fixture.Request = Request(fixture.Request.SessionId, RequestOrigin.ActivatedVoice);
            await fixture.RunAsync(async () => await fixture.Store.RecordControlIntentAsync(fixture.Request, fixture.Token));
        }
        var attach = () => Attach(fixture, Encoding.UTF8.GetBytes("denied"), () => hostileOrigin);
        await attach.Should().ThrowAsync<InvalidOperationException>();
        fixture.Count("session_file").Should().Be(0);
    }

    [Theory]
    [InlineData(262144)]
    [InlineData(262145)]
    public async Task OriginalByteLimitIncludesBomAndNeverTruncates(int count)
    {
        using var fixture = await Initialize();
        byte[] bytes = [0xef, 0xbb, 0xbf, .. Enumerable.Repeat((byte)'a', count - 3)];
        var attach = () => Attach(fixture, bytes);
        if (count <= LocalFilePolicy.MaximumBytes)
        {
            var admitted = await attach();
            admitted.File.Review.Metadata.ByteLength.Should().Be(count);
            admitted.File.Text.Length.Should().Be(count - 3);
            admitted.File.Digest.Should().Be(LocalFilePolicy.Digest(bytes));
        }
        else
        {
            await attach.Should().ThrowAsync<InvalidDataException>();
            fixture.Count("session_file").Should().Be(0);
        }
    }

    [Theory]
    [InlineData("audit")]
    [InlineData("gate")]
    public async Task AuditFailureOrCommitGateRollbackCannotPublishBodyOrLeaveOwnedCopy(string failure)
    {
        using var fixture = await Initialize();
        var allowed = true;
        var checkpoint = new AttachmentCheckpoint();
        fixture.Reopen(checkpoint);
        await fixture.Store.InitializeAsync(fixture.Token);
        if (string.Equals(failure, "audit", StringComparison.Ordinal))
        {
            checkpoint.Audit = () => throw new IOException("required audit unavailable");
        }
        else { checkpoint.Commit = () => allowed = false; }
        var attach = () => Attach(fixture, Encoding.UTF8.GetBytes("ROLLBACK_ATTACHMENT_SENTINEL_87364"), () => allowed);
        await attach.Should().ThrowAsync<Exception>();
        fixture.Count("session_file").Should().Be(0);
        Encoding.UTF8.GetString(File.ReadAllBytes(fixture.DatabasePath)).Should().NotContain("ROLLBACK_ATTACHMENT_SENTINEL_87364");
        Encoding.UTF8.GetString(File.ReadAllBytes(fixture.DatabasePath + "-journal")).Should().NotContain("ROLLBACK_ATTACHMENT_SENTINEL_87364");
        fixture.Reopen();
        await fixture.Store.InitializeAsync(fixture.Token);
        (await fixture.RunAsync(() => fixture.Store.ReadAttachment(fixture.Request.SessionId, fixture.Token))).Should().BeNull();
    }

    [WindowsFact]
    public async Task DoneSessionKeepsHistoricalSnapshotWithoutResumingAndExactRemovalPreservesOtherSessionBody()
    {
        using var fixture = await Initialize();
        var first = await Attach(fixture, Encoding.UTF8.GetBytes("REMOVE_ONLY_THIS_ATTACHMENT_7654"));
        await NewSession(fixture);
        var second = await Attach(fixture, Encoding.UTF8.GetBytes("KEEP_OTHER_SESSION_ATTACHMENT_7654"));
        await NewControl(fixture, first.Session);
        await fixture.RunAsync(async () =>
        {
            await fixture.Store.ChangeIdleLifecycleAsync(fixture.Request, new(1), false, () => true, fixture.Token);
            await fixture.Tasks.CommitAsync(new(fixture.Request, new(2), HostTaskState.Succeeded), 1, fixture.Token);
        });
        var afterDone = (await fixture.RunAsync(() => fixture.Store.ReadAttachment(first.Session, fixture.Token)))!;
        afterDone.File.Reference.Should().Be(first.File.Reference);
        (await fixture.Store.ReadSessionAsync(first.Session, fixture.Token))!.IsActive.Should().BeFalse();
        var removal = await fixture.RunAsync(() => fixture.Store.PreviewRemoval(first.Session, fixture.Token));
        removal.Generation.Value.Should().Be(2);
        await NewControl(fixture, first.Session);
        await fixture.RunAsync(async () => await fixture.Store.Remove(fixture.Request, removal, () => true, fixture.Token));
        (await fixture.RunAsync(() => fixture.Store.ReadAttachment(second.Session, fixture.Token)))!.File.Reference.Should().Be(second.File.Reference);
        var database = Encoding.UTF8.GetString(File.ReadAllBytes(fixture.DatabasePath));
        database.Should().NotContain("REMOVE_ONLY_THIS_ATTACHMENT_7654").And.Contain("KEEP_OTHER_SESSION_ATTACHMENT_7654");
    }

    [WindowsFact]
    public async Task RetentionInventoriesAndRevokesAttachmentBeforeDeletionWhileKeptSessionRemainsIntact()
    {
        using var fixture = await Initialize();
        var first = await Attach(fixture, Encoding.UTF8.GetBytes("RETENTION_DELETE_ATTACHMENT_7564"));
        var clock = await fixture.Store.ReadRetentionAsync(first.Session, fixture.Token);
        await NewSession(fixture);
        var kept = await Attach(fixture, Encoding.UTF8.GetBytes("RETAIN_KEPT_ATTACHMENT_7564"));
        await NewControl(fixture, kept.Session);
        await fixture.RunAsync(async () =>
        {
            await fixture.Store.SetPerpetualAsync(fixture.Request, new(1), true, () => true, fixture.Token);
            await fixture.Tasks.CommitAsync(new(fixture.Request, new(2), HostTaskState.Succeeded), 1, fixture.Token);
        });
        fixture.Time.Now = clock.DeleteDue;
        var revocations = new List<HostId<SessionIdentity>>();
        using (var root = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.HostSystem),
            HostActivityLayer.Windows, HostOperation.Retention))
        {
            var batch = await fixture.Store.ApplyRetentionAsync(() => true, (session, _) =>
            {
                revocations.Add(session);
                return ValueTask.CompletedTask;
            }, fixture.Token);
            batch.Deleted.Should().Be(1);
        }
        revocations.Should().Equal(first.Session);
        var removedRead = () => fixture.RunAsync(() => fixture.Store.ReadAttachment(first.Session, fixture.Token));
        await removedRead.Should().ThrowAsync<InvalidOperationException>();
        (await fixture.RunAsync(() => fixture.Store.ReadAttachment(kept.Session, fixture.Token)))!.File.Reference.Should().Be(kept.File.Reference);
        var database = Encoding.UTF8.GetString(File.ReadAllBytes(fixture.DatabasePath));
        database.Should().NotContain("RETENTION_DELETE_ATTACHMENT_7564").And.Contain("RETAIN_KEPT_ATTACHMENT_7564");
        new FileInfo(fixture.DatabasePath + "-journal").Length.Should().Be(0);
    }

    [WindowsFact]
    public async Task ExactSessionDispositionIncludesAttachmentInventoryAndVerifiesCommittedJournalRemoval()
    {
        using var fixture = await Initialize();
        var first = await Attach(fixture, Encoding.UTF8.GetBytes("DISPOSITION_ATTACHMENT_7854"));
        var preview = await fixture.Store.PreviewDispositionAsync(first.Session, new(1), 0, fixture.Token);
        preview.Attachments.Should().Be(1);
        await NewControl(fixture, first.Session);
        var receipt = await fixture.RunAsync(() => fixture.Store.DisposeSessionAsync(fixture.Request, preview, () => true, fixture.Token));
        receipt.Removed.Attachments.Should().Be(1);
        Encoding.UTF8.GetString(File.ReadAllBytes(fixture.DatabasePath)).Should().NotContain("DISPOSITION_ATTACHMENT_7854");
        new FileInfo(fixture.DatabasePath + "-journal").Length.Should().Be(0);
        fixture.Reopen();
        await fixture.Store.InitializeAsync(fixture.Token);
        var read = () => fixture.RunAsync(() => fixture.Store.ReadAttachment(first.Session, fixture.Token));
        await read.Should().ThrowAsync<InvalidOperationException>();
    }

    [WindowsFact]
    public async Task ExplicitRemovalAllowsFreshSingleFileAdmissionWithoutKeepingOldBodyOrRebindingOldCitations()
    {
        using var fixture = await Initialize();
        var original = await Attach(fixture, Encoding.UTF8.GetBytes("OLD_ATTACHMENT_VERSION_34878"));
        var removal = await fixture.RunAsync(() => fixture.Store.PreviewRemoval(original.Session, fixture.Token));
        await NewControl(fixture, original.Session);
        await fixture.RunAsync(async () => await fixture.Store.Remove(fixture.Request, removal, () => true, fixture.Token));
        var cleanup = await fixture.RunAsync(() => fixture.Store.PreviewRemoval(original.Session, fixture.Token));
        cleanup.BodyRetained.Should().BeFalse();
        await NewControl(fixture, original.Session);
        var replacement = await Attach(fixture, Encoding.UTF8.GetBytes("NEW_ATTACHMENT_VERSION_34878"));
        replacement.File.Reference.Should().NotBe(original.File.Reference);
        replacement.StorageRevision.Value.Should().Be(3);
        fixture.Count("session_file").Should().Be(1);
        fixture.Reopen();
        await fixture.Store.InitializeAsync(fixture.Token);
        (await fixture.RunAsync(() => fixture.Store.ReadAttachment(original.Session, fixture.Token)))!.File.Reference.Should().Be(replacement.File.Reference);
        foreach (var path in new[] { fixture.DatabasePath, fixture.DatabasePath + "-journal" })
        {
            Encoding.UTF8.GetString(File.ReadAllBytes(path)).Should().NotContain("OLD_ATTACHMENT_VERSION_34878");
        }
    }

    private static async Task<InteractionStorageFixture> Initialize()
    {
        var fixture = new InteractionStorageFixture();
        await fixture.InitializeAsync();
        await FinishControl(fixture);
        await NewControl(fixture, fixture.Request.SessionId);
        return fixture;
    }

    private static HostRequest Request(HostId<SessionIdentity>? session = null, RequestOrigin origin = RequestOrigin.LocalUi) =>
        new(new(Guid.NewGuid()), session ?? new(Guid.NewGuid()), new(Guid.NewGuid()), origin);

    private static async Task NewControl(InteractionStorageFixture fixture, HostId<SessionIdentity> session)
    {
        fixture.Request = Request(session);
        await fixture.RunAsync(async () => await fixture.Store.RecordControlIntentAsync(fixture.Request, fixture.Token));
    }

    private static async Task NewSession(InteractionStorageFixture fixture)
    {
        fixture.Request = Request();
        await fixture.RunAsync(async () =>
        {
            await fixture.Store.RecordControlIntentAsync(fixture.Request, fixture.Token);
            await fixture.Store.CreateSessionAsync(fixture.Request, fixture.Token);
        });
    }

    private static Task FinishControl(InteractionStorageFixture fixture) =>
        fixture.RunAsync(async () => await fixture.Tasks.CommitAsync(
            new(fixture.Request, new(2), HostTaskState.Denied), 1, fixture.Token));

    private static Task<SessionFileAttachment> Attach(InteractionStorageFixture fixture, byte[] bytes, Func<bool>? gate = null)
    {
        var file = new LocalFileRevision(new(Guid.NewGuid(), Guid.NewGuid(), fixture.Request,
            new(@"C:\Synthetic\source.md", "verified-synthetic-native-identity", bytes.Length, fixture.Time.Now)), bytes, fixture.Time.Now);
        return fixture.RunAsync(() => fixture.Store.Attach(fixture.Request, new(1), file, bytes, gate ?? (() => true), fixture.Token));
    }

    private static string ReadString(InteractionStorageFixture fixture, string sql)
    {
        using var connection = fixture.OpenRaw();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (string)command.ExecuteScalar()!;
    }

    private sealed class Picker(string path) : IUserFilePicker
    {
        public Task<string?> SelectAsync(CancellationToken cancellationToken) => Task.FromResult<string?>(path);
    }

    private sealed class Audit : ISecurityAuditLog
    {
        public void Write(SecurityAuditEvent auditEvent) { }
    }

    private sealed class AttachmentCheckpoint : IHostInteractionTransactionCheckpoint
    {
        internal Action? Audit { get; set; }
        internal Action? Commit { get; set; }
        internal Action? Copies { get; set; }
        internal Action<SqliteConnection, SqliteTransaction>? CommitBoundary { get; set; }
        public void BeforeAudit(SqliteConnection connection, SqliteTransaction transaction) => Audit?.Invoke();
        public void BeforeCommit(SqliteConnection connection, SqliteTransaction transaction)
        {
            Commit?.Invoke();
            CommitBoundary?.Invoke(connection, transaction);
        }
        public void BeforeAttachmentCopyVerification() => Copies?.Invoke();
    }
}
