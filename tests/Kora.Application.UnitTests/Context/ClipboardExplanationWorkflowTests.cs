using System.Diagnostics;
using System.Text;

using AwesomeAssertions;

using Kora.Application.Configuration;
using Kora.Application.Context;
using Kora.Application.Dependencies;
using Kora.Application.Hosting;
using Kora.Application.Tools;
using Kora.Application.UnitTests.Configuration;
using Kora.Application.Voice;
using Kora.Core.Auditing;
using Kora.Core.Authorization;
using Kora.Core.Context;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Providers;
using Kora.Core.Storage;
using Kora.Core.Tools;
using Kora.Tools.Clipboard;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Application.UnitTests.Context;

[Collection("Host tracing")]
public sealed class ClipboardExplanationWorkflowTests
{
    [Fact]
    public async Task AlreadyQueuedSourceNotificationCannotUseRetiredCancellation()
    {
        await using var fixture = new Fixture();
        using var root = fixture.Root();
        await fixture.Action.ExecuteAsync(null, () => true, fixture.Token);
        var source = fixture.Broker.Current!;
        using var releaseNotification = new SemaphoreSlim(0);
        var notificationEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void PauseNotification(object? sender, EventArgs args)
        {
            notificationEntered.TrySetResult();
            releaseNotification.Wait(fixture.Token);
        }
        fixture.Broker.Changed += PauseNotification;
        fixture.Adapter.Pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var run = fixture.Action.ExecuteAsync(source.SnapshotId, () => true, fixture.Token);
        await fixture.Adapter.Started.Task.WaitAsync(fixture.Token);
        var notification = Task.Run(() => fixture.Broker.Reuse(source.SnapshotId, () => true), fixture.Token);
        try
        {
            await notificationEntered.Task.WaitAsync(fixture.Token);
            fixture.Adapter.Pending.SetResult(fixture.Adapter.Reply());
            (await run).Outcome.Should().Be(ClipboardExplanationOutcome.Succeeded);
        }
        finally { releaseNotification.Release(); }
        (await notification).Should().Be(ClipboardOutcome.Reused);
        fixture.Broker.Changed -= PauseNotification;
        fixture.Workflow.CurrentExplanation.Should().NotBeNull();
    }

    [Fact]
    public async Task PresentationNotificationFailureCannotLeaveOwnedWorkPermanentlyActive()
    {
        await using var fixture = new Fixture();
        using var root = fixture.Root();
        await fixture.Action.ExecuteAsync(null, () => true, fixture.Token);
        void FailOnce(object? sender, EventArgs args)
        {
            fixture.Workflow.Changed -= FailOnce;
            throw new InvalidOperationException("synthetic presentation failure");
        }
        fixture.Workflow.Changed += FailOnce;
        var run = () => fixture.Action.ExecuteAsync(fixture.Broker.Current!.SnapshotId, () => true, fixture.Token);
        await run.Should().ThrowAsync<InvalidOperationException>();
        fixture.Workflow.IsQuiescent.Should().BeTrue();
        await fixture.Workflow.WaitForQuiescenceAsync();
        fixture.Workflow.CurrentExplanation.Should().BeNull();
    }

    [Fact]
    public async Task CancellationDuringDurableAdmissionReturnsExplicitCancelledOutcome()
    {
        await using var fixture = new Fixture();
        using var root = fixture.Root();
        await fixture.Action.ExecuteAsync(null, () => true, fixture.Token);
        fixture.PendingCreation = new TaskCompletionSource<Kora.Core.Authorization.WorkSessionAuthorization>(
            TaskCreationOptions.RunContinuationsAsynchronously).Task;
        var run = fixture.Action.ExecuteAsync(fixture.Broker.Current!.SnapshotId, () => true, fixture.Token);
        await fixture.CreationStarted.WaitAsync(fixture.Token);
        fixture.Workflow.Clear();
        (await run).Outcome.Should().Be(ClipboardExplanationOutcome.Cancelled);
        fixture.Adapter.Calls.Should().Be(0);
    }

