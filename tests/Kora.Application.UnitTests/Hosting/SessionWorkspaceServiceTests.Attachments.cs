using System.Diagnostics;
using System.Text;
using AwesomeAssertions;
using Kora.Application.Hosting;
using Kora.Core.Auditing;
using Kora.Core.Context;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;
using Kora.Tools.Files;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Application.UnitTests.Hosting;

public sealed partial class SessionWorkspaceServiceTests
{
    [Fact]
    public async Task ExactNativeCaptureReadSearchAndReviewedRemovalNeverRenewPassiveActivityOrSendModelContent()
    {
        using var fixture = new Fixture { FileReceipts = true };
        var audit = new FileAudit();
        var capture = new FileCapture();
        var action = FileAction(capture, audit);
        await using var service = FileService(fixture, action, audit);
        service.Review.Should().BeNull();
        service.IsQuiescent.Should().BeTrue();
        (await service.Select(new(fixture.Session, null), new FilePicker(), () => true, fixture.Token))
            .Should().Be(LocalFileOutcome.Reviewed);
        var review = service.Review!;
        service.IsQuiescent.Should().BeFalse();
        fixture.RetainedFile.Should().BeNull();
        (await service.Confirm(review.ReviewId, fixture.Token)).Should().Be(LocalFileOutcome.Admitted);
        capture.Disposals.Should().Be(1);
        fixture.RetainedFile!.Session.Should().Be(fixture.Session.SessionId);
        fixture.RetainedFile.File.Review.Request.SessionId.Should().Be(fixture.Session.SessionId);
        service.IsQuiescent.Should().BeTrue();
        var writes = fixture.TaskWrites.Count;
        var read = (await service.Read(fixture.Session.SessionId, fixture.Token))!;
        read.File.Text.Should().Be("original café source");
        var searched = await service.Search(read, "café", fixture.Token);
        searched.Outcome.Should().Be(LocalFileSearchOutcome.Matched);
        fixture.TaskWrites.Should().HaveCount(writes);
        var removal = await service.PreviewRemoval(fixture.Session.SessionId, fixture.Token);
        var revoked = false;
        service.Revoked += session => { session.Should().Be(fixture.Session.SessionId); revoked = true; };
        await service.Remove(removal, () => true, fixture.Token);
        revoked.Should().BeTrue();
        fixture.RetainedFile.Should().BeNull();
        audit.Events.Should().NotBeEmpty();
        string.Join(" ", audit.Events.Select(item => item.TargetId)).Should().NotContain("original café");
        await service.Clear();
        await service.WaitForQuiescence();
    }

    [Theory]
    [InlineData("disposed")]
    [InlineData("control")]
    [InlineData("done")]
    [InlineData("admission")]
    [InlineData("existing")]
    [InlineData("outstanding-review")]
    public async Task SelectionDeniesUnavailableInactiveExistingOrNonquiescentTarget(string boundary)
    {
        using var fixture = new Fixture();
        var audit = new FileAudit();
        var action = FileAction(new FileCapture(), audit);
        await using var service = FileService(fixture, action, audit);
        if (string.Equals(boundary, "disposed", StringComparison.Ordinal)) { await service.DisposeAsync(); }
        if (string.Equals(boundary, "control", StringComparison.Ordinal)) { fixture.CanControl = false; }
        if (string.Equals(boundary, "existing", StringComparison.Ordinal)) { fixture.RetainedFile = FileRecord(fixture.Request); }
        if (string.Equals(boundary, "outstanding-review", StringComparison.Ordinal))
        {
            await service.Select(new(fixture.Session, null), new FilePicker(), () => true, fixture.Token);
        }
        var target = string.Equals(boundary, "done", StringComparison.Ordinal)
            ? fixture.Session with { IsActive = false } : fixture.Session;
        var select = () => service.Select(new(target, null), new FilePicker(),
            () => !string.Equals(boundary, "admission", StringComparison.Ordinal), fixture.Token);
        await select.Should().ThrowAsync<InvalidOperationException>();
        fixture.TaskWrites.Should().BeEmpty();
    }

