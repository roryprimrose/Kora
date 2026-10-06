using System.Diagnostics;
using System.Text.Json;

using AwesomeAssertions;

using Kora.Application.Auditing;
using Kora.Application.Diagnostics;
using Kora.Application.Hosting;
using Kora.Core.Auditing;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;
using Kora.Windows.IntegrationTests.Audio;
using Kora.Windows.Storage;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Kora.Windows.IntegrationTests.Storage;

[Collection(nameof(DurableStorageCompositionTestGroup))]
public sealed class DurableStorageCompositionTests
{
    private const string RootVariable = "KORA_OWNED_COMPOSITION_CHILD_ROOT";
    private const string ModeVariable = "KORA_OWNED_COMPOSITION_CHILD_MODE";

    [Theory]
    [InlineData(RequestOrigin.LocalUi)]
    [InlineData(RequestOrigin.ActivatedVoice)]
    public async Task Actual_runner_provider_and_both_private_databases_preserve_ordered_correlated_receipts(RequestOrigin origin)
    {
        using var paths = new OwnedStorageFixture();
        using var composition = await ComposeAsync(paths);
        var invoked = 0;
        using var incoming = new Activity("untrusted.correlation").SetIdFormat(ActivityIdFormat.W3C).Start();
        var receipt = await composition.Query.RunAsync(origin, async () =>
        {
            var current = HostActivity.RequireCurrent();
            (await composition.Tasks.ReadTaskAsync(current.Request.TaskId, TestContext.Current.CancellationToken))!
                .State.Should().Be(HostTaskState.DispatchRecorded);
            invoked++;
        }, TestContext.Current.CancellationToken);
        invoked.Should().Be(1);
        receipt.State.Should().Be(HostTaskState.Succeeded);
        receipt.Revision.Value.Should().Be(3);
        receipt.Request.Origin.Should().Be(origin);
        HostActivity.Current.Should().BeNull();
        Activity.Current.Should().BeSameAs(incoming);
        composition.Observed.Gaps.Should().BeEmpty();
        composition.Observed.Diagnostics.Should().HaveCount(2).And.OnlyContain(row => row.Host == receipt.Request);
        composition.Observed.Audits.Select(row => row.Audit.Outcome).Should().Equal(
            SecurityAuditOutcome.Requested, SecurityAuditOutcome.Succeeded);
        composition.Observed.Audits.Should().OnlyContain(row => row.Diagnostic.Host == receipt.Request
            && row.Audit.CorrelationId == receipt.Request.RequestId.Value
            && row.Audit.Initiator == (origin == RequestOrigin.ActivatedVoice
                ? SecurityAuditInitiator.VoiceCommand : SecurityAuditInitiator.LocalUser));
        composition.Observed.Spans.Should().HaveCount(9).And.OnlyContain(row => row.Host == receipt.Request);
        var root = composition.Observed.Spans.Single(row => string.Equals(row.Trace.Name, "session.request", StringComparison.Ordinal));
        root.Trace.ParentSpanId.Should().BeNull();
        root.Trace.SourceVersion.Should().Be(typeof(HostActivity).Assembly.GetName().Version!.ToString());
        root.Trace.TraceId.Should().NotBe(incoming.TraceId.ToHexString());
        composition.Observed.Spans.Should().OnlyContain(row => string.Equals(row.Trace.TraceId, root.Trace.TraceId, StringComparison.Ordinal));

        (await new WindowsSqliteHostTaskStore(paths).ReadTaskAsync(receipt.Request.TaskId,
            TestContext.Current.CancellationToken)).Should().Be(receipt);
        AssertLedger(paths, HostTaskState.IntentRecorded, HostTaskState.DispatchRecorded, HostTaskState.Succeeded);
        AssertEvidence(paths, receipt.Request, root.Trace.TraceId, expectedAudits: 2);
        new WindowsSqliteEvidenceSink(paths).Initialize();
    }

