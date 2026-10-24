using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;

using AwesomeAssertions;

using Kora.Application.Agents;
using Kora.Application.Configuration;
using Kora.Application.Dependencies;
using Kora.Application.Hosting;
using Kora.Application.Tools;
using Kora.Core.Agents;
using Kora.Core.Auditing;
using Kora.Core.Authorization;
using Kora.Core.Context;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.Core.Providers;
using Kora.Core.Storage;
using Kora.Core.Tools;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Application.UnitTests.Agents;

[Collection("Host tracing")]
public sealed class AgentRunSetTests
{
    [Fact]
    public async Task DistinctConfirmedRunsActuallyOverlapAndRetainOnlyTheirOwnGenuineAnswer()
    {
        await using var f = new Fixture();
        await using var runs = f.Factory.Create();
        await f.Review(runs, 1);
        await f.Review(runs, 2);
        var one = runs.InvokeAsync(1, RequestOrigin.LocalUi, Token);
        await f.Adapter.Started(f.Sessions[0]).Task;
        var two = runs.InvokeAsync(2, RequestOrigin.ActivatedVoice, Token);
        await f.Adapter.Started(f.Sessions[1]).Task;
        f.Adapter.Active.Should().Be(2);
        f.Host.IsQuiescent.Should().BeFalse();
        f.Readers.Should().OnlyContain(item => !item.Released);
        f.Adapter.Finish(f.Sessions[0], "first genuine answer");
        await one;
        runs.Read(1)!.Receipt!.Message.AssistantResponse!.Body.Should().Be("first genuine answer");
        f.Readers[0].Released.Should().BeTrue();
        f.Readers[1].Released.Should().BeFalse();
        two.IsCompleted.Should().BeFalse();
        f.Host.IsQuiescent.Should().BeFalse();
        f.Adapter.Finish(f.Sessions[1], "second genuine answer");
        await two;
        runs.Read(2)!.Receipt!.Message.AssistantResponse!.Body.Should().Be("second genuine answer");
        f.Messages.Values.Select(item => item.Request.SessionId).Should().OnlyHaveUniqueItems();
        f.Messages.Values.Select(item => item.Execution!.Request.TaskId).Should().OnlyHaveUniqueItems();
        f.Messages.Values.Should().OnlyContain(item => item.Outcome == ModelTurnOutcome.Succeeded);
        f.Readers.Should().OnlyContain(item => item.Released);
        f.Host.IsQuiescent.Should().BeTrue();
        f.Audits.Count(item => string.Equals(item.ActionId, "agents.revision.invoke", StringComparison.Ordinal)
            && item.Outcome == SecurityAuditOutcome.Succeeded).Should().Be(2);
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("capacity-one")]
    [InlineData("expired")]
    [InlineData("future")]
    [InlineData("empty-revision")]
    [InlineData("selection")]
    [InlineData("adapter")]
    [InlineData("revoked")]
    public async Task UnqualifiedCapacityKeepsSecondRunBlockedWithoutDispatchOrRetention(string fault)
    {
        await using var f = new Fixture();
        f.Qualification.Proof = fault switch
        {
            "unknown" => null,
            "capacity-one" => f.Qualification.Proof! with { MaximumRuns = 1 },
            "expired" => f.Qualification.Proof! with { ExpiresAt = f.Time.GetUtcNow() },
            "future" => f.Qualification.Proof! with { ObservedAt = f.Time.GetUtcNow().AddSeconds(1) },
            "empty-revision" => f.Qualification.Proof! with { Revision = Guid.Empty },
            "selection" => f.Qualification.Proof! with { Selection = ModelProviderSelection.CopilotCandidate },
            "adapter" => f.Qualification.Proof! with { Adapter = new BarrierAdapter() },
            _ => f.Qualification.Proof,
        };
        if (fault is "revoked") { f.Qualification.Current = false; }
        await using var runs = f.Factory.Create();
        await f.Review(runs, 1);
        await f.Review(runs, 2);
        var one = runs.InvokeAsync(1, RequestOrigin.LocalUi, Token);
        await f.Adapter.Started(f.Sessions[0]).Task;
        await runs.Invoking(value => value.InvokeAsync(2, RequestOrigin.LocalUi, Token)).Should().ThrowAsync<InvalidOperationException>();
        f.Adapter.Active.Should().Be(1);
        f.Messages.Should().HaveCount(1);
        f.Adapter.Finish(f.Sessions[0], "serial genuine answer");
        await one;
        // A failed operation is reported again during truthful aggregate closure.
        await runs.Invoking(value => value.StopAsync(2)).Should().ThrowAsync<AggregateException>();
    }