    [Theory]
    [InlineData("none")]
    [InlineData("wrong-id")]
    [InlineData("unbound-review")]
    [InlineData("control-change")]
    [InlineData("privacy")]
    [InlineData("cancel")]
    public async Task ConfirmationCannotAdmitUnboundWrongOrRevokedPersistenceReview(string boundary)
    {
        using var fixture = new Fixture { FileReceipts = true };
        var audit = new FileAudit();
        var action = FileAction(new FileCapture(), audit);
        await using var service = FileService(fixture, action, audit);
        using var cancellation = new CancellationTokenSource();
        if (!string.Equals(boundary, "none", StringComparison.Ordinal))
        {
            if (string.Equals(boundary, "unbound-review", StringComparison.Ordinal))
            {
                using var root = HostActivity.BeginRoot(fixture.Request, HostActivityLayer.Application, HostOperation.Request);
                await action.Select(new FilePicker(), () => true, fixture.Token);
            }
            else { await service.Select(new(fixture.Session, null), new FilePicker(), () => true, cancellation.Token); }
        }
        var id = service.Review?.ReviewId ?? Guid.NewGuid();
        if (string.Equals(boundary, "wrong-id", StringComparison.Ordinal)) { id = Guid.NewGuid(); }
        if (string.Equals(boundary, "control-change", StringComparison.Ordinal)) { fixture.AdvanceFileControl(); }
        if (string.Equals(boundary, "privacy", StringComparison.Ordinal)) { fixture.CanControl = false; }
        if (string.Equals(boundary, "cancel", StringComparison.Ordinal)) { cancellation.Cancel(); }
        var confirm = () => service.Confirm(id, fixture.Token);
        await confirm.Should().ThrowAsync<InvalidOperationException>();
        fixture.RetainedFile.Should().BeNull();
    }

