using System.Text.Json;
using AwesomeAssertions;
using Kora.Core.Diagnostics;
using Kora.Windows.IntegrationTests.Audio;
using Kora.Windows.Storage;

namespace Kora.Windows.IntegrationTests.Storage;

public sealed partial class WindowsSqliteEvidenceSinkTests
{
    [WindowsFact]
    public void Segmented_legacy_cutover_and_readmission_use_initial_integrity_budget_not_query_budget()
    {
        using var paths = new OwnedStorageFixture();
        var seed = Bootstrap();
        new WindowsSqliteEvidenceSink(paths).WriteDiagnostic(seed);
        SeedDiagnosticHistory(paths, seed, 2048);
        var database = File.ReadAllBytes(DatabasePath(paths));
        var journal = File.ReadAllBytes(DatabasePath(paths) + "-journal");
        for (var admission = 0; admission < 2; admission++)
        {
            var clock = new JumpClock();
            using var store = AdmissionStore(paths, clock);

            store.Initialize();

            clock.TimestampReads.Should().BeGreaterThan(1, "the real read-only integrity path must consult the fake clock");
            File.ReadAllBytes(DatabasePath(paths)).Should().Equal(database);
            File.ReadAllBytes(DatabasePath(paths) + "-journal").Should().Equal(journal);
        }
    }

