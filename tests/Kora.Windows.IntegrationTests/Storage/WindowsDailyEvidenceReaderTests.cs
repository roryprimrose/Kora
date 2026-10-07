using System.Diagnostics;
using System.Security.AccessControl;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

using AwesomeAssertions;

using Kora.Application.Diagnostics;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Windows.IntegrationTests.Audio;
using Kora.Windows.Storage;

using Microsoft.Extensions.Logging.Abstractions;

using Serilog;
using Serilog.Formatting.Json;

namespace Kora.Windows.IntegrationTests.Storage;

[Collection("Evidence query")]
public sealed class WindowsDailyEvidenceReaderTests
{
    [WindowsFact]
    public async Task Versioned_envelope_scope_and_optional_metadata_are_preserved_with_policy_redaction()
    {
        using var fixture = new OwnedStorageFixture();
        using var listener = Listen();
        var envelope = Produce(HostRequest.Create(RequestOrigin.LocalUi), 1)[0] with
        {
            EventName = "ObservedCount", ExceptionType = "System.InvalidOperationException",
            Scopes = [new Dictionary<string, EvidenceValue>(StringComparer.Ordinal)
            {
                ["ScopeCount"] = new(EvidenceValueKind.WholeNumber, "42"),
                ["AccessToken"] = new(EvidenceValueKind.Text, "never present"),
            }],
        };
        Write(fixture, "kora-20261007.log", [envelope]);
        using var inspection = Root();
        var result = await Service(new WindowsDailyEvidenceReader(fixture)).QueryAsync(
            new() { Source = EvidenceSource.DailyLog }, null, TestContext.Current.CancellationToken);
        var record = result.Records.Single();
        record.EventName.Should().Be(envelope.EventName);
        record.ExceptionType.Should().Be(envelope.ExceptionType);
        var scope = record.Scopes!.Single();
        scope["ScopeCount"].Should().Be(envelope.Scopes[0]["ScopeCount"]);
        scope["AccessToken"].CanonicalValue.Should().Be("[redacted]");
        Encoding.UTF8.GetString(DurableEvidenceQuery.Serialize(result)).Should().NotContain("never present");
    }

    [WindowsFact]
    public async Task Composition_preserves_SQLite_All_counts_and_native_daily_trace_choice()
    {
        using var fixture = new OwnedStorageFixture();
        using var listener = Listen();
        var owner = HostRequest.Create(RequestOrigin.LocalUi);
        var envelopes = Produce(owner, 2);
        Write(fixture, "kora-20261007.log", envelopes);
        var sink = new WindowsSqliteEvidenceSink(fixture);
        using (var producer = HostActivity.BeginRoot(owner, HostActivityLayer.Application, HostOperation.Request))
        {
            sink.WriteDiagnostic(Produce(owner, 1)[0] with { Trace = TraceSnapshot.Capture(producer.Activity) });
        }
        var service = Service(new WindowsEvidenceReader(new(sink), new(fixture)));
        var allowed = true;
        var viewer = new Kora.EvidenceViewModel(service, () => allowed, NullLogger<Kora.EvidenceViewModel>.Instance);
        await viewer.SearchAsync();
        viewer.Records.Should().ContainSingle().Which.Reference.Source.Should().Be(EvidenceSource.Log);
        viewer.Source = EvidenceSource.DailyLog;
        await viewer.SearchAsync();
        viewer.Records.Should().HaveCount(2);
        viewer.Select(viewer.Records[0]);
        await viewer.ReadTraceAsync();
        viewer.Records.Should().HaveCount(2).And.OnlyContain(record => record.Reference.Source == EvidenceSource.DailyLog);
        viewer.Select(viewer.Records[0]);
        await viewer.NavigateAsync(viewer.Segments[0]);
        viewer.Status.Should().Contain("failed");
        viewer.Records.Should().BeEmpty();
        allowed = false;
        await viewer.SearchAsync();
        viewer.Records.Should().BeEmpty();
        viewer.ResultText.Should().BeEmpty();
        viewer.Close();
    }

