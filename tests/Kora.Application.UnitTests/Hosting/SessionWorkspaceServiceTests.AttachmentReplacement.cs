using AwesomeAssertions;
using Kora.Core.Context;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Application.UnitTests.Hosting;

public sealed partial class SessionWorkspaceServiceTests
{
    [Fact]
    public async Task NativeReplacementBindsBothExactReviewsRetiresOldReadersAndPublishesOnlyFreshHistoricalSource()
    {
        using var fixture = new Fixture { FileReceipts = true };
        var old = FileRecord(fixture.Request);
        fixture.RetainedFile = old;
        var audit = new FileAudit();
        await using var service = FileService(fixture, FileAction(new FileCapture(), audit), audit);
        service.ReplacementReview.Should().BeNull();
        var retired = 0;
        service.Revoked += session => { session.Should().Be(old.Session); retired++; };
        (await service.SelectReplacement(new(fixture.Session, null), old, new FilePicker(), () => true, fixture.Token))
            .Should().Be(LocalFileOutcome.Reviewed);
        var review = service.ReplacementReview!;
        review.Previous.File.Should().Be(old.File.Reference);
        review.PreviousMetadata.Should().Be(old.File.Review.Metadata);
        review.PreviousCapturedAt.Should().Be(old.File.AdmittedAt);
        review.Next.Should().BeSameAs(service.Review);
        review.Next.SourceId.Should().NotBe(old.File.Review.SourceId);
        fixture.RetainedFile.Should().BeSameAs(old);
        (await service.Confirm(review.Next.ReviewId, fixture.Token)).Should().Be(LocalFileOutcome.Admitted);
        service.ReplacementReview.Should().BeNull();
        retired.Should().Be(2);
        var current = (await service.Read(old.Session, fixture.Token))!;
        current.File.Reference.Should().NotBe(old.File.Reference);
        current.StorageRevision.Value.Should().Be(2);
        current.File.Review.Request.SessionId.Should().Be(old.Session);
        var stale = () => service.Search(old, "café", fixture.Token);
        await stale.Should().ThrowAsync<InvalidOperationException>();
        (await service.Search(current, "café", fixture.Token)).Outcome.Should().Be(LocalFileSearchOutcome.Matched);
        audit.Events.Where(item => string.Equals(item.ActionId, "session.file.replace-copy", StringComparison.Ordinal))
            .Select(item => item.Outcome).Should().Equal(Kora.Core.Auditing.SecurityAuditOutcome.Requested,
                Kora.Core.Auditing.SecurityAuditOutcome.Succeeded);
    }

    [Theory]
    [InlineData("absent")]
    [InlineData("session")]
    [InlineData("storage")]
    [InlineData("reference")]
    [InlineData("inventory-body")]
    [InlineData("inventory-pending")]
    [InlineData("inventory-unconfirmed")]
    [InlineData("inventory-storage")]
    [InlineData("inventory-reference")]
    [InlineData("inventory-generation")]
    [InlineData("inventory-admission")]
    public async Task ReplacementCannotSelectAgainstStaleHistoricalSnapshotOrDifferentExactInventory(string boundary)
    {
        using var fixture = new Fixture();
        var old = FileRecord(fixture.Request);
        fixture.RetainedFile = string.Equals(boundary, "absent", StringComparison.Ordinal) ? null : old;
        var selected = boundary switch
        {
            "session" => old with { Session = new(Guid.NewGuid()) },
            "storage" => old with { StorageRevision = new(99) },
            "reference" => FileRecord(fixture.Request),
            _ => old,
        };
        var admitted = true;
        fixture.FileInventory = () =>
        {
            var review = new SessionFileRemoval(Guid.NewGuid(), old.Session, new(1), new(1), old.File.Reference, "inventory");
            if (string.Equals(boundary, "inventory-admission", StringComparison.Ordinal)) { admitted = false; }
            return boundary switch
            {
                "inventory-body" => review with { BodyRetained = false },
                "inventory-pending" => review with { ReplacementCopyVerificationPending = true },
                "inventory-unconfirmed" => review with { ReplacementSwapUnconfirmed = true },
                "inventory-storage" => review with { StorageRevision = new(99) },
                "inventory-reference" => review with { File = FileRecord(fixture.Request).File.Reference },
                "inventory-generation" => review with { Generation = new(99) },
                _ => review,
            };
        };
        var audit = new FileAudit();
        var capture = new FileCapture();
        await using var service = FileService(fixture, FileAction(capture, audit), audit);
        var select = () => service.SelectReplacement(new(fixture.Session, null), selected, new FilePicker(), () => admitted, fixture.Token);
        await select.Should().ThrowAsync<InvalidOperationException>();
        fixture.TaskWrites.Should().BeEmpty();
        capture.Disposals.Should().Be(0);
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("capture")]
    [InlineData("stale-replace")]
    [InlineData("audit-request")]
    [InlineData("audit-terminal")]
    public async Task ReplacementFailureNeverClaimsRefreshAndRequiresFreshReadOfExactDurableState(string boundary)
    {
        using var fixture = new Fixture { FileReceipts = true };
        var old = FileRecord(fixture.Request);
        fixture.RetainedFile = old;
        var audit = new FileAudit();
        await using var service = FileService(fixture, FileAction(new FileCapture
            { ReadFailure = string.Equals(boundary, "capture", StringComparison.Ordinal) }, audit), audit);
        (await service.SelectReplacement(new(fixture.Session, null), old, new FilePicker(), () => true, fixture.Token))
            .Should().Be(LocalFileOutcome.Reviewed);
        var id = service.Review!.ReviewId;
        using var cancellation = new CancellationTokenSource();
        if (string.Equals(boundary, "cancel", StringComparison.Ordinal)) { await cancellation.CancelAsync(); }
        if (string.Equals(boundary, "stale-replace", StringComparison.Ordinal)) { fixture.RetainedFile = FileRecord(fixture.Request); }
        if (string.Equals(boundary, "audit-request", StringComparison.Ordinal)) { audit.FailRequested = true; }
        if (string.Equals(boundary, "audit-terminal", StringComparison.Ordinal))
        {
            fixture.BeforeFileReplace = () => audit.FailSuccess = true;
        }
        if (string.Equals(boundary, "audit-request", StringComparison.Ordinal))
        {
            var confirm = () => service.Confirm(id, cancellation.Token);
            await confirm.Should().ThrowAsync<IOException>();
        }
        else { (await service.Confirm(id, cancellation.Token)).Should().NotBe(LocalFileOutcome.Admitted); }
        service.ReplacementReview.Should().BeNull();
        if (boundary is "cancel" or "capture" or "audit-request") { fixture.RetainedFile.Should().BeSameAs(old); }
    }