    [Fact]
    public async Task UnrelatedCancellationExceptionIsNotMisreportedAsCallerCancellation()
    {
        await using var fixture = new Fixture();
        using var root = fixture.Root();
        await fixture.Action.ExecuteAsync(null, () => true, fixture.Token);
        fixture.BeforeIntent = () => throw new OperationCanceledException("synthetic unrelated failure");
        var run = () => fixture.Action.ExecuteAsync(fixture.Broker.Current!.SnapshotId, () => true, fixture.Token);
        await run.Should().ThrowAsync<OperationCanceledException>();
        fixture.Workflow.CurrentExplanation.Should().BeNull();
    }

    internal static (ClipboardExplain Action, ClipboardExplanationWorkflow Workflow, IAsyncDisposable Lifetime) CreateWorkflow(
        ClipboardSnapshotBroker broker)
    {
        var fixture = new Fixture(broker: broker);
        return (fixture.Action, fixture.Workflow, fixture);
    }

    [Fact]
    public async Task QuiescenceWaitsForActualLateProviderTerminationAndRejectsConcurrentWork()
    {
        await using var fixture = new Fixture();
        using var root = fixture.Root();
        fixture.Workflow.IsQuiescent.Should().BeTrue();
        await fixture.Workflow.WaitForQuiescenceAsync();
        await fixture.Action.ExecuteAsync(null, () => true, fixture.Token);
        var source = fixture.Broker.Current!;
        fixture.Adapter.Pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var run = fixture.Action.ExecuteAsync(source.SnapshotId, () => true, fixture.Token);
        await fixture.Adapter.Started.Task.WaitAsync(fixture.Token);
        fixture.Workflow.IsQuiescent.Should().BeFalse();
        (await fixture.Action.ExecuteAsync(source.SnapshotId, () => true, fixture.Token)).Outcome.Should().Be(ClipboardExplanationOutcome.Busy);
        fixture.Broker.Reuse(source.SnapshotId, () => true).Should().Be(ClipboardOutcome.Reused);
        fixture.Workflow.Clear();
        (await run).Outcome.Should().Be(ClipboardExplanationOutcome.Cancelled);
        (await fixture.Action.ExecuteAsync(source.SnapshotId, () => true, fixture.Token)).Outcome.Should().Be(ClipboardExplanationOutcome.Busy);
        var wait = fixture.Workflow.WaitForQuiescenceAsync();
        wait.IsCompleted.Should().BeFalse();
        fixture.Adapter.Pending.SetResult(fixture.Adapter.Reply());
        await wait;
        fixture.Workflow.IsQuiescent.Should().BeTrue();
    }

    [Theory]
    [InlineData("eligibility")]
    [InlineData("future")]
    [InlineData("review-expiry")]
    [InlineData("envelope-expiry")]
    [InlineData("dispose")]
    public async Task EveryPresentationReadRevalidatesTheExactSourceAndAnswerLifetime(string change)
    {
        await using var fixture = new Fixture();
        using var root = fixture.Root();
        var eligible = true;
        await fixture.Action.ExecuteAsync(null, () => eligible, fixture.Token);
        (await fixture.Action.ExecuteAsync(fixture.Broker.Current!.SnapshotId, () => eligible, fixture.Token))
            .Outcome.Should().Be(ClipboardExplanationOutcome.Succeeded);
        switch (change)
        {
            case "eligibility": eligible = false; break;
            case "future": fixture.Time.Now = DateTimeOffset.UnixEpoch.AddSeconds(-1); break;
            case "review-expiry": fixture.Time.Now += ClipboardExplanationContext.Lifetime; break;
            case "envelope-expiry": fixture.Time.Now = fixture.Time.Now.AddSeconds(15); break;
            default: fixture.Workflow.Dispose(); break;
        }
        fixture.Workflow.CurrentExplanation.Should().BeNull();
        eligible = true;
        fixture.Time.Now = DateTimeOffset.UnixEpoch;
        fixture.Workflow.CurrentExplanation.Should().BeNull();
    }

    [Fact]
    public async Task EnvelopeExpiryDuringTerminalEvidenceNeverPublishesAnAnswerOrExplanationSuccess()
    {
        await using var fixture = new Fixture();
        using var root = fixture.Root();
        await fixture.Action.ExecuteAsync(null, () => true, fixture.Token);
        fixture.BeforeTaskCommit = record =>
        {
            if (record.IsTerminal && fixture.Adapter.Calls > 0) { fixture.Time.Now = fixture.Time.Now.AddSeconds(15); }
        };
        (await fixture.Action.ExecuteAsync(fixture.Broker.Current!.SnapshotId, () => true, fixture.Token))
            .Outcome.Should().Be(ClipboardExplanationOutcome.Expired);
        fixture.Workflow.CurrentExplanation.Should().BeNull();
    }