    [WindowsFact]
    public async Task Outer_correlation_is_ignored_and_hostless_gaps_never_gain_a_session()
    {
        using var fixture = new OwnedStorageFixture();
        using var listener = Listen();
        var owner = HostRequest.Create(RequestOrigin.LocalUi);
        var original = Produce(owner, 1)[0];
        var outer = JsonNode.Parse(Line(original))!;
        outer["Properties"]!["HostSessionId"] = Guid.NewGuid();
        outer["Properties"]!["HostTraceId"] = new string('f', 32);
        outer["Properties"]!["EvidenceId"] = Guid.NewGuid();
        var hostless = original with
        {
            EvidenceId = new(Guid.NewGuid()), Host = null, Trace = null,
            Properties = new Dictionary<string, EvidenceValue>(StringComparer.Ordinal)
            {
                ["kora.bootstrap"] = new(EvidenceValueKind.Boolean, "false"),
                ["kora.evidence.gap"] = new(EvidenceValueKind.Text, "MissingHostContext"),
            },
        };
        Write(fixture, "kora-20261007.log", []);
        await File.WriteAllTextAsync(Log(fixture), outer.ToJsonString() + "\n" + Line(hostless),
            TestContext.Current.CancellationToken);
        var service = Service(new WindowsDailyEvidenceReader(fixture));
        using var inspection = Root();
        var all = await service.QueryAsync(new() { Source = EvidenceSource.DailyLog }, null, TestContext.Current.CancellationToken);
        all.Records[0].Host.Should().Be(owner);
        all.Records[0].Trace.Should().Be(original.Trace);
        all.Records[1].Host.Should().BeNull();
        all.Records[1].Trace.Should().BeNull();
        var filtered = await service.QueryAsync(new() { Source = EvidenceSource.DailyLog, SessionId = owner.SessionId },
            null, TestContext.Current.CancellationToken);
        filtered.Records.Should().ContainSingle();
    }

    [WindowsFact]
    public async Task Line_limit_is_inclusive_and_redacted_text_never_matches_secret_content()
    {
        using var fixture = new OwnedStorageFixture();
        using var listener = Listen();
        var envelope = Produce(HostRequest.Create(RequestOrigin.LocalUi), 1)[0];
        Write(fixture, "kora-20261007.log", []);
        var line = Line(envelope).TrimEnd('\n');
        var atLimit = line + new string(' ', WindowsDailyEvidenceReader.MaximumLineBytes - Encoding.UTF8.GetByteCount(line)) + "\n";
        await File.WriteAllTextAsync(Log(fixture), atLimit, new UTF8Encoding(false), TestContext.Current.CancellationToken);
        var service = Service(new WindowsDailyEvidenceReader(fixture));
        using var inspection = Root();
        var exact = await service.QueryAsync(new() { Source = EvidenceSource.DailyLog }, null, TestContext.Current.CancellationToken);
        exact.Status.Should().Be(EvidencePageStatus.Available);
        exact.Records.Should().ContainSingle();
        var hidden = await service.QueryAsync(new() { Source = EvidenceSource.DailyLog, Text = "never search this" },
            null, TestContext.Current.CancellationToken);
        hidden.Records.Should().BeEmpty();
        await File.WriteAllTextAsync(Log(fixture), " " + atLimit, new UTF8Encoding(false), TestContext.Current.CancellationToken);
        (await service.QueryAsync(new() { Source = EvidenceSource.DailyLog }, null, TestContext.Current.CancellationToken))
            .Status.Should().Be(EvidencePageStatus.Corrupt);
    }

    [WindowsFact]
    public async Task Cache_bound_eviction_does_not_reinterpret_a_snapshot_and_duplicate_ids_have_distinct_citations()
    {
        using var fixture = new OwnedStorageFixture();
        using var listener = Listen();
        var envelope = Produce(HostRequest.Create(RequestOrigin.LocalUi), 1)[0];
        Write(fixture, "kora-20261007.log", [envelope, envelope]);
        Write(fixture, "kora-20261008.log", [envelope]);
        var reader = new WindowsDailyEvidenceReader(fixture);
        using var inspection = Root();
        var query = new EvidenceQuery { Source = EvidenceSource.DailyLog, Limit = 1 };
        var now = DateTimeOffset.UtcNow;
        var first = await reader.ReadAsync(query, null, inspection.Request, now, TestContext.Current.CancellationToken);
        first.Candidates.Select(candidate => candidate.Record.Reference).Should().OnlyHaveUniqueItems();
        for (var index = 1; index < WindowsDailyEvidenceReader.MaximumSnapshots; index++)
        {
            await reader.ReadAsync(query, null, inspection.Request, now.AddSeconds(index), TestContext.Current.CancellationToken);
        }
        var checkpoint = new EvidenceReadCheckpoint(first.Snapshot, first.Candidates[0].Position);
        (await reader.ReadAsync(query, checkpoint, inspection.Request, now.AddSeconds(7), TestContext.Current.CancellationToken))
            .Status.Should().BeNull();
        await reader.ReadAsync(query, null, inspection.Request, now.AddSeconds(8), TestContext.Current.CancellationToken);
        (await reader.ReadAsync(query, checkpoint, inspection.Request, now.AddSeconds(8), TestContext.Current.CancellationToken))
            .Status.Should().Be(EvidencePageStatus.SnapshotExpired);
    }

