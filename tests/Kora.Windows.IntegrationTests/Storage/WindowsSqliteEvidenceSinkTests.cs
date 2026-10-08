using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;

using AwesomeAssertions;

using Kora.Core.Auditing;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Windows.IntegrationTests.Audio;
using Kora.Windows.Storage;

using Microsoft.Data.Sqlite;

namespace Kora.Windows.IntegrationTests.Storage;

public sealed class WindowsSqliteEvidenceSinkTests
{
    [WindowsFact]
    public async Task Typed_tables_preserve_host_W3C_business_links_and_independent_due_dates_without_keys()
    {
        using var fixture = new OwnedStorageFixture();
        using var listener = Listen();
        var clock = new FixedClock();
        var sink = new WindowsSqliteEvidenceSink(fixture, new EvidenceRetentionPolicy(120), clock);
        sink.Initialize();
        var tasks = new WindowsSqliteHostTaskStore(fixture);
        await tasks.InitializeAsync(TestContext.Current.CancellationToken);
        var request = Request();
        var audit = Audit();
        var cause = new ActivityLinkEnvelope("0123456789abcdef0123456789abcdef", "0123456789abcdef");
        using var host = HostActivity.BeginAudit(request, audit);
        var diagnostic = Diagnostic(host);
        sink.WriteDiagnostic(diagnostic);
        var audited = Diagnostic(host);
        sink.WriteAudit(new AuditEnvelope(audited, audit));
        var completed = new CompletedActivityEnvelope(new(Guid.NewGuid()), TraceSnapshot.Capture(host.Activity)!,
            request, clock.GetUtcNow().AddSeconds(-2), clock.GetUtcNow(), HostOperationOutcome.Completed, [cause],
            audit.CorrelationId, audit.ApprovalId);
        sink.WriteActivity(completed);
        using var connection = Open(fixture);
        using var command = connection.CreateCommand();
        foreach (var table in new[] { "application_log_events", "security_audit_events", "activity_spans" })
        {
            command.CommandText = $"SELECT request_id,session_id,task_id,invocation_id,trace_id,span_id,audit_correlation_id,approval_id,committed_utc,due_utc,envelope FROM {table};";
            using var reader = command.ExecuteReader();
            reader.Read().Should().BeTrue();
            reader.GetString(0).Should().Be(request.RequestId.Value.ToString("D"));
            reader.GetString(1).Should().Be(request.SessionId.Value.ToString("D"));
            reader.GetString(2).Should().Be(request.TaskId.Value.ToString("D"));
            reader.GetString(3).Should().Be(request.InvocationId!.Value.Value.ToString("D"));
            reader.GetString(4).Should().Be(host.Activity!.TraceId.ToHexString());
            reader.GetString(5).Should().Be(host.Activity!.SpanId.ToHexString());
            reader.GetString(6).Should().Be(audit.CorrelationId.ToString("D"));
            reader.GetString(7).Should().Be(audit.ApprovalId!.Value.ToString("D"));
            reader.GetInt64(8).Should().Be(clock.GetUtcNow().UtcTicks);
            reader.GetInt64(9).Should().Be(clock.GetUtcNow().AddDays(
                string.Equals(table, "security_audit_events", StringComparison.Ordinal) ? 120 : 30).UtcTicks);
            using var serialized = JsonDocument.Parse(reader.GetString(10));
            serialized.RootElement.ValueKind.Should().Be(JsonValueKind.Object);
            reader.Read().Should().BeFalse();
        }
        command.CommandText = """
            SELECT trace_id,span_id,ordinal,request_id,session_id,task_id,source_trace_id,source_span_id,
                audit_correlation_id,approval_id,committed_utc,due_utc,envelope FROM activity_links;
            """;
        using (var links = command.ExecuteReader())
        {
            links.Read().Should().BeTrue();
            links.GetString(0).Should().Be(cause.TraceId);
            links.GetString(1).Should().Be(cause.SpanId);
            links.GetInt64(2).Should().Be(0);
            links.GetString(3).Should().Be(request.RequestId.Value.ToString("D"));
            links.GetString(4).Should().Be(request.SessionId.Value.ToString("D"));
            links.GetString(5).Should().Be(request.TaskId.Value.ToString("D"));
            links.GetString(6).Should().Be(completed.Trace.TraceId);
            links.GetString(7).Should().Be(completed.Trace.SpanId);
            links.GetString(8).Should().Be(audit.CorrelationId.ToString("D"));
            links.GetString(9).Should().Be(audit.ApprovalId!.Value.ToString("D"));
            links.GetInt64(10).Should().Be(clock.GetUtcNow().UtcTicks);
            links.GetInt64(11).Should().Be(clock.GetUtcNow().AddDays(30).UtcTicks);
            JsonSerializer.Deserialize<ActivityLinkEnvelope>(links.GetString(12)).Should().Be(cause);
            links.Read().Should().BeFalse();
        }
        command.CommandText = "PRAGMA journal_mode;";
        command.ExecuteScalar().Should().Be("persist");
        command.CommandText = "PRAGMA synchronous;";
        command.ExecuteScalar().Should().Be(2L);
        Directory.Exists(fixture.Keys).Should().BeFalse();
        Directory.Exists(Path.Combine(fixture.LocalRoot, "HostStorageV1")).Should().BeTrue();
        Directory.Exists(Path.Combine(fixture.LocalRoot, WindowsSqliteEvidenceSink.PartitionName)).Should().BeTrue();
        new WindowsSqliteEvidenceSink(fixture, new EvidenceRetentionPolicy(30), clock).Initialize();
        command.CommandText = "SELECT due_utc FROM security_audit_events;";
        command.ExecuteScalar().Should().Be(clock.GetUtcNow().AddDays(120).UtcTicks);
        new WindowsSqliteEvidenceSink(fixture, new EvidenceRetentionPolicy(30), clock)
            .WriteAudit(new AuditEnvelope(Diagnostic(host), audit.WithOutcome(SecurityAuditOutcome.Succeeded)));
        command.CommandText = "SELECT due_utc FROM security_audit_events ORDER BY audit_sequence DESC LIMIT 1;";
        command.ExecuteScalar().Should().Be(clock.GetUtcNow().AddDays(30).UtcTicks);
    }