    [Fact]
    public async Task SourceCanBecomeStaleAfterTerminalCommitWithoutPublishingAnAnswer()
    {
        await using var fixture = new Fixture();
        using var root = fixture.Root();
        await fixture.Action.ExecuteAsync(null, () => true, fixture.Token);
        fixture.BeforeTaskCommit = record =>
        {
            if (record.IsTerminal && fixture.Adapter.Calls > 0) { fixture.Workflow.Clear(); }
        };
        (await fixture.Action.ExecuteAsync(fixture.Broker.Current!.SnapshotId, () => true, fixture.Token))
            .Outcome.Should().Be(ClipboardExplanationOutcome.Cancelled);
        fixture.Workflow.CurrentExplanation.Should().BeNull();
    }

    [Theory]
    [InlineData("clear")]
    [InlineData("dispose")]
    [InlineData("cancel")]
    [InlineData("expiry")]
    [InlineData("eligibility")]
    public async Task EligibilityIsCheckedAgainAfterNativeAdmissionNotification(string change)
    {
        await using var fixture = new Fixture();
        using var root = fixture.Root();
        await fixture.Action.ExecuteAsync(null, () => true, fixture.Token);
        var eligible = true;
        using var token = CancellationTokenSource.CreateLinkedTokenSource(fixture.Token);
        fixture.Workflow.Changed += OnChanged;
        void OnChanged(object? sender, EventArgs args)
        {
            fixture.Workflow.Changed -= OnChanged;
            switch (change)
            {
                case "clear": fixture.Workflow.Clear(); break;
                case "dispose": fixture.Workflow.Dispose(); break;
                case "cancel": token.Cancel(); break;
                case "expiry": fixture.Time.Now += ClipboardExplanationContext.Lifetime; break;
                default: eligible = false; break;
            }
        }
        (await fixture.Action.ExecuteAsync(fixture.Broker.Current!.SnapshotId, () => eligible, token.Token))
            .Outcome.Should().Be(ClipboardExplanationOutcome.Cancelled);
        fixture.Adapter.Calls.Should().Be(0);
    }

    [Theory]
    [InlineData(false, "io")]
    [InlineData(true, "io")]
    [InlineData(true, "data")]
    [InlineData(true, "credential")]
    public async Task ProviderExceptionsAreExplicitContentFreeFailuresAndCannotPresentSuccess(bool logging, string failure)
    {
        await using var fixture = new Fixture();
        using var root = fixture.Root();
        fixture.LoggingEnabled = logging;
        fixture.Adapter.Failure = failure switch
        {
            "data" => new InvalidDataException("private-marker must not enter diagnostics"),
            "credential" => new Kora.Core.Conversation.ConversationCredentialException(),
            _ => new IOException("private-marker must not enter diagnostics"),
        };
        await fixture.Action.ExecuteAsync(null, () => true, fixture.Token);
        var run = () => fixture.Action.ExecuteAsync(fixture.Broker.Current!.SnapshotId, () => true, fixture.Token);
        await run.Should().ThrowAsync<Exception>();
        fixture.Workflow.CurrentExplanation.Should().BeNull();
        fixture.Messages.Should().NotContain(message => message.Contains("private-marker", StringComparison.Ordinal));
        fixture.Audits.Should().Contain(e => e.ActionId == "clipboard.explain" && e.Outcome == SecurityAuditOutcome.Unknown);
    }

