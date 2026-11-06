using System.Collections.Immutable;
using AwesomeAssertions;
using Kora.Application.Dependencies;
using Kora.Core.Dependencies;
using Kora.Core.Storage;
using Kora.Core.Tools;
using Kora.Core.Authorization;
using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Application.UnitTests.Dependencies;

public sealed partial class ModelTurnHostTests
{
    [Fact]
    public async Task MetricsAggregateModelsWithinOnlyTheirHostSessionWithoutSummingContext()
    {
        using var f = new Fixture();
        using var root = f.Root();
        var local = (await f.Host.AdmitAsync(ModelProviderSelection.OllamaCandidate, f.Context(), Token)).Turn!;
        var other = local.Provenance with { Selection = ModelProviderSelection.CopilotCandidate };
        var ledger = new SessionModelMetricsLedger(f.Time);
        await ledger.RecordAsync(local.Provenance, null, new(ModelTurnOutcome.Succeeded, local.Provenance.Selection) { TokenUsage = new(100, 20, 100, 1000) }, TimeSpan.FromSeconds(1));
        await ledger.RecordAsync(other, null, new(ModelTurnOutcome.Succeeded, other.Selection) { TokenUsage = new(300, 30, 300, 2000) }, TimeSpan.FromSeconds(2));
        var snapshot = await ledger.ReadAsync(f.Request.SessionId, Token);
        snapshot.Executions.Should().Be(2);
        snapshot.InputTokens.Should().Be(400);
        snapshot.OutputTokens.Should().Be(50);
        snapshot.Models.Should().HaveCount(2);
        snapshot.Models.Select(model => model.ContextPercent).Should().Equal(10, 15);
        snapshot.Models.Select(model => model.TotalElapsed.TotalSeconds).Should().Equal(1, 2);
        snapshot.Validate();
        (await ledger.ReadAsync(new(Guid.NewGuid()), Token)).Executions.Should().Be(0);
        await ledger.RecordAsync(local.Provenance, null, null, TimeSpan.Zero);
        var partial = await ledger.ReadAsync(f.Request.SessionId, Token);
        partial.Executions.Should().Be(3);
        partial.InputReportedExecutions.Should().Be(2);
        partial.Models[0].LatestContextTokens.Should().BeNull("a later missing report cannot reuse an old context meter");
        ledger.Forget(f.Request.SessionId);
        (await ledger.ReadAsync(f.Request.SessionId, Token)).Models.Should().BeEmpty();
    }

    [Fact]
    public async Task RealHostCountsProviderReplyOnceAndScopesMetricsToolToOriginalSession()
    {
        using var f = new Fixture();
        using var root = f.Root();
        f.Adapter.Reply = new(ModelTurnOutcome.Succeeded, ModelProviderSelection.OllamaCandidate, new("answer", null))
        { TokenUsage = new(100, 20, 100, 1000) };
        await f.Run();
        (await f.Host.ReadMetricsAsync(f.Request.SessionId, Token)).InputTokens.Should().Be(100);
        f.Adapter.OnTool = async invoke =>
        {
            var reply = await invoke(SessionMetricsTool.Id, "{}", Token);
            reply.Outcome.Should().Be(CapabilityOutcome.Succeeded);
            reply.SessionMetrics!.InputTokens.Should().Be(100);
            (await invoke(SessionMetricsTool.Id, "{\"session\":\"foreign\"}", Token)).Outcome.Should().Be(CapabilityOutcome.Denied);
        };
        var turn = (await f.Host.AdmitAsync(ModelProviderSelection.OllamaCandidate, f.Context(), Token)).Turn!;
        await f.Host.RunAsync(turn, Token);
        await f.Host.RunAsync(turn, Token);
        var final = await f.Host.ReadMetricsAsync(f.Request.SessionId, Token);
        final.Executions.Should().Be(2);
        final.InputTokens.Should().Be(200);
        final.OutputTokens.Should().Be(40);
        f.Host.ForgetMetrics(f.Request.SessionId);
        (await f.Host.ReadMetricsAsync(f.Request.SessionId, Token)).Executions.Should().Be(0);
    }

