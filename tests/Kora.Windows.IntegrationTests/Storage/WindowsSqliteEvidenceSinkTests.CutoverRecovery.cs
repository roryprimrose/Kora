using System.Security.AccessControl;
using AwesomeAssertions;
using Kora.Windows.IntegrationTests.Audio;
using Kora.Windows.Storage;

namespace Kora.Windows.IntegrationTests.Storage;

public sealed partial class WindowsSqliteEvidenceSinkTests
{
    [WindowsFact]
    public void Explicit_cutover_recovery_completes_in_place_preserving_legacy_and_ownership_marker()
    {
        using var paths = new OwnedStorageFixture();
        new WindowsSqliteEvidenceSink(paths).WriteDiagnostic(Bootstrap());
        var shell = CreateCutoverShell(paths);
        var marker = Path.Combine(paths.LocalRoot, "Coordination", "unclean-owner");
        Directory.CreateDirectory(Path.GetDirectoryName(marker)!);
        File.WriteAllBytes(marker, [1]);
        var original = SnapshotRecoveryFiles(paths);
        var identities = original.Keys.ToDictionary(path => path, RecoveryFileIdentity, StringComparer.Ordinal);
        var authorityChecks = 0;
        using (var store = SmallSegmentStore(paths))
        {
            var receipt = store.RecoverInterruptedFirstCutover(() => authorityChecks++,
                TestContext.Current.CancellationToken);

            receipt.StoreIdentity.Should().NotBeEmpty();
            receipt.Generation.Should().Be(1);
            receipt.RegisteredSegments.Should().Be(2);
            store.ReadSegments(set =>
            {
                set.Segments[0].Id.Should().Be("legacy-v1");
                set.Segments[1].Id.Should().Be("segment-000000");
                set.StoreIdentity.Should().Be(receipt.StoreIdentity.ToString("D"));
                return true;
            }, TestContext.Current.CancellationToken).Should().BeTrue();
        }
        authorityChecks.Should().BeGreaterThan(3);
        foreach (var (path, bytes) in original)
        {
            File.ReadAllBytes(path).Should().Equal(bytes);
            RecoveryFileIdentity(path).Should().Be(identities[path]);
        }
        File.Exists(Path.Combine(shell.Root, "inventory.json")).Should().BeTrue();
        using var reopened = SmallSegmentStore(paths);
        reopened.Initialize();
    }

    [Theory]
    [InlineData("inventory.json")]
    [InlineData("inventory.pending")]
    [InlineData("unexpected")]
    [InlineData("candidate")]
    [InlineData("artifact")]
    [InlineData("nonempty-lock")]
    public void Explicit_cutover_recovery_refuses_any_state_beyond_the_exact_empty_shell(string extra)
    {
        using var paths = new OwnedStorageFixture();
        new WindowsSqliteEvidenceSink(paths).WriteDiagnostic(Bootstrap());
        var shell = CreateCutoverShell(paths);
        switch (extra)
        {
            case "candidate":
                Directory.CreateDirectory(Path.Combine(shell.Artifacts, "segment-000000"));
                break;
            case "artifact":
                File.WriteAllBytes(Path.Combine(shell.Artifacts, "unknown"), [1]);
                break;
            case "nonempty-lock":
                File.WriteAllBytes(Path.Combine(shell.Root, "operation.lock"), [1]);
                break;
            default:
                File.WriteAllBytes(Path.Combine(shell.Root, extra), [1]);
                break;
        }
        var original = SnapshotRecoveryFiles(paths);
        var directories = Directory.GetDirectories(paths.LocalRoot, "*", SearchOption.AllDirectories);
        using var store = SmallSegmentStore(paths);

        var recover = () => store.RecoverInterruptedFirstCutover(static () => { }, TestContext.Current.CancellationToken);

        recover.Should().Throw<InvalidDataException>();
        AssertRecoveryFilesUnchanged(original, paths);
        Directory.GetDirectories(paths.LocalRoot, "*", SearchOption.AllDirectories).Should().BeEquivalentTo(directories);
    }

    [WindowsFact]
    public void Explicit_recovery_requires_existing_shell_and_existing_legacy_never_creating_replacements()
    {
        using var paths = new OwnedStorageFixture();
        using (var missing = SmallSegmentStore(paths))
        {
            var recover = () => missing.RecoverInterruptedFirstCutover(static () => { }, TestContext.Current.CancellationToken);
            recover.Should().Throw<InvalidDataException>().WithMessage("*no existing interrupted*");
        }
        Directory.GetFileSystemEntries(paths.LocalRoot).Should().BeEmpty();
        var shell = CreateCutoverShell(paths);
        var original = SnapshotRecoveryFiles(paths);
        using var noLegacy = SmallSegmentStore(paths);

        var noLegacyRecovery = () => noLegacy.RecoverInterruptedFirstCutover(static () => { }, TestContext.Current.CancellationToken);

        noLegacyRecovery.Should().Throw<InvalidDataException>().WithMessage("*requires retained legacy*");
        AssertRecoveryFilesUnchanged(original, paths);
        Directory.GetFileSystemEntries(shell.Artifacts).Should().BeEmpty();
    }