    [Fact]
    public async Task DisposedWrongIdFutureAndFailedCaptureRequestsCannotDispatch()
    {
        await using var fixture = new Fixture();
        using var root = fixture.Root();
        fixture.Reader.Outcome = ClipboardOutcome.Empty;
        (await fixture.Action.ExecuteAsync(null, () => true, fixture.Token)).Outcome.Should().Be(ClipboardExplanationOutcome.Unavailable);
        fixture.Reader.Outcome = ClipboardOutcome.Captured;
        await fixture.Action.ExecuteAsync(null, () => true, fixture.Token);
        (await fixture.Action.ExecuteAsync(Guid.NewGuid(), () => true, fixture.Token)).Outcome.Should().Be(ClipboardExplanationOutcome.Stale);
        fixture.Time.Now = fixture.Time.Now.AddSeconds(-1);
        (await fixture.Action.ExecuteAsync(fixture.Broker.Current!.SnapshotId, () => true, fixture.Token)).Outcome.Should().Be(ClipboardExplanationOutcome.Expired);
        fixture.Workflow.Dispose();
        (await fixture.Action.ExecuteAsync(null, () => true, fixture.Token)).Outcome.Should().Be(ClipboardExplanationOutcome.Denied);
    }
    [Fact]
    public async Task ExplicitReviewThenExactNativeAdmissionRunsRealHostAndPresentsOnlySuccessfulLocalAnswer()
    {
        await using var fixture = new Fixture();
        using var root = fixture.Root(RequestOrigin.ActivatedVoice);
        var stopped = new List<Activity>();
        using var observer = new ActivityListener
        {
            ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = stopped.Add,
        };
        ActivitySource.AddActivityListener(observer);
        var review = await fixture.Action.ExecuteAsync(null, () => true, fixture.Token);
        review.Outcome.Should().Be(ClipboardExplanationOutcome.ReviewRequired);
        fixture.Adapter.Calls.Should().Be(0);
        fixture.Tasks.Should().BeEmpty();
        var source = fixture.Broker.Current!;
        await fixture.Action.ExecuteAsync(null, () => true, fixture.Token);
        fixture.Reader.Calls.Should().Be(1);
        var result = await fixture.Action.ExecuteAsync(source.SnapshotId, () => true, fixture.Token);
        result.Outcome.Should().Be(ClipboardExplanationOutcome.Succeeded);
        fixture.Workflow.CurrentExplanation.Should().Be("Genuine synthetic local explanation.");
        fixture.Adapter.Calls.Should().Be(1);
        HostActivity.Current.Should().BeSameAs(root);
        var sourceBoundary = stopped.Single(activity => Equals(activity.GetTagItem("kora.evidence.id"), source.SnapshotId));
        sourceBoundary.Status.Should().Be(ActivityStatusCode.Ok);
        sourceBoundary.Links.Should().Contain(link => link.Context == root.Activity!.Context);
        sourceBoundary.GetTagItem("kora.clipboard.source.id").Should().Be(source.SourceId);
        sourceBoundary.GetTagItem("kora.clipboard.capture.request.id").Should().Be(source.Request.RequestId.Value);
        stopped.SelectMany(activity => activity.TagObjects)
            .Should().NotContain(tag => Equals(tag.Value, fixture.Reader.Text));
        fixture.Adapter.Provenance!.Request.SessionId.Should().NotBe(source.Request.SessionId);
        fixture.Adapter.Provenance.Request.Origin.Should().Be(RequestOrigin.ActivatedVoice);
        fixture.Tasks.Last().State.Should().Be(HostTaskState.Succeeded);
        Encoding.UTF8.GetString(fixture.Adapter.Wire!).Should().Contain("private-marker");
        fixture.Audits.Should().Contain(e => e.ActionId == "clipboard.explain" && e.Outcome == SecurityAuditOutcome.Succeeded);
        fixture.Audits.Select(e => e.TargetId).Should().NotContain("private-marker");
        fixture.Workflow.CurrentExplanation.Should().Be("Genuine synthetic local explanation.");
        fixture.Reader.Calls.Should().Be(1);
        fixture.Adapter.Calls.Should().Be(1);
        fixture.Broker.Clear();
        fixture.Workflow.CurrentExplanation.Should().BeNull();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingQualificationOrFailedProviderNeverProducesSuccessOrFallback(bool qualified)
    {
        await using var fixture = new Fixture(qualified);
        using var root = fixture.Root();
        fixture.Adapter.Outcome = ModelTurnOutcome.Unavailable;
        await fixture.Action.ExecuteAsync(null, () => true, fixture.Token);
        var result = await fixture.Action.ExecuteAsync(fixture.Broker.Current!.SnapshotId, () => true, fixture.Token);
        result.Outcome.Should().Be(ClipboardExplanationOutcome.Unavailable);
        fixture.Workflow.CurrentExplanation.Should().BeNull();
        fixture.Adapter.Calls.Should().Be(qualified ? 1 : 0);
        fixture.Tasks.Last().State.Should().Be(HostTaskState.Failed);
    }

    [Theory]
    [InlineData("credential", ClipboardExplanationOutcome.CredentialRejected)]
    [InlineData("oversize", ClipboardExplanationOutcome.EnvelopeRejected)]
    [InlineData("stale", ClipboardExplanationOutcome.Stale)]
    [InlineData("expired", ClipboardExplanationOutcome.Expired)]
    [InlineData("denied", ClipboardExplanationOutcome.Denied)]
    public async Task RejectedSourceNeverReachesProvider(string change, ClipboardExplanationOutcome expected)
    {
        await using var fixture = new Fixture();
        using var root = fixture.Root();
        fixture.Reader.Text = change is "credential" ? "password=actual-sensitive-value-1234"
            : change is "oversize" ? new string('x', 32768) : "private-marker";
        await fixture.Action.ExecuteAsync(null, () => true, fixture.Token);
        var id = fixture.Broker.Current!.SnapshotId;
        if (change is "stale") { fixture.Broker.Clear(); }
        if (change is "expired") { fixture.Time.Now += ClipboardExplanationContext.Lifetime; }
        var result = await fixture.Action.ExecuteAsync(id, () => change is not "denied", fixture.Token);
        result.Outcome.Should().Be(expected);
        fixture.Adapter.Calls.Should().Be(0);
        fixture.Workflow.CurrentExplanation.Should().BeNull();
        fixture.Reader.Calls.Should().Be(1);
    }

    [Theory]
    [InlineData("source")]
    [InlineData("privacy")]
    [InlineData("expiry")]
    [InlineData("cancel")]
    public async Task ChangesDuringAdmissionFenceProviderDispatch(string change)
    {
        await using var fixture = new Fixture();
        using var root = fixture.Root();
        var eligible = true;
        await fixture.Action.ExecuteAsync(null, () => eligible, fixture.Token);
        var id = fixture.Broker.Current!.SnapshotId;
        fixture.BeforeMetadata = () =>
        {
            fixture.BeforeMetadata = null;
            switch (change)
            {
                case "source": fixture.Broker.Clear(); break;
                case "privacy": eligible = false; break;
                case "expiry": fixture.Time.Now += ClipboardExplanationContext.Lifetime; break;
                default: fixture.Workflow.Clear(); break;
            }
        };
        var result = await fixture.Action.ExecuteAsync(id, () => eligible, fixture.Token);
        result.Outcome.Should().NotBe(ClipboardExplanationOutcome.Succeeded);
        fixture.Adapter.Calls.Should().Be(0);
        fixture.Workflow.CurrentExplanation.Should().BeNull();
    }

    [Theory]
    [InlineData("source")]
    [InlineData("owner")]
    [InlineData("expiry")]
    [InlineData("clear")]
    public async Task RevocationDuringProviderRunSuppressesLateAnswerAndRetainsResourceSlotUntilTermination(string change)
    {
        await using var fixture = new Fixture();
        using var root = fixture.Root();
        var eligible = true;
        await fixture.Action.ExecuteAsync(null, () => eligible, fixture.Token);
        var source = fixture.Broker.Current!;
        fixture.Adapter.Pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var run = fixture.Action.ExecuteAsync(source.SnapshotId, () => eligible, fixture.Token);
        await fixture.Adapter.Started.Task.WaitAsync(fixture.Token);
        switch (change)
        {
            case "source": fixture.Broker.Clear(); break;
            case "owner": eligible = false; break;
            case "expiry": fixture.Time.Now += ClipboardExplanationContext.Lifetime; break;
            default: fixture.Workflow.Clear(); break;
        }
        if (change is "source" or "clear")
        {
            (await run).Outcome.Should().Be(ClipboardExplanationOutcome.Cancelled);
            fixture.Workflow.IsQuiescent.Should().BeFalse();
            fixture.Workflow.CurrentExplanation.Should().BeNull();
        }
        fixture.Adapter.Pending.SetResult(fixture.Adapter.Reply());
        (await run).Outcome.Should().NotBe(ClipboardExplanationOutcome.Succeeded);
        fixture.Workflow.CurrentExplanation.Should().BeNull();
        fixture.Adapter.Calls.Should().Be(1);
    }

    [Fact]
    public async Task TerminalEvidenceFailureCannotPublishAnswer()
    {
        await using var fixture = new Fixture();
        using var root = fixture.Root();
        await fixture.Action.ExecuteAsync(null, () => true, fixture.Token);
        fixture.FailTaskTerminal = true;
        var run = () => fixture.Action.ExecuteAsync(fixture.Broker.Current!.SnapshotId, () => true, fixture.Token);
        await run.Should().ThrowAsync<IOException>();
        fixture.Workflow.CurrentExplanation.Should().BeNull();
    }

    [Fact]
    public async Task SystemOriginCannotAcquireOrAdmitClipboardThroughAction()
    {
        await using var fixture = new Fixture();
        using var root = fixture.Root(RequestOrigin.HostSystem);
        (await fixture.Action.ExecuteAsync(null, () => true, fixture.Token)).Outcome.Should().Be(ClipboardExplanationOutcome.Denied);
        fixture.Reader.Calls.Should().Be(0);
        fixture.Adapter.Calls.Should().Be(0);
    }

    private sealed class Fixture : IAsyncDisposable, ISessionWorkspaceAccess, ICapabilityHostAccess, ISecurityAuditLog, IApplicationInfo,
        ILogger<ClipboardExplanationWorkflow>
    {
        private readonly ActivityListener listener = new()
        {
            ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
        };
        internal CancellationToken Token => TestContext.Current.CancellationToken;
        internal Reader Reader { get; } = new();
        internal Clock Time { get; } = new();
        internal Adapter Adapter { get; } = new();
        internal ClipboardSnapshotBroker Broker { get; }
        internal ClipboardExplanationWorkflow Workflow { get; }
        internal ClipboardExplain Action { get; }
        private readonly Store store = new();
        internal List<HostTaskRecord> Tasks => store.Tasks;
        internal List<SecurityAuditEvent> Audits { get; } = [];
        internal List<string> Messages { get; } = [];
        internal Action<HostTaskRecord>? BeforeTaskCommit { get => store.BeforeCommit; set => store.BeforeCommit = value; }
        internal Action? BeforeIntent { set => store.BeforeControlIntent = _ => value?.Invoke(); }
        internal Task<WorkSessionAuthorization>? PendingCreation { set => store.PendingCreation = value; }
        internal Task CreationStarted => store.CreationStarted.Task;
        internal Action? BeforeMetadata { get => store.BeforeMetadata; set => store.BeforeMetadata = value; }
        internal bool FailTaskTerminal { get => store.FailTerminal; set => store.FailTerminal = value; }
        public bool CanControl => true;
        public bool CanInspect => true;
        public long ControlRevision => 1;
        public bool IsCurrentHost => true;
        public string Version => "1.0";
        public void Write(SecurityAuditEvent auditEvent) => Audits.Add(auditEvent);
        private readonly ModelTurnHost models;
        private readonly ClipboardExplanationAdmission admission;
        private readonly AudioControlAdmission preferencesAdmission;
        internal bool LoggingEnabled { get; set; } = true;
        public bool IsEnabled(LogLevel logLevel) => LoggingEnabled;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Messages.Add(formatter(state, exception));
        internal Fixture(bool qualified = true, ClipboardSnapshotBroker? broker = null)
        {
            ActivitySource.AddActivityListener(listener);
            var dependencies = new DependencyBootstrapper([], NullLogger<DependencyBootstrapper>.Instance);
            var runtime = new Kora.Tools.Runtime.RecordedRuntimeObservation(dependencies);
            var registry = new ReadOnlyCapabilityRegistry(this, new(), new(),
                new(this), new(dependencies), new(runtime), new(runtime),
                NullLogger<ReadOnlyCapabilityRegistry>.Instance);
            var preferenceStore = new AudioControlTestStore();
            preferencesAdmission = new(preferenceStore, preferenceStore, new(preferenceStore));
            var configuration = new ProviderModeConfigurationService(new Preferences(), preferencesAdmission, this);
            models = new(store, this, this, registry, this, NullLogger<ModelTurnHost>.Instance, Time,
                [new(ModelProviderSelection.OllamaCandidate, qualified
                    ? ModelQualificationGate.LocalCandidateSelection | ModelQualificationGate.IntegratedHost : ModelQualificationGate.None,
                    Time.Now.AddDays(1), qualified ? Adapter : null)], configuration);
            if (broker is not null) { Time.Now = DateTimeOffset.UtcNow; }
            Broker = broker ?? new(Reader, Time, NullLogger<ClipboardSnapshotBroker>.Instance);
            if (broker is not null) { Time.ReadTime = () => broker.Current?.CapturedAt ?? Time.Now; }
            admission = new(store, new(store));
            Workflow = new(Broker, new(Broker), admission, models, this, Time, this);
            Action = new(Workflow);
        }
        internal HostActivity Root(RequestOrigin origin = RequestOrigin.LocalUi) =>
            HostActivity.BeginRoot(HostRequest.Create(origin), HostActivityLayer.Application, HostOperation.Request);
        public async ValueTask DisposeAsync()
        {
            Workflow.Dispose();
            Adapter.Pending?.TrySetResult(Adapter.Reply());
            await models.DisposeAsync();
            await admission.DisposeAsync();
            await preferencesAdmission.DisposeAsync();
            Broker.Dispose();
            listener.Dispose();
        }
    }

    private sealed class Store : AudioControlTestStore, ISessionWorkspaceStore
    {
        internal Action? BeforeMetadata { get; set; }
        internal TaskCompletionSource CreationStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public new async ValueTask<SessionWorkspaceEntry> CreateNamedSessionAsync(HostRequest request,
            SessionName name, Func<bool> canControl, CancellationToken cancellationToken)
        {
            CreationStarted.TrySetResult();
            return new(await CreateAudioControlSessionAsync(request, canControl, cancellationToken), null);
        }

        public override ValueTask<SessionWorkspaceEntry> ReadMetadataAsync(HostId<SessionIdentity> session, CancellationToken cancellationToken)
        {
            BeforeMetadata?.Invoke();
            return ValueTask.FromResult(new SessionWorkspaceEntry(Authority!, null));
        }

        public override ValueTask<HostTaskObservation?> ReadTaskAsync(HostId<SessionIdentity> session,
            HostId<TaskIdentity> task, CancellationToken cancellationToken) =>
            ValueTask.FromResult<HostTaskObservation?>(new(Tasks.Last(item => item.Request.TaskId == task),
                Authority!.Generation, "host", true, null));
    }

    private sealed class Preferences : IModelProviderModePreferences
    {
        public ModelProviderMode? Load() => null;
        public ModelProviderMode? ReadBack() => null;
        public void BeginWrite() => throw new NotSupportedException();
        public void Save(ModelProviderMode mode) => throw new NotSupportedException();
        public void ConfirmWrite() => throw new NotSupportedException();
    }

    private sealed class Reader : IPlainTextClipboardReader
    {
        internal int Calls { get; private set; }
        internal string Text { get; set; } = "private-marker untrusted clipboard";
        internal ClipboardOutcome Outcome { get; set; } = ClipboardOutcome.Captured;
        public Task<ClipboardReadResult> ReadAsync(CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new ClipboardReadResult(Outcome, Text, 17));
        }
    }

