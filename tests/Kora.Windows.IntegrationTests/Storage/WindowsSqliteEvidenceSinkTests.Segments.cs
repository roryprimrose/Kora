using System.Text.Json;
using AwesomeAssertions;
using Kora.Core.Diagnostics;
using Kora.Windows.IntegrationTests.Audio;
using Kora.Windows.Storage;

namespace Kora.Windows.IntegrationTests.Storage;

public sealed partial class WindowsSqliteEvidenceSinkTests
{
    [WindowsFact]
    public void Segmented_cutover_preserves_legacy_bytes_and_audits_across_rotation_and_reopen()
    {
        using var paths = new OwnedStorageFixture();
        using var listener = Listen();
        var audit = Audit();
        using var host = HostActivity.BeginAudit(Request(), audit);
        var legacy = new WindowsSqliteEvidenceSink(paths);
        legacy.WriteAudit(new(Diagnostic(host), audit));
        var legacyBytes = File.ReadAllBytes(DatabasePath(paths));
        var legacyJournal = File.ReadAllBytes(DatabasePath(paths) + "-journal");
        using (var store = SmallSegmentStore(paths))
        {
            store.Initialize();
            store.WriteAudit(new(Diagnostic(host), audit));
            store.WriteAudit(new(Diagnostic(host), audit.WithOutcome(Kora.Core.Auditing.SecurityAuditOutcome.Succeeded)));
            store.ReadSegments(set =>
            {
                set.Segments[0].Id.Should().Be("legacy-v1");
                var total = 0L;
                foreach (var segment in set.Segments)
                {
                    segment.AuditBase.Should().Be(total);
                    total += segment.Sink.ReadSegmentState(TestContext.Current.CancellationToken).AuditCount;
                }
                total.Should().Be(3);
                return true;
            }, TestContext.Current.CancellationToken);
            File.ReadAllBytes(DatabasePath(paths)).Should().Equal(legacyBytes);
            File.ReadAllBytes(DatabasePath(paths) + "-journal").Should().Equal(legacyJournal);
        }
        using var reopened = SmallSegmentStore(paths);
        reopened.Initialize();
        reopened.ReadSegments(set => set.Segments.Sum(segment =>
            segment.Sink.ReadSegmentState(TestContext.Current.CancellationToken).AuditCount),
            TestContext.Current.CancellationToken).Should().Be(3);
    }

    [WindowsFact]
    public void Prepared_segment_snapshots_keep_emission_context_after_rotation_without_live_authority()
    {
        using var paths = new OwnedStorageFixture();
        using var listener = Listen();
        using var store = SmallSegmentStore(paths);
        store.Initialize();
        IPreparedEvidenceWrite prepared;
        Kora.Core.Hosting.HostRequest request;
        using (var host = HostActivity.BeginRoot(Request(), HostActivityLayer.Application, HostOperation.Request))
        {
            request = host.Request;
            prepared = store.PrepareDiagnosticWrite(Diagnostic(host));
        }
        HostActivity.Current.Should().BeNull();
        store.WriteBatch([prepared]);
        store.ReadSegments(set =>
        {
            using var lease = set.Segments[^1].Sink.AcquireReadLease(TestContext.Current.CancellationToken);
            using var connection = set.Segments[^1].Sink.OpenReadOnly(TestContext.Current.CancellationToken);
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT session_id FROM application_log_events;";
            command.ExecuteScalar().Should().Be(request.SessionId.Value.ToString("D"));
            return true;
        }, TestContext.Current.CancellationToken);
    }