    [WindowsFact]
    public async Task Ordinary_contextless_provider_logs_override_spoofed_bootstrap_claims_and_store_only_gap_diagnostics()
    {
        using var paths = new OwnedStorageFixture();
        using var composition = await ComposeAsync(paths);
        using var incoming = new Activity("untrusted.logger-context").SetIdFormat(ActivityIdFormat.W3C).Start();
        HostActivity.Current.Should().BeNull();
        var logger = composition.CreateDiagnosticLogger();
        var state = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["{OriginalFormat}"] = "Hostless fixture.",
            ["Value"] = 1,
            ["kora.bootstrap"] = true,
            ["kora.evidence.gap"] = "forged-gap",
            ["SecurityAudit"] = true,
        };
        logger.Log(LogLevel.Information, new EventId(0), state, exception: null,
            static (_, _) => throw new InvalidOperationException("The fixture formatter must never be called."));
        var diagnostic = composition.Observed.Diagnostics.Should().ContainSingle().Which;
        diagnostic.Host.Should().BeNull();
        diagnostic.Trace.Should().BeNull();
        diagnostic.AuditCorrelationId.Should().BeNull();
        diagnostic.ApprovalId.Should().BeNull();
        diagnostic.Properties["kora.bootstrap"].Should().Be(new EvidenceValue(EvidenceValueKind.Boolean, "false"));
        diagnostic.Properties["kora.evidence.gap"].Should().Be(new EvidenceValue(EvidenceValueKind.Text, "MissingHostContext"));
        composition.Observed.Gaps.Should().ContainSingle().Which.Reason.Should().Be(EvidenceGapReason.MissingHostContext);
        composition.Observed.Audits.Should().BeEmpty();
        composition.Observed.Spans.Should().BeEmpty();
        HostActivity.Current.Should().BeNull();
        Activity.Current.Should().BeSameAs(incoming);
        using (var connection = Open(Path.Combine(paths.LocalRoot, WindowsSqliteEvidenceSink.PartitionName, "evidence.db")))
        {
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT request_id,session_id,task_id,origin,invocation_id,trace_id,span_id,parent_span_id,trace_flags,
                    activity_source,activity_version,activity_name,activity_kind,audit_correlation_id,approval_id,envelope
                FROM application_log_events;
                """;
            using (var rows = command.ExecuteReader())
            {
                rows.Read().Should().BeTrue();
                for (var column = 0; column < 15; column++)
                {
                    rows.IsDBNull(column).Should().BeTrue();
                }
                var persisted = JsonSerializer.Deserialize<DiagnosticEnvelope>(rows.GetString(15));
                persisted.Should().NotBeNull();
                persisted!.Properties["kora.bootstrap"].Should().Be(new EvidenceValue(EvidenceValueKind.Boolean, "false"));
                persisted.Properties["kora.evidence.gap"].Should().Be(new EvidenceValue(EvidenceValueKind.Text, "MissingHostContext"));
                rows.Read().Should().BeFalse();
            }
            foreach (var table in new[] { "security_audit_events", "activity_spans", "activity_links" })
            {
                command.CommandText = $"SELECT count(*) FROM {table};";
                command.ExecuteScalar().Should().Be(0L);
            }
        }
        (await composition.Tasks.ReadIncompleteAsync(1, TestContext.Current.CancellationToken)).Should().BeEmpty();
        new WindowsSqliteEvidenceSink(paths).Initialize();
    }

    [Theory]
    [InlineData(SecurityAuditOutcome.Requested, HostTaskState.IntentRecorded)]
    [InlineData(SecurityAuditOutcome.Succeeded, HostTaskState.DispatchRecorded)]
    public async Task Real_journal_admission_failure_attempts_independent_sink_and_never_returns_a_success_receipt(
        SecurityAuditOutcome failAt, HostTaskState expectedState)
    {
        using var paths = new OwnedStorageFixture();
        using var composition = await ComposeAsync(paths, failAuditAt: failAt);
        var invoked = false;
        var run = () => composition.Query.RunAsync(RequestOrigin.LocalUi, () =>
        {
            invoked = true;
            return Task.CompletedTask;
        }, TestContext.Current.CancellationToken);
        await run.Should().ThrowAsync<IOException>();
        invoked.Should().Be(failAt == SecurityAuditOutcome.Succeeded);
        HostActivity.Current.Should().BeNull();
        Activity.Current.Should().BeNull();
        composition.Observed.Audits.Should().Contain(row => row.Audit.Outcome == failAt);
        composition.Observed.Gaps.Should().NotBeEmpty().And.OnlyContain(gap =>
            string.Equals(gap.Sink, composition.Sink.Name, StringComparison.Ordinal));
        var request = composition.Observed.Audits.Last().Diagnostic.Host!;
        var receipt = await composition.Tasks.ReadTaskAsync(request.TaskId, TestContext.Current.CancellationToken);
        receipt!.State.Should().Be(expectedState);
        receipt.IsTerminal.Should().BeFalse();
        File.Exists(Path.Combine(paths.LocalRoot, WindowsSqliteEvidenceSink.PartitionName, "evidence.db-journal")).Should().BeFalse();
        var startup = () => new WindowsSqliteEvidenceSink(paths).Initialize();
        startup.Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData("intent", HostTaskState.Interrupted)]
    [InlineData("dispatch", HostTaskState.Unknown)]
    public async Task Killing_only_owned_composed_runner_child_recovers_with_typed_evidence_and_no_replay(
        string mode, HostTaskState recoveredState)
    {
        using var paths = new OwnedStorageFixture();
        new WindowsSqliteEvidenceSink(paths).Initialize();
        await new WindowsSqliteHostTaskStore(paths).InitializeAsync(TestContext.Current.CancellationToken);
        var marker = Path.Combine(paths.LocalRoot, "composition-ready");
        var invocation = Path.Combine(paths.LocalRoot, "query-entered");
        var start = OwnedStorageChildProcess.CreateStart(typeof(DurableStorageCompositionTests),
            nameof(Fixture_owned_composed_runner_only));
        start.Environment[RootVariable] = paths.LocalRoot;
        start.Environment[ModeVariable] = mode;
        using var child = Process.Start(start) ?? throw new InvalidOperationException("The composed fixture child could not start.");
        var output = child.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var error = child.StandardError.ReadToEndAsync(CancellationToken.None);
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(30));
            while (!File.Exists(marker))
            {
                if (child.HasExited)
                {
                    throw new InvalidOperationException($"The composed child exited before its checkpoint: {await output} {await error}");
                }
                await Task.Delay(TimeSpan.FromMilliseconds(25), deadline.Token);
            }
            child.Kill(entireProcessTree: true);
            await child.WaitForExitAsync(deadline.Token);
            var task = new HostId<TaskIdentity>(Guid.ParseExact(
                await File.ReadAllTextAsync(marker, deadline.Token), "D"));
            File.Exists(invocation).Should().Be(string.Equals(mode, "dispatch", StringComparison.Ordinal));
            var invocationBefore = File.Exists(invocation)
                ? await File.ReadAllBytesAsync(invocation, deadline.Token) : null;
            using var reopened = await ComposeAsync(paths);
            var prior = await reopened.Tasks.ReadTaskAsync(task, TestContext.Current.CancellationToken);
            prior!.State.Should().Be(string.Equals(mode, "dispatch", StringComparison.Ordinal)
                ? HostTaskState.DispatchRecorded : HostTaskState.IntentRecorded);
            var recovered = await reopened.Recovery.RecoverAsync(TestContext.Current.CancellationToken);
            recovered.Should().ContainSingle().Which.State.Should().Be(recoveredState);
            (await reopened.Tasks.ReadTaskAsync(task, TestContext.Current.CancellationToken)).Should().Be(recovered[0]);
            (await reopened.Recovery.RecoverAsync(TestContext.Current.CancellationToken)).Should().BeEmpty();
            reopened.Observed.Gaps.Should().BeEmpty();
            reopened.Observed.Audits.Should().ContainSingle().Which.Audit.ActionId.Should().Be("host.task.recovery");
            reopened.Observed.Audits[0].Audit.ReasonCode.Should().Be(recoveredState == HostTaskState.Interrupted
                ? "intent-interrupted" : "dispatch-unverified");
            if (invocationBefore is null)
            {
                File.Exists(invocation).Should().BeFalse();
            }
            else
            {
                (await File.ReadAllBytesAsync(invocation, TestContext.Current.CancellationToken)).Should().Equal(invocationBefore);
            }
            AssertLedger(paths, string.Equals(mode, "dispatch", StringComparison.Ordinal)
                ? [HostTaskState.IntentRecorded, HostTaskState.DispatchRecorded, recoveredState]
                : [HostTaskState.IntentRecorded, recoveredState]);
            new WindowsSqliteEvidenceSink(paths).Initialize();
        }
        finally
        {
            if (!child.HasExited)
            {
                child.Kill(entireProcessTree: true);
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await child.WaitForExitAsync(cleanup.Token);
            }
            await Task.WhenAll(output, error);
        }
    }

    [WindowsFact]
    public async Task Fixture_owned_composed_runner_only()
    {
        var root = Environment.GetEnvironmentVariable(RootVariable);
        if (root is null)
        {
            return;
        }
        OwnedStorageChildProcess.RequireOwnedRoot(root);
        var mode = Environment.GetEnvironmentVariable(ModeVariable);
        if (mode is not ("intent" or "dispatch"))
        {
            throw new InvalidOperationException("An unknown composed fixture child mode was requested.");
        }
        using var composition = await ComposeAsync(new ExistingPaths(root),
            pauseAfterIntent: string.Equals(mode, "intent", StringComparison.Ordinal));
        await composition.Query.RunAsync(RequestOrigin.LocalUi, async () =>
        {
            var request = HostActivity.RequireCurrent().Request;
            await File.WriteAllTextAsync(Path.Combine(root, "query-entered"), "synthetic offline query entered once",
                TestContext.Current.CancellationToken);
            await SignalAndWait(root, request.TaskId);
        }, TestContext.Current.CancellationToken);
    }

    private static async Task SignalAndWait(string root, HostId<TaskIdentity> task)
    {
        // Publish the checkpoint only after all bytes are visible, so the parent never sees a partial GUID.
        var pending = Path.Combine(root, "composition-ready.pending");
        await File.WriteAllTextAsync(pending, task.Value.ToString("D"), TestContext.Current.CancellationToken);
        File.Move(pending, Path.Combine(root, "composition-ready"));
        await Task.Delay(Timeout.InfiniteTimeSpan, TestContext.Current.CancellationToken);
    }

    private static async Task<Composition> ComposeAsync(IApplicationDataPaths paths,
        SecurityAuditOutcome? failAuditAt = null, bool pauseAfterIntent = false)
    {
        var sink = new WindowsSqliteEvidenceSink(paths);
        sink.Initialize();
        var tasks = new WindowsSqliteHostTaskStore(paths);
        await tasks.InitializeAsync(TestContext.Current.CancellationToken);
        IHostTaskStore admitted = pauseAfterIntent ? new IntentCheckpointStore(tasks, paths.LocalRoot) : tasks;
        IEvidenceSink evidence = failAuditAt is { } outcome ? new MissingJournalSink(sink, paths.LocalRoot, outcome) : sink;
        return new Composition(tasks, sink, admitted, evidence);
    }

    private static void AssertLedger(OwnedStorageFixture paths, params HostTaskState[] expected)
    {
        using var connection = Open(Path.Combine(paths.LocalRoot, "HostStorageV1", "host.db"));
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT revision,state FROM host_task_events ORDER BY revision;";
        using var rows = command.ExecuteReader();
        var index = 0;
        while (rows.Read())
        {
            index.Should().BeLessThan(expected.Length);
            rows.GetInt64(0).Should().Be(index + 1);
            rows.GetInt64(1).Should().Be((long)expected[index]);
            index++;
        }
        index.Should().Be(expected.Length);
    }

    private static void AssertEvidence(OwnedStorageFixture paths, HostRequest request, string trace, int expectedAudits)
    {
        using var connection = Open(Path.Combine(paths.LocalRoot, WindowsSqliteEvidenceSink.PartitionName, "evidence.db"));
        using var command = connection.CreateCommand();
        foreach (var table in new[] { "application_log_events", "security_audit_events", "activity_spans" })
        {
            command.CommandText = $"SELECT request_id,session_id,task_id,origin,trace_id,committed_utc,due_utc,envelope FROM {table};";
            using var rows = command.ExecuteReader();
            var count = 0;
            while (rows.Read())
            {
                rows.GetString(0).Should().Be(request.RequestId.Value.ToString("D"));
                rows.GetString(1).Should().Be(request.SessionId.Value.ToString("D"));
                rows.GetString(2).Should().Be(request.TaskId.Value.ToString("D"));
                rows.GetInt64(3).Should().Be((long)request.Origin);
                rows.GetString(4).Should().Be(trace);
                (rows.GetInt64(6) - rows.GetInt64(5)).Should().Be(TimeSpan.FromDays(
                    string.Equals(table, "security_audit_events", StringComparison.Ordinal) ? 90 : 30).Ticks);
                rows.GetString(7).Should().NotBeNullOrEmpty();
                count++;
            }
            count.Should().Be(string.Equals(table, "activity_spans", StringComparison.Ordinal) ? 9 : 2);
        }
        command.CommandText = "SELECT audit_sequence,audit_correlation_id,audit_outcome FROM security_audit_events ORDER BY audit_sequence;";
        using var audits = command.ExecuteReader();
        for (var sequence = 1; sequence <= expectedAudits; sequence++)
        {
            audits.Read().Should().BeTrue();
            audits.GetInt64(0).Should().Be(sequence);
            audits.GetString(1).Should().Be(request.RequestId.Value.ToString("D"));
            audits.GetInt64(2).Should().Be((long)(sequence == 1 ? SecurityAuditOutcome.Requested : SecurityAuditOutcome.Succeeded));
        }
        audits.Read().Should().BeFalse();
    }

    private static SqliteConnection Open(string path)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path, Mode = SqliteOpenMode.ReadWrite, Pooling = false,
        }.ToString());
        connection.Open();
        return connection;
    }

    private sealed class Composition : IDisposable
    {
        private readonly EvidenceLoggerProvider provider;
        private readonly ILoggerFactory factory;

        internal Composition(WindowsSqliteHostTaskStore tasks, WindowsSqliteEvidenceSink sink,
            IHostTaskStore admitted, IEvidenceSink evidence)
        {
            Tasks = tasks;
            Sink = sink;
            provider = new EvidenceLoggerProvider([evidence, Observed], Observed);
            factory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Trace).AddProvider(provider));
            var coordinator = new HostTaskCoordinator(admitted);
            var audit = new LoggerSecurityAuditLog(factory.CreateLogger<LoggerSecurityAuditLog>());
            Query = new DurableVersionQuery(coordinator, audit, factory.CreateLogger<DurableVersionQuery>());
            Recovery = new DurableHostRecovery(tasks, new HostTaskCoordinator(tasks), audit,
                factory.CreateLogger<DurableHostRecovery>());
        }

        internal WindowsSqliteHostTaskStore Tasks { get; }
        internal WindowsSqliteEvidenceSink Sink { get; }
        internal ObservedEvidence Observed { get; } = new();
        internal DurableVersionQuery Query { get; }
        internal DurableHostRecovery Recovery { get; }

        internal ILogger CreateDiagnosticLogger() => factory.CreateLogger("Kora.Tests.Contextless");

        public void Dispose()
        {
            factory.Dispose();
            provider.Dispose();
        }
    }

    private sealed class ObservedEvidence : IEvidenceSink, IEvidenceGapReporter
    {
        public string Name => "independent-fixture";
        internal List<DiagnosticEnvelope> Diagnostics { get; } = [];
        internal List<AuditEnvelope> Audits { get; } = [];
        internal List<CompletedActivityEnvelope> Spans { get; } = [];
        internal List<EvidenceGap> Gaps { get; } = [];
        public void WriteDiagnostic(DiagnosticEnvelope envelope) => Diagnostics.Add(envelope);
        public void WriteAudit(AuditEnvelope envelope) => Audits.Add(envelope);
        public void WriteActivity(CompletedActivityEnvelope envelope) => Spans.Add(envelope);
        public void Report(EvidenceGap gap) => Gaps.Add(gap);
    }

    private sealed class MissingJournalSink(WindowsSqliteEvidenceSink sink, string root, SecurityAuditOutcome failAt) : IEvidenceSink
    {
        public string Name => sink.Name;
        public void WriteDiagnostic(DiagnosticEnvelope envelope) => sink.WriteDiagnostic(envelope);
        public void WriteActivity(CompletedActivityEnvelope envelope) => sink.WriteActivity(envelope);
        public void WriteAudit(AuditEnvelope envelope)
        {
            if (envelope.Audit.Outcome == failAt)
            {
                File.Delete(Path.Combine(root, WindowsSqliteEvidenceSink.PartitionName, "evidence.db-journal"));
            }
            sink.WriteAudit(envelope);
        }
    }

    private sealed class IntentCheckpointStore(WindowsSqliteHostTaskStore store, string root) : IHostTaskStore
    {
        public async ValueTask CommitAsync(HostTaskRecord record, long expectedRevision, CancellationToken cancellationToken)
        {
            await store.CommitAsync(record, expectedRevision, cancellationToken);
            if (record.State == HostTaskState.IntentRecorded)
            {
                await SignalAndWait(root, record.Request.TaskId);
            }
        }

        public ValueTask<IReadOnlyList<HostTaskRecord>> ReadIncompleteAsync(int limit, CancellationToken cancellationToken) =>
            store.ReadIncompleteAsync(limit, cancellationToken);
    }

    private sealed class ExistingPaths(string root) : IApplicationDataPaths
    {
        public string LocalRoot { get; } = root;
        public string RoamingRoot => Path.Combine(LocalRoot, "UnusedRoaming");
    }
}
