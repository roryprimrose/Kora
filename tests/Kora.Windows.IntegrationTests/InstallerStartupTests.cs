using AwesomeAssertions;
using Kora.Setup;

namespace Kora.Windows.IntegrationTests;

public sealed class InstallerStartupTests
{
    private const string Expected = "\"C:\\Program Files\\Kora\\0.1.0\\Kora.exe\"";

    [Theory]
    [InlineData(null, InstallerStartupState.NotRegistered)]
    [InlineData(Expected, InstallerStartupState.Enabled)]
    [InlineData("\"c:\\program files\\kora\\0.1.0\\kora.exe\"", InstallerStartupState.Enabled)]
    [InlineData("", InstallerStartupState.Conflict)]
    [InlineData("\"C:\\Temp\\Kora.exe\"", InstallerStartupState.Conflict)]
    [InlineData(Expected + " --untrusted", InstallerStartupState.Conflict)]
    public void RegistryDefaultsRequireAbsentOrExactlyMatchingInstalledCommand(
        string? command, InstallerStartupState expected)
    {
        var status = InstallerStartupStatus.Inspect(command, [], Expected);
        status.State.Should().Be(expected);
        status.DefaultEnabled.Should().Be(expected is InstallerStartupState.NotRegistered or InstallerStartupState.Enabled);
    }

    [Theory]
    [InlineData(2, InstallerStartupState.Enabled, true)]
    [InlineData(3, InstallerStartupState.DisabledByWindows, false)]
    [InlineData(0, InstallerStartupState.Failed, false)]
    [InlineData(255, InstallerStartupState.Failed, false)]
    public void WindowsDisabledOrUnknownApprovalIsNotSilentlyEnabled(
        byte approval, InstallerStartupState expected, bool defaultEnabled)
    {
        byte[] record = new byte[12];
        record[0] = approval;
        var status = InstallerStartupStatus.Inspect(Expected, record, Expected);
        status.State.Should().Be(expected);
        status.DefaultEnabled.Should().Be(defaultEnabled);
    }

    [Fact]
    public void MalformedApprovalAndForeignCommandDoNotAcquireStartupAuthority()
    {
        InstallerStartupStatus.Inspect(Expected, [2], Expected).CanProceed.Should().BeFalse();
        InstallerStartupStatus.Inspect("\"C:\\Temp\\other.exe\"", new byte[12], Expected)
            .State.Should().Be(InstallerStartupState.Conflict);
    }

    [Theory]
    [InlineData(InstallerStartupState.Checking)]
    [InlineData(InstallerStartupState.Conflict)]
    [InlineData(InstallerStartupState.Failed)]
    [InlineData((InstallerStartupState)99)]
    public void UnknownOrConflictingRegistrationCannotAdmitEitherStartupChoice(InstallerStartupState state)
    {
        var missing = new InstallerDependencyStatus(InstallerDependencyState.Missing, "Missing");
        var unknown = new InstallerStartupStatus(state, "Unknown registration");
        var result = new InstallerPreflightResult(missing, missing, missing, unknown, unknown);
        foreach (var enabled in new[] { false, true })
        {
            var approve = () => result.ValidateSelection(new OptionalComponents(), InstallScope.CurrentUser, enabled);
            approve.Should().Throw<InvalidOperationException>();
        }
    }

    [Fact]
    public void SelectedScopeUsesItsOwnRegistrySnapshot()
    {
        var missing = new InstallerDependencyStatus(InstallerDependencyState.Missing, "Missing");
        var user = new InstallerStartupStatus(InstallerStartupState.NotRegistered, "Absent user registration");
        var machine = new InstallerStartupStatus(InstallerStartupState.DisabledByWindows, "Disabled machine registration");
        var result = new InstallerPreflightResult(missing, missing, missing, user, machine);
        result.StartupFor(InstallScope.CurrentUser).DefaultEnabled.Should().BeTrue();
        result.StartupFor(InstallScope.AllUsers).DefaultEnabled.Should().BeFalse();
        result.ValidateSelection(new OptionalComponents(), InstallScope.AllUsers, startAtLogin: false);
        var enableDisabled = () => result.ValidateSelection(new OptionalComponents(), InstallScope.AllUsers, startAtLogin: true);
        enableDisabled.Should().Throw<InvalidOperationException>();
    }
}
