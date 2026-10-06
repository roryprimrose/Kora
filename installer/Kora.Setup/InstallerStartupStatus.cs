using System.Buffers.Binary;

namespace Kora.Setup;

public sealed record InstallerStartupStatus(InstallerStartupState State, string Detail)
{
    public bool DefaultEnabled => State is InstallerStartupState.NotRegistered or InstallerStartupState.Enabled;

    public bool CanConfigure => DefaultEnabled;

    public bool CanProceed => State is InstallerStartupState.NotRegistered or InstallerStartupState.Enabled
        or InstallerStartupState.DisabledByWindows;

    public static InstallerStartupStatus Checking { get; } =
        new(InstallerStartupState.Checking, "Checking startup registration...");

    public static InstallerStartupStatus Inspect(string? command, ReadOnlySpan<byte> approval, string expectedCommand)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedCommand);
        if (command is not null && !string.Equals(command, expectedCommand, StringComparison.OrdinalIgnoreCase))
        {
            return new(InstallerStartupState.Conflict,
                "Another command occupies Kora's startup entry. Resolve it in Windows Startup apps before retrying; setup will not overwrite it.");
        }
        if (!approval.IsEmpty)
        {
            if (approval.Length != 12)
            {
                return new(InstallerStartupState.Failed, "Windows startup approval metadata is invalid; no default was assumed.");
            }
            var state = BinaryPrimitives.ReadUInt32LittleEndian(approval);
            if (state == 3)
            {
                return new(InstallerStartupState.DisabledByWindows,
                    "Disabled in Windows Startup apps. Enable it there and check dependencies again to request logon startup.");
            }
            if (state != 2)
            {
                return new(InstallerStartupState.Failed, "Windows startup approval state is unknown; no default was assumed.");
            }
        }
        return command is null
            ? new(InstallerStartupState.NotRegistered, "No startup entry found.")
            : new(InstallerStartupState.Enabled, "Existing Kora startup registration will be retained.");
    }
}