    [WindowsFact]
    public void Explicit_recovery_uses_initial_integrity_budget_and_leaves_legacy_bytes_unchanged()
    {
        using var paths = new OwnedStorageFixture();
        var seed = Bootstrap();
        new WindowsSqliteEvidenceSink(paths).WriteDiagnostic(seed);
        SeedDiagnosticHistory(paths, seed, 2048);
        CreateCutoverShell(paths);
        var database = File.ReadAllBytes(DatabasePath(paths));
        var clock = new JumpClock();
        using var store = AdmissionStore(paths, clock);

        store.RecoverInterruptedFirstCutover(static () => { }, TestContext.Current.CancellationToken);

        clock.TimestampReads.Should().BeGreaterThan(1);
        File.ReadAllBytes(DatabasePath(paths)).Should().Equal(database);
    }

    [WindowsFact]
    public void Explicit_recovery_timeout_preserves_the_shell_and_releases_legacy_pins()
    {
        using var paths = new OwnedStorageFixture();
        var seed = Bootstrap();
        new WindowsSqliteEvidenceSink(paths).WriteDiagnostic(seed);
        SeedDiagnosticHistory(paths, seed, 2048);
        CreateCutoverShell(paths);
        var original = SnapshotRecoveryFiles(paths);
        using (var store = AdmissionStore(paths, new ValidationClock(TimeSpan.FromSeconds(6))))
        {
            var recover = () => store.RecoverInterruptedFirstCutover(static () => { }, TestContext.Current.CancellationToken);
            recover.Should().Throw<IOException>().WithMessage("*interrupted*");
        }
        AssertRecoveryFilesUnchanged(original, paths);
        using var writer = new FileStream(DatabasePath(paths), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    }

    [WindowsFact]
    public void Explicit_recovery_refuses_changed_legacy_envelopes_without_creating_a_candidate()
    {
        using var paths = new OwnedStorageFixture();
        new WindowsSqliteEvidenceSink(paths).WriteDiagnostic(Bootstrap());
        var shell = CreateCutoverShell(paths);
        Mutate(paths, "UPDATE application_log_events SET event_name='Changed';");
        var original = SnapshotRecoveryFiles(paths);
        using var store = SmallSegmentStore(paths);

        var recover = () => store.RecoverInterruptedFirstCutover(static () => { }, TestContext.Current.CancellationToken);

        recover.Should().Throw<InvalidDataException>();
        AssertRecoveryFilesUnchanged(original, paths);
        Directory.GetFileSystemEntries(shell.Artifacts).Should().BeEmpty();
    }

    [WindowsFact]
    public void Explicit_recovery_does_not_bypass_an_existing_inventory_lease()
    {
        using var paths = new OwnedStorageFixture();
        new WindowsSqliteEvidenceSink(paths).WriteDiagnostic(Bootstrap());
        var shell = CreateCutoverShell(paths);
        var original = SnapshotRecoveryFiles(paths);
        using (shell.AcquireBoundedLease(requireExisting: true, TestContext.Current.CancellationToken))
        using (var store = SmallSegmentStore(paths))
        {
            var recover = () => store.RecoverInterruptedFirstCutover(static () => { }, TestContext.Current.CancellationToken);
            recover.Should().Throw<IOException>().WithMessage("*lease timed out*");
        }
        AssertRecoveryFilesUnchanged(original, paths);
    }

    [WindowsFact]
    public void Explicit_recovery_does_not_bypass_an_existing_legacy_writer()
    {
        using var paths = new OwnedStorageFixture();
        new WindowsSqliteEvidenceSink(paths).WriteDiagnostic(Bootstrap());
        var shell = CreateCutoverShell(paths);
        var original = SnapshotRecoveryFiles(paths);
        using (var writer = new FileStream(DatabasePath(paths), FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite))
        using (var store = SmallSegmentStore(paths))
        {
            var recover = () => store.RecoverInterruptedFirstCutover(static () => { }, TestContext.Current.CancellationToken);
            recover.Should().Throw<IOException>();
        }
        AssertRecoveryFilesUnchanged(original, paths);
        Directory.GetFileSystemEntries(shell.Artifacts).Should().BeEmpty();
    }

    [WindowsFact]
    public void Cancelled_or_unadmitted_recovery_never_creates_storage()
    {
        using var paths = new OwnedStorageFixture();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        using var store = SmallSegmentStore(paths);
        var authorityCalled = false;
        var cancelled = () => store.RecoverInterruptedFirstCutover(() => authorityCalled = true, cancellation.Token);

        cancelled.Should().Throw<OperationCanceledException>().Which.CancellationToken.Should().Be(cancellation.Token);
        authorityCalled.Should().BeFalse();
        var unadmitted = () => store.RecoverInterruptedFirstCutover(
            static () => throw new InvalidOperationException("No operator reservation."), TestContext.Current.CancellationToken);
        unadmitted.Should().Throw<InvalidOperationException>().WithMessage("*operator reservation*");
        Directory.GetFileSystemEntries(paths.LocalRoot).Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Authority_loss_before_or_during_publication_preserves_partial_files_and_refuses_replay(bool staged)
    {
        using var paths = new OwnedStorageFixture();
        new WindowsSqliteEvidenceSink(paths).WriteDiagnostic(Bootstrap());
        var shell = CreateCutoverShell(paths);
        var original = SnapshotRecoveryFiles(paths);
        using (var store = SmallSegmentStore(paths))
        {
            var recover = () => store.RecoverInterruptedFirstCutover(() =>
            {
                var boundary = staged ? File.Exists(Path.Combine(shell.Root, "inventory.pending"))
                    : Directory.EnumerateFileSystemEntries(shell.Artifacts).Any();
                if (boundary) { throw new InvalidOperationException("Operator session became unavailable."); }
            }, TestContext.Current.CancellationToken);

            recover.Should().Throw<InvalidOperationException>().WithMessage("*session became unavailable*");
        }
        foreach (var (path, bytes) in original) { File.ReadAllBytes(path).Should().Equal(bytes); }
        File.Exists(Path.Combine(shell.Root, "inventory.json")).Should().BeFalse();
        File.Exists(Path.Combine(shell.Root, "inventory.pending")).Should().Be(staged);
        Directory.GetDirectories(shell.Artifacts).Should().ContainSingle();
        var partial = SnapshotRecoveryFiles(paths);
        using var retry = SmallSegmentStore(paths);
        var replay = () => retry.RecoverInterruptedFirstCutover(static () => { }, TestContext.Current.CancellationToken);
        replay.Should().Throw<InvalidDataException>();
        AssertRecoveryFilesUnchanged(partial, paths);
    }

    [WindowsFact]
    public void Cancellation_after_durable_staging_preserves_pending_publication_and_the_original_token()
    {
        using var paths = new OwnedStorageFixture();
        new WindowsSqliteEvidenceSink(paths).WriteDiagnostic(Bootstrap());
        var shell = CreateCutoverShell(paths);
        using var cancellation = new CancellationTokenSource();
        using var store = SmallSegmentStore(paths);
        var recover = () => store.RecoverInterruptedFirstCutover(() =>
        {
            if (File.Exists(Path.Combine(shell.Root, "inventory.pending"))) { cancellation.Cancel(); }
        }, cancellation.Token);

        recover.Should().Throw<OperationCanceledException>().Which.CancellationToken.Should().Be(cancellation.Token);
        File.Exists(Path.Combine(shell.Root, "inventory.pending")).Should().BeTrue();
        File.Exists(Path.Combine(shell.Root, "inventory.json")).Should().BeFalse();
    }

    [WindowsFact]
    public void Cancellation_during_actual_legacy_integrity_validation_preserves_files_and_releases_pins()
    {
        using var paths = new OwnedStorageFixture();
        var seed = Bootstrap();
        new WindowsSqliteEvidenceSink(paths).WriteDiagnostic(seed);
        SeedDiagnosticHistory(paths, seed, 2048);
        CreateCutoverShell(paths);
        var original = SnapshotRecoveryFiles(paths);
        using var cancellation = new CancellationTokenSource();
        var clock = new RecoveryCancellationClock(cancellation);
        using (var store = AdmissionStore(paths, clock))
        {
            var recover = () => store.RecoverInterruptedFirstCutover(static () => { }, cancellation.Token);

            recover.Should().Throw<OperationCanceledException>().Which.CancellationToken.Should().Be(cancellation.Token);
        }
        clock.Reads.Should().BeGreaterThan(1);
        AssertRecoveryFilesUnchanged(original, paths);
        using var writer = new FileStream(DatabasePath(paths), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    }

    [WindowsFact]
    public void Explicit_recovery_never_repairs_unprotected_partition_permissions()
    {
        using var paths = new OwnedStorageFixture();
        new WindowsSqliteEvidenceSink(paths).WriteDiagnostic(Bootstrap());
        var shell = CreateCutoverShell(paths);
        var info = new DirectoryInfo(shell.Root);
        var acl = info.GetAccessControl(AccessControlSections.Access);
        acl.SetAccessRuleProtection(isProtected: false, preserveInheritance: true);
        info.SetAccessControl(acl);
        var original = SnapshotRecoveryFiles(paths);
        using var store = SmallSegmentStore(paths);
        var recover = () => store.RecoverInterruptedFirstCutover(static () => { }, TestContext.Current.CancellationToken);

        recover.Should().Throw<UnauthorizedAccessException>();
        AssertRecoveryFilesUnchanged(original, paths);
        info.GetAccessControl(AccessControlSections.Access).AreAccessRulesProtected.Should().BeFalse();
    }

    [Theory]
    [InlineData("operation.lock")]
    [InlineData("evidence.db-journal")]
    public void Explicit_recovery_never_recreates_missing_existing_admission_files(string missing)
    {
        using var paths = new OwnedStorageFixture();
        new WindowsSqliteEvidenceSink(paths).WriteDiagnostic(Bootstrap());
        var shell = CreateCutoverShell(paths);
        var path = string.Equals(missing, "operation.lock", StringComparison.Ordinal)
            ? Path.Combine(shell.Root, missing) : DatabasePath(paths) + "-journal";
        File.Delete(path);
        var original = SnapshotRecoveryFiles(paths);
        using var store = SmallSegmentStore(paths);
        var recover = () => store.RecoverInterruptedFirstCutover(static () => { }, TestContext.Current.CancellationToken);

        if (string.Equals(missing, "operation.lock", StringComparison.Ordinal))
        {
            recover.Should().Throw<InvalidDataException>();
        }
        else
        {
            recover.Should().Throw<FileNotFoundException>();
        }
        AssertRecoveryFilesUnchanged(original, paths);
        File.Exists(path).Should().BeFalse();
    }

    [WindowsFact]
    public void A_failed_recovery_instance_cannot_retry_or_fall_back_to_normal_initialization()
    {
        using var paths = new OwnedStorageFixture();
        new WindowsSqliteEvidenceSink(paths).WriteDiagnostic(Bootstrap());
        var shell = CreateCutoverShell(paths);
        using var pending = shell.CreateNewFile(Path.Combine(shell.Root, "inventory.pending"));
        using var store = SmallSegmentStore(paths);
        var recover = () => store.RecoverInterruptedFirstCutover(static () => { }, TestContext.Current.CancellationToken);

        recover.Should().Throw<InvalidDataException>();
        recover.Should().Throw<InvalidOperationException>().WithMessage("*fresh, uninitialized*");
        var initialize = () => store.Initialize();
        initialize.Should().Throw<IOException>().WithMessage("*previously failed*");
    }

    private static RestrictedStorageDirectory CreateCutoverShell(OwnedStorageFixture paths)
    {
        var shell = new RestrictedStorageDirectory(paths, includeKeys: false,
            partitionName: WindowsSegmentedEvidenceSink.PartitionName);
        shell.CreateNew();
        using (shell.AcquireBoundedLease(requireExisting: false, TestContext.Current.CancellationToken)) { }
        return shell;
    }

    private static Dictionary<string, byte[]> SnapshotRecoveryFiles(OwnedStorageFixture paths) =>
        Directory.GetFiles(paths.LocalRoot, "*", SearchOption.AllDirectories)
            .ToDictionary(path => path, File.ReadAllBytes, StringComparer.Ordinal);

    private static void AssertRecoveryFilesUnchanged(Dictionary<string, byte[]> original, OwnedStorageFixture paths)
    {
        Directory.GetFiles(paths.LocalRoot, "*", SearchOption.AllDirectories).Should().BeEquivalentTo(original.Keys);
        foreach (var (path, bytes) in original) { File.ReadAllBytes(path).Should().Equal(bytes); }
    }

    private static string RecoveryFileIdentity(string path)
    {
        using var handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        return WindowsFileIdentity.Read(handle);
    }

    private sealed class RecoveryCancellationClock(CancellationTokenSource cancellation) : TimeProvider
    {
        public int Reads { get; private set; }

        public override long GetTimestamp()
        {
            if (++Reads == 2) { cancellation.Cancel(); }
            return 0;
        }
    }
}