    [WindowsFact]
    public void Duplicate_prepared_identity_after_rotation_is_rejected_before_inventory_changes()
    {
        using var paths = new OwnedStorageFixture();
        using var listener = Listen();
        using var host = HostActivity.BeginRoot(Request(), HostActivityLayer.Application, HostOperation.Request);
        using var store = SmallSegmentStore(paths);
        store.Initialize();
        var prepared = store.PrepareDiagnosticWrite(Diagnostic(host));
        prepared.Write();
        store.WriteDiagnostic(Diagnostic(host));
        var inventoryPath = Path.Combine(paths.LocalRoot, WindowsSegmentedEvidenceSink.PartitionName, "inventory.json");
        var before = File.ReadAllBytes(inventoryPath);

        var duplicate = () => prepared.Write();

        duplicate.Should().Throw<IOException>().WithInnerException<InvalidDataException>();
        File.ReadAllBytes(inventoryPath).Should().Equal(before);
        var held = () => store.WriteDiagnostic(Diagnostic(host));
        held.Should().Throw<IOException>();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Unregistered_segment_or_incomplete_inventory_publication_is_preserved_and_rejected(bool candidate)
    {
        using var paths = new OwnedStorageFixture();
        using (var store = SmallSegmentStore(paths)) { store.Initialize(); }
        var root = Path.Combine(paths.LocalRoot, WindowsSegmentedEvidenceSink.PartitionName);
        var unexpected = candidate ? Path.Combine(root, "Artifacts", "segment-000001")
            : Path.Combine(root, "inventory.pending");
        if (candidate) { Directory.CreateDirectory(unexpected); }
        else { File.WriteAllText(unexpected, "{}"); }
        var before = File.ReadAllBytes(Path.Combine(root, "inventory.json"));
        using var reopened = SmallSegmentStore(paths);

        var initialize = () => reopened.Initialize();

        initialize.Should().Throw<InvalidDataException>();
        File.ReadAllBytes(Path.Combine(root, "inventory.json")).Should().Equal(before);
        Path.Exists(unexpected).Should().BeTrue();
    }

    [WindowsFact]
    public void Rewritten_legacy_history_is_not_readmitted()
    {
        using var paths = new OwnedStorageFixture();
        using var listener = Listen();
        using var host = HostActivity.BeginRoot(Request(), HostActivityLayer.Application, HostOperation.Request);
        var legacy = new WindowsSqliteEvidenceSink(paths);
        legacy.WriteDiagnostic(Diagnostic(host));
        using (var store = SmallSegmentStore(paths)) { store.Initialize(); }
        Mutate(paths, "UPDATE application_log_events SET event_name='Changed';");
        var before = File.ReadAllBytes(DatabasePath(paths));
        using var reopened = SmallSegmentStore(paths);

        var initialize = () => reopened.Initialize();

        initialize.Should().Throw<InvalidDataException>();
        File.ReadAllBytes(DatabasePath(paths)).Should().Equal(before);
    }

    [WindowsFact]
    public void Segmented_shutdown_releases_sealed_pins_and_inventory_lease_and_refuses_late_capture()
    {
        using var paths = new OwnedStorageFixture();
        using var listener = Listen();
        using var host = HostActivity.BeginRoot(Request(), HostActivityLayer.Application, HostOperation.Request);
        var store = SmallSegmentStore(paths);
        store.Initialize();
        store.WriteDiagnostic(Diagnostic(host));
        var prepared = store.PrepareDiagnosticWrite(Diagnostic(host));
        store.Dispose();
        var late = () => prepared.Write();
        late.Should().Throw<ObjectDisposedException>();
        using var reopened = SmallSegmentStore(paths);
        reopened.Initialize();
    }

    [WindowsFact]
    public void Invalid_inventory_capacity_and_sequence_are_rejected_without_reset()
    {
        using var paths = new OwnedStorageFixture();
        using (var store = SmallSegmentStore(paths)) { store.Initialize(); }
        var path = Path.Combine(paths.LocalRoot, WindowsSegmentedEvidenceSink.PartitionName, "inventory.json");
        var json = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!;
        json["Segments"]![0]!["AuditBase"] = 7;
        File.WriteAllText(path, json.ToJsonString());
        using var reopened = SmallSegmentStore(paths);
        var initialize = () => reopened.Initialize();
        initialize.Should().Throw<InvalidDataException>();
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        document.RootElement.GetProperty("Segments")[0]
            .GetProperty("AuditBase").GetInt64().Should().Be(7);
    }

    [WindowsFact]
    public void Missing_registered_segment_is_not_recreated_on_readmission()
    {
        using var paths = new OwnedStorageFixture();
        using (var store = SmallSegmentStore(paths)) { store.Initialize(); }
        var root = Path.Combine(paths.LocalRoot, WindowsSegmentedEvidenceSink.PartitionName);
        var segment = Path.Combine(root, "Artifacts", "segment-000000");
        var renamed = Path.Combine(root, "Artifacts", "missing-synthetic-segment");
        Directory.Move(segment, renamed);
        using var reopened = SmallSegmentStore(paths);

        var initialize = () => reopened.Initialize();

        initialize.Should().Throw<InvalidDataException>();
        Directory.Exists(segment).Should().BeFalse();
        Directory.Exists(renamed).Should().BeTrue();
    }

    [WindowsFact]
    public void Segment_inventory_capacity_holds_new_delivery_without_deleting_retained_history()
    {
        using var paths = new OwnedStorageFixture();
        using var listener = Listen();
        using var host = HostActivity.BeginRoot(Request(), HostActivityLayer.Application, HostOperation.Request);
        using var store = SmallSegmentStore(paths);
        store.Initialize();
        for (var index = 1; index < WindowsSegmentedEvidenceSink.MaximumSegments; index++)
        {
            store.WriteDiagnostic(Diagnostic(host));
        }
        var path = Path.Combine(paths.LocalRoot, WindowsSegmentedEvidenceSink.PartitionName, "inventory.json");
        var before = File.ReadAllBytes(path);

        var full = () => store.WriteDiagnostic(Diagnostic(host));

        full.Should().Throw<IOException>();
        File.ReadAllBytes(path).Should().Equal(before);
        Directory.GetDirectories(Path.Combine(paths.LocalRoot, WindowsSegmentedEvidenceSink.PartitionName,
            "Artifacts")).Length.Should().Be(WindowsSegmentedEvidenceSink.MaximumSegments);
    }

    [WindowsFact]
    public void Segmented_retention_keeps_global_deletion_bounds_and_reseals_history_for_readmission()
    {
        using var paths = new OwnedStorageFixture();
        using var listener = Listen();
        using var host = HostActivity.BeginRoot(Request(), HostActivityLayer.Application, HostOperation.Request);
        using (var store = SmallSegmentStore(paths))
        {
            store.Initialize();
            store.WriteDiagnostic(Diagnostic(host));
            store.WriteDiagnostic(Diagnostic(host));
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            var cancel = () => store.PruneOrdinaryDiagnostics(DateTimeOffset.UtcNow, cancelled.Token);
            cancel.Should().Throw<OperationCanceledException>();
            var total = 0;
            DiagnosticRetentionBatch result;
            do
            {
                result = store.PruneOrdinaryDiagnostics(DateTimeOffset.UtcNow.AddDays(366),
                    TestContext.Current.CancellationToken);
                result.Logs.Should().BeLessThanOrEqualTo(WindowsSqliteDiagnosticRetention.MaximumLogs);
                result.Spans.Should().BeLessThanOrEqualTo(WindowsSqliteDiagnosticRetention.MaximumSpans);
                total += result.Logs;
            } while (result.HasMore);
            total.Should().Be(2);
        }
        using var reopened = SmallSegmentStore(paths);
        reopened.Initialize();
        reopened.ReadSegments(set => set.Segments.Count, TestContext.Current.CancellationToken).Should().Be(3);
    }

    private static WindowsSegmentedEvidenceSink SmallSegmentStore(OwnedStorageFixture paths) =>
        new(paths, null, null, null, null, maximumPages: 256, rotationPages: 1);

    [WindowsFact]
    public async Task Ordinary_snapshot_admission_does_not_wait_for_segment_storage_gate()
    {
        using var paths = new OwnedStorageFixture();
        using var listener = Listen();
        using var host = HostActivity.BeginRoot(Request(), HostActivityLayer.Application, HostOperation.Request);
        using var store = SmallSegmentStore(paths);
        store.Initialize();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var blocked = Task.Run(() => store.ReadSegments(_ =>
        {
            entered.SetResult();
#pragma warning disable VSTHRD002 // The fake holds the synchronous store gate until the test releases it.
            release.Task.GetAwaiter().GetResult();
#pragma warning restore VSTHRD002
            return true;
        }, TestContext.Current.CancellationToken), TestContext.Current.CancellationToken);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            var capture = Task.Run(() => store.PrepareDiagnosticWrite(Diagnostic(host)),
                TestContext.Current.CancellationToken);
            var prepared = await capture.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            prepared.EvidenceId.Value.Should().NotBeEmpty();
            blocked.IsCompleted.Should().BeFalse();
        }
        finally
        {
            release.TrySetResult();
            await blocked;
        }
    }