    [WindowsFact]
    public void Ordinary_spoof_properties_never_acquire_audit_authority_and_audit_sequence_is_ordered()
    {
        using var fixture = new OwnedStorageFixture();
        using var listener = Listen();
        var sink = new WindowsSqliteEvidenceSink(fixture);
        var audit = Audit();
        using var host = HostActivity.BeginAudit(Request(), audit);
        var diagnostic = Diagnostic(host) with
        {
            Properties = new Dictionary<string, EvidenceValue>(StringComparer.Ordinal)
            {
                ["SecurityAudit"] = new(EvidenceValueKind.Boolean, "true"),
                ["ActionId"] = new(EvidenceValueKind.Text, "spoofed.action"),
            },
        };
        sink.WriteDiagnostic(diagnostic);
        sink.WriteAudit(new AuditEnvelope(Diagnostic(host), audit));
        sink.WriteAudit(new AuditEnvelope(Diagnostic(host), audit.WithOutcome(SecurityAuditOutcome.Succeeded)));
        using var connection = Open(fixture);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT count(*) FROM application_log_events;";
        command.ExecuteScalar().Should().Be(1L);
        command.CommandText = "SELECT audit_sequence,audit_outcome,action_id FROM security_audit_events ORDER BY audit_sequence;";
        using var reader = command.ExecuteReader();
        reader.Read().Should().BeTrue();
        reader.GetInt64(0).Should().Be(1);
        reader.GetInt64(1).Should().Be((long)SecurityAuditOutcome.Requested);
        reader.GetString(2).Should().Be(audit.ActionId);
        reader.Read().Should().BeTrue();
        reader.GetInt64(0).Should().Be(2);
        reader.GetInt64(1).Should().Be((long)SecurityAuditOutcome.Succeeded);
        reader.Read().Should().BeFalse();
    }