    [WindowsFact]
    public async Task Mutation_and_cancellation_during_a_read_discard_captured_content_and_close_handles()
    {
        using var fixture = new OwnedStorageFixture();
        using var listener = Listen();
        Write(fixture, "kora-20261007.log", Produce(HostRequest.Create(RequestOrigin.LocalUi), 2));
        using var inspection = Root();
        var mutatingTime = new HookClock(6, () => File.WriteAllText(Log(fixture), string.Empty));
        var mutated = await new WindowsDailyEvidenceReader(fixture, mutatingTime).ReadAsync(
            new() { Source = EvidenceSource.DailyLog }, null, inspection.Request, DateTimeOffset.UtcNow, TestContext.Current.CancellationToken);
        mutated.Status.Should().Be(EvidencePageStatus.Changed);
        mutated.Candidates.Should().BeEmpty();
        Write(fixture, "kora-20261007.log", Produce(HostRequest.Create(RequestOrigin.LocalUi), 2));
        using var cancellation = new CancellationTokenSource();
        var cancellingTime = new HookClock(6, cancellation.Cancel);
        var cancelled = async () => await new WindowsDailyEvidenceReader(fixture, cancellingTime).ReadAsync(
            new() { Source = EvidenceSource.DailyLog }, null, inspection.Request, DateTimeOffset.UtcNow, cancellation.Token);
        await cancelled.Should().ThrowAsync<OperationCanceledException>();
        using var exclusive = new FileStream(Log(fixture), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    }

    [WindowsFact]
    public async Task Permissive_source_permissions_are_rejected_without_repair()
    {
        using var fixture = new OwnedStorageFixture();
        using var listener = Listen();
        Write(fixture, "kora-20261007.log", Produce(HostRequest.Create(RequestOrigin.LocalUi), 1));
        var file = new FileInfo(Log(fixture));
        var permissions = file.GetAccessControl();
        permissions.AddAccessRule(new System.Security.AccessControl.FileSystemAccessRule(
            new System.Security.Principal.SecurityIdentifier(System.Security.Principal.WellKnownSidType.WorldSid, null),
            System.Security.AccessControl.FileSystemRights.Read, System.Security.AccessControl.AccessControlType.Allow));
        file.SetAccessControl(permissions);
        var before = file.GetAccessControl().GetSecurityDescriptorBinaryForm();
        using var inspection = Root();
        var denied = async () => await Service(new WindowsDailyEvidenceReader(fixture)).QueryAsync(
            new() { Source = EvidenceSource.DailyLog }, null, TestContext.Current.CancellationToken);
        await denied.Should().ThrowAsync<UnauthorizedAccessException>();
        file.GetAccessControl().GetSecurityDescriptorBinaryForm().Should().Equal(before);
    }

    [WindowsFact]
    public async Task Actual_writer_files_preserve_typed_fields_and_independent_citations_without_writes()
    {
        using var fixture = new OwnedStorageFixture();
        using var listener = Listen();
        var owner = HostRequest.Create(RequestOrigin.LocalUi);
        var other = HostRequest.Create(RequestOrigin.LocalUi);
        var records = Produce(owner, 55);
        var foreign = Produce(other, 1)[0];
        Write(fixture, "kora-20261007.log", records.Append(foreign));
        var path = Log(fixture);
        var before = await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken);
        var reader = new WindowsDailyEvidenceReader(fixture);
        var service = Service(reader);
        using var inspection = Root();
        var query = new EvidenceQuery { Source = EvidenceSource.DailyLog, SessionId = owner.SessionId, Limit = 10 };
        var actual = new List<EvidenceRecord>();
        string? cursor = null;
        do
        {
            var page = await service.QueryAsync(query, cursor, TestContext.Current.CancellationToken);
            page.Status.Should().Be(EvidencePageStatus.Available);
            page.DailyReport!.Files.Should().Be(1);
            page.DailyReport.Lines.Should().Be(56);
            page.Records.Should().HaveCountLessThanOrEqualTo(10);
            DurableEvidenceQuery.Serialize(page).Length.Should().BeLessThanOrEqualTo(65536);
            actual.AddRange(page.Records);
            cursor = page.Cursor;
        } while (cursor is not null);
        actual.Should().HaveCount(55);
        actual.Select(record => record.EventId).Should().Equal(Enumerable.Range(0, 55).Select(index => (int?)index));
        actual.Should().OnlyContain(record => record.Host!.SessionId == owner.SessionId && record.Audit == null
            && record.AuditSequence == null && record.DueUtc == null && record.CommittedUtc == null
            && record.Retention == EvidenceSegmentStatus.RetentionUnknown);
        actual[0].Properties["SecretValue"].CanonicalValue.Should().Be("[redacted]");
        actual[0].Properties["SecurityAudit"].CanonicalValue.Should().Be("true");
        actual[0].DailyProvenance!.SourceEvidenceId.Should().Be(records[0].EvidenceId);
        actual[0].Trace.Should().Be(records[0].Trace);
        actual[0].RelatedSegments.Single().Status.Should().Be(EvidenceSegmentStatus.Unavailable);
        var filtered = await service.QueryAsync(query with
        {
            Limit = 50, EventId = 12, Category = "daily.test", Severity = EvidenceSeverity.Information,
            Property = new("Count", new(EvidenceValueKind.WholeNumber, "12")), Text = "Observed",
            TaskId = owner.TaskId, RequestId = owner.RequestId,
            TraceId = records[0].Trace!.TraceId, SpanId = records[0].Trace!.SpanId,
            FromUtc = records[0].ObservedUtc, UntilUtc = records[0].ObservedUtc,
        }, null, TestContext.Current.CancellationToken);
        filtered.Records.Should().ContainSingle();
        var exact = await service.QueryAsync(new() { Record = actual[0].Reference }, null, TestContext.Current.CancellationToken);
        exact.Records.Single().Should().BeEquivalentTo(actual[0]);
        var excluded = await service.QueryAsync(query with { Record = actual[0].Reference, SessionId = other.SessionId },
            null, TestContext.Current.CancellationToken);
        excluded.Status.Should().Be(EvidencePageStatus.MissingOrRemoved);
        var auditFilter = await service.QueryAsync(query with { ActionId = "host.query.version" },
            null, TestContext.Current.CancellationToken);
        auditFilter.Records.Should().BeEmpty();
        (await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken)).Should().Equal(before);
        Directory.GetFiles(fixture.LocalRoot, "*", SearchOption.AllDirectories).Should().Equal(path);
    }

    [WindowsFact]
    public async Task Append_and_new_day_do_not_expand_snapshot_but_fresh_search_sees_them()
    {
        using var fixture = new OwnedStorageFixture();
        using var listener = Listen();
        var owner = HostRequest.Create(RequestOrigin.LocalUi);
        var envelopes = Produce(owner, 3);
        Write(fixture, "kora-20261007.log", envelopes.Take(2));
        var reader = new WindowsDailyEvidenceReader(fixture);
        var service = Service(reader);
        using var inspection = Root();
        var query = new EvidenceQuery { Source = EvidenceSource.DailyLog, Limit = 1 };
        var first = await service.QueryAsync(query, null, TestContext.Current.CancellationToken);
        await File.AppendAllTextAsync(Log(fixture), Line(envelopes[2]), TestContext.Current.CancellationToken);
        Write(fixture, "kora-20261008.log", Produce(owner, 1));
        var next = await service.QueryAsync(query, first.Cursor, TestContext.Current.CancellationToken);
        next.Records.Single().EventId.Should().Be(1);
        next.Cursor.Should().BeNull();
        next.DailyReport!.SnapshotId.Should().Be(first.DailyReport!.SnapshotId);
        next.DailyReport.Files.Should().Be(1);
        var fresh = await service.QueryAsync(query with { Limit = 50 }, null, TestContext.Current.CancellationToken);
        fresh.Records.Should().HaveCount(4);
        fresh.Records[0].Reference.Should().Be(first.Records[0].Reference);
    }

    [Theory]
    [InlineData("replace", EvidencePageStatus.Changed)]
    [InlineData("rewrite", EvidencePageStatus.Changed)]
    [InlineData("truncate", EvidencePageStatus.Changed)]
    [InlineData("prune", EvidencePageStatus.MissingOrRemoved)]
    [InlineData("rotate", EvidencePageStatus.MissingOrRemoved)]
    public async Task Changed_prefix_replacement_rotation_and_pruning_never_admit_old_cursor(
        string operation, EvidencePageStatus expected)
    {
        using var fixture = new OwnedStorageFixture();
        using var listener = Listen();
        var owner = HostRequest.Create(RequestOrigin.LocalUi);
        Write(fixture, "kora-20261007.log", Produce(owner, 2));
        var reader = new WindowsDailyEvidenceReader(fixture);
        var service = Service(reader);
        using var inspection = Root();
        var query = new EvidenceQuery { Source = EvidenceSource.DailyLog, Limit = 1 };
        var first = await service.QueryAsync(query, null, TestContext.Current.CancellationToken);
        var path = Log(fixture);
        switch (operation)
        {
            case "replace":
                var bytes = await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken);
                File.Move(path, path + ".old");
                await File.WriteAllBytesAsync(path, bytes, TestContext.Current.CancellationToken);
                OwnFile(path);
                break;
            case "rewrite":
                var text = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);
                await File.WriteAllTextAsync(path, text.Replace("Observed", "Replaced", StringComparison.Ordinal),
                    TestContext.Current.CancellationToken);
                break;
            case "truncate": await File.WriteAllTextAsync(path, string.Empty, TestContext.Current.CancellationToken); break;
            case "prune": File.Delete(path); break;
            case "rotate": File.Move(path, path + ".old"); break;
        }
        var next = await service.QueryAsync(query, first.Cursor, TestContext.Current.CancellationToken);
        next.Status.Should().Be(expected);
        next.Records.Should().BeEmpty();
        next.Cursor.Should().BeNull();
    }

    [Theory]
    [InlineData("invalid-json", EvidencePageStatus.Corrupt)]
    [InlineData("invalid-utf8", EvidencePageStatus.Corrupt)]
    [InlineData("truncated", EvidencePageStatus.Truncated)]
    [InlineData("schema", EvidencePageStatus.Corrupt)]
    [InlineData("missing-field", EvidencePageStatus.Corrupt)]
    [InlineData("duplicate", EvidencePageStatus.Corrupt)]
    [InlineData("hostile-trace", EvidencePageStatus.Corrupt)]
    [InlineData("hostile-host", EvidencePageStatus.Corrupt)]
    [InlineData("oversize-line", EvidencePageStatus.Corrupt)]
    public async Task Invalid_source_is_explicit_and_returns_no_partial_success(string corruption, EvidencePageStatus expected)
    {
        using var fixture = new OwnedStorageFixture();
        using var listener = Listen();
        var envelope = Produce(HostRequest.Create(RequestOrigin.LocalUi), 1)[0];
        var payload = JsonNode.Parse(WindowsSqliteEvidenceSink.EncodeDailyDiagnostic(envelope))!;
        var line = Line(envelope);
        switch (corruption)
        {
            case "invalid-json": line = "{oops}\n"; break;
            case "invalid-utf8": line = "\n"; break;
            case "truncated": line = line.TrimEnd('\r', '\n'); break;
            case "schema": payload["SchemaVersion"] = 99; break;
            case "missing-field": payload.AsObject().Remove("ObservedUtc"); break;
            case "hostile-trace": payload["Trace"]!["TraceId"] = new string('0', 32); break;
            case "hostile-host": payload["Host"]!["SessionId"]!["Value"] = Guid.Empty; break;
            case "duplicate":
                var encoded = payload.ToJsonString();
                line = Outer(encoded[..^1] + ",\"SchemaVersion\":1}");
                break;
            case "oversize-line": line = new string(' ', WindowsDailyEvidenceReader.MaximumLineBytes + 1) + "\n"; break;
        }
        if (corruption is "schema" or "missing-field" or "hostile-trace" or "hostile-host") { line = Outer(payload.ToJsonString()); }
        Write(fixture, "kora-20261007.log", []);
        await File.WriteAllTextAsync(Log(fixture), Line(envelope) + line, new UTF8Encoding(false),
            TestContext.Current.CancellationToken);
        if (string.Equals(corruption, "invalid-utf8", StringComparison.Ordinal))
        {
            await File.WriteAllBytesAsync(Log(fixture), [0xff, (byte)'\n'], TestContext.Current.CancellationToken);
        }
        var service = Service(new WindowsDailyEvidenceReader(fixture));
        using var inspection = Root();
        var page = await service.QueryAsync(new() { Source = EvidenceSource.DailyLog }, null, TestContext.Current.CancellationToken);
        page.Status.Should().Be(expected);
        page.Records.Should().BeEmpty();
        page.Cursor.Should().BeNull();
    }

    [WindowsFact]
    public async Task Mirrors_legacy_activity_and_gap_copies_are_reported_without_audit_or_graph_authority()
    {
        using var fixture = new OwnedStorageFixture();
        using var listener = Listen();
        var envelope = Produce(HostRequest.Create(RequestOrigin.LocalUi), 1)[0];
        var mirror = JsonNode.Parse(Line(envelope))!;
        mirror["Properties"]!["EvidenceAuthority"] = "TypedAuditCopy";
        var gap = JsonSerializer.Serialize(new
        {
            Timestamp = envelope.ObservedUtc, Level = "Warning", MessageTemplate = "Evidence ingestion gap",
            Properties = new { EvidenceGap = "{}" },
        });
        var legacy = JsonSerializer.Serialize(new
        {
            Timestamp = envelope.ObservedUtc, Level = "Information", MessageTemplate = "Completed activity",
            Properties = new { ActivityEvidence = "{}" },
        });
        Write(fixture, "kora-20261007.log", [envelope]);
        await File.AppendAllTextAsync(Log(fixture), mirror.ToJsonString() + "\n" + gap + "\n" + legacy + "\n",
            TestContext.Current.CancellationToken);
        var service = Service(new WindowsDailyEvidenceReader(fixture));
        using var inspection = Root();
        var page = await service.QueryAsync(new() { Source = EvidenceSource.DailyLog }, null, TestContext.Current.CancellationToken);
        page.Status.Should().Be(EvidencePageStatus.Partial);
        page.Records.Should().ContainSingle();
        page.DailyReport!.AuditMirrors.Should().Be(1);
        page.DailyReport.IngestionGaps.Should().Be(1);
        page.DailyReport.UnsupportedRecords.Should().Be(1);
        page.Records.Single().Audit.Should().BeNull();
        page.Records.Single().Properties["SecurityAudit"].CanonicalValue.Should().Be("true");
    }

    [WindowsFact]
    public async Task Exact_line_count_and_byte_limits_are_reported_not_claimed_as_complete()
    {
        using var fixture = new OwnedStorageFixture();
        using var listener = Listen();
        var envelope = Produce(HostRequest.Create(RequestOrigin.LocalUi), 1)[0];
        var path = Log(fixture);
        Write(fixture, "kora-20261007.log", Enumerable.Repeat(envelope, WindowsDailyEvidenceReader.MaximumLines));
        var service = Service(new WindowsDailyEvidenceReader(fixture));
        using var inspection = Root();
        var query = new EvidenceQuery { Source = EvidenceSource.DailyLog, Text = "not present" };
        var exact = await service.QueryAsync(query, null, TestContext.Current.CancellationToken);
        exact.Status.Should().Be(EvidencePageStatus.Available);
        exact.DailyReport!.Lines.Should().Be(4096);
        await File.AppendAllTextAsync(path, Line(envelope), TestContext.Current.CancellationToken);
        var over = await service.QueryAsync(query, null, TestContext.Current.CancellationToken);
        over.Status.Should().Be(EvidencePageStatus.ScanLimitReached);
        over.DailyReport!.Lines.Should().Be(4096);
        var padding = Outer(WindowsSqliteEvidenceSink.EncodeDailyDiagnostic(envelope)).TrimEnd('\n');
        var large = padding + new string(' ', 131072 - Encoding.UTF8.GetByteCount(padding) - 1) + "\n";
        await File.WriteAllTextAsync(path, string.Concat(Enumerable.Repeat(large, 64)), new UTF8Encoding(false),
            TestContext.Current.CancellationToken);
        var bytesExact = await service.QueryAsync(query, null, TestContext.Current.CancellationToken);
        bytesExact.Status.Should().Be(EvidencePageStatus.Available);
        bytesExact.DailyReport!.Bytes.Should().Be(WindowsDailyEvidenceReader.MaximumPrefixBytes);
        await File.AppendAllTextAsync(path, large, TestContext.Current.CancellationToken);
        var bytesOver = await service.QueryAsync(query, null, TestContext.Current.CancellationToken);
        bytesOver.Status.Should().Be(EvidencePageStatus.ScanLimitReached);
        bytesOver.DailyReport!.Bytes.Should().Be(WindowsDailyEvidenceReader.MaximumPrefixBytes);
    }

    [WindowsFact]
    public async Task File_count_names_missing_source_and_snapshot_expiry_are_bounded()
    {
        using var fixture = new OwnedStorageFixture();
        using var listener = Listen();
        var reader = new WindowsDailyEvidenceReader(fixture);
        var service = Service(reader);
        using var inspection = Root();
        var query = new EvidenceQuery { Source = EvidenceSource.DailyLog, Limit = 1 };
        (await service.QueryAsync(query, null, TestContext.Current.CancellationToken)).Status.Should().Be(EvidencePageStatus.Unavailable);
        Directory.Exists(Path.GetDirectoryName(Log(fixture))).Should().BeFalse();
        var owner = HostRequest.Create(RequestOrigin.LocalUi);
        for (var index = 0; index < 32; index++)
        {
            Write(fixture, "kora-" + new DateOnly(2026, 1, 1).AddDays(index).ToString("yyyyMMdd",
                System.Globalization.CultureInfo.InvariantCulture) + ".log", Produce(owner, 1));
        }
        await File.WriteAllTextAsync(Path.Combine(fixture.LocalRoot, "Logs", "kora-99999999.log"), "invalid",
            TestContext.Current.CancellationToken);
        var exact = await service.QueryAsync(query, null, TestContext.Current.CancellationToken);
        exact.Status.Should().Be(EvidencePageStatus.Available);
        exact.DailyReport!.Files.Should().Be(32);
        Write(fixture, "kora-20260202.log", Produce(owner, 1));
        (await service.QueryAsync(query, null, TestContext.Current.CancellationToken)).Status.Should().Be(EvidencePageStatus.ScanLimitReached);
        var checkpoint = new EvidenceReadCheckpoint(new(0, 0, 0, 0, DailySnapshotId: exact.DailyReport.SnapshotId), null);
        var expired = await reader.ReadAsync(query, checkpoint, inspection.Request, DateTimeOffset.UtcNow.AddMinutes(16),
            TestContext.Current.CancellationToken);
        expired.Status.Should().Be(EvidencePageStatus.SnapshotExpired);
    }

    [WindowsFact]
    public async Task Cancellation_deadline_and_host_admission_fail_closed_and_release_handles()
    {
        using var fixture = new OwnedStorageFixture();
        using var listener = Listen();
        Write(fixture, "kora-20261007.log", Produce(HostRequest.Create(RequestOrigin.LocalUi), 2));
        var reader = new WindowsDailyEvidenceReader(fixture);
        var request = HostRequest.Create(RequestOrigin.LocalUi);
        var noHost = async () => await reader.ReadAsync(new(), null, request, DateTimeOffset.UtcNow, TestContext.Current.CancellationToken);
        await noHost.Should().ThrowAsync<InvalidOperationException>();
        using (var inspection = Root())
        {
            var mismatch = async () => await reader.ReadAsync(new(), null, request, DateTimeOffset.UtcNow, TestContext.Current.CancellationToken);
            await mismatch.Should().ThrowAsync<InvalidOperationException>();
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            var cancel = async () => await reader.ReadAsync(new(), null, inspection.Request, DateTimeOffset.UtcNow, cancelled.Token);
            await cancel.Should().ThrowAsync<OperationCanceledException>();
            var timed = new WindowsDailyEvidenceReader(fixture, new DeadlineClock());
            var page = await timed.ReadAsync(new() { Source = EvidenceSource.DailyLog }, null, inspection.Request,
                DateTimeOffset.UtcNow, TestContext.Current.CancellationToken);
            page.Status.Should().Be(EvidencePageStatus.TimedOut);
            page.Candidates.Should().BeEmpty();
        }
        using (var system = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.HostSystem),
            HostActivityLayer.Desktop, HostOperation.Evidence))
        {
            var denied = async () => await reader.ReadAsync(new(), null, system.Request, DateTimeOffset.UtcNow, TestContext.Current.CancellationToken);
            await denied.Should().ThrowAsync<InvalidOperationException>();
        }
        using var exclusive = new FileStream(Log(fixture), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        exclusive.Length.Should().BeGreaterThan(0);
    }

    [WindowsFact]
    public async Task Snapshot_and_citations_cannot_cross_viewer_sessions_or_replacement_sources()
    {
        using var fixture = new OwnedStorageFixture();
        using var listener = Listen();
        var owner = HostRequest.Create(RequestOrigin.LocalUi);
        Write(fixture, "kora-20261007.log", Produce(owner, 2));
        var reader = new WindowsDailyEvidenceReader(fixture);
        EvidenceReadBatch first;
        using (var inspection = Root())
        {
            first = await reader.ReadAsync(new() { Source = EvidenceSource.DailyLog, Limit = 1 }, null,
                inspection.Request, DateTimeOffset.UtcNow, TestContext.Current.CancellationToken);
        }
        using var foreign = Root();
        var checkpoint = new EvidenceReadCheckpoint(first.Snapshot, first.Candidates[0].Position);
        var page = await reader.ReadAsync(new() { Source = EvidenceSource.DailyLog, Limit = 1 }, checkpoint,
            foreign.Request, DateTimeOffset.UtcNow, TestContext.Current.CancellationToken);
        page.Status.Should().Be(EvidencePageStatus.SnapshotExpired);
        File.Move(Log(fixture), Log(fixture) + ".old");
        Write(fixture, "kora-20261007.log", Produce(owner, 2));
        var service = Service(reader);
        var old = await service.QueryAsync(new() { Record = first.Candidates[0].Record.Reference }, null, TestContext.Current.CancellationToken);
        old.Status.Should().Be(EvidencePageStatus.MissingOrRemoved);
    }

    internal static DiagnosticEnvelope[] Produce(HostRequest request, int count)
    {
        using var producer = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request);
        var now = new DateTimeOffset(2026, 10, 7, 0, 0, 0, TimeSpan.Zero);
        return Enumerable.Range(0, count).Select(index => new DiagnosticEnvelope(1, new(Guid.NewGuid()), now,
            index, null, "Information", "daily.test", "Observed {Count}",
            new Dictionary<string, EvidenceValue>(StringComparer.Ordinal)
            {
                ["Count"] = new(EvidenceValueKind.WholeNumber, index.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                ["SecretValue"] = new(EvidenceValueKind.Text, "never search this"),
                ["SecurityAudit"] = new(EvidenceValueKind.Boolean, "true"),
            }, [], TraceSnapshot.Capture(producer.Activity), request, null)).ToArray();
    }

    internal static void Write(OwnedStorageFixture fixture, string name, IEnumerable<DiagnosticEnvelope> envelopes)
    {
        var directory = Path.Combine(fixture.LocalRoot, "Logs");
        if (!Directory.Exists(directory))
        {
            using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            RestrictedStorageDirectory.CreateRestrictedDirectory(directory, identity.User!);
        }
        var path = Path.Combine(directory, name);
        using (var logger = new LoggerConfiguration().MinimumLevel.Verbose()
            .WriteTo.File(new JsonFormatter(renderMessage: true), path).CreateLogger())
        {
            var sink = new Kora.FileEvidenceSink(logger, new FileEvidenceHealth());
            foreach (var envelope in envelopes) { sink.WriteDiagnostic(envelope); }
        }
        OwnFile(path);
    }

    private static void OwnFile(string path)
    {
        using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
        var file = new FileInfo(path);
        var security = file.GetAccessControl();
        security.SetOwner(identity.User!);
        file.SetAccessControl(security);
    }

    internal static string Line(DiagnosticEnvelope envelope) => Outer(WindowsSqliteEvidenceSink.EncodeDailyDiagnostic(envelope));
    private static string Outer(string payload) => JsonSerializer.Serialize(new
    {
        Timestamp = new DateTimeOffset(2026, 10, 7, 0, 0, 0, TimeSpan.Zero),
        Level = "Information", MessageTemplate = "Observed {Count}",
        Properties = new { EvidenceAuthority = "Diagnostic", EvidenceEnvelope = payload },
    }) + "\n";
    private static string Log(OwnedStorageFixture fixture) => Path.Combine(fixture.LocalRoot, "Logs", "kora-20261007.log");
    internal static DurableEvidenceQuery Service(IEvidenceReader reader) =>
        new(reader, new Access(), TimeProvider.System, NullLogger<DurableEvidenceQuery>.Instance);
    internal static ActivityListener Listen()
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
        };
        ActivitySource.AddActivityListener(listener);
        return listener;
    }
    private static HostActivity Root() =>
        HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Desktop, HostOperation.Evidence);
    private sealed class Access : IEvidenceQueryAccess { public bool CanInspect => true; }
    private sealed class DeadlineClock : TimeProvider
    {
        private long calls;
        public override long TimestampFrequency => 1;
        public override long GetTimestamp() => Interlocked.Increment(ref calls) * 5;
    }
    private sealed class HookClock(long at, Action action) : TimeProvider
    {
        private long calls;
        public override long GetTimestamp()
        {
            if (Interlocked.Increment(ref calls) == at) { action(); }
            return 0;
        }
    }
}
