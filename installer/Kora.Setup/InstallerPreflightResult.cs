namespace Kora.Setup;

public sealed record InstallerPreflightResult(
    InstallerDependencyStatus PowerShell,
    InstallerDependencyStatus Ollama,
    InstallerDependencyStatus Kokoro,
    InstallerStartupStatus CurrentUserStartup,
    InstallerStartupStatus AllUsersStartup)
{
    public static InstallerPreflightResult Checking { get; } = new(
        InstallerDependencyStatus.Checking, InstallerDependencyStatus.Checking,
        InstallerDependencyStatus.Checking, InstallerStartupStatus.Checking, InstallerStartupStatus.Checking);

    public InstallerStartupStatus StartupFor(InstallScope scope) => scope switch
    {
        InstallScope.CurrentUser => CurrentUserStartup,
        InstallScope.AllUsers => AllUsersStartup,
        _ => throw new ArgumentOutOfRangeException(nameof(scope)),
    };

    public void ValidateCompleted()
    {
        foreach (var status in new[] { PowerShell, Ollama, Kokoro })
        {
            if (status is null || !Enum.IsDefined(status.State) ||
                status.State == InstallerDependencyState.Checking || string.IsNullOrWhiteSpace(status.Detail))
            {
                throw new InvalidDataException("Dependency detection returned an incomplete or unknown status.");
            }
        }
        foreach (var status in new[] { CurrentUserStartup, AllUsersStartup })
        {
            if (status is null || !Enum.IsDefined(status.State) || status.State == InstallerStartupState.Checking
                || string.IsNullOrWhiteSpace(status.Detail))
            {
                throw new InvalidDataException("Startup detection returned an incomplete or unknown status.");
            }
        }
    }

    public void ValidateSelection(OptionalComponents selection, InstallScope scope, bool startAtLogin)
    {
        ArgumentNullException.ThrowIfNull(selection);
        var startup = StartupFor(scope);
        if (!startup.CanProceed || (startAtLogin && !startup.CanConfigure))
        {
            throw new InvalidOperationException("Resolve the startup registration status before approving this configuration.");
        }
        if ((selection.PowerShell && !PowerShell.CanPrepare) ||
            (selection.LocalInference && !Ollama.CanPrepare) ||
            (selection.Kokoro && !Kokoro.CanPrepare))
        {
            throw new InvalidOperationException("Selected optional work is already available, incompatible, or unverified. Review its detection status before continuing.");
        }
    }
}