    [Fact]
    public async Task SameSessionAndThirdEntryFailBeforeReadingSourceAndChangingOneRunDoesNotRetireAnother()
    {
        await using var f = new Fixture();
        await using var runs = f.Factory.Create();
        await f.Review(runs, 1);
        Action duplicate = () => _ = runs.ReviewAsync(2, "selected", f.Sessions[0], "other", RequestOrigin.LocalUi, () => true, Token);
        duplicate.Should().Throw<InvalidOperationException>();
        Action third = () => _ = runs.ReviewAsync(3, "selected", f.Sessions[2], "third", RequestOrigin.LocalUi, () => true, Token);
        third.Should().Throw<ArgumentOutOfRangeException>();
        f.Readers.Should().HaveCount(1);
        await f.Review(runs, 2);
        runs.Revoke(1);
        await runs.StopAsync(1);
        runs.Read(1).Should().BeNull();
        runs.Read(2)!.Review!.IsInvocationReady.Should().BeTrue();
        f.Readers[1].Released.Should().BeFalse();
        var running = runs.InvokeAsync(2, RequestOrigin.LocalUi, Token);
        await f.Adapter.Started(f.Sessions[1]).Task;
        f.Adapter.Finish(f.Sessions[1], "unaffected answer");
        await running;
    }

    [Theory]
    [InlineData("source")]
    [InlineData("input")]
    [InlineData("privacy")]
    [InlineData("expired")]
    public async Task ExactReviewInvalidationNeverDispatchesOrRetainsARequest(string fault)
    {
        await using var f = new Fixture();
        var current = true;
        await using var runs = f.Factory.Create();
        await runs.ReviewAsync(1, "selected", f.Sessions[0], "original task", RequestOrigin.LocalUi, () => current, Token);
        if (fault is "source") { f.Readers[0].Text += "\nchanged"; }
        if (fault is "input") { current = false; }
        if (fault is "privacy") { f.CanControl = false; }
        if (fault is "expired") { f.Time.Now = f.Time.Now.AddMinutes(3); }
        await runs.Invoking(value => value.InvokeAsync(1, RequestOrigin.LocalUi, Token)).Should().ThrowAsync<Exception>();
        f.Adapter.Active.Should().Be(0);
        f.Messages.Should().BeEmpty();
        await runs.Invoking(value => value.StopAsync(1)).Should().ThrowAsync<AggregateException>();
        f.Readers[0].Released.Should().BeTrue();
    }

    [Theory]
    [InlineData(RequestOrigin.LocalUi)]
    [InlineData(RequestOrigin.ActivatedVoice)]
    public async Task OriginalTaskChannelIsRetainedIndependentlyOfConfirmationChannel(RequestOrigin origin)
    {
        await using var f = new Fixture();
        await using var runs = f.Factory.Create();
        await runs.ReviewAsync(1, "selected", f.Sessions[0], "exact original", origin, () => true, Token);
        var operation = runs.InvokeAsync(1, RequestOrigin.LocalUi, Token);
        await f.Adapter.Started(f.Sessions[0]).Task;
        f.Adapter.Finish(f.Sessions[0], "genuine original channel answer");
        await operation;
        runs.Read(1)!.Receipt!.Message.Request.Origin.Should().Be(origin);
    }

