using System.Diagnostics;

using AwesomeAssertions;

using Kora.Application.Dependencies;
using Kora.Application.Hosting;
using Kora.Core.Authorization;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Application.UnitTests.Hosting;

[Collection("Host tracing")]
public sealed class NewRequestComposerTests : IDisposable
{
    private readonly ActivityListener listener = new()
    {
        ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
        Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
    };
    public NewRequestComposerTests() => ActivitySource.AddActivityListener(listener);
    public void Dispose() => listener.Dispose();

    [Fact]
    public async Task TwoPhaseNativeIntentRetainsOriginalBytesOnlyAfterExactTargetAcknowledgementAndFreshReview()
    {
        using var f = new Fixture();
        var c = f.Composer;
        var changes = 0;
        c.PropertyChanged += (_, _) => changes++;
        c.Status.Should().Contain("Relatedness inference is unavailable");
        c.Dirty.Should().BeFalse();
        c.CanReview.Should().BeFalse();
        c.CanInspectAttempt.Should().BeFalse();
        await c.Submit();
        await c.Inspect();
        c.Review();
        const string body = "  original e\u0301🙂\r\nend  ";
        c.Body = body;
        c.Dirty.Should().BeTrue();
        await c.Create();
        c.Target!.Authority.SessionId.Should().Be(c.Creation!.Request.SessionId);
        c.Target.Metadata!.Name.Value.Should().Be("New conversation");
        f.Retained.Should().BeNull();
        c.CanCreate.Should().BeFalse();
        await c.Create();
        c.CanSubmit.Should().BeFalse();
        c.Review();
        c.ReviewedBody.Should().Be(body);
        c.CanSubmit.Should().BeTrue();
        await c.Inspect();
        c.ReviewedBody.Should().BeEmpty();
        c.CanSubmit.Should().BeFalse();
        c.CanReview.Should().BeTrue();
        c.Review();
        await c.Submit();
        f.Retained!.Body.Should().Be(body);
        f.Retained.Request.SessionId.Should().Be(c.Target.Authority.SessionId);
        f.Retained.Outcome.Should().Be(ModelTurnOutcome.Unavailable);
        c.Submission.Should().Be(f.Retained.Request);
        c.Status.Should().Contain("HOST Unavailable").And.NotContain(body);
        c.Body.Should().BeEmpty();
        c.CanEdit.Should().BeFalse();
        c.Body = "late callback";
        c.Body.Should().BeEmpty();
        await c.Submit();
        await c.Inspect();
        c.Status.Should().Contain("task Unavailable");
        f.Retentions.Should().Be(1);
        changes.Should().BeGreaterThan(0);
        c.Dispose();
        c.Dispose();
        c.Body = body;
        c.Body.Should().BeEmpty();
        c.Target.Should().BeNull();
        c.CanCreate.Should().BeFalse();
        c.CanInspectAttempt.Should().BeFalse();
    }

    [Theory]
    [InlineData("edit")]
    [InlineData("cancel")]
    [InlineData("control")]
    public async Task SameBytesAndReopenedAdmissionNeverReviveConsumedReview(string change)
    {
        using var f = new Fixture();
        var c = f.Composer;
        await c.Create();
        c.Body = "original";
        c.Review();
        if (change is "edit") { c.Body = "original"; }
        if (change is "cancel") { c.Cancel(); }
        if (change is "control") { f.Access.ControlRevision++; }
        c.CanSubmit.Should().BeFalse();
        await c.Submit();
        f.Retained.Should().BeNull();
        c.Review();
        c.CanSubmit.Should().BeTrue();
        await c.Submit();
        f.Retained!.Body.Should().Be("original");
    }

