using System.Security.Cryptography;
using AwesomeAssertions;
using Kora.Core.Auditing;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Windows.IntegrationTests.Audio;
using Kora.Windows.Storage;

namespace Kora.Windows.IntegrationTests.Storage;

public sealed partial class WindowsSqliteDiagnosticImpactTests
{
    [WindowsFact]
    public async Task Production_inventory_combines_legacy_sealed_and_active_diagnostics_without_audit_counts_or_writes()
    {
        using var fixture = new InteractionStorageFixture();
        Seed(fixture);
        using var store = new WindowsSegmentedEvidenceSink(fixture.Paths, null, fixture.Time, null, null,
            maximumPages: 256, rotationPages: 1);
        store.Initialize();
        using (var host = HostActivity.BeginRoot(fixture.Request, HostActivityLayer.Application, HostOperation.Request))
        {
            store.WriteDiagnostic(Diagnostic(host, fixture.Time.Now));
            store.WriteActivity(new(new(Guid.NewGuid()), TraceSnapshot.Capture(host.Activity)!, host.Request,
                fixture.Time.Now, fixture.Time.Now, HostOperationOutcome.Completed, []));
        }
        var audit = new SecurityAuditEvent(Guid.NewGuid(), SecurityAuditCategory.ConfigurationWrite,
            "fixture.segmented-diagnostic-impact", SecurityAuditOutcome.Succeeded, SecurityAuditInitiator.LocalUser,
            "sentinel-private");
        using (var host = HostActivity.BeginAudit(fixture.Request, audit))
        {
            store.WriteAudit(new(Diagnostic(host, fixture.Time.Now), audit));
        }
        store.ReadSegments(set =>
        {
            set.Segments.Count.Should().BeGreaterThan(2);
            set.Segments[0].Id.Should().Be("legacy-v1");
            return 0;
        }, fixture.Token);
        var before = DiagnosticFiles(fixture.Paths.LocalRoot);
        var cache = store.ReadSegments(set => set.Segments.Sum(segment => segment.Sink.PersistedEnvelopeDecodeCount),
            fixture.Token);
        using var reader = new WindowsSqliteDiagnosticImpactReader(store, fixture.Paths);
        var result = await reader.ReadAsync(new(1), fixture.Time.Now.AddDays(2),
            HostRequest.Create(RequestOrigin.LocalUi), fixture.Token);
        result.Logs.Aggregate!.NewlyDueCount.Should().Be(2);
        result.Spans.Aggregate!.NewlyDueCount.Should().Be(2);
        result.Links.Aggregate!.NewlyDueCount.Should().Be(2);
        DiagnosticRetentionImpact.Validate(result, new(1), fixture.Time.Now.AddDays(2));
        reader.IsCurrent(result).Should().BeTrue();
        DiagnosticFiles(fixture.Paths.LocalRoot).Should().BeEquivalentTo(before);
        store.ReadSegments(set => set.Segments.Sum(segment => segment.Sink.PersistedEnvelopeDecodeCount),
            fixture.Token).Should().Be(cache);
        using (var host = HostActivity.BeginRoot(fixture.Request, HostActivityLayer.Application, HostOperation.Request))
        {
            store.WriteDiagnostic(Diagnostic(host, fixture.Time.Now));
        }
        reader.IsCurrent(result).Should().BeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Inventory_scan_budgets_do_not_reset_at_the_legacy_to_active_partition_boundary(bool bytes)
    {
        using var fixture = new InteractionStorageFixture();
        Seed(fixture, large: bytes);
        CloneLogs(fixture.Paths, bytes ? 79 : 2047);
        IReadOnlyList<IReadOnlyDictionary<string, EvidenceValue>> scopes = [];
        if (bytes)
        {
            using var connection = Open(fixture.Paths);
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT envelope FROM application_log_events LIMIT 1;";
            scopes = WindowsSqliteEvidenceSink.DecodeDiagnostic((string)command.ExecuteScalar()!).Scopes;
        }
        using var store = new WindowsSegmentedEvidenceSink(fixture.Paths, timeProvider: fixture.Time);
        store.Initialize();
        using (var host = HostActivity.BeginRoot(fixture.Request, HostActivityLayer.Application, HostOperation.Request))
        {
            var diagnostic = Diagnostic(host, fixture.Time.Now) with { Scopes = scopes };
            store.WriteDiagnostic(diagnostic);
        }
        var active = store.ReadSegments(set => set.Segments[^1].Id, fixture.Token);
        var activePath = Path.Combine(fixture.Paths.LocalRoot, WindowsSegmentedEvidenceSink.PartitionName,
            "Artifacts", active, "evidence.db");
        using (var connection = OpenDatabase(activePath)) { CloneLogs(connection, bytes ? 79 : 2047); }
        if (!bytes)
        {
            store.ReadDiagnosticImpact(new(1), fixture.Time.Now.AddDays(2), fixture.Token)
                .Logs.Aggregate!.NewlyDueCount.Should().Be(DiagnosticRetentionImpact.MaximumRows);
            using var connection = OpenDatabase(activePath);
            CloneLogs(connection, 1);
        }
        var before = DiagnosticFiles(fixture.Paths.LocalRoot);
        var result = store.ReadDiagnosticImpact(new(1), fixture.Time.Now.AddDays(2), fixture.Token);
        result.Logs.Status.Should().Be(DiagnosticImpactStatus.Incomplete);
        result.Logs.Aggregate.Should().BeNull();
        result.Spans.Status.Should().Be(DiagnosticImpactStatus.Complete);
        result.Links.Status.Should().Be(DiagnosticImpactStatus.Complete);
        DiagnosticFiles(fixture.Paths.LocalRoot).Should().BeEquivalentTo(before);
    }

    [WindowsFact]
    public async Task Segmented_cancellation_and_lost_store_admission_never_publish_a_false_zero()
    {
        using var fixture = new InteractionStorageFixture();
        using var store = new WindowsSegmentedEvidenceSink(fixture.Paths, timeProvider: fixture.Time);
        store.Initialize();
        using var reader = new WindowsSqliteDiagnosticImpactReader(store, fixture.Paths);
        var before = DiagnosticFiles(fixture.Paths.LocalRoot);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        await ((Func<Task>)(async () => await reader.ReadAsync(new(1), fixture.Time.Now,
            HostRequest.Create(RequestOrigin.LocalUi), cancellation.Token))).Should().ThrowAsync<OperationCanceledException>();
        DiagnosticFiles(fixture.Paths.LocalRoot).Should().BeEquivalentTo(before);
        store.Dispose();
        await ((Func<Task>)(async () => await reader.ReadAsync(new(1), fixture.Time.Now,
            HostRequest.Create(RequestOrigin.LocalUi), fixture.Token))).Should().ThrowAsync<ObjectDisposedException>();
    }

    private static Dictionary<string, string> DiagnosticFiles(string root) =>
        Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(path => path.EndsWith(".db", StringComparison.Ordinal)
                || path.EndsWith(".db-journal", StringComparison.Ordinal)
                || string.Equals(Path.GetFileName(path), "inventory.json", StringComparison.Ordinal))
            .ToDictionary(path => path, path => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))),
                StringComparer.Ordinal);
}