    private sealed class Clock : TimeProvider
    {
        internal DateTimeOffset Now { get; set; } = DateTimeOffset.UnixEpoch;
        internal Func<DateTimeOffset>? ReadTime { get; set; }
        public override DateTimeOffset GetUtcNow() => ReadTime?.Invoke() ?? Now;
    }

    private sealed class Adapter : IModelTurnAdapter
    {
        internal int Calls { get; private set; }
        internal byte[]? Wire { get; private set; }
        internal ModelTurnProvenance? Provenance { get; private set; }
        internal TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource<ModelProviderReply>? Pending { get; set; }
        internal ModelTurnOutcome Outcome { get; set; } = ModelTurnOutcome.Succeeded;
        internal Exception? Failure { get; set; }
        internal ModelProviderReply Reply() => new(Outcome, ModelProviderSelection.OllamaCandidate,
            new("Genuine synthetic local explanation.", null));
        public Task<ModelProviderReply> RunAsync(ModelTurnProvenance provenance, ProviderProfileTuple? profile,
            ReadOnlyMemory<byte> context, Func<string, string, CancellationToken, ValueTask<CapabilityReply>> invokeTool,
            CancellationToken cancellationToken)
        {
            Calls++;
            Wire = context.ToArray();
            Provenance = provenance;
            Started.TrySetResult();
            if (Failure is not null) { throw Failure; }
            return Pending?.Task ?? Task.FromResult(Reply());
        }
    }
}