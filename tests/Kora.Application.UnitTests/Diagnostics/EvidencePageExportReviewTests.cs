using System.Diagnostics;
using AwesomeAssertions;
using Kora.Application.Diagnostics;
using Kora.Core.Auditing;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Application.UnitTests.Diagnostics;

[Collection("Host tracing")]
public sealed class EvidencePageExportReviewTests : IDisposable
{
    private readonly ActivityListener listener = new()
    {
        ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
        Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
    };
    public EvidencePageExportReviewTests() => ActivitySource.AddActivityListener(listener);
    public void Dispose() => listener.Dispose();
    private static EvidencePageExportSource Source(Func<CancellationToken, Task>? validate = null) =>
        new(new(new(EvidencePageStatus.Available, [], null, [], EvidencePage.StorageDisclosure)), validate ?? (_ => Task.CompletedTask));

    [Fact]
    public async Task FreshNativeReviewShowsExactBytesThenAuditsOneUseOnly()
    {
        using var host = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Application, HostOperation.Request);
        var source = Source();
        var destination = new Destination();
        var audit = new Audit();
        await using var review = new EvidencePageExportReview(source, destination, () => true, audit, NullLogger<EvidencePageExportReview>.Instance);
        await review.ValidateAsync(TestContext.Current.CancellationToken);
        review.Snapshot.Should().BeSameAs(source.Snapshot);
        review.Destination.Should().Be(destination.Path);
        destination.Calls.Should().Be(0);
        audit.Events.Should().BeEmpty();
        (await review.ConfirmAsync(TestContext.Current.CancellationToken)).Should().Be(EvidencePageExportOutcome.Saved);
        destination.Bytes.Should().Equal(source.Snapshot.CopyBytes());
        audit.Events.Select(item => item.Outcome).Should().Equal(SecurityAuditOutcome.Requested, SecurityAuditOutcome.Succeeded);
        audit.Events.Should().OnlyContain(item => item.ActionId == "evidence.export");
        var replay = () => review.ConfirmAsync(TestContext.Current.CancellationToken);
        await replay.Should().ThrowAsync<InvalidOperationException>();
        await review.DisposeAsync();
        await review.DisposeAsync();
        destination.Disposals.Should().Be(1);
        destination.Calls.Should().Be(1);
        EvidencePageExportReview.RetentionDisclosure.Should().Contain("does not delete").And.Contain("sync");
    }

    [Theory]
    [InlineData("privacy")]
    [InlineData("source")]
    [InlineData("after-audit")]
    [InlineData("requested-audit")]
    [InlineData("cancel")]
    [InlineData("request")]
    [InlineData("disposed")]
    [InlineData("during-read")]
    public async Task DeniesDriftOrLostAuthorityWithoutPublication(string fault)
    {
        var request = HostRequest.Create(RequestOrigin.LocalUi);
        using var host = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request);
        using var cancellation = new CancellationTokenSource();
        var live = fault is not "privacy";
        var destination = new Destination();
        var audit = new Audit { FailRequested = fault is "requested-audit",
            AfterRequested = () => { if (fault is "after-audit") { live = false; } } };
        var source = Source(_ =>
        {
            if (fault is "during-read") { live = false; }
            return fault is "source" ? Task.FromException(new InvalidOperationException("source drift")) : Task.CompletedTask;
        });
        await using var review = new EvidencePageExportReview(source, destination, () => live, audit, NullLogger<EvidencePageExportReview>.Instance);
        if (fault is "cancel") { cancellation.Cancel(); }
        if (fault is "disposed")
        {
            await review.DisposeAsync();
            var confirm = () => review.ConfirmAsync(cancellation.Token);
            await confirm.Should().ThrowAsync<InvalidOperationException>();
        }
        else if (fault is "request")
        {
            using var other = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Application, HostOperation.Request);
            (await review.ConfirmAsync(cancellation.Token)).Should().Be(EvidencePageExportOutcome.NotWritten);
        }
        else
        {
            (await review.ConfirmAsync(cancellation.Token)).Should().Be(fault is "cancel"
                ? EvidencePageExportOutcome.Cancelled : EvidencePageExportOutcome.NotWritten);
        }
        destination.Calls.Should().Be(0);
        audit.Events.Should().NotContain(item => item.Outcome == SecurityAuditOutcome.Succeeded);
    }

    [Theory]
    [InlineData(EvidencePageExportOutcome.Saved, false)]
    [InlineData(EvidencePageExportOutcome.NotWritten, false)]
    [InlineData(EvidencePageExportOutcome.Cancelled, false)]
    [InlineData(EvidencePageExportOutcome.Uncertain, false)]
    [InlineData(EvidencePageExportOutcome.Saved, true)]
    [InlineData(EvidencePageExportOutcome.NotWritten, true)]
    [InlineData(EvidencePageExportOutcome.Cancelled, true)]
    [InlineData(EvidencePageExportOutcome.Uncertain, true)]
    public async Task TerminalReceiptPreservesEffectUncertainty(EvidencePageExportOutcome outcome, bool failTerminal)
    {
        using var host = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Application, HostOperation.Request);
        var destination = new Destination { Outcome = outcome };
        await using var review = new EvidencePageExportReview(Source(), destination, () => true,
            new Audit { FailTerminal = failTerminal }, NullLogger<EvidencePageExportReview>.Instance);
        (await review.ConfirmAsync(TestContext.Current.CancellationToken)).Should().Be(
            failTerminal && outcome == EvidencePageExportOutcome.Saved ? EvidencePageExportOutcome.Uncertain : outcome);
        destination.Calls.Should().Be(1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AThrowingWriterCannotClaimRollback(bool cancel)
    {
        using var host = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Application, HostOperation.Request);
        var destination = new Destination { Error = cancel ? new OperationCanceledException() : new IOException("uncertain effect") };
        await using var review = new EvidencePageExportReview(Source(), destination, () => true, new Audit(), NullLogger<EvidencePageExportReview>.Instance);
        (await review.ConfirmAsync(TestContext.Current.CancellationToken)).Should().Be(EvidencePageExportOutcome.Uncertain);
    }

    [Theory]
    [InlineData(RequestOrigin.ActivatedVoice, false)]
    [InlineData(RequestOrigin.LocalUi, true)]
    public void DiagnosticAndModelIdentityIsNotNativeWriteAuthority(RequestOrigin origin, bool invocation)
    {
        var request = HostRequest.Create(origin);
        if (invocation) { request = new(request.RequestId, request.SessionId, request.TaskId, origin, new(Guid.NewGuid())); }
        using var host = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request);
        var create = () => new EvidencePageExportReview(Source(), new Destination(), () => true, new Audit(), NullLogger<EvidencePageExportReview>.Instance);
        create.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public async Task DurableSourceUsesOriginalSnapshotAndPagePositionWithoutFollowingCursor()
    {
        using var host = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Application, HostOperation.Evidence);
        var reader = new Reader();
        var access = new Access();
        var query = new DurableEvidenceQuery(reader, access, TimeProvider.System, NullLogger<DurableEvidenceQuery>.Instance);
        var page = await query.QueryAsync(new() { Source = EvidenceSource.Log }, null, TestContext.Current.CancellationToken);
        var source = query.CaptureExportSource(page);
        query.CanExport(page).Should().BeTrue();
        query.CanExport(page with { Cursor = null }).Should().BeFalse();
        using (var freshExportActor = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Application, HostOperation.Storage))
        {
            await source.ValidateAsync(TestContext.Current.CancellationToken);
            HostActivity.RequireCurrent().Request.Should().Be(freshExportActor.Request);
        }
        reader.Checkpoints.Should().HaveCount(2);
        reader.Checkpoints[0].Should().BeNull();
        reader.Checkpoints[1]!.Snapshot.Should().Be(reader.Batch.Snapshot);
        reader.Checkpoints[1]!.After.Should().BeNull();
        source.Snapshot.Text.Should().Contain("\"Cursor\":null");
        reader.Batch = reader.Batch with { HasMore = true };
        var changed = () => source.ValidateAsync(TestContext.Current.CancellationToken);
        await changed.Should().ThrowAsync<InvalidOperationException>();
        access.CanInspect = false;
        await changed.Should().ThrowAsync<InvalidOperationException>();
        Action fabricated = () => query.CaptureExportSource(page with { Cursor = null });
        fabricated.Should().Throw<InvalidOperationException>();
    }

    private sealed class Destination : IEvidencePageExportDestination
    {
        public string Path => "Q:\\synthetic\\evidence.json";
        internal int Calls { get; private set; }
        internal int Disposals { get; private set; }
        internal byte[]? Bytes { get; private set; }
        internal EvidencePageExportOutcome Outcome { get; init; } = EvidencePageExportOutcome.Saved;
        internal Exception? Error { get; init; }
        public async Task<EvidencePageExportOutcome> PublishAsync(EvidencePageExportSnapshot source,
            Func<CancellationToken, Task> revalidate, Func<bool> admitted, CancellationToken token)
        {
            Calls++;
            await revalidate(token);
            admitted().Should().BeTrue();
            Bytes = source.CopyBytes();
            if (Error is not null) { throw Error; }
            return Outcome;
        }
        public ValueTask DisposeAsync() { Disposals++; return ValueTask.CompletedTask; }
    }
    private sealed class Audit : ISecurityAuditLog
    {
        internal List<SecurityAuditEvent> Events { get; } = [];
        internal bool FailRequested { get; init; }
        internal bool FailTerminal { get; init; }
        internal Action? AfterRequested { get; init; }
        public void Write(SecurityAuditEvent auditEvent)
        {
            if (auditEvent.Outcome == SecurityAuditOutcome.Requested)
            {
                if (FailRequested) { throw new IOException("requested audit unavailable"); }
                AfterRequested?.Invoke();
            }
            else if (FailTerminal) { throw new IOException("terminal audit unavailable"); }
            Events.Add(auditEvent);
        }
    }
    private sealed class Access : IEvidenceQueryAccess { public bool CanInspect { get; set; } = true; }
    private sealed class Reader : IEvidenceReader
    {
        internal EvidenceReadBatch Batch { get; set; } = new(new(1, 2, 3, 4), [], null, false, false);
        internal List<EvidenceReadCheckpoint?> Checkpoints { get; } = [];
        public ValueTask<EvidenceReadBatch> ReadAsync(EvidenceQuery query, EvidenceReadCheckpoint? checkpoint,
            HostRequest request, DateTimeOffset now, CancellationToken cancellationToken)
        {
            HostActivity.RequireCurrent().Request.Should().Be(request);
            Checkpoints.Add(checkpoint);
            return ValueTask.FromResult(Batch);
        }
    }
}