    [Fact]
    public async Task ReplacementSelectionCancelsInFlightOldReaderBeforeFreshMetadataReviewPublication()
    {
        using var fixture = new Fixture();
        var old = FileRecord(fixture.Request);
        fixture.RetainedFile = old;
        var audit = new FileAudit();
        await using var service = FileService(fixture, FileAction(new FileCapture(), audit), audit);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var released = new TaskCompletionSource<SessionFileAttachment?>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.FileReader = () =>
        {
            if (fixture.FileReads != 1) { return Task.FromResult<SessionFileAttachment?>(old); }
            started.SetResult();
            return released.Task;
        };
        var read = service.Read(old.Session, fixture.Token);
        await started.Task;
        service.Revoked += _ => released.TrySetResult(old);
        (await service.SelectReplacement(new(fixture.Session, null), old, new FilePicker(), () => true, fixture.Token))
            .Should().Be(LocalFileOutcome.Reviewed);
        await ((Func<Task>)(async () => await read)).Should().ThrowAsync<OperationCanceledException>();
        fixture.RetainedFile.Should().BeSameAs(old);
    }

    [Fact]
    public async Task HeldReplacementIsExplicitAndDoesNotDiscloseOrRestoreEitherBody()
    {
        using var fixture = new Fixture();
        fixture.FileReader = () => Task.FromException<SessionFileAttachment?>(new SessionFileReplacementHeldException());
        var audit = new FileAudit();
        await using var service = FileService(fixture, FileAction(new FileCapture(), audit), audit);
        var read = () => service.Read(fixture.Session.SessionId, fixture.Token);
        await read.Should().ThrowAsync<SessionFileReplacementHeldException>().WithMessage("*no old source is restored*");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativeCaptureCancellationPropagatesWithQuiescenceAndTruthfulUnchangedDurableState(bool replacing)
    {
        using var fixture = new Fixture();
        var old = FileRecord(fixture.Request);
        fixture.RetainedFile = replacing ? old : null;
        var audit = new FileAudit();
        await using var service = FileService(fixture, FileAction(new FileCapture { ReadCancellation = true }, audit), audit);
        if (replacing) { await service.SelectReplacement(new(fixture.Session, null), old, new FilePicker(), () => true, fixture.Token); }
        else { await service.Select(new(fixture.Session, null), new FilePicker(), () => true, fixture.Token); }
        if (!replacing) { service.ReplacementReview.Should().BeNull(); }
        var confirm = () => service.Confirm(service.Review!.ReviewId, fixture.Token);
        await confirm.Should().ThrowAsync<OperationCanceledException>();
        service.IsQuiescent.Should().BeTrue();
        fixture.RetainedFile.Should().BeSameAs(replacing ? old : null);
    }

    [Fact]
    public async Task RevokedNativeMetadataReviewCannotExposeCachedReplacementConsent()
    {
        using var fixture = new Fixture();
        var old = FileRecord(fixture.Request);
        fixture.RetainedFile = old;
        var audit = new FileAudit();
        var action = FileAction(new FileCapture(), audit);
        await using var service = FileService(fixture, action, audit);
        await service.SelectReplacement(new(fixture.Session, null), old, new FilePicker(), () => true, fixture.Token);
        action.Revoke();
        service.ReplacementReview.Should().BeNull();
        var confirm = () => service.Confirm(Guid.NewGuid(), fixture.Token);
        await confirm.Should().ThrowAsync<InvalidOperationException>();
        fixture.RetainedFile.Should().BeSameAs(old);
    }

    [Fact]
    public async Task NewReadersCannotPublishHistoricalBodyWhileReplacementControlsItsSource()
    {
        using var fixture = new Fixture { FileReceipts = true };
        var old = FileRecord(fixture.Request);
        fixture.RetainedFile = old;
        var audit = new FileAudit();
        await using var service = FileService(fixture, FileAction(new FileCapture(), audit), audit);
        await service.SelectReplacement(new(fixture.Session, null), old, new FilePicker(), () => true, fixture.Token);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.FileReplacer = async () => { entered.SetResult(); await release.Task; };
        var confirm = service.Confirm(service.Review!.ReviewId, fixture.Token);
        await entered.Task;
        var read = () => service.Read(old.Session, fixture.Token);
        await read.Should().ThrowAsync<InvalidOperationException>().WithMessage("*controlling this source*");
        release.SetResult();
        (await confirm).Should().Be(LocalFileOutcome.Admitted);
        (await service.Read(old.Session, fixture.Token))!.File.Reference.Should().NotBe(old.File.Reference);
    }
}
