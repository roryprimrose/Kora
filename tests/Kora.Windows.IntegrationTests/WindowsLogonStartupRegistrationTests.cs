using AwesomeAssertions;
using Kora.Windows.Startup;

namespace Kora.Windows.IntegrationTests;

public sealed class WindowsLogonStartupRegistrationTests
{
    private const string Executable = @"C:\Users\Test User\Programs\Kora\0.1.0\Kora.exe";
    private const string Command = "\"" + Executable + "\"";

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ExactInstalledOwnedRegistrationIsQuotedReadBackAndNeverLaunches(bool initial)
    {
        var native = new Native { User = initial ? Command : null };
        var service = new WindowsLogonStartupRegistration(native);
        var observed = service.Inspect();
        observed.Available.Should().BeTrue();
        observed.Enabled.Should().Be(initial);
        native.Writes.Should().Be(0);
        service.Apply(!initial, observed, static () => true);
        native.User.Should().Be(initial ? null : Command);
        native.Writes.Should().Be(1);
        service.Inspect().Enabled.Should().Be(!initial);
        native.Machine.Should().BeNull();
        native.Approval.Should().BeEmpty();
    }

    [Theory]
    [InlineData("developer")]
    [InlineData("machine")]
    [InlineData("foreign")]
    [InlineData("unquoted")]
    [InlineData("arguments")]
    [InlineData("disabled")]
    [InlineData("malformed")]
    [InlineData("unknown")]
    public void UnknownUnownedOrWindowsDisabledRegistrationIsPreserved(string boundary)
    {
        var native = new Native();
        switch (boundary)
        {
            case "developer": native.Installation = null; break;
            case "machine": native.Machine = Command; break;
            case "foreign": native.User = "\"C:\\Other\\Kora.exe\""; break;
            case "unquoted": native.User = Executable; break;
            case "arguments": native.User = Command + " --listen"; break;
            case "disabled": native.Approval = new byte[12]; native.Approval[0] = 3; break;
            case "malformed": native.Approval = [2]; break;
            case "unknown": native.Approval = new byte[12]; break;
        }
        var before = native.User;
        var service = new WindowsLogonStartupRegistration(native);
        var observation = service.Inspect();
        observation.Available.Should().BeFalse();
        observation.Enabled.Should().BeNull();
        Action apply = () => service.Apply(false, observation, static () => true);
        apply.Should().Throw<InvalidOperationException>();
        native.User.Should().Be(before);
        native.Writes.Should().Be(0);
    }

    [Theory]
    [InlineData("image")]
    [InlineData("entry")]
    [InlineData("host")]
    [InlineData("install-late")]
    [InlineData("readback")]
    public void InstallRegistrationAndAdmissionDriftNeverBecomeSuccess(string boundary)
    {
        var native = new Native();
        var service = new WindowsLogonStartupRegistration(native);
        var review = service.Inspect();
        if (boundary is "image") { native.Installation = native.Installation! with { Identity = "new image" }; }
        if (boundary is "entry") { native.User = Command; }
        if (boundary is "install-late") { native.OnInstallRead = count => { if (count == 3) { native.Installation = null; } }; }
        if (boundary is "readback") { native.IgnoreWrite = true; }
        Action apply = () => service.Apply(true, review, () => boundary is not "host");
        apply.Should().Throw<Exception>();
        native.Writes.Should().Be(boundary is "readback" ? 1 : 0);
    }

    [Theory]
    [InlineData("relative\\Kora.exe")]
    [InlineData("C:\\Installed\\Other.exe")]
    [InlineData("C:\\Installed\\\"bad\\Kora.exe")]
    [InlineData("C:\\Installed\\bad\n\\Kora.exe")]
    [InlineData("\\\\server\\share\\Kora.exe")]
    public void InvalidExecutableIdentityCannotProduceACommand(string path)
    {
        var native = new Native { User = "existing", Installation = new(path, "invalid") };
        new WindowsLogonStartupRegistration(native).Invoking(item => item.Inspect()).Should().Throw<InvalidDataException>();
        native.Writes.Should().Be(0);
    }

    internal sealed class Native : IWindowsLogonStartupNative
    {
        public InstalledLogonApplication? Installation { get; set; } = new(Executable, "exact-installed-exe-dll");
        public string? User { get; set; }
        public string? Machine { get; set; }
        public byte[] Approval { get; set; } = [];
        public int Writes { get; private set; }
        public bool IgnoreWrite { get; set; }
        public Action<int>? OnInstallRead { get; set; }
        private int reads;
        public InstalledLogonApplication? InspectInstallation() { reads++; OnInstallRead?.Invoke(reads); return Installation; }
        public string? ReadUserCommand() => User;
        public string? ReadMachineCommand() => Machine;
        public byte[] ReadUserApproval() => Approval;
        public void WriteUserCommand(InstalledLogonApplication installation, string? expected, string? replacement, Func<bool> admitted)
        {
            if (Installation != installation || !string.Equals(User, expected, StringComparison.Ordinal) || !admitted())
            { throw new InvalidOperationException(); }
            Writes++;
            if (!IgnoreWrite) { User = replacement; }
        }
    }
}