    [Theory]
    [InlineData("initial-private")]
    [InlineData("initial-control")]
    [InlineData("edit")]
    [InlineData("cancel")]
    [InlineData("revision")]
    [InlineData("private")]
    [InlineData("control")]
    [InlineData("close")]
    [InlineData("storage")]
    [InlineData("receipt")]
    [InlineData("wrong-target")]
    [InlineData("late")]
    [InlineData("readback-metadata")]
    [InlineData("readback-task")]
    [InlineData("readback-state")]
    [InlineData("readback-revision")]
    [InlineData("readback-cancel")]
    public async Task CreationFencesAndUnconfirmedReceiptsKeepOneExactAttemptWithoutBlindSecondSession(string fault)
    {
        using var f = new Fixture();
        var c = f.Composer;
        c.Body = "private memory";
        if (fault is "initial-private") { f.Access.CanInspect = false; }
        if (fault is "initial-control") { f.Access.CanControl = false; }
        f.BeforeCreate = () =>
        {
            if (fault is "edit") { c.Body = "changed"; }
            if (fault is "cancel") { c.Cancel(); }
            if (fault is "revision") { f.Access.ControlRevision++; }
            if (fault is "private") { f.Access.CanInspect = false; }
            if (fault is "control") { f.Access.CanControl = false; }
            if (fault is "close") { c.Dispose(); }
            if (fault is "storage") { throw new IOException("required storage/audit unavailable"); }
        };
        if (fault is "receipt") { f.TerminalFailure = true; }
        if (fault is "wrong-target") { f.ForeignCreation = true; }
        if (fault is "late") { f.AfterCreate = c.Cancel; }
        if (fault.StartsWith("readback-", StringComparison.Ordinal)) { f.ReadFault = fault; }
        await c.Create();
        c.CanReview.Should().BeFalse();
        f.BeforeCreate = null;
        f.AfterCreate = null;
        f.TerminalFailure = false;
        f.Access.CanControl = true;
        f.Access.CanInspect = true;
        if (fault.StartsWith("initial", StringComparison.Ordinal))
        {
            c.Creation.Should().BeNull();
            await c.Create();
            c.Target.Should().NotBeNull();
        }
        else
        {
            c.Creation.Should().NotBeNull();
            var original = c.Creation;
            await c.Create();
            c.Creation.Should().BeSameAs(original);
            f.Creates.Should().BeLessThanOrEqualTo(1);
            c.Review();
            c.CanSubmit.Should().BeFalse();
        }
        if (fault is "private" or "close") { c.Body.Should().BeEmpty(); }
        else { c.Body.Should().NotBeEmpty(); }
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData("credentials")]
    [InlineData("rejection-audit")]
    [InlineData("preflight-control")]
    [InlineData("retain")]
    [InlineData("terminal")]
    [InlineData("edit")]
    [InlineData("cancel")]
    [InlineData("private")]
    [InlineData("close")]
    [InlineData("late")]
    [InlineData("quiescence")]
    public async Task SubmissionUsesCanonicalScreeningAndOneShotPhysicalCommitFencesWithExplicitRecoveryHold(string fault)
    {
        using var f = new Fixture();
        var c = f.Composer;
        await c.Create();
        c.Body = fault is "invalid" ? new string('x', 4097)
            : fault is "credentials" or "rejection-audit" ? "api_key=synthetic-only-value" : "exact request";
        var body = c.Body;
        c.Review();
        if (fault is "rejection-audit") { f.RejectionFailure = true; }
        if (fault is "preflight-control") { f.Access.CanControl = false; }
        if (fault is "quiescence") { f.IsQuiescent = false; }
        f.BeforeRetain = () =>
        {
            if (fault is "retain") { throw new IOException("retention/audit unavailable"); }
            if (fault is "edit") { c.Body = "changed"; }
            if (fault is "cancel") { c.Cancel(); }
            if (fault is "private") { f.Access.CanInspect = false; }
            if (fault is "close") { c.Dispose(); }
        };
        if (fault is "terminal") { f.CompletionFailure = true; }
        if (fault is "late") { f.AfterComplete = c.Cancel; }
        await c.Submit();
        c.CanSubmit.Should().BeFalse();
        if (fault is "credentials") { f.Rejections.Should().Be(1); }
        if (fault is "private" or "close") { c.Body.Should().BeEmpty(); }
        else if (fault is "edit") { c.Body.Should().Be("changed"); }
        else { c.Body.Should().Be(body); }
        c.Status.Should().NotContain(body.Length > 20 ? body[..20] : body);
        var holds = fault is "retain" or "terminal" or "edit" or "cancel" or "private" or "close" or "late";
        c.Submission.Should().Be(holds ? f.Attempted : null);
        f.BeforeRetain = null;
        f.AfterComplete = null;
        f.CompletionFailure = false;
        f.RejectionFailure = false;
        f.Access.CanInspect = true;
        f.Access.CanControl = true;
        f.IsQuiescent = true;
        c.Body = "corrected request";
        c.Review();
        c.CanSubmit.Should().Be(!holds);
        await c.Submit();
        if (holds) { f.Retentions.Should().Be(1); }
        else { f.Retained!.Body.Should().Be("corrected request"); }
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("foreign-task")]
    [InlineData("foreign-session")]
    [InlineData("changed-target")]
    [InlineData("done")]
    [InlineData("cancel")]
    [InlineData("private")]
    public async Task InspectionIsPassiveExactAndCannotPromoteChangedOrUnconfirmedAuthority(string fault)
    {
        using var f = new Fixture();
        var c = f.Composer;
        await c.Create();
        c.Body = "memory";
        c.Review();
        var original = c.Target!;
        if (fault is "missing") { f.Records.Clear(); }
        if (fault is "foreign-task") { f.ForeignObservation = true; }
        if (fault is "foreign-session") { f.Entry = original with { Authority = original.Authority with { SessionId = new(Guid.NewGuid()) } }; }
        if (fault is "changed-target") { f.Entry = original with { Metadata = original.Metadata! with { Revision = new(2) } }; }
        if (fault is "done") { f.Entry = original with { Authority = original.Authority with { IsActive = false } }; }
        f.AfterRead = () =>
        {
            if (fault is "cancel") { c.Cancel(); }
            if (fault is "private") { f.Access.CanInspect = false; }
        };
        var writes = f.Writes;
        await c.Inspect();
        f.Writes.Should().Be(writes);
        c.CanSubmit.Should().BeFalse();
        c.ReviewedBody.Should().BeEmpty();
        c.CanReview.Should().BeFalse();
    }

