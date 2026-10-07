using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;

using AwesomeAssertions;

using Kora.Application.Diagnostics;
using Kora.Core.Auditing;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Windows.IntegrationTests.Audio;
using Kora.Windows.Storage;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests.Storage;

[Collection(nameof(DurableStorageCompositionTestGroup))]
public sealed class WindowsSqliteDiagnosticRetentionTests
{
    private static readonly string[] AuthorityPartitions = ["HostStorageV1", "InteractionStorageV1"];
    [WindowsFact]
    public async Task Exact_due_boundary_uses_committed_policy_not_observation_or_reads_and_preserves_all_authority()
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.InitializeAsync();
        var grant = await fixture.GrantAsync("perpetual");
        await WindowsSqliteSessionWorkspaceTests.FinishAsync(fixture, HostTaskState.Succeeded);
        var authority = AuthorityHashes(fixture.Paths);
        var clock = new Clock();
        var committed = clock.Now;
        var sink = new WindowsSqliteEvidenceSink(fixture.Paths, new EvidenceRetentionPolicy(30), clock);
        using var producer = HostActivity.BeginRoot(fixture.Request, HostActivityLayer.Application, HostOperation.Request);
        var dueLog = WriteLog(sink, producer, clock);
        var dueSpan = WriteSpan(sink, producer, clock, links: 32);
        var audit = new SecurityAuditEvent(Guid.NewGuid(), SecurityAuditCategory.ApplicationExecution,
            "fixture.retention", SecurityAuditOutcome.Succeeded, SecurityAuditInitiator.System, "application.current");
        using (var audited = HostActivity.BeginAudit(fixture.Request, audit))
        {
            sink.WriteAudit(new(Diagnostic(audited, clock), audit));
        }
        clock.Now = clock.Now.AddTicks(1);
        var retained = WriteLog(sink, producer, clock);
        clock.Now = committed.AddDays(EvidenceRetentionPolicy.DiagnosticDays);
        (await Prune(sink, clock)).Should().Be(new DiagnosticRetentionBatch(1, 1, 32, false));
        using var viewer = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Desktop, HostOperation.Evidence);
        var service = Query(sink, clock);
        foreach (var reference in new[] { new EvidenceReference(EvidenceSource.Log, dueLog),
            new EvidenceReference(EvidenceSource.Span, dueSpan), new EvidenceReference(EvidenceSource.Link, dueSpan, 0) })
        {
            (await service.QueryAsync(new() { Record = reference }, null, Token)).Status.Should().Be(EvidencePageStatus.MissingOrRemoved);
        }
        var beforeRead = EvidenceHash(fixture.Paths);
        var page = await service.QueryAsync(new() { Record = new(EvidenceSource.Log, retained) }, null, Token);
        page.Records.Should().ContainSingle().Which.DueUtc.Should().Be(clock.Now.AddTicks(1));
        page.Records[0].Retention.Should().Be(EvidenceSegmentStatus.Present);
        EvidenceHash(fixture.Paths).Should().Equal(beforeRead);
        clock.Now = committed.AddDays(400);
        var restarted = new WindowsSqliteEvidenceSink(fixture.Paths, new EvidenceRetentionPolicy(365), clock);
        (await Prune(restarted, clock)).Should().Be(new DiagnosticRetentionBatch(1, 0, 0, false));
        var audits = await Query(restarted, clock).QueryAsync(new() { Source = EvidenceSource.Audit }, null, Token);
        audits.Records.Should().ContainSingle().Which.Retention.Should().Be(EvidenceSegmentStatus.ExpiredButPresent);
        audits.Records[0].DueUtc.Should().Be(committed.AddDays(30));
        audits.Records[0].AuditSequence.Should().Be(1);
        audits.Records[0].Audit.Should().BeEquivalentTo(audit);
        AuthorityHashes(fixture.Paths).Should().Equal(authority);
        (await fixture.Store.ReadGrantsAsync(fixture.Token)).Single(record => record.Id == grant.Id).Should().Be(grant);
        restarted.Initialize();
    }

    [WindowsFact]
    public async Task Batch_limits_include_all_32_owned_links_and_restart_continues_without_rewriting_receipts()
    {
        using var paths = new OwnedStorageFixture();
        using var listener = Listen();
        var clock = new Clock();
        var sink = new WindowsSqliteEvidenceSink(paths, timeProvider: clock);
        using var producer = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.HostSystem), HostActivityLayer.Windows, HostOperation.Startup);
        for (var index = 0; index <= WindowsSqliteDiagnosticRetention.MaximumLogs; index++)
        {
            WriteLog(sink, producer, clock);
        }
        for (var index = 0; index <= WindowsSqliteDiagnosticRetention.MaximumSpans; index++)
        {
            WriteSpan(sink, producer, clock, 32);
        }
        clock.Now = clock.Now.AddDays(30);
        (await Prune(sink, clock)).Should().Be(new DiagnosticRetentionBatch(128, 32, 1024, true));
        Count(paths, "application_log_events").Should().Be(1);
        Count(paths, "activity_spans").Should().Be(1);
        Count(paths, "activity_links").Should().Be(32);
        var restarted = new WindowsSqliteEvidenceSink(paths, timeProvider: clock);
        restarted.Initialize();
        (await Prune(restarted, clock)).Should().Be(new DiagnosticRetentionBatch(1, 1, 32, false));
        (await Prune(restarted, clock)).Should().Be(new DiagnosticRetentionBatch(0, 0, 0, false));
        restarted.Initialize();
    }

    [WindowsFact]
    public async Task Retained_child_log_audit_and_link_citations_truthfully_report_removed_parent_and_expired_backlog()
    {
        using var paths = new OwnedStorageFixture();
        using var listener = Listen();
        var clock = new Clock();
        var sink = new WindowsSqliteEvidenceSink(paths, timeProvider: clock);
        var request = HostRequest.Create(RequestOrigin.HostSystem);
        var audit = new SecurityAuditEvent(Guid.NewGuid(), SecurityAuditCategory.ApplicationExecution,
            "fixture.retention.citation", SecurityAuditOutcome.Succeeded, SecurityAuditInitiator.System, "application.current");
        HostId<EvidenceIdentity> parentId;
        TraceSnapshot parent;
        using (var producer = HostActivity.BeginAudit(request, audit))
        {
            parent = TraceSnapshot.Capture(producer.Activity)!;
            parentId = WriteSpan(sink, producer, clock, 0);
            sink.WriteAudit(new(Diagnostic(producer, clock), audit));
        }
        clock.Now = clock.Now.AddDays(30);
        HostId<EvidenceIdentity> childId;
        using (var root = HostActivity.BeginRoot(request, HostActivityLayer.Windows, HostOperation.Request))
        {
            var trace = TraceSnapshot.Capture(root.Activity)! with { TraceId = parent.TraceId, ParentSpanId = parent.SpanId };
            childId = new(Guid.NewGuid());
            sink.WriteActivity(new(childId, trace, request, clock.Now, clock.Now, HostOperationOutcome.Completed,
                [new(parent.TraceId, parent.SpanId)]));
        }
        using (var producer = HostActivity.BeginRoot(request, HostActivityLayer.Windows, HostOperation.Request))
        {
            WriteLog(sink, producer, clock);
        }
        using var viewer = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Desktop, HostOperation.Evidence);
        var service = Query(sink, clock);
        var before = await service.QueryAsync(new() { Record = new(EvidenceSource.Span, childId) }, null, Token);
        before.Records[0].RelatedSegments.Should().OnlyContain(segment => segment.Status == EvidenceSegmentStatus.ExpiredButPresent
            && segment.Record!.Id == parentId);
        (await Prune(sink, clock)).Spans.Should().Be(1);
        var hash = EvidenceHash(paths);
        foreach (var source in new[] { EvidenceSource.Span, EvidenceSource.Link })
        {
            var result = await service.QueryAsync(new() { Record = new(source, childId, source == EvidenceSource.Link ? 0 : null) },
                null, Token);
            result.Records.Should().ContainSingle().Which.Retention.Should().Be(EvidenceSegmentStatus.Present);
            result.Records[0].RelatedSegments.Should().OnlyContain(segment => segment.Status == EvidenceSegmentStatus.MissingOrRemoved
                && segment.Record == null);
        }
        var audits = await service.QueryAsync(new() { Source = EvidenceSource.Audit }, null, Token);
        audits.Records.Should().ContainSingle().Which.RelatedSegments.Should().ContainSingle()
            .Which.Status.Should().Be(EvidenceSegmentStatus.MissingOrRemoved);
        audits.Records[0].Audit.Should().BeEquivalentTo(audit);
        EvidenceHash(paths).Should().Equal(hash);
    }

    [Theory]
    [InlineData(EvidenceSource.Log)]
    [InlineData(EvidenceSource.Span)]
    [InlineData(EvidenceSource.Link)]
    public async Task Cursor_rejects_removed_or_reused_ceiling_instead_of_returning_new_rows_in_old_snapshot(EvidenceSource source)
    {
        using var paths = new OwnedStorageFixture();
        using var listener = Listen();
        var clock = new Clock();
        var sink = new WindowsSqliteEvidenceSink(paths, timeProvider: clock);
        using var producer = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.HostSystem), HostActivityLayer.Windows, HostOperation.Startup);
        WriteSource(sink, producer, clock, source);
        WriteSource(sink, producer, clock, source);
        using var viewer = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Desktop, HostOperation.Evidence);
        var query = new EvidenceQuery { Source = source, Limit = 1 };
        var service = Query(sink, clock);
        var first = await service.QueryAsync(query, null, Token);
        first.Cursor.Should().NotBeNull();
        clock.Now = clock.Now.AddDays(30);
        await Prune(sink, clock);
        // Keep the viewer's signed cursor unexpired while the persisted rows are gone.
        clock.Now = clock.Now.AddDays(-30);
        var removed = () => service.QueryAsync(query, first.Cursor, Token).AsTask();
        await removed.Should().ThrowAsync<InvalidDataException>().WithMessage("*snapshot changed*");
        using (var replacement = HostActivity.BeginRoot(producer.Request, HostActivityLayer.Windows, HostOperation.Request))
        {
            WriteSource(sink, replacement, clock, source);
            WriteSource(sink, replacement, clock, source);
        }
        await removed.Should().ThrowAsync<InvalidDataException>().WithMessage("*snapshot changed*");
        (await service.QueryAsync(query, null, Token)).Records.Should().ContainSingle();
    }

    [WindowsFact]
    public async Task Completion_diagnostic_reenters_actual_sink_after_lease_release_and_late_cancellation_keeps_commit_truth()
    {
        using var paths = new OwnedStorageFixture();
        using var listener = Listen();
        var clock = new Clock();
        var sink = new WindowsSqliteEvidenceSink(paths, timeProvider: clock);
        using var host = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.HostSystem), HostActivityLayer.Windows, HostOperation.Startup);
        WriteLog(sink, host, clock);
        clock.Now = clock.Now.AddDays(30);
        using var cancellation = new CancellationTokenSource();
        var logger = new CompletionLogger(sink, clock, cancellation);
        var service = new WindowsSqliteDiagnosticRetention(sink, clock, logger);
        (await service.RunAsync(cancellation.Token)).Should().Be(new DiagnosticRetentionBatch(1, 0, 0, false));
        logger.Request.Should().Be(host.Request);
        logger.Trace!.ParentSpanId.Should().Be(host.Activity!.SpanId.ToHexString());
        logger.Trace.Source.Should().Be("Kora.Windows");
        logger.Trace.Name.Should().Be("retention.run");
        logger.Status.Should().Be(ActivityStatusCode.Ok);
        cancellation.IsCancellationRequested.Should().BeTrue();
        Count(paths, "application_log_events").Should().Be(1);
        Count(paths, "security_audit_events").Should().Be(0);
        sink.Initialize();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cancellation_before_commit_or_fault_rolls_back_entire_batch_and_releases_owned_resources(bool cancel)
    {
        using var paths = new OwnedStorageFixture();
        using var listener = Listen();
        var clock = new Clock();
        var sink = new WindowsSqliteEvidenceSink(paths, timeProvider: clock);
        using var host = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.HostSystem), HostActivityLayer.Windows, HostOperation.Startup);
        WriteLog(sink, host, clock);
        WriteSpan(sink, host, clock, 32);
        clock.Now = clock.Now.AddDays(30);
        using var cancellation = new CancellationTokenSource();
        var checkpoint = new SqliteTransactionCheckpoint
        {
            Write = SqliteTransactionCheckpoint.SpillPages,
            Commit = (connection, _) =>
            {
                SqliteTransactionCheckpoint.FlushPages(connection);
                if (cancel) { cancellation.Cancel(); }
                else { throw new IOException("Owned retention fault."); }
            },
        };
        var failing = new WindowsSqliteEvidenceSink(paths, null, clock, checkpoint);
        var run = () => Prune(failing, clock, cancellation.Token);
        if (cancel) { await run.Should().ThrowAsync<OperationCanceledException>(); }
        else { await run.Should().ThrowAsync<IOException>().WithMessage("Owned retention fault."); }
        new WindowsSqliteEvidenceSink(paths).Initialize();
        Count(paths, "application_log_events").Should().Be(1);
        Count(paths, "activity_spans").Should().Be(1);
        Count(paths, "activity_links").Should().Be(32);
        (await Prune(sink, clock)).Should().Be(new DiagnosticRetentionBatch(1, 1, 32, false));
    }

    [WindowsFact]
    public async Task Actual_writer_and_reader_serialize_with_retention_and_cancelled_waiter_never_mutates()
    {
        using var paths = new OwnedStorageFixture();
        using var listener = Listen();
        var clock = new Clock();
        var sink = new WindowsSqliteEvidenceSink(paths, timeProvider: clock);
        using var host = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.HostSystem), HostActivityLayer.Windows, HostOperation.Startup);
        WriteLog(sink, host, clock);
        clock.Now = clock.Now.AddDays(30);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        var checkpoint = new SqliteTransactionCheckpoint
        {
            Commit = (_, _) =>
            {
                entered.SetResult();
                release.Wait(Token);
            },
        };
        var pruning = Prune(new WindowsSqliteEvidenceSink(paths, null, clock, checkpoint), clock);
        await entered.Task.WaitAsync(Token);
        using var cancellation = new CancellationTokenSource();
        var waiter = Prune(sink, clock, cancellation.Token);
        cancellation.Cancel();
        var awaitWaiter = () => waiter;
        await awaitWaiter.Should().ThrowAsync<OperationCanceledException>();
        var writer = Task.Run(() =>
        {
            using var producer = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.HostSystem), HostActivityLayer.Windows, HostOperation.Request);
            return WriteLog(sink, producer, clock);
        }, Token);
        var reader = Task.Run(async () =>
        {
            using var viewer = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Desktop, HostOperation.Evidence);
            return await Query(sink, clock).QueryAsync(new() { Source = EvidenceSource.Log }, null, Token);
        }, Token);
        try
        {
            release.Set();
            (await pruning).Logs.Should().Be(1);
            var id = await writer;
            var page = await reader;
            page.Records.Should().OnlyContain(record => record.Reference.Id == id && record.Retention == EvidenceSegmentStatus.Present);
            Count(paths, "application_log_events").Should().Be(1);
            new WindowsSqliteEvidenceSink(paths).Initialize();
        }
        finally { release.Set(); }
    }

    [Theory]
    [InlineData("missing-database")]
    [InlineData("missing-journal")]
    [InlineData("missing-lease")]
    [InlineData("schema")]
    [InlineData("due")]
    [InlineData("envelope")]
    [InlineData("link")]
    [InlineData("acl")]
    public async Task Invalid_existing_storage_fails_explicitly_without_replacement_repair_or_pruning(string fault)
    {
        using var paths = new OwnedStorageFixture();
        using var listener = Listen();
        var clock = new Clock();
        var sink = new WindowsSqliteEvidenceSink(paths, timeProvider: clock);
        using var host = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.HostSystem), HostActivityLayer.Windows, HostOperation.Startup);
        WriteLog(sink, host, clock);
        WriteSpan(sink, host, clock, 1);
        clock.Now = clock.Now.AddDays(30);
        var database = DatabasePath(paths);
        switch (fault)
        {
            case "missing-database": File.Delete(database); break;
            case "missing-journal": File.Delete(database + "-journal"); break;
            case "missing-lease": File.Delete(Path.Combine(Path.GetDirectoryName(database)!, "operation.lock")); break;
            case "schema": Mutate(paths, "DROP INDEX ix_activity_links_due;"); break;
            case "due": Mutate(paths, "UPDATE application_log_events SET due_utc=due_utc+1;"); break;
            case "envelope": Mutate(paths, "UPDATE application_log_events SET envelope='{}';"); break;
            case "link": Mutate(paths, "DELETE FROM activity_links;"); break;
            case "acl":
                var info = new FileInfo(database);
                var acl = info.GetAccessControl();
                acl.AddAccessRule(new(new SecurityIdentifier(WellKnownSidType.WorldSid, null), FileSystemRights.Read, AccessControlType.Allow));
                info.SetAccessControl(acl);
                break;
        }
        var files = Directory.GetFiles(Path.GetDirectoryName(database)!).Order(StringComparer.Ordinal).ToArray();
        var before = files.Select(path => SHA256.HashData(File.ReadAllBytes(path))).ToArray();
        var run = () => Prune(sink, clock);
        if (string.Equals(fault, "acl", StringComparison.Ordinal)) { await run.Should().ThrowAsync<UnauthorizedAccessException>(); }
        else { await run.Should().ThrowAsync<InvalidDataException>(); }
        Directory.GetFiles(Path.GetDirectoryName(database)!).Order(StringComparer.Ordinal).Should().Equal(files);
        for (var index = 0; index < files.Length; index++)
        {
            SHA256.HashData(File.ReadAllBytes(files[index])).Should().Equal(before[index]);
        }
    }

    [WindowsFact]
    public async Task Missing_partition_cancelled_call_and_unadmitted_requests_create_nothing()
    {
        using var paths = new OwnedStorageFixture();
        using var listener = Listen();
        var clock = new Clock();
        var sink = new WindowsSqliteEvidenceSink(paths);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var cancelled = () => Prune(sink, clock, cancellation.Token);
        await cancelled.Should().ThrowAsync<OperationCanceledException>();
        var missing = () => Prune(sink, clock);
        await missing.Should().ThrowAsync<FileNotFoundException>();
        var service = Retention(sink, clock);
        var unadmitted = () => service.RunAsync(Token).AsTask();
        await unadmitted.Should().ThrowAsync<InvalidOperationException>();
        using (var ui = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Desktop, HostOperation.Evidence))
        {
            await unadmitted.Should().ThrowAsync<InvalidOperationException>();
        }
        using (var stopped = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.HostSystem), HostActivityLayer.Windows, HostOperation.Startup))
        {
            stopped.Activity!.Stop();
            await unadmitted.Should().ThrowAsync<InvalidOperationException>();
        }
        Directory.Exists(Path.GetDirectoryName(DatabasePath(paths))).Should().BeFalse();
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static WindowsSqliteDiagnosticRetention Retention(WindowsSqliteEvidenceSink sink, TimeProvider clock) =>
        new(sink, clock, NullLogger<WindowsSqliteDiagnosticRetention>.Instance);
    private static async Task<DiagnosticRetentionBatch> Prune(WindowsSqliteEvidenceSink sink, TimeProvider clock,
        CancellationToken? token = null)
    {
        using var host = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.HostSystem), HostActivityLayer.Windows, HostOperation.Startup);
        return await Retention(sink, clock).RunAsync(token ?? Token);
    }
    private static DurableEvidenceQuery Query(WindowsSqliteEvidenceSink sink, TimeProvider clock) =>
        new(new WindowsSqliteEvidenceReader(sink), new Access(), clock, NullLogger<DurableEvidenceQuery>.Instance);
    private static HostId<EvidenceIdentity> WriteLog(WindowsSqliteEvidenceSink sink, HostActivity host, Clock clock)
    {
        var envelope = Diagnostic(host, clock);
        sink.WriteDiagnostic(envelope);
        return envelope.EvidenceId;
    }
    private static void WriteSource(WindowsSqliteEvidenceSink sink, HostActivity host, Clock clock, EvidenceSource source)
    {
        if (source == EvidenceSource.Log) { WriteLog(sink, host, clock); }
        else { WriteSpan(sink, host, clock, 1); }
    }
    private static DiagnosticEnvelope Diagnostic(HostActivity host, Clock clock) => new(1, new(Guid.NewGuid()),
        clock.Now.AddYears(-10), 1, "RetentionFixture", "Information", "Kora.Tests", "Owned retention fixture.",
        new Dictionary<string, EvidenceValue>(StringComparer.Ordinal), [], TraceSnapshot.Capture(host.Activity),
        host.Request, null, host.CorrelationId, host.ApprovalId);
    private static HostId<EvidenceIdentity> WriteSpan(WindowsSqliteEvidenceSink sink, HostActivity host, Clock clock, int links)
    {
        var id = new HostId<EvidenceIdentity>(Guid.NewGuid());
        sink.WriteActivity(new(id, TraceSnapshot.Capture(host.Activity)!, host.Request, clock.Now, clock.Now,
            HostOperationOutcome.Completed, Enumerable.Range(0, links).Select(_ =>
                new ActivityLinkEnvelope(ActivityTraceId.CreateRandom().ToHexString(), ActivitySpanId.CreateRandom().ToHexString())).ToArray()));
        return id;
    }
    private static string DatabasePath(OwnedStorageFixture paths) =>
        Path.Combine(paths.LocalRoot, WindowsSqliteEvidenceSink.PartitionName, "evidence.db");
    private static SqliteConnection Open(OwnedStorageFixture paths)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath(paths), Mode = SqliteOpenMode.ReadWrite, Pooling = false,
        }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA locking_mode=EXCLUSIVE; PRAGMA journal_mode=PERSIST; PRAGMA synchronous=FULL;";
        command.ExecuteNonQuery();
        return connection;
    }
    private static long Count(OwnedStorageFixture paths, string table)
    {
        using var connection = Open(paths);
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT count(*) FROM {table};";
        return (long)command.ExecuteScalar()!;
    }
    private static void Mutate(OwnedStorageFixture paths, string sql)
    {
        using var connection = Open(paths);
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
    private static byte[] EvidenceHash(OwnedStorageFixture paths) =>
        SHA256.HashData(File.ReadAllBytes(DatabasePath(paths)).Concat(File.ReadAllBytes(DatabasePath(paths) + "-journal")).ToArray());
    private static byte[] AuthorityHashes(OwnedStorageFixture paths) => SHA256.HashData(
        AuthorityPartitions.SelectMany(partition =>
            Directory.GetFiles(Path.Combine(paths.LocalRoot, partition)).Order(StringComparer.Ordinal)
                .SelectMany(File.ReadAllBytes)).ToArray());
    private static ActivityListener Listen()
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
        };
        ActivitySource.AddActivityListener(listener);
        return listener;
    }
    private sealed class Access : IEvidenceQueryAccess { public bool CanInspect => true; }
    private sealed class CompletionLogger(WindowsSqliteEvidenceSink sink, Clock clock, CancellationTokenSource cancellation)
        : ILogger<WindowsSqliteDiagnosticRetention>
    {
        internal HostRequest? Request { get; private set; }
        internal TraceSnapshot? Trace { get; private set; }
        internal ActivityStatusCode Status { get; private set; }
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            eventId.Id.Should().Be(208);
            var current = HostActivity.RequireCurrent();
            Request = current.Request;
            Trace = TraceSnapshot.Capture(current.Activity);
            Status = current.Activity!.Status;
            sink.WriteDiagnostic(Diagnostic(current, clock));
            cancellation.Cancel();
        }
    }
    private sealed class Clock : TimeProvider
    {
        internal DateTimeOffset Now { get; set; } = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