    [Fact]
    public async Task MissingBusyStoppedAndClosedEntriesNeverAcquireReplacementAuthority()
    {
        await using var f = new Fixture();
        var runs = f.Factory.Create();
        Action missing = () => _ = runs.InvokeAsync(1, RequestOrigin.LocalUi, Token);
        missing.Should().Throw<InvalidOperationException>();
        Action resolveMissing = () => _ = runs.ResolveAsync(2, RequestOrigin.LocalUi, Token);
        resolveMissing.Should().Throw<InvalidOperationException>();
        Action invalid = () => runs.Read(0);
        invalid.Should().Throw<ArgumentOutOfRangeException>();
        await runs.StopAsync(1);
        await f.Review(runs, 1);
        var one = runs.InvokeAsync(1, RequestOrigin.LocalUi, Token);
        await f.Adapter.Started(f.Sessions[0]).Task;
        Action busy = () => _ = runs.InvokeAsync(1, RequestOrigin.LocalUi, Token);
        busy.Should().Throw<InvalidOperationException>();
        Action replace = () => _ = f.Review(runs, 1);
        replace.Should().Throw<InvalidOperationException>();
        f.Adapter.Finish(f.Sessions[0], "actual");
        await one;
        await runs.DisposeAsync();
        Action closed = () => _ = f.Review(runs, 1);
        closed.Should().Throw<ObjectDisposedException>();
        Action closedInvoke = () => _ = runs.InvokeAsync(1, RequestOrigin.LocalUi, Token);
        closedInvoke.Should().Throw<ObjectDisposedException>();
        await runs.DisposeAsync();
    }

    [Fact]
    public async Task InvalidToolCapableProfileIsReleasedWithoutAnyModelOrConversationEffect()
    {
        await using var f = new Fixture();
        f.ConfigureReader = reader => reader.Text = reader.Text
            .Replace("tools: none", "tools: " + ReadOnlyCapabilityCatalog.List, StringComparison.Ordinal)
            .Replace("maximum-calls: 0", "maximum-calls: 1", StringComparison.Ordinal);
        await using var runs = f.Factory.Create();
        await runs.Invoking(value => f.Review(value, 1)).Should().ThrowAsync<Exception>();
        f.Readers[0].Released.Should().BeTrue();
        f.Messages.Should().BeEmpty();
        f.Adapter.Active.Should().Be(0);
        await runs.Invoking(value => value.StopAsync(1)).Should().ThrowAsync<AggregateException>();
    }

    [Fact]
    public async Task SourceReleaseFailureDoesNotSkipOtherIndependentSourceOwner()
    {
        await using var f = new Fixture();
        var runs = f.Factory.Create();
        await f.Review(runs, 1);
        await f.Review(runs, 2);
        f.Readers[0].FailDispose = true;
        await runs.Invoking(value => value.DisposeAsync().AsTask()).Should().ThrowAsync<AggregateException>();
        f.Readers.Should().OnlyContain(item => item.Released);
    }

    internal static CancellationToken Token => TestContext.Current.CancellationToken;

