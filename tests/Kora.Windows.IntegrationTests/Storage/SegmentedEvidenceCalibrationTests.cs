using System.Diagnostics;
using AwesomeAssertions;
using Kora.Core.Auditing;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Windows.IntegrationTests.Audio;
using Kora.Windows.Storage;

namespace Kora.Windows.IntegrationTests.Storage;

public sealed class SegmentedEvidenceCalibrationTests(ITestOutputHelper output)
{
    [WindowsFact]
    public void Default_rotation_checks_seals_and_publishes_a_full_active_segment_before_audit_receipt()
    {
        using var paths = new OwnedStorageFixture();
        using var listener = WindowsSqliteEvidenceSinkTests.Listen();
        var audit = new SecurityAuditEvent(Guid.NewGuid(), SecurityAuditCategory.ProtectedOperation,
            "synthetic.operation", SecurityAuditOutcome.Requested, SecurityAuditInitiator.LocalUser, "synthetic.target");
        using var host = HostActivity.BeginAudit(new(new(Guid.NewGuid()), new(Guid.NewGuid()), new(Guid.NewGuid()),
            RequestOrigin.LocalUi, null), audit);
        using var store = new WindowsSegmentedEvidenceSink(paths);
        store.Initialize();
        var path = Path.Combine(paths.LocalRoot, WindowsSegmentedEvidenceSink.PartitionName,
            "Artifacts", "segment-000000", "evidence.db");
        var properties = new Dictionary<string, EvidenceValue>(StringComparer.Ordinal);
        for (var index = 0; index < 32; index++)
        {
            properties.Add($"field{index}", new(EvidenceValueKind.Text, new string('x', 1024)));
        }
        var batches = 0;
        while (new FileInfo(path).Length < WindowsSegmentedEvidenceSink.RotationPages * 4096L)
        {
            batches.Should().BeLessThan(32);
            store.WriteBatch(Enumerable.Range(0, 32).Select(_ =>
                store.PrepareDiagnosticWrite(WindowsSqliteEvidenceSinkTests.Diagnostic(host) with
                {
                    Properties = properties,
                })).ToArray());
            batches++;
        }
        var fullBytes = new FileInfo(path).Length;
        fullBytes.Should().BeLessThanOrEqualTo(WindowsSegmentedEvidenceSink.MaximumSegmentPages * 4096L);
        var measured = Stopwatch.StartNew();
        store.WriteAudit(new(WindowsSqliteEvidenceSinkTests.Diagnostic(host), audit));
        store.WriteAudit(new(WindowsSqliteEvidenceSinkTests.Diagnostic(host), audit.WithOutcome(SecurityAuditOutcome.Succeeded)));
        measured.Stop();
        store.ReadSegments(set =>
        {
            set.Segments.Count.Should().Be(2);
            set.InventoryGeneration.Should().Be(2);
            set.Segments[0].Sink.ReadSegmentState(TestContext.Current.CancellationToken).AuditCount.Should().Be(0);
            set.Segments[1].Sink.ReadSegmentState(TestContext.Current.CancellationToken).AuditCount.Should().Be(2);
            return true;
        }, TestContext.Current.CancellationToken);
        output.WriteLine("Synthetic rotation: sealed bytes {0}; records {1}; default page cap {2}; two checked audits including full seal and inventory publication {3} ms.",
            fullBytes, batches * 32, WindowsSegmentedEvidenceSink.MaximumSegmentPages, measured.Elapsed.TotalMilliseconds);
    }

    [WindowsFact]
    public void Checked_active_audits_are_bounded_independently_of_preserved_legacy_history()
    {
        using var paths = new OwnedStorageFixture();
        using var listener = WindowsSqliteEvidenceSinkTests.Listen();
        var audit = new SecurityAuditEvent(Guid.NewGuid(), SecurityAuditCategory.ProtectedOperation,
            "synthetic.operation", SecurityAuditOutcome.Requested, SecurityAuditInitiator.LocalUser, "synthetic.target");
        using var host = HostActivity.BeginAudit(new(new(Guid.NewGuid()), new(Guid.NewGuid()), new(Guid.NewGuid()),
            RequestOrigin.LocalUi, null), audit);
        var legacy = new WindowsSqliteEvidenceSink(paths);
        var properties = new Dictionary<string, EvidenceValue>(StringComparer.Ordinal);
        for (var index = 0; index < 32; index++)
        {
            properties.Add($"field{index}", new(EvidenceValueKind.Text, new string('x', 1024)));
        }
        for (var batch = 0; batch < 8; batch++)
        {
            legacy.WriteBatch(Enumerable.Range(0, 32).Select(_ =>
                legacy.PrepareDiagnosticWrite(WindowsSqliteEvidenceSinkTests.Diagnostic(host) with
                {
                    Properties = properties,
                })).ToArray());
        }
        var control = Stopwatch.StartNew();
        legacy.WriteAudit(new(WindowsSqliteEvidenceSinkTests.Diagnostic(host), audit));
        legacy.WriteAudit(new(WindowsSqliteEvidenceSinkTests.Diagnostic(host), audit.WithOutcome(SecurityAuditOutcome.Succeeded)));
        control.Stop();
        var legacyPath = WindowsSqliteEvidenceSinkTests.DatabasePath(paths);
        var legacyBytes = File.ReadAllBytes(legacyPath);
        var admission = Stopwatch.StartNew();
        using var store = new WindowsSegmentedEvidenceSink(paths);
        store.Initialize();
        admission.Stop();
        var measured = Stopwatch.StartNew();
        store.WriteAudit(new(WindowsSqliteEvidenceSinkTests.Diagnostic(host), audit));
        store.WriteAudit(new(WindowsSqliteEvidenceSinkTests.Diagnostic(host), audit.WithOutcome(SecurityAuditOutcome.Succeeded)));
        measured.Stop();

        File.ReadAllBytes(legacyPath).Should().Equal(legacyBytes);
        var activePages = store.ReadSegments(set =>
        {
            set.Segments.Count.Should().Be(2);
            set.Segments[1].AuditBase.Should().Be(2);
            var state = set.Segments[1].Sink.ReadSegmentState(TestContext.Current.CancellationToken);
            state.AuditCount.Should().Be(2);
            return state.Pages;
        }, TestContext.Current.CancellationToken);
        activePages.Should().BeLessThan(WindowsSegmentedEvidenceSink.RotationPages);
        output.WriteLine("Synthetic legacy bytes: {0}; legacy rows: 256; checked two-audit control: {1} ms; segmented admission: {2} ms; checked two-audit active: {3} ms; active pages: {4}; page cap: {5}.",
            legacyBytes.Length, control.Elapsed.TotalMilliseconds, admission.Elapsed.TotalMilliseconds,
            measured.Elapsed.TotalMilliseconds, activePages, WindowsSegmentedEvidenceSink.MaximumSegmentPages);
    }
}