    [Theory]
    [InlineData("voice")]
    [InlineData("system")]
    [InlineData("invocation")]
    [InlineData("stopped")]
    [InlineData("completed")]
    [InlineData("ambient-replaced")]
    public async Task AllNativeAttachmentRoutesRejectOriginInvocationStoppedAndForeignAmbientContext(string boundary)
    {
        using var fixture = new Fixture();
        var audit = new FileAudit();
        await using var service = FileService(fixture, FileAction(new FileCapture(), audit), audit);
        var request = boundary switch
        {
            "voice" => HostRequest.Create(RequestOrigin.ActivatedVoice),
            "system" => HostRequest.Create(RequestOrigin.HostSystem),
            "invocation" => new HostRequest(fixture.Request.RequestId, fixture.Request.SessionId, fixture.Request.TaskId,
                RequestOrigin.LocalUi, new(Guid.NewGuid())),
            _ => fixture.Request,
        };
        using var root = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request);
        if (string.Equals(boundary, "stopped", StringComparison.Ordinal)) { root.Activity!.Stop(); }
        if (string.Equals(boundary, "completed", StringComparison.Ordinal)) { root.Complete(HostOperationOutcome.Completed); }
        using var foreign = string.Equals(boundary, "ambient-replaced", StringComparison.Ordinal) ? new Activity("foreign").Start() : null;
        var select = () => service.Select(new(fixture.Session, null), new FilePicker(), () => true, fixture.Token);
        var read = () => service.Read(fixture.Request.SessionId, fixture.Token);
        await select.Should().ThrowAsync<InvalidOperationException>();
        await read.Should().ThrowAsync<InvalidOperationException>();
        var remove = () => fixture.Service.RemoveAttachment(new(Guid.NewGuid(), fixture.Request.SessionId,
            new(1), new(1), FileRecord(fixture.Request).File.Reference, "inventory"), () => true, fixture.Token);
        await remove.Should().ThrowAsync<InvalidOperationException>();
    }

    [Theory]
    [InlineData("privacy")]
    [InlineData("control")]
    [InlineData("generation")]
    [InlineData("audit")]
    [InlineData("storage")]
    [InlineData("cancel")]
    public async Task PassiveReadRevocationAndAuditFailureCannotPublishRetainedBody(string boundary)
    {
        using var fixture = new Fixture();
        fixture.RetainedFile = FileRecord(fixture.Request);
        var audit = new FileAudit();
        await using var service = FileService(fixture, FileAction(new FileCapture(), audit), audit);
        fixture.BeforeFileRead = () =>
        {
            switch (boundary)
            {
                case "privacy": fixture.CanInspect = false; break;
                case "control": fixture.AdvanceFileControl(); break;
                case "generation": service.Revoke(); break;
                case "audit": audit.FailSuccess = true; break;
                case "storage": throw new InvalidDataException("corrupt descriptor");
                default: throw new OperationCanceledException();
            }
        };
        var read = () => service.Read(fixture.Request.SessionId, fixture.Token);
        await read.Should().ThrowAsync<Exception>();
        service.IsQuiescent.Should().BeTrue();
    }

    [Theory]
    [InlineData("absent")]
    [InlineData("reference")]
    [InlineData("revision")]
    [InlineData("late")]
    public async Task LexicalSearchDeniesChangedSourceReferenceAndLateRemoval(string boundary)
    {
        using var fixture = new Fixture();
        var original = FileRecord(fixture.Request);
        fixture.RetainedFile = original;
        var audit = new FileAudit();
        await using var service = FileService(fixture, FileAction(new FileCapture(), audit), audit);
        if (string.Equals(boundary, "absent", StringComparison.Ordinal)) { fixture.RetainedFile = null; }
        if (string.Equals(boundary, "reference", StringComparison.Ordinal))
        {
            fixture.RetainedFile = FileRecord(fixture.Request);
        }
        if (string.Equals(boundary, "revision", StringComparison.Ordinal))
        {
            fixture.RetainedFile = original with { StorageRevision = new(2) };
        }
        if (string.Equals(boundary, "late", StringComparison.Ordinal))
        {
            fixture.BeforeFileRead = () =>
            {
                if (fixture.FileReads == 2) { fixture.RetainedFile = null; }
            };
        }
        var search = () => service.Search(original, "café", fixture.Token);
        await search.Should().ThrowAsync<InvalidOperationException>();
    }

    [Theory]
    [InlineData("none")]
    [InlineData("copy")]
    [InlineData("control")]
    [InlineData("expire")]
    [InlineData("gate")]
    public async Task RemovalRequiresExactFreshOriginalNativeReviewAndCurrentControl(string boundary)
    {
        using var fixture = new Fixture();
        fixture.RetainedFile = FileRecord(fixture.Request);
        var audit = new FileAudit();
        var clock = new FileClock();
        await using var service = FileService(fixture, FileAction(new FileCapture(), audit), audit, clock);
        var review = await service.PreviewRemoval(fixture.Request.SessionId, fixture.Token);
        switch (boundary)
        {
            case "none": service.Revoke(); break;
            case "copy": review = review with { ConfirmationId = Guid.NewGuid() }; break;
            case "control": fixture.AdvanceFileControl(); break;
            case "expire": clock.Now = clock.Now.AddMinutes(2); break;
        }
        var remove = () => service.Remove(review, () => !string.Equals(boundary, "gate", StringComparison.Ordinal), fixture.Token);
        await remove.Should().ThrowAsync<InvalidOperationException>();
        fixture.RetainedFile.Should().NotBeNull();
    }

    [Fact]
    public async Task RevokedOutstandingReadRemainsNonquiescentUntilWorkerActuallyExits()
    {
        using var fixture = new Fixture();
        fixture.RetainedFile = FileRecord(fixture.Request);
        var audit = new FileAudit();
        await using var service = FileService(fixture, FileAction(new FileCapture(), audit), audit);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.FileReader = async () => { entered.SetResult(); await release.Task; return fixture.RetainedFile; };
        var read = service.Read(fixture.Request.SessionId, fixture.Token);
        await entered.Task;
        service.IsQuiescent.Should().BeFalse();
        service.Revoke();
        var wait = service.WaitForQuiescence();
        wait.IsCompleted.Should().BeFalse();
        release.SetResult();
        await ((Func<Task>)(async () => await read)).Should().ThrowAsync<Exception>();
        await wait;
        service.IsQuiescent.Should().BeTrue();
    }

    [Theory]
    [InlineData("storage")]
    [InlineData("audit")]
    [InlineData("cancel")]
    [InlineData("requested-audit")]
    public async Task RemovalFailureReportsExplicitTypedCleanupOutcomeWithoutRestoringCommittedBody(string failure)
    {
        using var fixture = new Fixture { FileReceipts = true };
        fixture.RetainedFile = FileRecord(fixture.Request);
        var audit = new FileAudit();
        await using var service = FileService(fixture, FileAction(new FileCapture(), audit), audit);
        var review = await service.PreviewRemoval(fixture.Request.SessionId, fixture.Token);
        switch (failure)
        {
            case "storage": fixture.FileRemoveFailure = new IOException("audit transaction unavailable"); break;
            case "cancel": fixture.FileRemoveFailure = new OperationCanceledException(); break;
            case "requested-audit": audit.FailRequested = true; break;
            default: audit.FailSuccess = true; break;
        }
        var remove = () => service.Remove(review, () => true, fixture.Token);
        await remove.Should().ThrowAsync<Exception>();
        if (string.Equals(failure, "audit", StringComparison.Ordinal)) { fixture.RetainedFile.Should().BeNull(); }
        else { fixture.RetainedFile.Should().NotBeNull(); }
        service.IsQuiescent.Should().BeTrue();
        audit.Events.Should().Contain(item => string.Equals(item.ActionId, "session.file.copy-cleanup", StringComparison.Ordinal)
            && (item.Outcome == SecurityAuditOutcome.Failed || item.Outcome == SecurityAuditOutcome.Cancelled));
    }

    [Fact]
    public async Task CopyCleanupIsNonquiescentUntilRealControlFinishesAndRevocationDoesNotDeadlockItsOwnCleanup()
    {
        using var fixture = new Fixture { FileReceipts = true };
        fixture.RetainedFile = FileRecord(fixture.Request);
        var audit = new FileAudit();
        await using var service = FileService(fixture, FileAction(new FileCapture(), audit), audit);
        var review = await service.PreviewRemoval(fixture.Request.SessionId, fixture.Token);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.FileRemover = async () => { entered.SetResult(); await release.Task; };
        var remove = service.Remove(review, () => true, fixture.Token);
        await entered.Task;
        service.IsQuiescent.Should().BeFalse();
        var wait = service.WaitForQuiescence();
        wait.IsCompleted.Should().BeFalse();
        release.SetResult();
        await remove;
        await wait;
        service.IsQuiescent.Should().BeTrue();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CancelledSelectionAndFailedCaptureNeverProduceDurableAttachmentSuccess(bool cancelPicker)
    {
        using var fixture = new Fixture { FileReceipts = true };
        var audit = new FileAudit();
        var action = FileAction(new FileCapture { ReadFailure = !cancelPicker }, audit);
        await using var service = FileService(fixture, action, audit);
        var selected = await service.Select(new(fixture.Session, null), new FilePicker { Cancelled = cancelPicker },
            () => true, fixture.Token);
        if (cancelPicker) { selected.Should().Be(LocalFileOutcome.Cancelled); }
        else
        {
            selected.Should().Be(LocalFileOutcome.Reviewed);
            (await service.Confirm(service.Review!.ReviewId, fixture.Token)).Should().Be(LocalFileOutcome.Unavailable);
        }
        fixture.RetainedFile.Should().BeNull();
    }

    [Fact]
    public async Task ExplicitRevokeNotifiesNativeInspectionOwnerAndCancellationLoggerCarriesNoSourceContent()
    {
        using var fixture = new Fixture();
        var audit = new FileAudit();
        await using var service = new SessionFileAttachmentService(fixture.Service, fixture, FileAction(new FileCapture(), audit),
            new LocalFileLexicalRetrieval(), TimeProvider.System, audit, new EnabledFileLogger());
        var revocations = 0;
        service.InspectionRevoked += () => revocations++;
        service.Revoke();
        revocations.Should().Be(1);
        fixture.BeforeFileRead = () => throw new OperationCanceledException();
        var read = () => service.Read(fixture.Request.SessionId, fixture.Token);
        await read.Should().ThrowAsync<OperationCanceledException>();
        fixture.BeforeFileRead = () => throw new InvalidDataException("untrusted source");
        service.Revoked += session => session.Should().Be(fixture.Request.SessionId);
        await read.Should().ThrowAsync<InvalidDataException>();
        await service.DisposeAsync();
        await read.Should().ThrowAsync<ObjectDisposedException>();
    }

    [Fact]
    public async Task UncomposedAttachmentStoreIsExplicitlyUnavailableWithoutCreatingAnotherAuthority()
    {
        using var fixture = new Fixture();
        var sessions = new SessionWorkspaceService(new NoFileStore(fixture), new(fixture), fixture,
            fixture.Logger);
        using var root = HostActivity.BeginRoot(fixture.Request, HostActivityLayer.Application, HostOperation.Request);
        var read = () => sessions.ReadAttachment(fixture.Request.SessionId, fixture.Token);
        await read.Should().ThrowAsync<InvalidOperationException>().WithMessage("*file store is unavailable*");
        fixture.TaskWrites.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ChangedExactSessionGenerationBeforeReviewOrConfirmationCannotReadOrPersist(bool afterReview)
    {
        using var fixture = new Fixture { FileReceipts = true };
        var audit = new FileAudit();
        var capture = new FileCapture();
        await using var service = FileService(fixture, FileAction(capture, audit), audit);
        if (afterReview)
        {
            await service.Select(new(fixture.Session, null), new FilePicker(), () => true, fixture.Token);
        }
        fixture.ExactListReader = session => new(fixture.Session with { Generation = new(2) }, null);
        Func<Task<LocalFileOutcome>> operation = afterReview
            ? () => service.Confirm(service.Review!.ReviewId, fixture.Token)
            : () => service.Select(new(fixture.Session, null), new FilePicker(), () => true, fixture.Token);
        await operation.Should().ThrowAsync<InvalidOperationException>().WithMessage("*generation*");
        fixture.RetainedFile.Should().BeNull();
        fixture.TaskWrites.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OriginalAdmissionClosureDuringExactTargetResolutionRejectsBeforeCapture(bool afterReview)
    {
        using var fixture = new Fixture { FileReceipts = true };
        var audit = new FileAudit();
        var admitted = true;
        await using var service = FileService(fixture, FileAction(new FileCapture(), audit), audit);
        if (afterReview)
        {
            await service.Select(new(fixture.Session, null), new FilePicker(), () => admitted, fixture.Token);
        }
        fixture.ExactListReader = session =>
        {
            admitted = false;
            return new(fixture.Session, null);
        };
        Func<Task<LocalFileOutcome>> operation = afterReview
            ? () => service.Confirm(service.Review!.ReviewId, fixture.Token)
            : () => service.Select(new(fixture.Session, null), new FilePicker(), () => admitted, fixture.Token);
        await operation.Should().ThrowAsync<InvalidOperationException>();
        fixture.RetainedFile.Should().BeNull();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExactSessionDispositionRevokesAndQuiescesNativeAttachmentBeforeDurableControl(bool nativeViewer)
    {
        using var fixture = new Fixture();
        var audit = new FileAudit();
        var action = FileAction(new FileCapture(), audit);
        await using var service = FileService(fixture, action, audit);
        fixture.Service.BindAttachmentLifecycle(service);
        using var root = HostActivity.BeginRoot(fixture.Request, HostActivityLayer.Application, HostOperation.Request);
        await action.Select(new FilePicker(), () => true, fixture.Token);
        var preview = await fixture.Service.PreviewDispositionAsync(fixture.Request.SessionId, new(1), 0, fixture.Token);
        var revoked = false;
        if (nativeViewer) { service.Revoked += session => { revoked = true; session.Should().Be(fixture.Request.SessionId); }; }
        await fixture.Service.ConfirmDispositionAsync(preview, RequestOrigin.LocalUi, () => true, fixture.Token);
        revoked.Should().Be(nativeViewer);
        service.Review.Should().BeNull();
        service.IsQuiescent.Should().BeTrue();
    }

    [Fact]
    public async Task CancellationInsideRequiredSuccessAuditCannotPublishBodyAfterOperationReturns()
    {
        using var fixture = new Fixture();
        fixture.RetainedFile = FileRecord(fixture.Request);
        var audit = new FileAudit();
        await using var service = FileService(fixture, FileAction(new FileCapture(), audit), audit);
        audit.AfterSuccess = service.Revoke;
        var read = () => service.Read(fixture.Request.SessionId, fixture.Token);
        await read.Should().ThrowAsync<OperationCanceledException>();
    }

    private static LocalSessionFileAttach FileAction(FileCapture capture, FileAudit audit) =>
        new(capture, audit, TimeProvider.System, NullLogger<LocalFilePreview>.Instance);

    private static SessionFileAttachmentService FileService(Fixture fixture, LocalSessionFileAttach action, FileAudit audit,
        TimeProvider? time = null) => new(fixture.Service, fixture, action, new LocalFileLexicalRetrieval(),
            time ?? TimeProvider.System, audit, NullLogger<SessionFileAttachmentService>.Instance);

    private static SessionFileAttachment FileRecord(HostRequest request)
    {
        byte[] bytes = [0xef, 0xbb, 0xbf, .. Encoding.UTF8.GetBytes("original café source")];
        return new(request.SessionId, new(1), new(1), new(
            new(Guid.NewGuid(), Guid.NewGuid(), request, new(@"C:\Team\source.md", "native", bytes.Length, DateTimeOffset.UnixEpoch)),
            bytes, DateTimeOffset.UnixEpoch));
    }

    private sealed partial class Fixture : ISessionFileStore
    {
        internal bool FileReceipts { get; init; }
        internal SessionFileAttachment? RetainedFile { get; set; }
        internal Action? BeforeFileRead { get; set; }
        internal Func<Task<SessionFileAttachment?>>? FileReader { get; set; }
        internal Func<Task>? FileRemover { get; set; }
        internal Exception? FileRemoveFailure { get; set; }
        internal int FileReads { get; private set; }
        internal Func<SessionFileRemoval>? FileInventory { get; set; }
        internal Action? BeforeFileReplace { get; set; }
        internal void AdvanceFileControl() => ControlRevision++;
        public async ValueTask<SessionFileAttachment?> ReadAttachment(HostId<SessionIdentity> session, CancellationToken token)
        {
            FileReads++;
            BeforeFileRead?.Invoke();
            return FileReader is not null ? await FileReader() : RetainedFile;
        }
        public async ValueTask<SessionFileAttachment> Attach(HostRequest request, HostRevision generation, LocalFileRevision file,
            ReadOnlyMemory<byte> bytes, Func<bool> admitted, CancellationToken token)
        {
            admitted().Should().BeTrue();
            file.Digest.Should().Be(LocalFilePolicy.Digest(bytes.Span));
            RetainedFile = new(request.SessionId, generation, new(1), file);
            await CommitAsync(new(request, new(2), HostTaskState.Succeeded), 1, token);
            return RetainedFile;
        }
        public async ValueTask<SessionFileAttachment> Replace(HostRequest request, SessionFileRemoval previous,
            LocalFileRevision file, ReadOnlyMemory<byte> bytes, Func<bool> admitted, CancellationToken token)
        {
            BeforeFileReplace?.Invoke();
            if (RetainedFile?.StorageRevision != previous.StorageRevision || RetainedFile.File.Reference != previous.File)
            {
                throw new InvalidOperationException("Exact previous attachment changed.");
            }
            admitted().Should().BeTrue();
            file.Digest.Should().Be(LocalFilePolicy.Digest(bytes.Span));
            RetainedFile = new(request.SessionId, previous.Generation, new(previous.StorageRevision.Value + 1), file);
            await CommitAsync(new(request, new(2), HostTaskState.Succeeded), 1, token);
            return RetainedFile;
        }
        public ValueTask<SessionFileRemoval> PreviewRemoval(HostId<SessionIdentity> session, CancellationToken token) =>
            ValueTask.FromResult(FileInventory?.Invoke()
                ?? new SessionFileRemoval(Guid.NewGuid(), session, new(1), new(1), RetainedFile!.File.Reference, "inventory"));
        public async ValueTask Remove(HostRequest request, SessionFileRemoval review, Func<bool> admitted, CancellationToken token)
        {
            if (FileRemoveFailure is { } failure) { throw failure; }
            if (FileRemover is not null) { await FileRemover(); }
            admitted().Should().BeTrue();
            RetainedFile = null;
            await CommitAsync(new(request, new(2), HostTaskState.Succeeded), 1, token);
        }
    }

    private sealed class FilePicker : IUserFilePicker
    {
        internal bool Cancelled { get; init; }
        public Task<string?> SelectAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Cancelled ? null : @"C:\Team\source.md");
    }

    private sealed class FileCapture : ILocalFileInspector, ILocalFileSelection
    {
        private readonly byte[] bytes = [0xef, 0xbb, 0xbf, .. Encoding.UTF8.GetBytes("original café source")];
        internal int Disposals { get; private set; }
        internal bool ReadFailure { get; init; }
        internal bool ReadCancellation { get; init; }
        public LocalFileMetadata Metadata => new(@"C:\Team\source.md", "native", bytes.Length, DateTimeOffset.UnixEpoch);
        public Task<ILocalFileSelection> InspectAsync(string selectedPath, CancellationToken cancellationToken) =>
            Task.FromResult<ILocalFileSelection>(this);
        public Task<byte[]> ReadAsync(CancellationToken cancellationToken) =>
            ReadCancellation ? Task.FromException<byte[]>(new OperationCanceledException("native capture cancellation"))
                : ReadFailure ? Task.FromException<byte[]>(new IOException("source unavailable")) : Task.FromResult(bytes);
        public void Dispose() => Disposals++;
    }

    private sealed class FileAudit : ISecurityAuditLog
    {
        internal List<SecurityAuditEvent> Events { get; } = [];
        internal bool FailSuccess { get; set; }
        internal bool FailRequested { get; set; }
        internal Action? AfterSuccess { get; set; }
        public void Write(SecurityAuditEvent auditEvent)
        {
            if (FailRequested && auditEvent.Outcome == SecurityAuditOutcome.Requested)
            {
                FailRequested = false;
                throw new IOException("required requested audit failure");
            }
            if (FailSuccess && auditEvent.Outcome == SecurityAuditOutcome.Succeeded)
            {
                FailSuccess = false;
                throw new IOException("required audit failure");
            }
            Events.Add(auditEvent);
            if (auditEvent.Outcome == SecurityAuditOutcome.Succeeded) { AfterSuccess?.Invoke(); }
        }
    }

    private sealed class FileClock : TimeProvider
    {
        internal DateTimeOffset Now { get; set; } = DateTimeOffset.UnixEpoch;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class EnabledFileLogger : Microsoft.Extensions.Logging.ILogger<SessionFileAttachmentService>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;
        public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId,
            TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            formatter(state, exception).Should().NotContain("original café");
    }

    private sealed class NoFileStore(Fixture fixture) : ISessionWorkspaceStore
    {
        public ValueTask<SessionDispositionPreview> PreviewDispositionAsync(HostId<SessionIdentity> session,
            HostRevision expectedGeneration, long expectedMetadataRevision, CancellationToken cancellationToken) =>
            fixture.PreviewDispositionAsync(session, expectedGeneration, expectedMetadataRevision, cancellationToken);
        public ValueTask<SessionDispositionReceipt> DisposeSessionAsync(HostRequest request, SessionDispositionPreview preview,
            Func<bool> canControl, CancellationToken cancellationToken) => fixture.DisposeSessionAsync(request, preview, canControl, cancellationToken);
        public ValueTask<HostTaskRecord> RecordControlIntentAsync(HostRequest request, CancellationToken cancellationToken) =>
            fixture.RecordControlIntentAsync(request, cancellationToken);
        public ValueTask<SessionPage<Kora.Core.Authorization.WorkSessionAuthorization>> ReadSessionsAsync(Guid? after, int limit,
            CancellationToken cancellationToken) => fixture.ReadSessionsAsync(after, limit, cancellationToken);
        public ValueTask<SessionPage<SessionWorkspaceEntry>> ReadMetadataPageAsync(Guid? after, int limit,
            CancellationToken cancellationToken) => fixture.ReadMetadataPageAsync(after, limit, cancellationToken);
        public ValueTask<SessionWorkspaceEntry> ReadMetadataAsync(HostId<SessionIdentity> session, CancellationToken cancellationToken) =>
            fixture.ReadMetadataAsync(session, cancellationToken);
        public ValueTask<HostTaskObservation?> ReadTaskAsync(HostId<SessionIdentity> session, HostId<TaskIdentity> task,
            CancellationToken cancellationToken) => fixture.ReadTaskAsync(session, task, cancellationToken);
        public ValueTask<HostTaskObservation> CancelWaitingTaskAsync(HostRequest control, HostTaskCancellationTarget target,
            Func<bool> canControl, CancellationToken cancellationToken) => fixture.CancelWaitingTaskAsync(control, target, canControl, cancellationToken);
        public ValueTask<SessionWorkspaceEntry> CreateNamedSessionAsync(HostRequest request, SessionName name,
            Func<bool> canControl, CancellationToken cancellationToken) => fixture.CreateNamedSessionAsync(request, name, canControl, cancellationToken);
        public ValueTask<SessionWorkspaceEntry> RenameSessionAsync(HostRequest request, HostRevision expectedGeneration,
            long expectedMetadataRevision, SessionName name, Func<bool> canControl, CancellationToken cancellationToken) =>
            fixture.RenameSessionAsync(request, expectedGeneration, expectedMetadataRevision, name, canControl, cancellationToken);
        public ValueTask<SessionPage<Kora.Core.Interaction.HostQuestionRecord>> ReadQuestionPageAsync(HostId<SessionIdentity> session,
            Guid? after, int limit, CancellationToken cancellationToken) => fixture.ReadQuestionPageAsync(session, after, limit, cancellationToken);
        public ValueTask<SessionPage<HostTaskRecord>> ReadTaskPageAsync(HostId<SessionIdentity> session, Guid? after, int limit,
            CancellationToken cancellationToken) => fixture.ReadTaskPageAsync(session, after, limit, cancellationToken);
        public ValueTask<Kora.Core.Authorization.WorkSessionAuthorization> ChangeIdleLifecycleAsync(HostRequest request,
            HostRevision expectedGeneration, bool active, Func<bool> canControl, CancellationToken cancellationToken) =>
            fixture.ChangeIdleLifecycleAsync(request, expectedGeneration, active, canControl, cancellationToken);
    }
}