    [Fact]
    public async Task InheritedProviderAuthorityCannotRelabelFreshNativeCreation()
    {
        using var f = new Fixture();
        using var root = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.HostSystem), HostActivityLayer.Application, HostOperation.Request);
        await f.Composer.Create();
        f.Creates.Should().Be(0);
        f.Writes.Should().Be(0);
        f.Composer.CanCreate.Should().BeFalse();
    }

    [Theory]
    [InlineData("fresh")]
    [InlineData("native")]
    [InlineData("provider")]
    [InlineData("expired")]
    public async Task CohesiveCreationSeamNeverRelabelsExpiredOrProviderAuthorityAsOriginalNativeIntent(string source)
    {
        using var f = new Fixture();
        using var root = source is "fresh" ? null : HostActivity.BeginRoot(
            HostRequest.Create(source is "provider" ? RequestOrigin.HostSystem : RequestOrigin.LocalUi),
            HostActivityLayer.Application, HostOperation.Request);
        if (source is "expired") { root!.Activity!.Stop(); }
        var attempt = new NewRequestCreationAttempt();
        if (source is "provider" or "expired")
        {
            await f.Workspace.Invoking(value => value.CreateAsync(attempt, static () => true, TestContext.Current.CancellationToken))
                .Should().ThrowAsync<InvalidOperationException>();
            await f.Composer.Create();
            f.Writes.Should().Be(0);
            f.Creates.Should().Be(0);
        }
        else
        {
            var target = await f.Workspace.CreateAsync(attempt, static () => true, TestContext.Current.CancellationToken);
            target.Authority.SessionId.Should().Be(attempt.Request.SessionId);
            f.Records.Values.Should().Contain(record => record.Request == attempt.Request && record.State == HostTaskState.Succeeded);
        }
    }

    [Fact]
    public async Task DoubleClickDuringCreationAndSubmissionConsumesOnlyOneNativeOperation()
    {
        using var f = new Fixture();
        var createEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var createRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.CreateWait = async () => { createEntered.SetResult(); await createRelease.Task; };
        var create = f.Composer.Create();
        await createEntered.Task;
        f.Composer.Busy.Should().BeTrue();
        f.Composer.CanInspectAttempt.Should().BeFalse();
        await f.Composer.Create();
        createRelease.SetResult();
        await create;
        f.Creates.Should().Be(1);
        f.Composer.Body = "double click";
        f.Composer.Review();
        var retainEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var retainRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.RetainWait = async () => { retainEntered.SetResult(); await retainRelease.Task; };
        var submit = f.Composer.Submit();
        await retainEntered.Task;
        await f.Composer.Submit();
        retainRelease.SetResult();
        await submit;
        f.Retentions.Should().Be(1);
    }

    [Fact]
    public async Task PrivateInspectionControlsAndStructuredFailureDiagnosticsNeverRetainCandidateContent()
    {
        using var f = new Fixture(enabledLogger: true);
        const string body = "PRIVATE_NEW_REQUEST_DIAGNOSTIC_MARKER";
        f.Composer.Body = body;
        f.BeforeCreate = () => throw new IOException("synthetic boundary");
        await f.Composer.Create();
        f.Composer.CanInspectAttempt.Should().BeTrue();
        f.Access.CanInspect = false;
        f.Composer.CanInspectAttempt.Should().BeFalse();
        f.Composer.Body = "late private edit";
        f.Composer.Body.Should().Be(body);
        f.Messages.Should().ContainSingle().Which.Should().NotContain(body);
        f.Composer.Dispose();
        f.Composer.CanInspectAttempt.Should().BeFalse();
    }

    [Fact]
    public async Task SeparateSubmissionLinksToCompletedCreationTraceWithoutCandidateTagsBaggageOrIncomingAuthority()
    {
        var stopped = new List<Activity>();
        using var captured = new ActivityListener
        {
            ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = stopped.Add,
        };
        ActivitySource.AddActivityListener(captured);
        using var incoming = new Activity("untrusted-provider-header").SetIdFormat(ActivityIdFormat.W3C).Start();
        incoming.SetTag("kora.session.id", Guid.NewGuid());
        using var f = new Fixture();
        const string body = "PRIVATE_TRACE_BODY_UNRELATED_TO_IDENTIFIERS";
        f.Composer.Body = body;
        await f.Composer.Create();
        var creationRoots = stopped.Where(activity => activity.ParentId is null).Select(activity => activity.Context).ToArray();
        creationRoots.Should().NotBeEmpty();
        f.Composer.Review();
        await f.Composer.Submit();
        stopped.Should().Contain(activity => activity.Links.Any(link => creationRoots.Contains(link.Context)));
        stopped.Should().OnlyContain(activity => activity.IdFormat == ActivityIdFormat.W3C && activity.Status != ActivityStatusCode.Unset);
        stopped.SelectMany(activity => activity.TagObjects).Should().NotContain(tag => Equals(tag.Value, body));
        stopped.SelectMany(activity => activity.Baggage).Should().BeEmpty();
        stopped.Where(activity => activity.ParentId is null).Should().OnlyContain(activity => activity.ParentId != incoming.Id);
        f.Retained!.Request.SessionId.Should().Be(f.Composer.Creation!.Request.SessionId);
        Activity.Current.Should().BeSameAs(incoming);
    }

    private sealed class Fixture : IDisposable, ISessionWorkspaceStore, ISessionConversationStore, IHostTaskStore, IModelTurnAdmission, ILogger<NewRequestComposer>
    {
        internal AccessState Access { get; } = new();
        public bool IsQuiescent { get; set; } = true;
        internal NewRequestComposer Composer { get; }
        internal SessionWorkspaceService Workspace { get; }
        internal Dictionary<HostId<TaskIdentity>, HostTaskRecord> Records { get; } = [];
        internal SessionWorkspaceEntry? Entry { get; set; }
        internal ConversationMessage? Retained { get; private set; }
        internal HostRequest? Attempted { get; private set; }
        internal int Creates { get; private set; }
        internal int Retentions { get; private set; }
        internal int Rejections { get; private set; }
        internal int Writes { get; private set; }
        internal bool TerminalFailure { get; set; }
        internal bool CompletionFailure { get; set; }
        internal bool RejectionFailure { get; set; }
        internal bool ForeignCreation { get; set; }
        internal bool ForeignObservation { get; set; }
        internal Action? BeforeCreate { get; set; }
        internal Action? AfterCreate { get; set; }
        internal Action? BeforeRetain { get; set; }
        internal Action? AfterComplete { get; set; }
        internal Action? AfterRead { get; set; }
        internal Func<Task>? CreateWait { get; set; }
        internal Func<Task>? RetainWait { get; set; }
        internal string? ReadFault { get; set; }
        internal List<string> Messages { get; } = [];

        internal Fixture(bool enabledLogger = false)
        {
            var workspace = new SessionWorkspaceService(this, new(this), Access, NullLogger<SessionWorkspaceService>.Instance);
            Workspace = workspace;
            var conversation = new SessionConversationService(this, this, workspace, Access, this,
                TimeProvider.System, NullLogger<SessionConversationService>.Instance);
            Composer = new(workspace, conversation, Access, enabledLogger ? this : NullLogger<NewRequestComposer>.Instance);
        }

        public Task<ModelTurnAdmission> AdmitAsync(ModelTurnChoice choice, ModelContextEnvelope context, ModelHandoffReview? review, CancellationToken token) =>
            Task.FromResult(new ModelTurnAdmission(new(ModelTurnOutcome.Unavailable, ModelTurnReason.UnregisteredModel)));

        public ValueTask<HostTaskRecord> RecordControlIntentAsync(HostRequest request, CancellationToken token)
        {
            var intent = new HostTaskRecord(request, new(1), HostTaskState.IntentRecorded);
            Records.Add(request.TaskId, intent);
            Writes++;
            return ValueTask.FromResult(intent);
        }

        public ValueTask CommitAsync(HostTaskRecord record, long revision, CancellationToken token)
        {
            if (TerminalFailure) { throw new IOException("terminal receipt unavailable"); }
            Records[record.Request.TaskId] = record;
            Writes++;
            return ValueTask.CompletedTask;
        }

        public ValueTask<SessionWorkspaceEntry> ReadMetadataAsync(HostId<SessionIdentity> session, CancellationToken token)
        {
            AfterRead?.Invoke();
            if (ReadFault is "readback-cancel") { throw new OperationCanceledException("readback cancellation"); }
            if (ReadFault is "readback-metadata") { return ValueTask.FromResult(Entry! with { Metadata = Entry!.Metadata! with { Revision = new(2) } }); }
            return ValueTask.FromResult(Entry ?? throw new IOException("No exact durable target"));
        }

        public ValueTask<HostTaskObservation?> ReadTaskAsync(HostId<SessionIdentity> session, HostId<TaskIdentity> task, CancellationToken token)
        {
            AfterRead?.Invoke();
            var record = Records.GetValueOrDefault(task);
            if (ForeignObservation) { record = new(HostRequest.Create(RequestOrigin.LocalUi), new(2), HostTaskState.Succeeded); }
            if (ReadFault is "readback-task") { record = null; }
            if (ReadFault is "readback-state") { record = new(record!.Request, record.Revision, HostTaskState.Denied); }
            if (ReadFault is "readback-revision") { record = new(record!.Request, new(3), record.State); }
            return ValueTask.FromResult<HostTaskObservation?>(record is null ? null : new(record, new(1), "host", true, null));
        }

        public ValueTask<HostTaskRecord> RecordConversationCredentialRejectionAsync(HostRequest request, HostRevision generation, Func<bool> admitted, CancellationToken token)
        {
            if (RejectionFailure) { throw new IOException("required rejection audit unavailable"); }
            Rejections++;
            return ValueTask.FromResult(new HostTaskRecord(request, new(2), HostTaskState.Denied));
        }

        public ValueTask<ConversationSubmissionReceipt> CompleteConversationSubmissionAsync(HostRequest request,
            ModelTurnOutcome outcome, ModelTurnReason reason, Func<bool> admitted, CancellationToken token)
        {
            if (CompletionFailure) { throw new IOException("terminal audit unavailable"); }
            Retained = Retained! with { Revision = 2, Outcome = outcome, Reason = reason };
            var task = Records[Retained.Request.TaskId].Next(Retained.ExpectedTaskState);
            Records[task.Request.TaskId] = task;
            AfterComplete?.Invoke();
            return ValueTask.FromResult(new ConversationSubmissionReceipt(Retained, task));
        }

        public async ValueTask<SessionWorkspaceEntry> CreateNamedSessionAsync(HostRequest request, SessionName name, Func<bool> admitted, CancellationToken token)
        {
            Creates++;
            if (CreateWait is { } wait) { await wait(); }
            BeforeCreate?.Invoke();
            if (!admitted()) { throw new InvalidOperationException("commit admission revoked"); }
            var id = ForeignCreation ? new HostId<SessionIdentity>(Guid.NewGuid()) : request.SessionId;
            Entry = new(new(id, new(1), true), new(id, new(1), name));
            AfterCreate?.Invoke();
            return Entry;
        }

        public async ValueTask<ConversationSubmissionReceipt> RetainConversationMessageAsync(HostRequest request, HostRevision generation,
            long revision, string body, Func<bool> admitted, CancellationToken token)
        {
            Retentions++;
            Attempted = request;
            if (RetainWait is { } wait) { await wait(); }
            BeforeRetain?.Invoke();
            if (!admitted()) { throw new InvalidOperationException("commit admission revoked"); }
            Retained = new(Attempted, generation, 1, 1, DateTimeOffset.UnixEpoch, body, null, null);
            var task = new HostTaskRecord(Attempted, new(1), HostTaskState.IntentRecorded);
            Records.Add(task.Request.TaskId, task);
            return new(Retained, task);
        }

        public void Dispose() => Composer.Dispose();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => true;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Messages.Add(formatter(state, exception));
        public ValueTask<IReadOnlyList<HostTaskRecord>> ReadIncompleteAsync(int limit, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<HostTaskRecord?> ReadTaskAsync(HostId<TaskIdentity> task, CancellationToken token) => ValueTask.FromResult(Records.GetValueOrDefault(task));
        public ValueTask<SessionDispositionPreview> PreviewDispositionAsync(HostId<SessionIdentity> session, HostRevision generation, long revision, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<SessionDispositionReceipt> DisposeSessionAsync(HostRequest request, SessionDispositionPreview preview, Func<bool> admitted, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<SessionPage<WorkSessionAuthorization>> ReadSessionsAsync(Guid? after, int limit, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<SessionPage<SessionWorkspaceEntry>> ReadMetadataPageAsync(Guid? after, int limit, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<HostTaskObservation> CancelWaitingTaskAsync(HostRequest request, HostTaskCancellationTarget target, Func<bool> admitted, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<SessionWorkspaceEntry> RenameSessionAsync(HostRequest request, HostRevision generation, long revision, SessionName name, Func<bool> admitted, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<SessionPage<Kora.Core.Interaction.HostQuestionRecord>> ReadQuestionPageAsync(HostId<SessionIdentity> session, Guid? after, int limit, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<SessionPage<HostTaskRecord>> ReadTaskPageAsync(HostId<SessionIdentity> session, Guid? after, int limit, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<WorkSessionAuthorization> ChangeIdleLifecycleAsync(HostRequest request, HostRevision generation, bool active, Func<bool> admitted, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<ConversationDraftSnapshot> ReadConversationDraftAsync(HostId<SessionIdentity> session, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<ConversationDraftSnapshot> CommitConversationDraftAsync(HostRequest request, ConversationDraftSnapshot expected, string? body, Func<bool> admitted, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<ConversationPage> ReadConversationPageAsync(HostId<SessionIdentity> session, ConversationCursor? cursor, int limit, CancellationToken token) => throw new NotSupportedException();
    }

    private sealed class AccessState : ISessionWorkspaceAccess
    {
        public bool CanInspect { get; set; } = true;
        public bool CanControl { get; set; } = true;
        public long ControlRevision { get; set; } = 1;
    }
}
