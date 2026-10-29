using System.Buffers.Binary;
using Kora.Core.Configuration;

namespace Kora.Windows.Startup;

public sealed class WindowsLogonStartupRegistration : ILogonStartupRegistration
{
    private readonly IWindowsLogonStartupNative native;
    public WindowsLogonStartupRegistration() : this(new WindowsLogonStartupNative()) { }
    internal WindowsLogonStartupRegistration(IWindowsLogonStartupNative native) => this.native = native;

    public LogonStartupRegistration Inspect()
    {
        var application = native.InspectInstallation();
        if (application is null) { return new(false, null, string.Empty, "No exact current-user MSI installation matches this running executable. Install/repair this version before reviewing startup."); }
        var command = native.ReadUserCommand();
        if (native.ReadMachineCommand() is not null)
        { return new(false, null, application.Identity, "A machine-wide Kora startup entry exists. Use its installer; no per-user duplicate or machine mutation is available."); }
        if (command is not null && !string.Equals(command, Quote(application.Executable), StringComparison.Ordinal))
        { return new(false, null, application.Identity, "An unowned or changed Kora startup command exists. It will not be overwritten or removed."); }
        var approval = native.ReadUserApproval();
        if (approval.Length != 0 && (approval.Length != 12 || BinaryPrimitives.ReadUInt32LittleEndian(approval) != 2))
        { return new(false, null, application.Identity, "Windows startup approval is disabled or unknown. Inspect Windows Startup apps; Kora will not override it."); }
        return new(true, command is not null, application.Identity, null);
    }

    public void Apply(bool enabled, LogonStartupRegistration reviewed, Func<bool> admitted)
    {
        var current = Inspect();
        if (!reviewed.Available || current != reviewed || !admitted())
        { throw new InvalidOperationException("Startup registration, installation or host admission changed after review."); }
        var application = native.InspectInstallation();
        if (application is null || !string.Equals(application.Identity, reviewed.Identity, StringComparison.Ordinal))
        { throw new InvalidOperationException("The reviewed installed application changed."); }
        native.WriteUserCommand(application, reviewed.Enabled == true ? Quote(application.Executable) : null,
            enabled ? Quote(application.Executable) : null, admitted);
        var readback = Inspect();
        if (!readback.Available || !string.Equals(readback.Identity, reviewed.Identity, StringComparison.Ordinal) || readback.Enabled != enabled)
        { throw new InvalidDataException("Windows startup registration readback is unconfirmed."); }
    }

    private static string Quote(string executable)
    {
        if (!Path.IsPathFullyQualified(executable) || Path.GetPathRoot(executable)?.Length != 3
            || !string.Equals(Path.GetFileName(executable), "Kora.exe", StringComparison.Ordinal)
            || executable.IndexOfAny(['"', '\r', '\n', '\0']) >= 0)
        { throw new InvalidDataException("Installed startup executable identity is invalid."); }
        return "\"" + executable + "\"";
    }
}