    [WindowsFact]
    public void Audit_and_host_diagnostics_require_matching_live_request_trace_correlation_and_approval()
    {
        using var fixture = new OwnedStorageFixture();
        using var listener = Listen();
        var sink = new WindowsSqliteEvidenceSink(fixture);
        var audit = Audit();
        DiagnosticEnvelope captured;
        using (var host = HostActivity.BeginAudit(Request(), audit))
        {
            captured = Diagnostic(host);
            var wrongHost = () => sink.WriteAudit(new AuditEnvelope(captured with { Host = Request() }, audit));
            wrongHost.Should().Throw<InvalidOperationException>();
            var wrongTrace = () => sink.WriteDiagnostic(captured with
            {
                Trace = captured.Trace! with { SpanId = "0123456789abcdef" },
            });
            wrongTrace.Should().Throw<InvalidOperationException>();
            var wrongCorrelation = () => sink.WriteAudit(new AuditEnvelope(
                captured with { AuditCorrelationId = Guid.NewGuid() }, audit));
            wrongCorrelation.Should().Throw<InvalidDataException>();
            var wrongApproval = () => sink.WriteAudit(new AuditEnvelope(
                captured with { ApprovalId = Guid.NewGuid() }, audit));
            wrongApproval.Should().Throw<InvalidDataException>();
        }
        var expired = () => sink.WriteAudit(new AuditEnvelope(captured, audit));
        expired.Should().Throw<InvalidOperationException>();
        var diagnostic = () => sink.WriteDiagnostic(captured);
        diagnostic.Should().Throw<InvalidOperationException>();
        Directory.Exists(EvidencePartition(fixture)).Should().BeFalse();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Only_explicitly_classified_gap_diagnostics_may_lack_host_context(bool bootstrap)
    {
        using var fixture = new OwnedStorageFixture();
        var sink = new WindowsSqliteEvidenceSink(fixture);
        var envelope = Bootstrap(bootstrap) with { Properties = new Dictionary<string, EvidenceValue>(StringComparer.Ordinal) };
        var missing = () => sink.WriteDiagnostic(envelope);
        missing.Should().Throw<InvalidDataException>();
        sink.WriteDiagnostic(Bootstrap(bootstrap));
        using var connection = Open(fixture);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT count(*) FROM application_log_events WHERE request_id IS NULL AND trace_id IS NULL;";
        command.ExecuteScalar().Should().Be(1L);
        new WindowsSqliteEvidenceSink(fixture).Initialize();
    }

    [Theory]
    [InlineData("trace")]
    [InlineData("audit-correlation")]
    [InlineData("approval")]
    public void Non_authoritative_gap_diagnostics_cannot_carry_trusted_trace_or_business_fields(string field)
    {
        using var fixture = new OwnedStorageFixture();
        var sink = new WindowsSqliteEvidenceSink(fixture);
        var envelope = Bootstrap(bootstrap: false);
        envelope = field switch
        {
            "trace" => envelope with
            {
                Trace = new TraceSnapshot("0123456789abcdef0123456789abcdef", "0123456789abcdef", null,
                    ActivityTraceFlags.Recorded, "Kora.Windows", typeof(HostActivity).Assembly.GetName().Version!.ToString(),
                    "synthetic.request", ActivityKind.Internal),
            },
            "audit-correlation" => envelope with { AuditCorrelationId = Guid.NewGuid() },
            "approval" => envelope with { ApprovalId = Guid.NewGuid() },
            _ => throw new InvalidOperationException("An unknown fixture correlation field was requested."),
        };
        var write = () => sink.WriteDiagnostic(envelope);
        write.Should().Throw<InvalidDataException>();
        Directory.Exists(EvidencePartition(fixture)).Should().BeFalse();
    }

    [WindowsFact]
    public void Gap_classification_cannot_downgrade_mismatched_hosts_or_authorize_audits_and_tasks()
    {
        using var fixture = new OwnedStorageFixture();
        using var listener = Listen();
        var sink = new WindowsSqliteEvidenceSink(fixture);
        var audit = Audit();
        using var host = HostActivity.BeginAudit(Request(), audit);
        var diagnostic = Diagnostic(host) with { Properties = Bootstrap(bootstrap: false).Properties, Host = Request() };
        var write = () => sink.WriteDiagnostic(diagnostic);
        write.Should().Throw<InvalidOperationException>();
        var audited = () => sink.WriteAudit(new AuditEnvelope(diagnostic, audit));
        audited.Should().Throw<InvalidOperationException>();
        var hostless = () => sink.WriteAudit(new AuditEnvelope(Bootstrap(bootstrap: false), audit));
        hostless.Should().Throw<InvalidDataException>();
        var store = new WindowsSqliteHostTaskStore(fixture);
        var commit = () => store.CommitAsync(new HostTaskRecord(diagnostic.Host!, new(1), HostTaskState.IntentRecorded),
            0, TestContext.Current.CancellationToken);
        commit.Should().Throw<InvalidOperationException>();
        Directory.Exists(EvidencePartition(fixture)).Should().BeFalse();
        Directory.Exists(Path.Combine(fixture.LocalRoot, "HostStorageV1")).Should().BeFalse();
    }

    [WindowsFact]
    public void Top_level_budget_is_32_user_properties_plus_only_two_actual_markers_and_scopes_remain_32()
    {
        using var fixture = new OwnedStorageFixture();
        var sink = new WindowsSqliteEvidenceSink(fixture);
        var envelope = Bootstrap(bootstrap: false);
        var fields = Enumerable.Range(0, 32).ToDictionary(index => $"field{index}",
            _ => new EvidenceValue(EvidenceValueKind.WholeNumber, "1"), StringComparer.Ordinal);
        var properties = fields.Concat(envelope.Properties)
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        properties.Count.Should().Be(34);
        var oversized = new Dictionary<string, EvidenceValue>(properties, StringComparer.Ordinal)
        {
            ["field32"] = new(EvidenceValueKind.WholeNumber, "1"),
        };
        var tooManyUserFields = () => sink.WriteDiagnostic(envelope with { Properties = oversized });
        tooManyUserFields.Should().Throw<InvalidDataException>().WithMessage("*property count*");
        var invalidMarker = new Dictionary<string, EvidenceValue>(properties, StringComparer.Ordinal)
        {
            ["kora.evidence.gap"] = new(EvidenceValueKind.Text, "OtherGap"),
        };
        var noExtraSlotForClaim = () => sink.WriteDiagnostic(envelope with { Properties = invalidMarker });
        noExtraSlotForClaim.Should().Throw<InvalidDataException>().WithMessage("*property count*");
        var invalidBoolean = new Dictionary<string, EvidenceValue>(properties, StringComparer.Ordinal)
        {
            ["kora.bootstrap"] = new(EvidenceValueKind.Text, "false"),
        };
        var noExtraSlotForUntypedClaim = () => sink.WriteDiagnostic(envelope with { Properties = invalidBoolean });
        noExtraSlotForUntypedClaim.Should().Throw<InvalidDataException>().WithMessage("*property count*");
        var scope = fields.Take(31).Concat(envelope.Properties)
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        scope.Count.Should().Be(33);
        var noScopeMetadataBudget = () => sink.WriteDiagnostic(envelope with { Scopes = [scope] });
        noScopeMetadataBudget.Should().Throw<InvalidDataException>().WithMessage("*property count*");
        Directory.Exists(EvidencePartition(fixture)).Should().BeFalse();
        sink.WriteDiagnostic(envelope with { Properties = properties, Scopes = [fields] });
        new WindowsSqliteEvidenceSink(fixture).Initialize();
    }

    [WindowsFact]
    public void Manually_restoring_a_stopped_host_activity_does_not_restore_write_authority()
    {
        using var fixture = new OwnedStorageFixture();
        using var listener = Listen();
        var sink = new WindowsSqliteEvidenceSink(fixture);
        var audit = Audit();
        using var host = HostActivity.BeginAudit(Request(), audit);
        var envelope = Diagnostic(host);
        host.Activity!.Stop();
        Activity.Current = host.Activity;
        var auditWrite = () => sink.WriteAudit(new AuditEnvelope(envelope, audit));
        auditWrite.Should().Throw<InvalidOperationException>();
        var diagnosticWrite = () => sink.WriteDiagnostic(envelope);
        diagnosticWrite.Should().Throw<InvalidOperationException>();
        var store = new WindowsSqliteHostTaskStore(fixture);
        var commit = () => store.CommitAsync(new HostTaskRecord(host.Request, new(1), HostTaskState.IntentRecorded),
            0, TestContext.Current.CancellationToken);
        commit.Should().Throw<InvalidOperationException>();
        Directory.Exists(EvidencePartition(fixture)).Should().BeFalse();
        Directory.Exists(Path.Combine(fixture.LocalRoot, "HostStorageV1")).Should().BeFalse();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Bounded_properties_scopes_and_serialized_envelopes_fail_before_storage_admission(bool bootstrap)
    {
        using var fixture = new OwnedStorageFixture();
        var sink = new WindowsSqliteEvidenceSink(fixture);
        var oversized = Bootstrap(bootstrap) with { MessageTemplate = new string('x', 4097) };
        var write = () => sink.WriteDiagnostic(oversized);
        write.Should().Throw<InvalidDataException>();
        var properties = Enumerable.Range(0, 32).ToDictionary(index => $"field{index}",
            _ => new EvidenceValue(EvidenceValueKind.Text, new string('x', 1024)), StringComparer.Ordinal);
        var large = Bootstrap(bootstrap) with { Scopes = [properties, properties, properties] };
        Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(large)).Should().BeGreaterThan(WindowsSqliteEvidenceSink.MaximumEnvelopeBytes);
        var serialized = () => sink.WriteDiagnostic(large);
        serialized.Should().Throw<InvalidDataException>();
        Directory.Exists(EvidencePartition(fixture)).Should().BeFalse();
    }

    [Theory]
    [InlineData("PRAGMA user_version=3;")]
    [InlineData("PRAGMA application_id=0;")]
    [InlineData("DROP INDEX ix_application_log_events_trace_id;")]
    [InlineData("CREATE TRIGGER hidden_rewrite AFTER INSERT ON application_log_events BEGIN DELETE FROM application_log_events; END;")]
    [InlineData("CREATE TRIGGER sqlitehidden_rewrite AFTER INSERT ON application_log_events BEGIN DELETE FROM application_log_events; END;")]
    [InlineData("ALTER TABLE activity_links ADD COLUMN unexpected TEXT;")]
    public void Version_identity_constraint_index_and_trigger_schema_changes_fail_without_repair(string mutation)
    {
        using var fixture = new OwnedStorageFixture();
        var sink = new WindowsSqliteEvidenceSink(fixture);
        sink.Initialize();
        Mutate(fixture, mutation);
        var before = File.ReadAllBytes(DatabasePath(fixture));
        var initialize = () => new WindowsSqliteEvidenceSink(fixture).Initialize();
        initialize.Should().Throw<InvalidDataException>();
        File.ReadAllBytes(DatabasePath(fixture)).Should().Equal(before);
    }

    [Theory]
    [InlineData("UPDATE application_log_events SET category='mismatched';")]
    [InlineData("UPDATE application_log_events SET envelope='{}';")]
    [InlineData("UPDATE application_log_events SET due_utc=committed_utc+1;")]
    public void Persisted_projection_envelope_and_retention_integrity_are_verified_even_without_reads(string mutation)
    {
        using var fixture = new OwnedStorageFixture();
        var sink = new WindowsSqliteEvidenceSink(fixture);
        sink.WriteDiagnostic(Bootstrap());
        Mutate(fixture, mutation);
        var before = File.ReadAllBytes(DatabasePath(fixture));
        var initialize = () => new WindowsSqliteEvidenceSink(fixture).Initialize();
        initialize.Should().Throw<InvalidDataException>();
        File.ReadAllBytes(DatabasePath(fixture)).Should().Equal(before);
    }

    [Theory]
    [InlineData("UPDATE security_audit_events SET action_id='mismatched.action';")]
    [InlineData("UPDATE security_audit_events SET audit_sequence=3;")]
    [InlineData("DELETE FROM security_audit_events;")]
    [InlineData("UPDATE sqlite_sequence SET seq=99;")]
    public void Authoritative_audit_projection_and_order_cannot_be_rewritten_or_removed_silently(string mutation)
    {
        using var fixture = new OwnedStorageFixture();
        using var listener = Listen();
        var sink = new WindowsSqliteEvidenceSink(fixture);
        var audit = Audit();
        using var host = HostActivity.BeginAudit(Request(), audit);
        sink.WriteAudit(new AuditEnvelope(Diagnostic(host), audit));
        Mutate(fixture, mutation);
        var before = File.ReadAllBytes(DatabasePath(fixture));
        var initialize = () => sink.Initialize();
        initialize.Should().Throw<InvalidDataException>();
        File.ReadAllBytes(DatabasePath(fixture)).Should().Equal(before);
    }

    [WindowsFact]
    public void Corrupt_missing_database_and_missing_journal_are_preserved_and_never_replaced()
    {
        using var fixture = new OwnedStorageFixture();
        var sink = new WindowsSqliteEvidenceSink(fixture);
        sink.Initialize();
        var database = DatabasePath(fixture);
        var bytes = "Synthetic corrupt evidence database"u8.ToArray();
        File.WriteAllBytes(database, bytes);
        var initialize = () => sink.Initialize();
        initialize.Should().Throw<InvalidDataException>();
        File.ReadAllBytes(database).Should().Equal(bytes);
        File.Delete(database);
        initialize.Should().Throw<InvalidDataException>();
        File.Exists(database).Should().BeFalse();
        using var other = new OwnedStorageFixture();
        var valid = new WindowsSqliteEvidenceSink(other);
        valid.Initialize();
        var before = File.ReadAllBytes(DatabasePath(other));
        File.Delete(DatabasePath(other) + "-journal");
        var missing = () => valid.Initialize();
        missing.Should().Throw<InvalidDataException>();
        File.Exists(DatabasePath(other) + "-journal").Should().BeFalse();
        File.ReadAllBytes(DatabasePath(other)).Should().Equal(before);
    }

    [WindowsFact]
    public void Partial_first_run_directory_and_zero_length_database_fail_closed()
    {
        using var fixture = new OwnedStorageFixture();
        var directory = new RestrictedStorageDirectory(fixture, includeKeys: false,
            partitionName: WindowsSqliteEvidenceSink.PartitionName);
        directory.CreateNew();
        var initialize = () => new WindowsSqliteEvidenceSink(fixture).Initialize();
        initialize.Should().Throw<InvalidDataException>();
        File.Exists(DatabasePath(fixture)).Should().BeFalse();
        using var lease = directory.AcquireLease();
        using (var empty = directory.CreateNewFile(DatabasePath(fixture))) { }
        using (var journal = directory.CreateNewFile(DatabasePath(fixture) + "-journal")) { }
        lease.Dispose();
        initialize.Should().Throw<InvalidDataException>();
        new FileInfo(DatabasePath(fixture)).Length.Should().Be(0);
    }

    [WindowsFact]
    public async Task Reparse_partition_is_rejected_without_following_or_mutating_owned_target()
    {
        using var fixture = new OwnedStorageFixture();
        using var identity = WindowsIdentity.GetCurrent();
        var target = Path.Combine(fixture.LocalRoot, "OwnedEvidenceTarget");
        RestrictedStorageDirectory.CreateRestrictedDirectory(target,
            identity.User ?? throw new InvalidOperationException("The test profile is unavailable."));
        var before = new DirectoryInfo(target).GetAccessControl().GetSecurityDescriptorBinaryForm();
        var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"))
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            Arguments = $"/d /c mklink /J \"{EvidencePartition(fixture)}\" \"{target}\"",
        };
        await RunOwnedJunctionCommand(start, TestContext.Current.CancellationToken);
        try
        {
            var initialize = () => new WindowsSqliteEvidenceSink(fixture).Initialize();
            initialize.Should().Throw<UnauthorizedAccessException>();
            Directory.EnumerateFileSystemEntries(target).Should().BeEmpty();
            new DirectoryInfo(target).GetAccessControl().GetSecurityDescriptorBinaryForm().Should().Equal(before);
        }
        finally
        {
            start.Arguments = $"/d /c rmdir \"{EvidencePartition(fixture)}\"";
            await RunOwnedJunctionCommand(start, CancellationToken.None);
        }
    }