    [Theory]
    [InlineData("current")]
    [InlineData("privacy")]
    [InlineData("control")]
    [InlineData("selection")]
    [InlineData("source")]
    [InlineData("cancel")]
    [InlineData("unavailable")]
    [InlineData("late-source")]
    [InlineData("late-admission")]
    public async Task PassiveMetricsReadRevalidatesExactSessionAndPrivateSourceWithoutModelExecution(string fault)
    {
        using var f = new Fixture();
        using var root = f.Root();
        await using var service = f.ConversationService(f.Host, execution: fault is "unavailable" ? null : f.Host);
        var target = new SessionWorkspaceEntry(f.Session, f.Metadata);
        var admitted = true;
        if (fault is "privacy") { f.Inspect = false; }
        if (fault is "selection") { admitted = false; }
        if (fault is "source") { target = target with { Authority = target.Authority with { Generation = new(2) } }; }
        if (fault is "control") { f.OnRead = () => f.ControlRevision++; }
        var reads = 0;
        if (fault is "late-source" or "late-admission")
        {
            f.OnRead = () =>
            {
                if (++reads != 2) { return; }
                if (fault is "late-source") { f.Session = f.Session with { Generation = new(2) }; }
                else { admitted = false; }
            };
        }
        using var cancelled = new CancellationTokenSource();
        if (fault is "cancel") { cancelled.Cancel(); }
        var read = () => service.ReadMetricsAsync(target, () => admitted, cancelled.Token);
        if (fault is "current") { (await read()).Executions.Should().Be(0); }
        else { await read.Should().ThrowAsync<Exception>(); }
        f.Adapter.Calls.Should().Be(0);
    }

    [Fact]
    public async Task RetainedMetricsReopenAndMergeActualModelNamesAcrossCatalogueRevisions()
    {
        using var f = new Fixture();
        using var root = f.Root();
        var turn = (await f.Host.AdmitAsync(ModelProviderSelection.OllamaCandidate, f.Context(), Token)).Turn!;
        var store = new MetricsStore();
        var first = new SessionModelMetricsLedger(f.Time, store);
        var profile = Profile() with { Model = new("alpha") };
        await first.RecordAsync(turn.Provenance, profile,
            new(ModelTurnOutcome.Succeeded, turn.Provenance.Selection) { TokenUsage = new(100, 25, 100, 1000) }, TimeSpan.FromSeconds(1));
        var changed = turn.Provenance with
        {
            Turn = new(Guid.NewGuid()),
            Selection = turn.Provenance.Selection with { Model = new(Guid.NewGuid()), Revision = new(2) },
        };
        await first.RecordAsync(changed, profile with { CatalogueRevision = new(2) },
            new(ModelTurnOutcome.Succeeded, changed.Selection) { TokenUsage = new(50, 5, 50, 500) }, TimeSpan.FromSeconds(2));
        var beta = changed with { Turn = new(Guid.NewGuid()) };
        await first.RecordAsync(beta, profile with { Model = new("beta") },
            new(ModelTurnOutcome.Cancelled, beta.Selection), TimeSpan.Zero);
        var reopened = new SessionModelMetricsLedger(f.Time, store);
        var snapshot = await reopened.ReadAsync(f.Request.SessionId, Token);
        snapshot.Retained.Should().BeTrue();
        snapshot.Executions.Should().Be(3);
        snapshot.InputTokens.Should().Be(150);
        snapshot.OutputTokens.Should().Be(30);
        snapshot.Models.Should().HaveCount(2);
        var alpha = snapshot.Models[0];
        alpha.Model.Should().Be("alpha");
        alpha.Executions.Should().Be(2);
        alpha.LatestContextTokens.Should().Be(50);
        alpha.ContextCapacityTokens.Should().Be(500);
        alpha.TotalElapsed.Should().Be(TimeSpan.FromSeconds(3));
        snapshot.Models[1].InputReportedExecutions.Should().Be(0);
        (await reopened.ReadAsync(new(Guid.NewGuid()), Token)).Models.Should().BeEmpty();
        reopened.Forget(f.Request.SessionId);
        (await reopened.ReadAsync(f.Request.SessionId, Token)).Executions.Should().Be(3, "forgetting the volatile cache cannot purge retained usage");
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("foreign")]
    public async Task RetainedMetricsRejectMissingOrCrossSessionStorageData(string fault)
    {
        using var f = new Fixture();
        using var root = f.Root();
        var turn = (await f.Host.AdmitAsync(ModelProviderSelection.OllamaCandidate, f.Context(), Token)).Turn!;
        var store = new MetricsStore { ReturnAll = true };
        store.Records = fault is "missing" ? default
            : [new(turn.Provenance, "model", null, TimeSpan.Zero, f.Time.Now)];
        var ledger = new SessionModelMetricsLedger(f.Time, store);
        var read = () => ledger.ReadAsync(new(Guid.NewGuid()), Token);
        await read.Should().ThrowAsync<InvalidDataException>();
    }

