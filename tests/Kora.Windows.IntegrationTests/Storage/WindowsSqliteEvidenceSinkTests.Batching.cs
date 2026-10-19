using AwesomeAssertions;
using System.Diagnostics.Metrics;
using Kora.Core.Auditing;
using Kora.Core.Configuration;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Windows.Storage;
using Microsoft.Data.Sqlite;

namespace Kora.Windows.IntegrationTests.Storage;

public sealed partial class WindowsSqliteEvidenceSinkTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Audit_phase_timing_observes_only_renderer_audits_and_preserves_checked_durability(bool renderer)
    {
        using var paths = new OwnedStorageFixture();
        using var listener = Listen();
        var checkpoint = new BatchCheckpoint();
        var sink = new WindowsSqliteEvidenceSink(paths, null, null, checkpoint);
        sink.Initialize();
        var phases = new List<PresenceSwitchPhase>();
        using var timing = new MeterListener();
        var instrument = PresenceSwitchTiming.Instrument;
        timing.InstrumentPublished = (published, observer) =>
        {
            if (ReferenceEquals(published, instrument)) { observer.EnableMeasurementEvents(published); }
        };
        timing.SetMeasurementEventCallback<long>((_, _, tags, _) =>
        {
            if (tags[0].Value is PresenceSwitchPhase phase) { phases.Add(phase); }
        });
        timing.Start();
        var audit = renderer
            ? new SecurityAuditEvent(Guid.NewGuid(), SecurityAuditCategory.ConfigurationWrite,
                AppearanceOptionRegistry.Get(AppearanceOption.NativePresenceRenderer).AuditAction,
                SecurityAuditOutcome.Requested, SecurityAuditInitiator.LocalUser, "preferences.device-local")
            : Audit();
        using (var host = HostActivity.BeginAudit(Request(), audit))
        {
            sink.WriteAudit(new(Diagnostic(host), audit));
            sink.WriteAudit(new(Diagnostic(host), audit.WithOutcome(SecurityAuditOutcome.Succeeded)));
        }
        checkpoint.Writes.Should().Be(2);
        checkpoint.Commits.Should().Be(2);
        if (renderer)
        {
            phases.Count(phase => phase == PresenceSwitchPhase.SqliteAuditCommitReturned).Should().Be(2);
            phases.Should().NotContain(PresenceSwitchPhase.SqliteAuditHistoryStarted,
                "this sink already trusts its own checked committed digest");
            var first = phases.Take(phases.Count / 2).ToArray();
            phases.Skip(first.Length).Should().Equal(first);
            first.Should().ContainInOrder(PresenceSwitchPhase.SqliteAuditAdmissionStarted,
                PresenceSwitchPhase.SqliteAuditAdmissionReturned, PresenceSwitchPhase.SqliteAuditLeaseStarted,
                PresenceSwitchPhase.SqliteAuditLeaseReturned, PresenceSwitchPhase.SqliteAuditOpenStarted,
                PresenceSwitchPhase.SqliteAuditConnectionStarted, PresenceSwitchPhase.SqliteAuditConnectionReturned,
                PresenceSwitchPhase.SqliteAuditSchemaStarted, PresenceSwitchPhase.SqliteAuditSchemaReturned,
                PresenceSwitchPhase.SqliteAuditIntegrityStarted, PresenceSwitchPhase.SqliteAuditIntegrityReturned,
                PresenceSwitchPhase.SqliteAuditDigestBeforeStarted, PresenceSwitchPhase.SqliteAuditDigestBeforeReturned,
                PresenceSwitchPhase.SqliteAuditOpenReturned, PresenceSwitchPhase.SqliteAuditBeginTransactionStarted,
                PresenceSwitchPhase.SqliteAuditBeginTransactionReturned, PresenceSwitchPhase.SqliteAuditInsertStarted,
                PresenceSwitchPhase.SqliteAuditInsertReturned, PresenceSwitchPhase.SqliteAuditFileCheckStarted,
                PresenceSwitchPhase.SqliteAuditFileCheckReturned, PresenceSwitchPhase.SqliteAuditCommitStarted,
                PresenceSwitchPhase.SqliteAuditCommitReturned, PresenceSwitchPhase.SqliteAuditDigestAfterStarted,
                PresenceSwitchPhase.SqliteAuditDigestAfterReturned);
        }
        else { phases.Should().BeEmpty(); }
        var reopened = new WindowsSqliteEvidenceSink(paths);
        using var lease = reopened.AcquireReadLease(TestContext.Current.CancellationToken);
        using var connection = reopened.OpenReadOnly(TestContext.Current.CancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT count(*) FROM security_audit_events;";
        command.ExecuteScalar().Should().Be(2L);
    }

    [Fact]
    public void Mixed_batch_preserves_emission_snapshots_sessions_and_links_in_one_checked_commit()
    {
        using var paths = new OwnedStorageFixture();
        using var listener = Listen();
        var checkpoint = new BatchCheckpoint();
        var sink = new WindowsSqliteEvidenceSink(paths, null, null, checkpoint);
        sink.Initialize();
        var requests = new[] { Request(), Request() };
        var writes = new List<IPreparedEvidenceWrite>();
        CompletedActivityEnvelope? activity = null;
        foreach (var request in requests)
        {
            using var host = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request);
            var properties = new Dictionary<string, EvidenceValue>(StringComparer.Ordinal)
            {
                ["Count"] = new(EvidenceValueKind.WholeNumber, "1"),
            };
            writes.Add(sink.PrepareDiagnosticWrite(Diagnostic(host) with { Properties = properties }));
            properties["Count"] = new(EvidenceValueKind.WholeNumber, "999");
            if (activity is null)
            {
                var trace = TraceSnapshot.Capture(host.Activity)!;
                var links = new List<ActivityLinkEnvelope> { new(trace.TraceId, "0123456789abcdef") };
                activity = new(new(Guid.NewGuid()), trace, host.Request, DateTimeOffset.UtcNow,
                    DateTimeOffset.UtcNow, HostOperationOutcome.Completed, links);
                writes.Add(sink.PrepareActivityWrite(activity));
                links.Clear();
            }
            host.Complete(HostOperationOutcome.Completed);
        }
        HostActivity.Current.Should().BeNull();
        sink.WriteBatch(writes);
        checkpoint.Writes.Should().Be(1);
        checkpoint.Commits.Should().Be(1);
        using var lease = sink.AcquireReadLease(TestContext.Current.CancellationToken);
        using var connection = sink.OpenReadOnly(TestContext.Current.CancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT envelope FROM application_log_events ORDER BY rowid;";
        using (var rows = command.ExecuteReader())
        {
            var index = 0;
            while (rows.Read())
            {
                var diagnostic = WindowsSqliteEvidenceSink.DecodeDiagnostic(rows.GetString(0));
                diagnostic.Host.Should().Be(requests[index++]);
                diagnostic.Properties["Count"].CanonicalValue.Should().Be("1");
            }
            index.Should().Be(2);
        }
        command.CommandText = "SELECT envelope FROM activity_spans;";
        var persisted = WindowsSqliteEvidenceSink.DecodeActivity((string)command.ExecuteScalar()!);
        persisted.Host.Should().Be(requests[0]);
        persisted.Trace.Should().Be(activity!.Trace);
        persisted.Links.Should().ContainSingle().Which.SpanId.Should().Be("0123456789abcdef");
        command.CommandText = "SELECT count(*) FROM activity_links;";
        command.ExecuteScalar().Should().Be(1L);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Failed_batch_rolls_back_every_member_and_releases_storage(bool checkpointFailure)
    {
        using var paths = new OwnedStorageFixture();
        var checkpoint = new BatchCheckpoint { FailCommit = checkpointFailure };
        var sink = new WindowsSqliteEvidenceSink(paths, null, null, checkpoint);
        sink.Initialize();
        var first = sink.PrepareDiagnosticWrite(Bootstrap());
        var second = checkpointFailure ? sink.PrepareDiagnosticWrite(Bootstrap()) : first;
        var before = File.ReadAllBytes(DatabasePath(paths));
        var write = () => sink.WriteBatch([first, second]);
        write.Should().Throw<IOException>();
        File.ReadAllBytes(DatabasePath(paths)).Should().Equal(before);
        checkpoint.Writes.Should().Be(1);
        checkpoint.Commits.Should().Be(checkpointFailure ? 1 : 0);
        var reopened = new WindowsSqliteEvidenceSink(paths);
        reopened.Initialize();
        using var lease = reopened.AcquireReadLease(TestContext.Current.CancellationToken);
        using var connection = reopened.OpenReadOnly(TestContext.Current.CancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT count(*) FROM application_log_events;";
        command.ExecuteScalar().Should().Be(0L);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Batch_rejects_empty_oversized_foreign_and_forged_snapshots_before_writing(int invalid)
    {
        using var paths = new OwnedStorageFixture();
        var sink = new WindowsSqliteEvidenceSink(paths);
        sink.Initialize();
        var prepared = sink.PrepareDiagnosticWrite(Bootstrap());
        IReadOnlyList<IPreparedEvidenceWrite> writes = invalid switch
        {
            0 => [],
            1 => Enumerable.Repeat(prepared, IBatchedEvidenceSink.MaximumBatchSize + 1).ToArray(),
            2 => [new WindowsSqliteEvidenceSink(paths).PrepareDiagnosticWrite(Bootstrap())],
            3 => [new ForgedWrite()],
            _ => throw new ArgumentOutOfRangeException(nameof(invalid)),
        };
        var before = File.ReadAllBytes(DatabasePath(paths));
        var write = () => sink.WriteBatch(writes);
        write.Should().Throw<ArgumentException>();
        File.ReadAllBytes(DatabasePath(paths)).Should().Equal(before);
    }

    [Fact]
    public void Batch_still_rejects_changed_persisted_history_before_any_member_is_written()
    {
        using var paths = new OwnedStorageFixture();
        var checkpoint = new BatchCheckpoint();
        var sink = new WindowsSqliteEvidenceSink(paths, null, null, checkpoint);
        sink.WriteDiagnostic(Bootstrap());
        var first = sink.PrepareDiagnosticWrite(Bootstrap());
        var second = sink.PrepareDiagnosticWrite(Bootstrap());
        Mutate(paths, "UPDATE application_log_events SET event_name='Rewritten';");
        var before = File.ReadAllBytes(DatabasePath(paths));
        var write = () => sink.WriteBatch([first, second]);
        write.Should().Throw<IOException>().WithInnerException<InvalidDataException>();
        checkpoint.Writes.Should().Be(1, "the changed store must be rejected before the batch transaction");
        File.ReadAllBytes(DatabasePath(paths)).Should().Equal(before);
    }

    private sealed class BatchCheckpoint : ISqliteTransactionCheckpoint
    {
        public int Writes { get; private set; }
        public int Commits { get; private set; }
        public bool FailCommit { get; init; }
        public void BeforeWrite(SqliteConnection connection, SqliteTransaction transaction) => Writes++;
        public void BeforeCommit(SqliteConnection connection, SqliteTransaction transaction)
        {
            Commits++;
            if (FailCommit) { throw new IOException("Synthetic pre-commit failure."); }
        }
    }

    private sealed class ForgedWrite : IPreparedEvidenceWrite
    {
        public HostId<EvidenceIdentity> EvidenceId => new(Guid.NewGuid());
        public void Write() => throw new InvalidOperationException("Untrusted callbacks must never acquire batch authority.");
    }
}