    internal sealed class Fixture : ISessionWorkspaceStore, ISessionConversationStore, ISessionWorkspaceAccess,
        ICapabilityHostAccess, IApplicationInfo, ISecurityAuditLog, IAsyncDisposable
    {
        private readonly ActivityListener listener = new()
        {
            ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
        };
        private readonly DependencyBootstrapper dependencies = new([], NullLogger<DependencyBootstrapper>.Instance);
        private readonly Kora.Application.Voice.AudioControlAdmission admission;
        internal TestTime Time { get; } = new();
        internal BarrierAdapter Adapter { get; } = new();
        internal Qualification Qualification { get; }
        internal ModelTurnHost Host { get; }
        internal AgentRunSetFactory Factory { get; }
        internal List<Reader> Readers { get; } = [];
        internal Action<Reader>? ConfigureReader { get; set; }
        internal SessionWorkspaceEntry[] Sessions { get; } = Enumerable.Range(0, 3)
            .Select(_ => new SessionWorkspaceEntry(new(new(Guid.NewGuid()), new(1), true), null)).ToArray();
        internal ConcurrentDictionary<HostId<RequestIdentity>, ConversationMessage> Messages { get; } = new();
        internal ConcurrentDictionary<HostId<TaskIdentity>, HostTaskObservation> Tasks { get; } = new();
        internal ConcurrentQueue<SecurityAuditEvent> Audits { get; } = new();
        public bool CanControl { get; set; } = true;
        public bool CanInspect => CanControl;
        public long ControlRevision { get; set; }
        public bool IsCurrentHost => true;
        public string Version => "test";

        internal Fixture(IModelTurnAdapter? extra = null)
        {
            ActivitySource.AddActivityListener(listener);
            Qualification = new(Time, Adapter);
            var store = new Configuration.AudioControlTestStore();
            admission = new(store, store, new(store));
            var mode = new ProviderModeConfigurationService(new Preferences(), admission, this);
            mode.Observe();
            var runtimes = new Kora.Tools.Runtime.RecordedRuntimeObservation(dependencies);
            var registry = new ReadOnlyCapabilityRegistry(this, new(), new(), new(this), new(dependencies),
                new(runtimes), new(runtimes), NullLogger<ReadOnlyCapabilityRegistry>.Instance);
            List<ModelProviderRegistration> registrations =
                [new(ModelProviderSelection.OllamaCandidate,
                    ModelQualificationGate.LocalCandidateSelection | ModelQualificationGate.IntegratedHost,
                    Time.Now.AddMinutes(10), Adapter)];
            if (extra is not null) { registrations.Add(new(ModelProviderSelection.CopilotCandidate, ModelQualificationGate.None, Time.Now, extra)); }
            Host = new(this, this, this, registry, this, NullLogger<ModelTurnHost>.Instance, Time, [.. registrations], mode);
            var workspace = new SessionWorkspaceService(this, new HostTaskCoordinator(store), this,
                NullLogger<SessionWorkspaceService>.Instance);
            Factory = new(Host, Qualification, lane =>
            {
                var reader = new Reader();
                ConfigureReader?.Invoke(reader);
                Readers.Add(reader);
                var conversation = new SessionConversationService(this, this, workspace, this, lane, Time,
                    NullLogger<SessionConversationService>.Instance, lane, this);
                return new(reader, this, this, conversation, lane, this, Time, Instructions.Resolver);
            });
        }

        internal AgentInstructionReferenceResolverTests.Fixture Instructions { get; } = new();

        internal Task Review(AgentRunSet runs, int number) => runs.ReviewAsync(number, "selected",
            Sessions[number - 1], $"original task {number}", RequestOrigin.LocalUi, () => true, Token);

        public void Write(SecurityAuditEvent auditEvent)
        {
            HostActivity.RequireCurrent();
            Audits.Enqueue(auditEvent);
        }

        public ValueTask<SessionWorkspaceEntry> ReadMetadataAsync(HostId<SessionIdentity> session, CancellationToken cancellationToken) =>
            ValueTask.FromResult(Sessions.Single(item => item.Authority.SessionId == session));
        public ValueTask<HostTaskObservation?> ReadTaskAsync(HostId<SessionIdentity> session, HostId<TaskIdentity> task, CancellationToken cancellationToken) =>
            ValueTask.FromResult(Tasks.GetValueOrDefault(task));
        public ValueTask<ConversationSubmissionReceipt> RetainConversationMessageAsync(HostRequest request,
            HostRevision generation, long expectedMetadataRevision, string body, Func<bool> admitted, CancellationToken token)
        {
            Require(admitted, token);
            Messages.TryAdd(request.RequestId, new(request, generation, 1, 1, Time.Now, body, null, null)).Should().BeTrue();
            return ValueTask.FromResult(Publish(request, HostTaskState.IntentRecorded));
        }
        public ValueTask<ConversationSubmissionReceipt> BeginConversationExecutionAsync(HostRequest request,
            ModelTurnProvenance provenance, long metadataRevision, Func<bool> admitted, CancellationToken token)
        {
            Require(admitted, token);
            Messages[request.RequestId] = Messages[request.RequestId] with
            { Version = 2, Revision = 2, Execution = provenance, ExecutionMetadataRevision = metadataRevision };
            return ValueTask.FromResult(Publish(request, HostTaskState.DispatchRecorded));
        }
        public ValueTask<ConversationSubmissionReceipt> CompleteConversationExecutionAsync(HostRequest request,
            ModelTurnResult result, Func<bool> admitted, CancellationToken token)
        {
            Require(admitted, token);
            var before = Messages[request.RequestId];
            Messages[request.RequestId] = before with
            {
                Revision = 3, Outcome = result.Outcome, Reason = result.Reason,
                AssistantResponse = result.Outcome == ModelTurnOutcome.Succeeded
                    ? ConversationResponse.FromResult(result, before.Execution!) : null,
            };
            return ValueTask.FromResult(Publish(request, Messages[request.RequestId].ExpectedTaskState));
        }
        public ValueTask<ConversationSubmissionReceipt> CompleteConversationSubmissionAsync(HostRequest request,
            ModelTurnOutcome outcome, ModelTurnReason reason, Func<bool> admitted, CancellationToken token)
        {
            Require(admitted, token);
            Messages[request.RequestId] = Messages[request.RequestId] with { Revision = 2, Outcome = outcome, Reason = reason };
            return ValueTask.FromResult(Publish(request, Messages[request.RequestId].ExpectedTaskState));
        }
        private ConversationSubmissionReceipt Publish(HostRequest request, HostTaskState state)
        {
            var message = Messages[request.RequestId];
            var task = new HostTaskRecord(request, new(message.Revision), state);
            Tasks[request.TaskId] = new(task, message.Generation, "host", true, null);
            return new(message, task);
        }
        private static void Require(Func<bool> admitted, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (!admitted()) { throw new InvalidOperationException("Fake commit admission revoked."); }
        }
        public ValueTask<ConversationMessage> ReadConversationMessageAsync(HostId<SessionIdentity> session,
            HostId<RequestIdentity> request, CancellationToken token) => ValueTask.FromResult(Messages[request]);

        public async ValueTask DisposeAsync()
        {
            try { await Host.DisposeAsync(); }
            finally { await admission.DisposeAsync(); dependencies.Dispose(); listener.Dispose(); }
        }

        public ValueTask<SessionDispositionPreview> PreviewDispositionAsync(HostId<SessionIdentity> session, HostRevision expectedGeneration, long expectedMetadataRevision, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<SessionDispositionReceipt> DisposeSessionAsync(HostRequest request, SessionDispositionPreview preview, Func<bool> canControl, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<HostTaskRecord> RecordControlIntentAsync(HostRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<SessionPage<WorkSessionAuthorization>> ReadSessionsAsync(Guid? after, int limit, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<SessionPage<SessionWorkspaceEntry>> ReadMetadataPageAsync(Guid? after, int limit, CancellationToken cancellationToken) => ValueTask.FromResult(new SessionPage<SessionWorkspaceEntry>([.. Sessions], null));
        public ValueTask<HostTaskObservation> CancelWaitingTaskAsync(HostRequest control, HostTaskCancellationTarget target, Func<bool> canControl, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<SessionWorkspaceEntry> CreateNamedSessionAsync(HostRequest request, SessionName name, Func<bool> canControl, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<SessionWorkspaceEntry> RenameSessionAsync(HostRequest request, HostRevision expectedGeneration, long expectedMetadataRevision, SessionName name, Func<bool> canControl, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<SessionPage<HostQuestionRecord>> ReadQuestionPageAsync(HostId<SessionIdentity> session, Guid? after, int limit, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<SessionPage<HostTaskRecord>> ReadTaskPageAsync(HostId<SessionIdentity> session, Guid? after, int limit, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<WorkSessionAuthorization> ChangeIdleLifecycleAsync(HostRequest request, HostRevision expectedGeneration, bool active, Func<bool> canControl, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<HostTaskRecord> RecordConversationCredentialRejectionAsync(HostRequest request, HostRevision generation, Func<bool> admitted, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<ConversationDraftSnapshot> ReadConversationDraftAsync(HostId<SessionIdentity> session, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<ConversationDraftSnapshot> CommitConversationDraftAsync(HostRequest control, ConversationDraftSnapshot expected, string? body, Func<bool> admitted, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<ConversationPage> ReadConversationPageAsync(HostId<SessionIdentity> session, ConversationCursor? cursor, int limit, CancellationToken token) => throw new NotSupportedException();
    }

    internal sealed class TestTime : TimeProvider
    {
        internal DateTimeOffset Now { get; set; } = DateTimeOffset.UnixEpoch;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    internal sealed class Qualification(TestTime time, IModelTurnAdapter adapter) : ILocalAgentConcurrencyQualification
    {
        internal LocalAgentCapacity? Proof { get; set; } = new(ModelProviderSelection.OllamaCandidate, null, adapter,
            Guid.NewGuid(), time.Now, time.Now.AddMinutes(5), 2);
        internal bool Current { get; set; } = true;
        internal Action? OnObserve { get; set; }
        internal Action? OnCurrent { get; set; }
        public LocalAgentCapacity? Observe(ModelProviderSelection selection, ProviderProfileTuple? profile, IModelTurnAdapter adapter)
        {
            OnObserve?.Invoke();
            return Proof;
        }
        public bool IsCurrent(LocalAgentCapacity capacity)
        {
            OnCurrent?.Invoke();
            return Current && capacity == Proof;
        }
    }

    internal sealed class BarrierAdapter : IModelTurnAdapter, IAsyncDisposable
    {
        private readonly ConcurrentDictionary<HostId<SessionIdentity>, TaskCompletionSource> started = new();
        private readonly ConcurrentDictionary<HostId<SessionIdentity>, TaskCompletionSource> cancelled = new();
        private readonly ConcurrentDictionary<HostId<SessionIdentity>, TaskCompletionSource<ModelProviderReply>> replies = new();
        private int active;
        internal int Active => Volatile.Read(ref active);
        public bool IsQuiescent => Quiescent ?? Active == 0;
        internal bool FailDispose { get; set; }
        internal bool Disposed { get; private set; }
        internal bool? Quiescent { get; set; }
        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return FailDispose ? ValueTask.FromException(new IOException("Adapter disposal failed.")) : ValueTask.CompletedTask;
        }
        internal TaskCompletionSource Started(SessionWorkspaceEntry session) =>
            started.GetOrAdd(session.Authority.SessionId, _ => new(TaskCreationOptions.RunContinuationsAsynchronously));
        internal TaskCompletionSource Cancelled(SessionWorkspaceEntry session) =>
            cancelled.GetOrAdd(session.Authority.SessionId, _ => new(TaskCreationOptions.RunContinuationsAsynchronously));
        internal void Finish(SessionWorkspaceEntry session, string answer) =>
            replies[session.Authority.SessionId].SetResult(new(ModelTurnOutcome.Succeeded, ModelProviderSelection.OllamaCandidate, new(answer, null)));
        internal void Fail(SessionWorkspaceEntry session) => replies[session.Authority.SessionId].SetException(new IOException("Late adapter failure."));
        public async Task<ModelProviderReply> RunAsync(ModelTurnProvenance provenance, ProviderProfileTuple? profile,
            ReadOnlyMemory<byte> context, Func<string, string, CancellationToken, ValueTask<CapabilityReply>> invokeTool,
            CancellationToken token)
        {
            var pending = replies.GetOrAdd(provenance.Request.SessionId, _ => new(TaskCreationOptions.RunContinuationsAsynchronously));
            Interlocked.Increment(ref active);
            started.GetOrAdd(provenance.Request.SessionId, _ => new(TaskCreationOptions.RunContinuationsAsynchronously)).TrySetResult();
            using var registration = token.Register(() =>
                cancelled.GetOrAdd(provenance.Request.SessionId, _ => new(TaskCreationOptions.RunContinuationsAsynchronously)).TrySetResult());
            try { return await pending.Task; }
            finally { Interlocked.Decrement(ref active); }
        }
    }

    internal sealed class Reader : IAgentProfileReader, IAgentProfileSelection
    {
        internal bool Released { get; private set; }
        internal bool FailDispose { get; set; }
        internal string Text { get; set; } = """
            ---
            name: existing-profile
            description: Inert role
            kora-agent-format: 1
            runtime: local
            input: text
            output: text
            references: none
            tools: none
            maximum-calls: 0
            maximum-rounds: 1
            maximum-input-bytes: 32768
            maximum-output-bytes: 4096
            deadline-ms: 15000
            ---
            Explain only the original task.
            """;
        public LocalFileMetadata Metadata { get; } = new(@"C:\owner\existing.agent.md", "source", 512, DateTimeOffset.UnixEpoch);
        public Task<IAgentProfileSelection> OpenAsync(string path, CancellationToken token) => Task.FromResult<IAgentProfileSelection>(this);
        public Task<byte[]> ReadAsync(CancellationToken token) => Task.FromResult(Encoding.UTF8.GetBytes(Text));
        public void Dispose()
        {
            Released = true;
            if (FailDispose) { throw new IOException("Source release failed."); }
        }
    }

    private sealed class Preferences : IModelProviderModePreferences
    {
        public ModelProviderMode? Load() => ModelProviderMode.LocalOnly;
        public ModelProviderMode? ReadBack() => Load();
        public void BeginWrite() => throw new NotSupportedException();
        public void ConfirmWrite() => throw new NotSupportedException();
        public void Save(ModelProviderMode mode) => throw new NotSupportedException();
    }
}
