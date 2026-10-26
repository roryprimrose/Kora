using System.Diagnostics;
using System.Text;
using AwesomeAssertions;
using Kora.Application.KnowledgeSources;
using Kora.Core.Auditing;
using Kora.Core.Context;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.KnowledgeSources;

namespace Kora.Application.UnitTests.KnowledgeSources;

[Collection("Host tracing")]
public sealed class KnowledgeSourceManagerTests : IDisposable
{
    private readonly ActivityListener listener = new()
    {
        ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
        Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
    };
    public KnowledgeSourceManagerTests() => ActivitySource.AddActivityListener(listener);
    public void Dispose() => listener.Dispose();
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ExactNativeReviewReadsOnlyAfterConsentAndPublishesOnlyAfterSnapshotConfirmation()
    {
        using var host = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Application, HostOperation.Request);
        var fixture = new Fixture();
        using var manager = fixture.Manager();
        var review = await manager.ReviewAsync("  Team notes  ", @"C:\Team", null, () => true, Token);
        fixture.Reads.Should().Be(0);
        fixture.Published.Should().BeNull();
        review.Metadata.Files.Should().Equal(fixture.Metadata.Files);
        var snapshot = await manager.CaptureAsync(review.ReviewId, () => true, Token);
        fixture.Reads.Should().Be(1);
        manager.Review.Should().BeNull();
        manager.Candidate.Should().BeSameAs(snapshot);
        fixture.Published.Should().BeNull();
        var source = await manager.PublishAsync(snapshot, () => true, Token);
        source.Name.Should().Be("Team notes");
        fixture.Published.Should().BeSameAs(snapshot);
        fixture.BytesAtPublish.Should().Equal(Encoding.UTF8.GetBytes("synthetic notes"));
        fixture.CapturedBytes!.Should().OnlyContain(value => value == 0);
        manager.Candidate.Should().BeNull();
        fixture.Disposed.Should().BeTrue();
        fixture.Events.Count(item => item.Outcome == SecurityAuditOutcome.Succeeded).Should().Be(2);
        var inspected = await manager.OpenAsync(source, () => true, Token);
        inspected.Should().BeSameAs(snapshot);
        (await manager.ListAsync(() => true, Token)).Should().ContainSingle();
        await manager.DisableAsync(source, () => true, Token);
        fixture.Disabled.Should().BeTrue();
        await manager.RemoveAsync(source, fixture.Time.GetUtcNow(), () => true, Token);
        fixture.Removed.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("bad\nname")]
    [InlineData("xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx")]
    public async Task InvalidNamesCannotInspectOrRead(string name)
    {
        using var host = HostActivity.BeginOperation(HostActivityLayer.Application, HostOperation.Request, RequestOrigin.LocalUi);
        var fixture = new Fixture();
        using var manager = fixture.Manager();
        await FluentActions.Awaiting(() => manager.ReviewAsync(name, @"C:\Team", null, () => true, Token))
            .Should().ThrowAsync<InvalidDataException>();
        fixture.Inspections.Should().Be(0);
        fixture.Reads.Should().Be(0);
    }

    [Theory]
    [InlineData(Scenario.Origin)]
    [InlineData(Scenario.Invocation)]
    [InlineData(Scenario.Gate)]
    [InlineData(Scenario.Terminal)]
    public async Task UnknownOrForeignAdmissionCannotRead(Scenario scenario)
    {
        var request = HostRequest.Create(scenario == Scenario.Origin ? RequestOrigin.ActivatedVoice : RequestOrigin.LocalUi);
        if (scenario == Scenario.Invocation)
        { request = new(request.RequestId, request.SessionId, request.TaskId, request.Origin, new(Guid.NewGuid())); }
        using var host = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request);
        if (scenario == Scenario.Terminal) { host.Complete(HostOperationOutcome.Completed); }
        var fixture = new Fixture();
        using var manager = fixture.Manager();
        await FluentActions.Awaiting(() => manager.ReviewAsync("notes", @"C:\Team", null, () => scenario != Scenario.Gate, Token))
            .Should().ThrowAsync<InvalidOperationException>();
        fixture.Inspections.Should().Be(0);
    }

    [Theory]
    [InlineData(Scenario.Root)]
    [InlineData(Scenario.Identity)]
    [InlineData(Scenario.Inspection)]
    [InlineData(Scenario.Revoked)]
    public async Task RefreshRetiresOldRevisionBeforeInspectionAndNeverExpandsRoot(Scenario scenario)
    {
        using var host = HostActivity.BeginOperation(HostActivityLayer.Application, HostOperation.Request, RequestOrigin.LocalUi);
        var fixture = new Fixture();
        using var manager = fixture.Manager();
        var source = fixture.Source();
        fixture.ThrowInspect = scenario == Scenario.Inspection;
        if (scenario == Scenario.Identity) { fixture.DirectoryIdentity = "replaced-root"; }
        var eligible = true;
        if (scenario == Scenario.Revoked) { fixture.AfterInspect = () => eligible = false; }
        var action = () => manager.ReviewAsync(source.Name, scenario == Scenario.Root ? @"C:\Other" : @"C:\Team",
            source, () => eligible, Token);
        if (scenario == Scenario.Revoked) { await action.Should().ThrowAsync<InvalidOperationException>(); }
        else if (scenario == Scenario.Inspection) { await action.Should().ThrowAsync<IOException>(); }
        else { await action.Should().ThrowAsync<InvalidDataException>(); }
        fixture.RefreshStarted.Should().Be(scenario != Scenario.Root);
        fixture.Reads.Should().Be(0);
        manager.Review.Should().BeNull();
        if (scenario != Scenario.Root && scenario != Scenario.Inspection) { fixture.Disposed.Should().BeTrue(); }
    }

    [Fact]
    public async Task RefreshOfExactPhysicalRootPublishesOnlyItsNewCompleteRevision()
    {
        using var host = HostActivity.BeginOperation(HostActivityLayer.Application, HostOperation.Request, RequestOrigin.LocalUi);
        var fixture = new Fixture();
        using var manager = fixture.Manager();
        var source = fixture.Source();
        var review = await manager.ReviewAsync(source.Name, @"C:\Team", source, () => true, Token);
        fixture.RefreshStarted.Should().BeTrue();
        review.SourceId.Should().Be(source.Id);
        var snapshot = await manager.CaptureAsync(review.ReviewId, () => true, Token);
        var published = await manager.PublishAsync(snapshot, () => true, Token);
        published.Id.Should().Be(source.Id);
        fixture.ExpectedAtPublish!.State.Should().Be(KnowledgeSourceState.Unavailable);
    }

    [Theory]
    [InlineData(Scenario.Expired)]
    [InlineData(Scenario.Wrong)]
    [InlineData(Scenario.Validate)]
    [InlineData(Scenario.Read)]
    [InlineData(Scenario.Changed)]
    [InlineData(Scenario.Cancelled)]
    [InlineData(Scenario.Revoked)]
    public async Task InvalidCapturePublishesNothingAndReleasesExactHandles(Scenario scenario)
    {
        using var host = HostActivity.BeginOperation(HostActivityLayer.Application, HostOperation.Request, RequestOrigin.LocalUi);
        var fixture = new Fixture();
        using var manager = fixture.Manager();
        var review = await manager.ReviewAsync("notes", @"C:\Team", null, () => true, Token);
        if (scenario == Scenario.Expired) { fixture.Time.Advance(); }
        fixture.ThrowValidationAt = scenario == Scenario.Validate ? 1 : scenario == Scenario.Changed ? 2 : 0;
        fixture.ThrowRead = scenario == Scenario.Read;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        if (scenario == Scenario.Cancelled) { fixture.AfterRead = cancellation.Cancel; }
        var gate = true;
        if (scenario == Scenario.Revoked) { fixture.AfterRead = () => gate = false; }
        var action = () => manager.CaptureAsync(scenario == Scenario.Wrong ? Guid.NewGuid() : review.ReviewId, () => gate, cancellation.Token);
        if (scenario == Scenario.Cancelled) { await action.Should().ThrowAsync<OperationCanceledException>(); }
        else if (scenario is Scenario.Validate or Scenario.Changed) { await action.Should().ThrowAsync<InvalidDataException>(); }
        else if (scenario == Scenario.Read) { await action.Should().ThrowAsync<IOException>(); }
        else { await action.Should().ThrowAsync<InvalidOperationException>(); }
        manager.Candidate.Should().BeNull();
        fixture.Published.Should().BeNull();
        fixture.Disposed.Should().BeTrue();
        if (fixture.CapturedBytes is not null) { fixture.CapturedBytes.Should().OnlyContain(value => value == 0); }
    }

    [Theory]
    [InlineData(Scenario.Wrong)]
    [InlineData(Scenario.Expired)]
    [InlineData(Scenario.Changed)]
    [InlineData(Scenario.Revoked)]
    [InlineData(Scenario.TerminalAudit)]
    [InlineData(Scenario.Publish)]
    public async Task InvalidPublicationCannotLeaveAnEnabledSource(Scenario scenario)
    {
        using var host = HostActivity.BeginOperation(HostActivityLayer.Application, HostOperation.Request, RequestOrigin.LocalUi);
        var fixture = new Fixture();
        using var manager = fixture.Manager();
        var review = await manager.ReviewAsync("notes", @"C:\Team", null, () => true, Token);
        var snapshot = await manager.CaptureAsync(review.ReviewId, () => true, Token);
        if (scenario == Scenario.Expired) { fixture.Time.Advance(); }
        if (scenario == Scenario.Changed) { fixture.ThrowValidationAt = 3; }
        var gate = true;
        if (scenario == Scenario.Revoked) { fixture.AfterPublish = () => gate = false; }
        if (scenario == Scenario.TerminalAudit) { fixture.ThrowAuditAt = fixture.Events.Count + 2; }
        fixture.ThrowPublish = scenario == Scenario.Publish;
        var action = () => manager.PublishAsync(scenario == Scenario.Wrong ? new(snapshot.Review, snapshot.Files) : snapshot, () => gate, Token);
        if (scenario is Scenario.TerminalAudit or Scenario.Publish) { await action.Should().ThrowAsync<IOException>(); }
        else if (scenario == Scenario.Changed) { await action.Should().ThrowAsync<InvalidDataException>(); }
        else { await action.Should().ThrowAsync<InvalidOperationException>(); }
        fixture.Disabled.Should().Be(scenario is Scenario.Revoked or Scenario.TerminalAudit);
        fixture.Disposed.Should().BeTrue();
        manager.Candidate.Should().BeNull();
    }

    [Theory]
    [InlineData(Scenario.Members)]
    [InlineData(Scenario.Identity)]
    [InlineData(Scenario.Revoked)]
    public async Task InspectionFailsClosedAfterOriginalAccessOrPrivateAdmissionChanges(Scenario scenario)
    {
        using var host = HostActivity.BeginOperation(HostActivityLayer.Application, HostOperation.Request, RequestOrigin.LocalUi);
        var fixture = new Fixture();
        using var manager = fixture.Manager();
        var source = fixture.Source();
        if (scenario == Scenario.Identity) { fixture.DirectoryIdentity = "foreign"; }
        if (scenario == Scenario.Members) { fixture.Body = "changed"; }
        var gate = true;
        if (scenario == Scenario.Revoked) { fixture.AfterReadStore = () => gate = false; }
        var action = () => manager.OpenAsync(source, () => gate, Token);
        if (scenario == Scenario.Revoked) { await action.Should().ThrowAsync<InvalidOperationException>(); }
        else { await action.Should().ThrowAsync<InvalidDataException>(); }
        fixture.Disposed.Should().BeTrue();
    }

    [Theory]
    [InlineData(-2)]
    [InlineData(1)]
    public async Task RemovalRequiresUnexpiredInventoryReview(int minutes)
    {
        using var host = HostActivity.BeginOperation(HostActivityLayer.Application, HostOperation.Request, RequestOrigin.LocalUi);
        var fixture = new Fixture();
        using var manager = fixture.Manager();
        await FluentActions.Awaiting(() => manager.RemoveAsync(fixture.Source(), fixture.Time.GetUtcNow().AddMinutes(minutes),
            () => true, Token)).Should().ThrowAsync<InvalidOperationException>();
        fixture.Removed.Should().BeFalse();
    }

    [Fact]
    public async Task NoncooperativeSelectionRemainsOwnedUntilJoinedAndLateResultCannotPublish()
    {
        using var host = HostActivity.BeginOperation(HostActivityLayer.Application, HostOperation.Request, RequestOrigin.LocalUi);
        var fixture = new Fixture();
        var barrier = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.InspectWait = barrier.Task;
        using var manager = fixture.Manager();
        var eligible = true;
        var pending = manager.ReviewAsync("notes", @"C:\Team", null, () => eligible, Token);
        await FluentActions.Awaiting(() => manager.ListAsync(() => true, Token)).Should().ThrowAsync<InvalidOperationException>();
        FluentActions.Invoking(manager.Dispose).Should().Throw<InvalidOperationException>();
        eligible = false;
        barrier.SetResult();
        await FluentActions.Awaiting(() => pending).Should().ThrowAsync<InvalidOperationException>();
        fixture.Disposed.Should().BeTrue();
        fixture.Reads.Should().Be(0);
        manager.Dispose();
        await FluentActions.Awaiting(() => manager.ListAsync(() => true, Token)).Should().ThrowAsync<ObjectDisposedException>();
    }

    [Fact]
    public async Task ConsumedReadConfirmationCannotReadAgain()
    {
        using var host = HostActivity.BeginOperation(HostActivityLayer.Application, HostOperation.Request, RequestOrigin.LocalUi);
        var fixture = new Fixture();
        using var manager = fixture.Manager();
        var review = await manager.ReviewAsync("notes", @"C:\Team", null, () => true, Token);
        _ = await manager.CaptureAsync(review.ReviewId, () => true, Token);
        await FluentActions.Awaiting(() => manager.CaptureAsync(review.ReviewId, () => true, Token))
            .Should().ThrowAsync<InvalidOperationException>();
        fixture.Reads.Should().Be(1);
        fixture.Published.Should().BeNull();
    }

    [Fact]
    public async Task FailedFinalAuditRetainsOwnershipThroughAsynchronousDisableCleanup()
    {
        using var host = HostActivity.BeginOperation(HostActivityLayer.Application, HostOperation.Request, RequestOrigin.LocalUi);
        var fixture = new Fixture();
        using var manager = fixture.Manager();
        var review = await manager.ReviewAsync("notes", @"C:\Team", null, () => true, Token);
        var snapshot = await manager.CaptureAsync(review.ReviewId, () => true, Token);
        fixture.ThrowAuditAt = fixture.Events.Count + 2;
        var cleanup = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.DisableWait = cleanup.Task;
        var publication = manager.PublishAsync(snapshot, () => true, Token);
        publication.IsCompleted.Should().BeFalse();
        FluentActions.Invoking(manager.Dispose).Should().Throw<InvalidOperationException>();
        cleanup.SetResult();
        await FluentActions.Awaiting(() => publication).Should().ThrowAsync<IOException>();
        fixture.Disabled.Should().BeTrue();
        fixture.Disposed.Should().BeTrue();
    }

    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset now = new(2026, 10, 26, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => now;
        internal void Advance() => now = now.AddMinutes(2);
    }

    public enum Scenario { Origin, Invocation, Gate, Terminal, Root, Identity, Inspection, Revoked, Expired, Wrong, Validate,
        Read, Changed, Cancelled, TerminalAudit, Members, Publish }

    private sealed class Fixture : ILocalFileInspector, ILocalFolderSelection, IKnowledgeSourceStore, ISecurityAuditLog
    {
        internal readonly Clock Time = new();
        internal string DirectoryIdentity = "physical-root";
        internal string Body = "synthetic notes";
        internal int Reads;
        internal int Inspections;
        internal int Validations;
        internal int ThrowValidationAt;
        internal int ThrowAuditAt;
        internal bool ThrowInspect;
        internal bool ThrowRead;
        internal bool ThrowPublish;
        internal bool Disposed;
        internal bool RefreshStarted;
        internal bool Disabled;
        internal bool Removed;
        internal Action? AfterInspect;
        internal Action? AfterRead;
        internal Action? AfterPublish;
        internal Action? AfterReadStore;
        internal Task? InspectWait;
        internal Task? DisableWait;
        internal byte[]? CapturedBytes;
        internal byte[]? BytesAtPublish;
        internal LocalFolderRevision? Published;
        internal KnowledgeSource? ExpectedAtPublish;
        internal readonly List<SecurityAuditEvent> Events = [];
        public LocalFolderMetadata Metadata => new(@"C:\Team", DirectoryIdentity,
            [new(@"C:\Team\notes.md", "file-identity", Encoding.UTF8.GetByteCount(Body), Time.GetUtcNow())]);
        internal KnowledgeSourceManager Manager() => new(this, this, this, Time);
        internal KnowledgeSource Source() => new(Guid.NewGuid(), 1, "notes", Metadata, KnowledgeSourceState.Enabled, Time.GetUtcNow(), 100);
        public Task<ILocalFileSelection> InspectAsync(string selectedPath, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("This workflow must never inspect a free-form file.");
        public async Task<ILocalFolderSelection> InspectFolderAsync(string selectedPath, CancellationToken cancellationToken)
        {
            Inspections++;
            if (InspectWait is not null) { await InspectWait; }
            if (ThrowInspect) { throw new IOException("Synthetic inaccessible source."); }
            AfterInspect?.Invoke();
            return this;
        }
        public Task ValidateAsync(CancellationToken cancellationToken)
        {
            Validations++;
            if (Validations == ThrowValidationAt) { throw new InvalidDataException("Synthetic changed inventory."); }
            return Task.CompletedTask;
        }
        public Task<byte[]> ReadAsync(LocalFileMetadata exactItem, CancellationToken cancellationToken)
        {
            Reads++;
            if (ThrowRead) { throw new IOException("Synthetic read failure."); }
            CapturedBytes = Encoding.UTF8.GetBytes(Body);
            AfterRead?.Invoke();
            return Task.FromResult(CapturedBytes);
        }
        public void Dispose() => Disposed = true;
        public Task<IReadOnlyList<KnowledgeSource>> ListAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<KnowledgeSource>>([Source()]);
        public Task<KnowledgeSource> BeginRefreshAsync(KnowledgeSource exact, CancellationToken cancellationToken)
        {
            RefreshStarted = true;
            return Task.FromResult(exact with { State = KnowledgeSourceState.Unavailable, Version = exact.Version + 1 });
        }
        public Task<KnowledgeSource> PublishAsync(KnowledgeSource? expected, string name, LocalFolderRevision snapshot,
            IReadOnlyList<ReadOnlyMemory<byte>> originalBytes, CancellationToken cancellationToken)
        {
            if (ThrowPublish) { throw new IOException("Synthetic failed publication."); }
            ExpectedAtPublish = expected;
            Published = snapshot;
            BytesAtPublish = originalBytes[0].ToArray();
            AfterPublish?.Invoke();
            return Task.FromResult(new KnowledgeSource(snapshot.Review.SourceId, 1, name, snapshot.Review.Metadata,
                KnowledgeSourceState.Enabled, Time.GetUtcNow(), 100));
        }
        public Task<LocalFolderRevision> ReadAsync(KnowledgeSource exact, CancellationToken cancellationToken)
        {
            AfterReadStore?.Invoke();
            return Task.FromResult(Published!);
        }
        public async Task DisableAsync(KnowledgeSource exact, CancellationToken cancellationToken)
        {
            if (DisableWait is not null) { await DisableWait; }
            Disabled = true;
        }
        public Task RemoveAsync(KnowledgeSource exact, CancellationToken cancellationToken) { Removed = true; return Task.CompletedTask; }
        public void Write(SecurityAuditEvent auditEvent)
        {
            Events.Add(auditEvent);
            if (Events.Count == ThrowAuditAt) { throw new IOException("Synthetic unavailable audit."); }
        }
    }
}