    [WindowsFact]
    public async Task Sink_admission_blocks_but_times_out_instead_of_queueing_or_dropping_evidence()
    {
        using var fixture = new OwnedStorageFixture();
        var sink = new WindowsSqliteEvidenceSink(fixture);
        sink.Initialize();
        var directory = new RestrictedStorageDirectory(fixture, includeKeys: false,
            partitionName: WindowsSqliteEvidenceSink.PartitionName);
        using var lease = directory.AcquireLease();
        var before = File.ReadAllBytes(DatabasePath(fixture));
        var started = Stopwatch.GetTimestamp();
        var write = () => Task.Run(() => sink.WriteDiagnostic(Bootstrap()), TestContext.Current.CancellationToken);
        await write.Should().ThrowAsync<IOException>();
        var elapsed = Stopwatch.GetElapsedTime(started);
        elapsed.Should().BeGreaterThanOrEqualTo(TimeSpan.FromSeconds(5));
        elapsed.Should().BeLessThan(TimeSpan.FromSeconds(10));
        File.ReadAllBytes(DatabasePath(fixture)).Should().Equal(before);
    }

    [Theory]
    [InlineData("evidence.db")]
    [InlineData("evidence.db-journal")]
    [InlineData("operation.lock")]
    public void Permissive_file_ACL_is_rejected_without_mutation_or_repair(string fileName)
    {
        using var fixture = new OwnedStorageFixture();
        var sink = new WindowsSqliteEvidenceSink(fixture);
        sink.Initialize();
        var file = new FileInfo(Path.Combine(EvidencePartition(fixture), fileName));
        var acl = file.GetAccessControl();
        acl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.WorldSid, null),
            FileSystemRights.Read, AccessControlType.Allow));
        file.SetAccessControl(acl);
        var permissions = file.GetAccessControl().GetSecurityDescriptorBinaryForm();
        var before = File.ReadAllBytes(DatabasePath(fixture));
        var initialize = () => sink.Initialize();
        initialize.Should().Throw<UnauthorizedAccessException>();
        file.GetAccessControl().GetSecurityDescriptorBinaryForm().Should().Equal(permissions);
        File.ReadAllBytes(DatabasePath(fixture)).Should().Equal(before);
    }

    [WindowsFact]
    public void Database_and_journal_remain_protected_user_owned_across_actual_commits()
    {
        using var fixture = new OwnedStorageFixture();
        using var identity = WindowsIdentity.GetCurrent();
        var sink = new WindowsSqliteEvidenceSink(fixture);
        sink.Initialize();
        var paths = new[] { DatabasePath(fixture), DatabasePath(fixture) + "-journal" };
        var permissions = paths.Select(path => new FileInfo(path).GetAccessControl()
            .GetSecurityDescriptorBinaryForm()).ToArray();
        for (var index = 0; index < 3; index++)
        {
            sink.WriteDiagnostic(Bootstrap());
            new WindowsSqliteEvidenceSink(fixture).Initialize();
            for (var file = 0; file < paths.Length; file++)
            {
                var security = new FileInfo(paths[file]).GetAccessControl();
                security.GetOwner(typeof(SecurityIdentifier)).Should().Be(identity.User);
                security.AreAccessRulesProtected.Should().BeTrue();
                security.GetSecurityDescriptorBinaryForm().Should().Equal(permissions[file]);
            }
        }
    }

    [WindowsFact]
    public async Task Concurrent_sink_instances_commit_without_an_unbounded_background_writer()
    {
        using var fixture = new OwnedStorageFixture();
        new WindowsSqliteEvidenceSink(fixture).Initialize();
        var writes = Enumerable.Range(0, 12).Select(_ => Task.Run(() =>
            new WindowsSqliteEvidenceSink(fixture).WriteDiagnostic(Bootstrap()), TestContext.Current.CancellationToken));
        await Task.WhenAll(writes);
        using var connection = Open(fixture);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT count(*) FROM application_log_events;";
        command.ExecuteScalar().Should().Be(12L);
        new WindowsSqliteEvidenceSink(fixture).Initialize();
    }

    [WindowsFact]
    public void Duplicate_evidence_does_not_partially_publish_activity_links()
    {
        using var fixture = new OwnedStorageFixture();
        using var listener = Listen();
        using var host = HostActivity.BeginRoot(Request(), HostActivityLayer.Application, HostOperation.Request);
        var clock = new FixedClock();
        var completed = new CompletedActivityEnvelope(new(Guid.NewGuid()), TraceSnapshot.Capture(host.Activity)!,
            host.Request, clock.GetUtcNow(), clock.GetUtcNow(), HostOperationOutcome.Completed,
            [new("0123456789abcdef0123456789abcdef", "0123456789abcdef")]);
        var sink = new WindowsSqliteEvidenceSink(fixture);
        sink.WriteActivity(completed);
        var duplicate = () => sink.WriteActivity(completed);
        duplicate.Should().Throw<IOException>().WithInnerException<SqliteException>();
        using var connection = Open(fixture);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT count(*) FROM activity_links;";
        command.ExecuteScalar().Should().Be(1L);
        new WindowsSqliteEvidenceSink(fixture).Initialize();
        Mutate(fixture, "DELETE FROM activity_links;");
        var missing = () => sink.Initialize();
        missing.Should().Throw<InvalidDataException>();
    }

    internal static string EvidencePartition(OwnedStorageFixture fixture) =>
        Path.Combine(fixture.LocalRoot, WindowsSqliteEvidenceSink.PartitionName);

    internal static string DatabasePath(OwnedStorageFixture fixture) => Path.Combine(EvidencePartition(fixture), "evidence.db");

    private static void Mutate(OwnedStorageFixture fixture, string sql)
    {
        using var connection = Open(fixture);
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static async Task RunOwnedJunctionCommand(ProcessStartInfo start, CancellationToken cancellationToken)
    {
        using var process = Process.Start(start) ?? throw new InvalidOperationException("The owned junction command could not start.");
        var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var error = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        process.ExitCode.Should().Be(0, $"owned junction operation must succeed: {await output} {await error}");
    }

    private static SqliteConnection Open(OwnedStorageFixture fixture)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath(fixture), Mode = SqliteOpenMode.ReadWrite, Pooling = false,
        }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode=PERSIST; PRAGMA synchronous=FULL;";
        command.ExecuteNonQuery();
        return connection;
    }

    private static HostRequest Request() => new(new(Guid.NewGuid()), new(Guid.NewGuid()), new(Guid.NewGuid()),
        RequestOrigin.LocalUi, new HostId<InvocationIdentity>(Guid.NewGuid()));

    private static SecurityAuditEvent Audit() => new(Guid.NewGuid(), SecurityAuditCategory.ProtectedOperation,
        "synthetic.operation", SecurityAuditOutcome.Requested, SecurityAuditInitiator.LocalUser, "synthetic.target",
        Guid.NewGuid());

    private static DiagnosticEnvelope Diagnostic(HostActivity host) => new(1, new(Guid.NewGuid()),
        DateTimeOffset.UtcNow, 42, "SyntheticEvent", "Information", "Kora.Tests", "Synthetic {Count}",
        new Dictionary<string, EvidenceValue>(StringComparer.Ordinal) { ["Count"] = new(EvidenceValueKind.WholeNumber, "1") },
        [], TraceSnapshot.Capture(host.Activity), host.Request, null, host.CorrelationId, host.ApprovalId);

    private static DiagnosticEnvelope Bootstrap(bool bootstrap = true) => new(1, new(Guid.NewGuid()),
        DateTimeOffset.UtcNow, 1, bootstrap ? "Bootstrap" : "Contextless", "Information", "Kora.Tests",
        bootstrap ? "Synthetic bootstrap" : "Synthetic contextless",
        new Dictionary<string, EvidenceValue>(StringComparer.Ordinal)
        {
            ["kora.bootstrap"] = new(EvidenceValueKind.Boolean, bootstrap ? "true" : "false"),
            ["kora.evidence.gap"] = new(EvidenceValueKind.Text, "MissingHostContext"),
        }, [], null, null, null);

    private static ActivityListener Listen()
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
        };
        ActivitySource.AddActivityListener(listener);
        return listener;
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 10, 6, 0, 0, 0, TimeSpan.Zero);
    }
}
