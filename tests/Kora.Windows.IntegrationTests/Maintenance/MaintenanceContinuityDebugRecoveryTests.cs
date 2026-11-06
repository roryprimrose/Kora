#if DEBUG
using System.Security.Principal;
using AwesomeAssertions;
using Kora.Windows.Coordination;

namespace Kora.Windows.IntegrationTests.Maintenance;

public sealed class MaintenanceContinuityDebugRecoveryTests : IDisposable
{
    private readonly string root = Path.Combine(Environment.CurrentDirectory, ".net-test-artifacts",
        "maintenance-debug-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Automatic_debug_recovery_cannot_clear_an_uncertain_maintenance_reservation()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var sid = identity.User?.Value ?? throw new InvalidOperationException("The test user SID is unavailable.");
        var marker = Path.Combine(root, "unclean-owner");
        using var continuity = new InstallerMaintenanceContinuity(new WindowsInstallerMaintenanceMarkerStore(marker, sid));
        continuity.BeginNativeApply();
        continuity.Dispose();

        var ownerBytes = File.ReadAllBytes(marker);
        var guardBytes = File.ReadAllBytes(marker + ".debug-recovery");
        var lifetimeReads = 0;
        var recovery = new DebugOwnerRecovery(marker, sid, new(123, 456, 789), _ =>
        {
            lifetimeReads++;
            return true;
        });
        var inspect = () => recovery.CanStart;
        inspect.Should().Throw<InvalidDataException>();
        recovery.Invoking(value => value.Prepare()).Should().Throw<InvalidDataException>();
        lifetimeReads.Should().Be(0);
        File.ReadAllBytes(marker).Should().Equal(ownerBytes);
        File.ReadAllBytes(marker + ".debug-recovery").Should().Equal(guardBytes);
    }

    [Fact]
    public void Confirmed_terminal_disposal_clears_the_owned_pair_before_debug_reentry()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var sid = identity.User?.Value ?? throw new InvalidOperationException("The test user SID is unavailable.");
        var marker = Path.Combine(root, "unclean-owner");
        using var continuity = new InstallerMaintenanceContinuity(new WindowsInstallerMaintenanceMarkerStore(marker, sid));
        continuity.BeginNativeApply();
        continuity.ConfirmNativeApplyComplete().Should().BeTrue();
        continuity.Dispose();

        File.Exists(marker).Should().BeFalse();
        File.Exists(marker + ".debug-recovery").Should().BeFalse();
        var recovery = new DebugOwnerRecovery(marker, sid, new(123, 456, 789), _ => true);
        recovery.CanStart.Should().BeTrue();
        recovery.Prepare();
        recovery.Recovered.Should().BeFalse();
    }

    public void Dispose()
    {
        if (Directory.Exists(root)) { Directory.Delete(root, recursive: true); }
    }
}
#endif