    [WindowsFact]
    public void Segmented_cold_start_admits_123000_legacy_spans_without_rewriting_history()
    {
        using var paths = new OwnedStorageFixture();
        using var listener = Listen();
        using var host = HostActivity.BeginRoot(Request(), HostActivityLayer.Application, HostOperation.Request);
        var now = DateTimeOffset.UtcNow;
        var activity = new CompletedActivityEnvelope(new(Guid.NewGuid()), TraceSnapshot.Capture(host.Activity)!,
            host.Request, now, now, HostOperationOutcome.Completed, []);
        new WindowsSqliteEvidenceSink(paths).WriteActivity(activity);
        SeedEvidenceHistory(paths, activity.EvidenceId.Value, 123000, "activity_spans");
        var before = File.ReadAllBytes(DatabasePath(paths));
        using var store = AdmissionStore(paths, new JumpClock());

        store.Initialize();

        File.ReadAllBytes(DatabasePath(paths)).Should().Equal(before);
        var root = Path.Combine(paths.LocalRoot, WindowsSegmentedEvidenceSink.PartitionName);
        using var inventory = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root, "inventory.json")));
        inventory.RootElement.GetProperty("Segments").GetArrayLength().Should().Be(2);
    }

    [WindowsFact]
    public void Initial_legacy_integrity_timeout_creates_no_inventory_and_never_replaces_history()
    {
        using var paths = new OwnedStorageFixture();
        var seed = Bootstrap();
        new WindowsSqliteEvidenceSink(paths).WriteDiagnostic(seed);
        SeedDiagnosticHistory(paths, seed, 2048);
        var database = File.ReadAllBytes(DatabasePath(paths));
        var journal = File.ReadAllBytes(DatabasePath(paths) + "-journal");
        var clock = new ValidationClock(TimeSpan.FromSeconds(6));
        using (var store = AdmissionStore(paths, clock))
        {
            var initialize = () => store.Initialize();

            initialize.Should().Throw<IOException>().WithMessage("*interrupted*");
            clock.TimestampReads.Should().Be(6, "initial admission is bounded at thirty seconds");
            var retry = () => store.Initialize();
            retry.Should().Throw<IOException>().WithMessage("*previously failed*");
        }

        Directory.Exists(Path.Combine(paths.LocalRoot, WindowsSegmentedEvidenceSink.PartitionName)).Should().BeFalse();
        File.ReadAllBytes(DatabasePath(paths)).Should().Equal(database);
        File.ReadAllBytes(DatabasePath(paths) + "-journal").Should().Equal(journal);
        using var databaseWriter = new FileStream(DatabasePath(paths), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        using var journalWriter = new FileStream(DatabasePath(paths) + "-journal", FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    }

    [WindowsFact]
    public void Existing_inventory_without_committed_metadata_is_preserved_and_explicitly_unavailable()
    {
        using var paths = new OwnedStorageFixture();
        new WindowsSqliteEvidenceSink(paths).WriteDiagnostic(Bootstrap());
        var database = File.ReadAllBytes(DatabasePath(paths));
        var directory = new RestrictedStorageDirectory(paths, includeKeys: false,
            partitionName: WindowsSegmentedEvidenceSink.PartitionName);
        directory.CreateNew();
        using (directory.AcquireBoundedLease(requireExisting: false, TestContext.Current.CancellationToken)) { }
        var entries = Directory.GetFileSystemEntries(directory.Root);
        using var store = AdmissionStore(paths, new JumpClock());

        var initialize = () => store.Initialize();

        initialize.Should().Throw<InvalidDataException>().WithMessage("*inventory*incomplete*");
        Directory.GetFileSystemEntries(directory.Root).Should().BeEquivalentTo(entries);
        Directory.GetFileSystemEntries(directory.Artifacts).Should().BeEmpty();
        File.ReadAllBytes(DatabasePath(paths)).Should().Equal(database);
    }

    [WindowsFact]
    public void Ordinary_read_only_integrity_still_interrupts_at_five_seconds()
    {
        using var paths = new OwnedStorageFixture();
        var seed = Bootstrap();
        new WindowsSqliteEvidenceSink(paths).WriteDiagnostic(seed);
        SeedDiagnosticHistory(paths, seed, 2048);
        var clock = new JumpClock();
        var sink = new WindowsSqliteEvidenceSink(paths, WindowsSqliteEvidenceSink.PartitionName,
            null, null, null, null, null, null, clock);
        using var lease = sink.AcquireReadLease(TestContext.Current.CancellationToken);
        var read = () =>
        {
            using var connection = sink.OpenReadOnly(TestContext.Current.CancellationToken);
        };

        read.Should().Throw<IOException>().WithMessage("*interrupted*");
        clock.TimestampReads.Should().Be(2);
    }

    [Theory]
    [InlineData("state", true)]
    [InlineData("state", false)]
    [InlineData("duplicate", true)]
    [InlineData("duplicate", false)]
    [InlineData("identities", true)]
    public void Segment_admission_helpers_use_initial_budget_without_relaxing_ordinary_reads(
        string operation, bool initialAdmission)
    {
        using var paths = new OwnedStorageFixture();
        var seed = Bootstrap();
        new WindowsSqliteEvidenceSink(paths).WriteDiagnostic(seed);
        SeedDiagnosticHistory(paths, seed, 2048);
        var clock = new JumpClock();
        var sink = new WindowsSqliteEvidenceSink(paths, WindowsSqliteEvidenceSink.PartitionName,
            null, null, null, null, null, null, clock);
        var visited = 0;
        var read = () =>
        {
            switch (operation)
            {
                case "state":
                    sink.ReadSegmentState(initialAdmission, TestContext.Current.CancellationToken).AuditCount.Should().Be(0);
                    break;
                case "duplicate":
                    sink.ContainsEvidence(seed.EvidenceId.Value, initialAdmission,
                        TestContext.Current.CancellationToken).Should().BeTrue();
                    break;
                case "identities":
                    sink.VisitAdmissionEvidenceIds(_ => visited++, TestContext.Current.CancellationToken);
                    visited.Should().Be(2048);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(operation));
            }
        };

        if (initialAdmission) { read.Should().NotThrow(); }
        else { read.Should().Throw<IOException>().WithMessage("*interrupted*"); }
        clock.TimestampReads.Should().BeGreaterThan(1);
    }

    [WindowsFact]
    public void Legacy_projection_failure_before_cutover_creates_no_inventory_and_releases_pins()
    {
        using var paths = new OwnedStorageFixture();
        new WindowsSqliteEvidenceSink(paths).WriteDiagnostic(Bootstrap());
        Mutate(paths, "UPDATE application_log_events SET event_name='Changed';");
        var before = File.ReadAllBytes(DatabasePath(paths));
        using var store = AdmissionStore(paths, new JumpClock());

        var initialize = () => store.Initialize();

        initialize.Should().Throw<InvalidDataException>();
        Directory.Exists(Path.Combine(paths.LocalRoot, WindowsSegmentedEvidenceSink.PartitionName)).Should().BeFalse();
        File.ReadAllBytes(DatabasePath(paths)).Should().Equal(before);
        using var writer = new FileStream(DatabasePath(paths), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Read_only_admission_budget_never_overrides_cancellation_or_creates_storage(bool initialAdmission)
    {
        using var paths = new OwnedStorageFixture();
        var sink = new WindowsSqliteEvidenceSink(paths);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var read = () =>
        {
            using var connection = sink.OpenReadOnly(initialAdmission, cancellation.Token);
        };

        read.Should().Throw<OperationCanceledException>().Which.CancellationToken.Should().Be(cancellation.Token);
        Directory.Exists(Path.Combine(paths.LocalRoot, WindowsSqliteEvidenceSink.PartitionName)).Should().BeFalse();
    }

    private static WindowsSegmentedEvidenceSink AdmissionStore(OwnedStorageFixture paths, TimeProvider clock) =>
        new(paths, null, null, null, null, maximumPages: 256, rotationPages: 128, integrityTimeProvider: clock);
}