    private sealed class MetricsStore : Kora.Core.Storage.ISessionModelMetricsStore
    {
        internal ImmutableArray<SessionModelUsage> Records { get; set; } = [];
        internal bool ReturnAll { get; set; }
        public ValueTask RecordModelUsageAsync(SessionModelUsage usage, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            usage.Validate();
            Records = Records.Add(usage);
            return ValueTask.CompletedTask;
        }
        public ValueTask<ImmutableArray<SessionModelUsage>> ReadModelUsageAsync(Kora.Core.Hosting.HostId<Kora.Core.Hosting.SessionIdentity> session,
            CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            return ValueTask.FromResult(ReturnAll ? Records : Records.Where(record => record.Provenance.Request.SessionId == session).ToImmutableArray());
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MetricsPersistenceFailuresCannotBecomeSuccessfulModelAnswersOrHideProviderFailure(bool providerFailure)
    {
        using var f = new Fixture();
        using var root = f.Root();
        var workspace = new FailingMetricsWorkspace(f);
        using var dependencies = new DependencyBootstrapper([], NullLogger<DependencyBootstrapper>.Instance);
        var runtimes = new Kora.Tools.Runtime.RecordedRuntimeObservation(dependencies);
        var registry = new Kora.Application.Tools.ReadOnlyCapabilityRegistry(f, new(), new(), new(f), new(dependencies),
            new(runtimes), new(runtimes), NullLogger<Kora.Application.Tools.ReadOnlyCapabilityRegistry>.Instance);
        await using var host = new ModelTurnHost(workspace, f, f, registry, f, f, f.Time,
            [new(ModelProviderSelection.OllamaCandidate, Fixture.Gates(ModelProviderSelection.OllamaCandidate),
                f.Time.Now.AddMinutes(1), f.Adapter)]);
        if (providerFailure) { f.Adapter.OnRun = () => throw new InvalidOperationException("private provider content"); }
        var turn = (await host.AdmitAsync(ModelProviderSelection.OllamaCandidate, f.Context(), Token)).Turn!;
        var run = () => host.RunAsync(turn, Token);
        if (providerFailure)
        {
            var failure = await run.Should().ThrowAsync<AggregateException>();
            failure.Which.InnerExceptions.Should().ContainSingle(error => error is InvalidOperationException)
                .And.ContainSingle(error => error is IOException);
        }
        else { await run.Should().ThrowAsync<IOException>(); }
        f.Adapter.Calls.Should().Be(1);
        host.IsQuiescent.Should().BeTrue();
        f.Audits.Last().Outcome.Should().Be(Kora.Core.Auditing.SecurityAuditOutcome.Unknown);
        f.Messages.Should().NotContain(message => message.Contains("private provider content", StringComparison.Ordinal));
    }

    private sealed class FailingMetricsWorkspace(Fixture f) : ISessionWorkspaceStore, ISessionModelMetricsStore
    {
        public ValueTask RecordModelUsageAsync(SessionModelUsage usage, CancellationToken token) =>
            ValueTask.FromException(new IOException("usage storage unavailable"));
        public ValueTask<ImmutableArray<SessionModelUsage>> ReadModelUsageAsync(HostId<SessionIdentity> session, CancellationToken token) =>
            throw new NotSupportedException();
        public ValueTask<SessionWorkspaceEntry> ReadMetadataAsync(HostId<SessionIdentity> session, CancellationToken token) => f.ReadMetadataAsync(session, token);
        public ValueTask<HostTaskObservation?> ReadTaskAsync(HostId<SessionIdentity> session, HostId<TaskIdentity> task, CancellationToken token) => f.ReadTaskAsync(session, task, token);
        public ValueTask<SessionDispositionPreview> PreviewDispositionAsync(HostId<SessionIdentity> session, HostRevision generation, long revision, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<SessionDispositionReceipt> DisposeSessionAsync(HostRequest request, SessionDispositionPreview preview, Func<bool> admitted, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<HostTaskRecord> RecordControlIntentAsync(HostRequest request, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<SessionPage<WorkSessionAuthorization>> ReadSessionsAsync(Guid? after, int limit, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<SessionPage<SessionWorkspaceEntry>> ReadMetadataPageAsync(Guid? after, int limit, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<HostTaskObservation> CancelWaitingTaskAsync(HostRequest request, HostTaskCancellationTarget target, Func<bool> admitted, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<SessionWorkspaceEntry> CreateNamedSessionAsync(HostRequest request, SessionName name, Func<bool> admitted, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<SessionWorkspaceEntry> RenameSessionAsync(HostRequest request, HostRevision generation, long revision, SessionName name, Func<bool> admitted, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<SessionPage<HostQuestionRecord>> ReadQuestionPageAsync(HostId<SessionIdentity> session, Guid? after, int limit, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<SessionPage<HostTaskRecord>> ReadTaskPageAsync(HostId<SessionIdentity> session, Guid? after, int limit, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<WorkSessionAuthorization> ChangeIdleLifecycleAsync(HostRequest request, HostRevision generation, bool active, Func<bool> admitted, CancellationToken token) => throw new NotSupportedException();
    }
}