    [WindowsFact]
    public void Unexpected_segment_page_exhaustion_rolls_back_and_holds_new_writes_without_replay()
    {
        using var paths = new OwnedStorageFixture();
        using var listener = Listen();
        using var host = HostActivity.BeginRoot(Request(), HostActivityLayer.Application, HostOperation.Request);
        using var store = new WindowsSegmentedEvidenceSink(paths, null, null, null, null,
            maximumPages: 64, rotationPages: 63);
        store.Initialize();
        var properties = new Dictionary<string, EvidenceValue>(StringComparer.Ordinal);
        for (var index = 0; index < 32; index++)
        {
            properties.Add($"field{index}", new(EvidenceValueKind.Text, new string('x', 1024)));
        }
        var writes = Enumerable.Range(0, 32).Select(_ =>
            store.PrepareDiagnosticWrite(Diagnostic(host) with { Properties = properties })).ToArray();
        var path = Path.Combine(paths.LocalRoot, WindowsSegmentedEvidenceSink.PartitionName,
            "Artifacts", "segment-000000", "evidence.db");
        var before = File.ReadAllBytes(path);
        var write = () => store.WriteBatch(writes);

        write.Should().Throw<IOException>();
        File.ReadAllBytes(path).Should().Equal(before);
        var held = () => store.WriteDiagnostic(Diagnostic(host));
        held.Should().Throw<IOException>();
    }
}
