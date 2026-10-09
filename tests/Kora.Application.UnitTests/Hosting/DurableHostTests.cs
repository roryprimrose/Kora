using AwesomeAssertions;

using Kora.Application.Auditing;
using Kora.Application.Diagnostics;
using Kora.Application.Hosting;
using Kora.Core.Auditing;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;

using Microsoft.Extensions.Logging;

namespace Kora.Application.UnitTests.Hosting;

[Collection("Host tracing")]
public sealed class DurableHostTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Admitted_wait_precedes_dispatch_and_safe_cancellation_has_no_query_callback(bool cancel)
    {
        using var f = new Fixture();
        var invoked = false;
        var result = await f.Query.RunAsync(RequestOrigin.LocalUi, () =>
        {
            invoked = true;
            return Task.CompletedTask;
        }, CancellationToken.None, async intent =>
        {
            f.Store.Records.Last().Should().Be(intent);
            intent.State.Should().Be(HostTaskState.IntentRecorded);
            var coordinator = new HostTaskCoordinator(f.Store);
            return cancel ? await coordinator.RecordOutcomeAsync(intent, HostTaskState.Cancelled, CancellationToken.None)
                : await coordinator.RecordDispatchAsync(intent, CancellationToken.None);
        });
        result.State.Should().Be(cancel ? HostTaskState.Cancelled : HostTaskState.Succeeded);
        invoked.Should().Be(!cancel);
    }

    [Theory]
    [InlineData("foreign")]
    [InlineData("revision")]
    [InlineData("state")]
    [InlineData("cancelled")]
    [InlineData("failed")]
    public async Task Missing_or_hostile_wait_receipt_never_dispatches_or_claims_success(string failure)
    {
        using var f = new Fixture();
        var invoked = false;
        var run = () => f.Query.RunAsync(RequestOrigin.LocalUi, () =>
        {
            invoked = true;
            return Task.CompletedTask;
        }, CancellationToken.None, intent => failure switch
        {
            "foreign" => Task.FromResult(new HostTaskRecord(HostRequest.Create(RequestOrigin.LocalUi), new(2), HostTaskState.DispatchRecorded)),
            "revision" => Task.FromResult(new HostTaskRecord(intent.Request, new(3), HostTaskState.DispatchRecorded)),
            "state" => Task.FromResult(new HostTaskRecord(intent.Request, new(2), HostTaskState.Succeeded)),
            "cancelled" => Task.FromException<HostTaskRecord>(new OperationCanceledException()),
            _ => Task.FromException<HostTaskRecord>(new IOException("host wait failed")),
        });
        await run.Should().ThrowAsync<Exception>();
        invoked.Should().BeFalse();
        f.Store.Records.Should().ContainSingle().Which.State.Should().Be(HostTaskState.IntentRecorded);
        HostActivity.Current.Should().BeNull();
    }
    [Theory]
    [InlineData(RequestOrigin.LocalUi)]
    [InlineData(RequestOrigin.ActivatedVoice)]
    public async Task Version_query_commits_identified_ordered_receipt_after_required_evidence(RequestOrigin origin)
    {
        using var fixture = new Fixture();
        var invoked = false;
        var terminal = await fixture.Query.RunAsync(origin, () =>
        {
            invoked = true;
            fixture.Store.Records.Last().State.Should().Be(HostTaskState.DispatchRecorded);
            return Task.CompletedTask;
        }, CancellationToken.None);
        invoked.Should().BeTrue();
        terminal.State.Should().Be(HostTaskState.Succeeded);
        fixture.Store.Records.Select(record => record.State).Should().Equal(
            HostTaskState.IntentRecorded, HostTaskState.DispatchRecorded, HostTaskState.Succeeded);
        fixture.Sink.Audits.Select(row => row.Audit.Outcome).Should().Equal(
            SecurityAuditOutcome.Requested, SecurityAuditOutcome.Succeeded);
        fixture.Sink.Audits.Should().OnlyContain(row => row.Diagnostic.Host == terminal.Request
            && row.Audit.CorrelationId == terminal.Request.RequestId.Value);
        fixture.Sink.Diagnostics.Should().OnlyContain(row => row.Host == terminal.Request);
        fixture.Sink.Spans.Should().Contain(row => row.Host == terminal.Request
            && row.Outcome == HostOperationOutcome.Completed);
        DurableVersionQuery.StorageDisclosure.Should().Contain("not encrypted").And.Contain("readable");
    }

    [Fact]
    public async Task Invalid_or_pre_cancelled_query_never_records_intent()
    {
        using var fixture = new Fixture();
        var invalid = () => fixture.Query.RunAsync(RequestOrigin.LocalUi, null!, CancellationToken.None);
        await invalid.Should().ThrowAsync<ArgumentNullException>();
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        var run = () => fixture.Query.RunAsync(RequestOrigin.LocalUi, () => Task.CompletedTask, cancelled.Token);
        await run.Should().ThrowAsync<OperationCanceledException>();
        fixture.Store.Records.Should().BeEmpty();
    }

    [Fact]
    public async Task Asynchronous_intent_commit_preserves_the_presenters_synchronization_context()
    {
        using var fixture = new Fixture();
        var committed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Store.IntentGate = committed.Task;
        var context = new PresenterContext();
        var previous = SynchronizationContext.Current;
        Task<HostTaskRecord> run;
        SynchronizationContext.SetSynchronizationContext(context);
        try
        {
            run = fixture.Query.RunAsync(RequestOrigin.LocalUi, () =>
            {
                SynchronizationContext.Current.Should().BeSameAs(context);
                return Task.CompletedTask;
            }, CancellationToken.None);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
        committed.SetResult();
        (await run).State.Should().Be(HostTaskState.Succeeded);
    }

    [Theory]
    [InlineData(HostTaskState.IntentRecorded)]
    [InlineData(HostTaskState.DispatchRecorded)]
    [InlineData(HostTaskState.Succeeded)]
    public async Task Commit_failure_never_becomes_a_success_receipt(HostTaskState failAt)
    {
        using var fixture = new Fixture();
        fixture.Store.FailAt = failAt;
        var run = () => fixture.Query.RunAsync(RequestOrigin.LocalUi, () => Task.CompletedTask, CancellationToken.None);
        await run.Should().ThrowAsync<IOException>();
        fixture.Store.Records.Should().NotContain(record => record.State == HostTaskState.Succeeded);
    }

    [Theory]
    [InlineData(SecurityAuditOutcome.Requested)]
    [InlineData(SecurityAuditOutcome.Succeeded)]
    public async Task Audit_admission_failure_blocks_dispatch_or_terminal_receipt(SecurityAuditOutcome failAt)
    {
        using var fixture = new Fixture();
        fixture.Sink.FailAuditAt = failAt;
        var invoked = false;
        var run = () => fixture.Query.RunAsync(RequestOrigin.LocalUi, () =>
        {
            invoked = true;
            return Task.CompletedTask;
        }, CancellationToken.None);
        await run.Should().ThrowAsync<Exception>();
        invoked.Should().Be(failAt == SecurityAuditOutcome.Succeeded);
        fixture.Store.Records.Last().State.Should().Be(failAt == SecurityAuditOutcome.Requested
            ? HostTaskState.IntentRecorded : HostTaskState.DispatchRecorded);
        HostActivity.Current.Should().BeNull();
    }

    [Fact]
    public async Task Query_failure_has_failed_audit_and_receipt_but_is_not_swallowed()
    {
        using var fixture = new Fixture();
        var run = () => fixture.Query.RunAsync(RequestOrigin.LocalUi,
            () => Task.FromException(new IOException("fixture")), CancellationToken.None);
        await run.Should().ThrowAsync<IOException>();
        fixture.Store.Records.Last().State.Should().Be(HostTaskState.Failed);
        fixture.Sink.Audits.Last().Audit.Outcome.Should().Be(SecurityAuditOutcome.Failed);
    }

    [Fact]
    public async Task Interrupted_dispatched_query_and_late_callback_remain_unknown_without_replay()
    {
        using var fixture = new Fixture();
        using var cancellation = new CancellationTokenSource();
        var callback = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task lateCallback = Task.CompletedTask;
        var run = fixture.Query.RunAsync(RequestOrigin.LocalUi, () =>
        {
            started.SetResult();
            lateCallback = CompleteLateAsync();
            return lateCallback;
        }, cancellation.Token);
        await started.Task;
        await cancellation.CancelAsync();
        var cancelled = () => run;
        await cancelled.Should().ThrowAsync<OperationCanceledException>();
        fixture.Store.Records.Last().State.Should().Be(HostTaskState.DispatchRecorded);
        var recovered = await fixture.Recovery.RecoverAsync(CancellationToken.None);
        recovered.Should().ContainSingle().Which.State.Should().Be(HostTaskState.Unknown);
        callback.SetResult();
        await lateCallback;
        fixture.Store.Records.Should().NotContain(record => record.State == HostTaskState.Succeeded);
        (await fixture.Recovery.RecoverAsync(CancellationToken.None)).Should().BeEmpty();

        async Task CompleteLateAsync()
        {
            await callback.Task;
            var reenter = () => HostActivity.RequireCurrent();
            reenter.Should().Throw<InvalidOperationException>();
        }
    }

    [Fact]
    public async Task Cancellation_between_evidence_and_dispatch_leaves_intent_only_interrupted()
    {
        using var fixture = new Fixture();
        using var cancellation = new CancellationTokenSource();
        fixture.Sink.OnDiagnostic = cancellation.Cancel;
        var invoked = false;
        var run = () => fixture.Query.RunAsync(RequestOrigin.LocalUi, () =>
        {
            invoked = true;
            return Task.CompletedTask;
        }, cancellation.Token);
        await run.Should().ThrowAsync<OperationCanceledException>();
        invoked.Should().BeFalse();
        fixture.Sink.OnDiagnostic = null;
        (await fixture.Recovery.RecoverAsync(CancellationToken.None)).Should().ContainSingle()
            .Which.State.Should().Be(HostTaskState.Interrupted);
    }

    [Fact]
    public async Task Recovery_admission_failure_never_commits_or_invokes_an_executor()
    {
        using var fixture = new Fixture();
        fixture.Store.Records.Add(new(HostRequest.Create(RequestOrigin.LocalUi), new(1), HostTaskState.IntentRecorded));
        fixture.Sink.FailAuditAt = SecurityAuditOutcome.Unknown;
        var recovery = () => fixture.Recovery.RecoverAsync(CancellationToken.None);
        await recovery.Should().ThrowAsync<Exception>();
        fixture.Store.Records.Should().ContainSingle().Which.State.Should().Be(HostTaskState.IntentRecorded);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Invalid_or_over_limit_recovery_data_fails_explicitly(bool terminal)
    {
        using var fixture = new Fixture();
        fixture.Store.InvalidRead = terminal ? 1 : 101;
        var recovery = () => fixture.Recovery.RecoverAsync(CancellationToken.None);
        await recovery.Should().ThrowAsync<InvalidDataException>();
    }

    [Fact]
    public async Task Remaining_recovery_work_is_explicit_and_batch_is_bounded()
    {
        using var fixture = new Fixture();
        fixture.Store.Remaining = true;
        var recovery = () => fixture.Recovery.RecoverAsync(CancellationToken.None);
        await recovery.Should().ThrowAsync<InvalidOperationException>();
    }

    private sealed class Fixture : IDisposable
    {
        private readonly EvidenceLoggerProvider provider;
        private readonly ILoggerFactory factory;

        public Fixture()
        {
            provider = new EvidenceLoggerProvider([Sink], Sink);
            factory = LoggerFactory.Create(builder => builder.AddProvider(provider));
            var coordinator = new HostTaskCoordinator(Store);
            var audit = new LoggerSecurityAuditLog(factory.CreateLogger<LoggerSecurityAuditLog>());
            Query = new DurableVersionQuery(coordinator, audit, factory.CreateLogger<DurableVersionQuery>());
            Recovery = new DurableHostRecovery(Store, coordinator, audit, factory.CreateLogger<DurableHostRecovery>());
        }

        public Store Store { get; } = new();
        public Sink Sink { get; } = new();
        public DurableVersionQuery Query { get; }
        public DurableHostRecovery Recovery { get; }

        public void Dispose()
        {
            factory.Dispose();
            provider.Dispose();
        }
    }

    private sealed class Store : IHostTaskStore
    {
        public List<HostTaskRecord> Records { get; } = [];
        public HostTaskState? FailAt { get; set; }
        public int InvalidRead { get; set; }
        public bool Remaining { get; set; }
        public Task? IntentGate { get; set; }

        public ValueTask CommitAsync(HostTaskRecord record, long expectedRevision, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (record.State == FailAt)
            {
                throw new IOException("fixture commit failure");
            }
            if (record.State == HostTaskState.IntentRecorded && IntentGate is { } gate)
            {
                return new ValueTask(CommitLaterAsync(gate, record));
            }
            Records.Add(record);
            return ValueTask.CompletedTask;
        }

        private async Task CommitLaterAsync(Task gate, HostTaskRecord record)
        {
            await gate.ConfigureAwait(false);
            Records.Add(record);
        }

        public ValueTask<IReadOnlyList<HostTaskRecord>> ReadIncompleteAsync(int limit, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyList<HostTaskRecord> records = InvalidRead != 0
                ? Enumerable.Range(0, InvalidRead).Select(_ => new HostTaskRecord(
                    HostRequest.Create(RequestOrigin.LocalUi), new(1), HostTaskState.Succeeded)).ToArray()
                : Records.GroupBy(record => record.Request.TaskId).Select(group => group.Last())
                    .Where(record => !record.IsTerminal).Take(limit).ToArray();
            if (Remaining && limit == 1)
            {
                records = [new(HostRequest.Create(RequestOrigin.LocalUi), new(1), HostTaskState.IntentRecorded)];
            }
            return ValueTask.FromResult(records);
        }
    }

    private sealed class PresenterContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback callback, object? state) =>
            ThreadPool.QueueUserWorkItem(_ =>
            {
                var previous = Current;
                SetSynchronizationContext(this);
                try
                {
                    callback(state);
                }
                finally
                {
                    SetSynchronizationContext(previous);
                }
            });
    }

    private sealed class Sink : IEvidenceSink, IEvidenceGapReporter
    {
        public string Name => "fixture";
        public List<DiagnosticEnvelope> Diagnostics { get; } = [];
        public List<AuditEnvelope> Audits { get; } = [];
        public List<CompletedActivityEnvelope> Spans { get; } = [];
        public SecurityAuditOutcome? FailAuditAt { get; set; }
        public Action? OnDiagnostic { get; set; }

        public void WriteDiagnostic(DiagnosticEnvelope envelope)
        {
            Diagnostics.Add(envelope);
            OnDiagnostic?.Invoke();
        }

        public void WriteAudit(AuditEnvelope envelope)
        {
            if (envelope.Audit.Outcome == FailAuditAt)
            {
                throw new IOException("fixture audit failure");
            }
            Audits.Add(envelope);
        }

        public void WriteActivity(CompletedActivityEnvelope envelope) => Spans.Add(envelope);
        public void Report(EvidenceGap gap) { }
    }
}
