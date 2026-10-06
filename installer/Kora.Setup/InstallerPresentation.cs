namespace Kora.Setup;

public sealed class InstallerPresentation
{
    private readonly InstallerSession session;
    private readonly bool requiresInstalledPackage;
    private SetupAction maintenanceAction;

    public InstallerPresentation(InstallerSession session, SetupAction maintenanceAction = SetupAction.Uninstall,
        bool requiresInstalledPackage = false)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (maintenanceAction is not (SetupAction.Repair or SetupAction.Uninstall))
        {
            throw new ArgumentOutOfRangeException(nameof(maintenanceAction));
        }
        this.session = session;
        this.requiresInstalledPackage = requiresInstalledPackage;
        this.maintenanceAction = maintenanceAction;
    }

    public SetupAction Action => session.Installed ? maintenanceAction : SetupAction.Install;

    public SetupAction AlternateAction => Action == SetupAction.Repair ? SetupAction.Uninstall : SetupAction.Repair;

    public bool IsTerminal => session.Phase is SetupPhase.Succeeded or SetupPhase.Failed or SetupPhase.Cancelled;

    public bool MissingInstallation => requiresInstalledPackage && session.Phase == SetupPhase.Ready && !session.Installed;

    public bool ShowAction => !IsTerminal && session.Phase != SetupPhase.Detecting && !MissingInstallation;

    public bool ShowConfiguration => ShowAction && Action != SetupAction.Uninstall;

    public bool ShowScopeChoice => ShowConfiguration && !session.Installed;

    public bool ShowScopeDescription => ShowAction;

    public bool CanSwitchAction => session.Installed && session.CanStart;

    public bool ShowDependencyRetry => ShowConfiguration && session.CanStart;

    public string Heading => MissingInstallation ? "Kora is not installed" : session.Phase switch
    {
        SetupPhase.Detecting => "Checking Kora",
        SetupPhase.Succeeded => Action switch
        {
            SetupAction.Uninstall => "Kora removed",
            SetupAction.Repair => "Kora repaired",
            _ => "Kora installed",
        },
        SetupPhase.Failed => "Setup incomplete",
        SetupPhase.Cancelled => "Setup cancelled",
        _ => Action switch
        {
            SetupAction.Uninstall => "Remove Kora",
            SetupAction.Repair => "Repair Kora",
            _ => "Your local Windows assistant",
        },
    };

    public string ScopeDescription => session.Installed
        ? session.Scope == InstallScope.CurrentUser ? "Current installation: Just for me." : "Current installation: All users."
        : session.Scope == InstallScope.CurrentUser
            ? "Kora and its Start menu shortcut are installed only for you."
            : "Kora and its Start menu shortcut are installed for every Windows user.";

    public string ConsentText => Action switch
    {
        SetupAction.Uninstall => "Uninstall removes Kora and its startup entry, not your preferences, models, databases or logs. Shared runtimes and optional components are kept.",
        SetupAction.Repair => "Repair restores Kora in its existing scope and approves the displayed startup setting and required/selected preparation. Shared runtimes may require administrator approval.",
        _ => "Install approves scope, startup settings and required/selected downloads. Shared runtimes may require administrator approval.",
    };

    public string ReadyStatus => MissingInstallation ? "No installed Kora package was found. Close setup and use install mode to install Kora." : Action switch
    {
        SetupAction.Uninstall => "Ready to remove Kora. Your device-local data will be kept.",
        SetupAction.Repair => "Ready to repair Kora in its existing installation scope.",
        _ => session.Status,
    };

    public void SwitchAction()
    {
        if (!CanSwitchAction)
        {
            throw new InvalidOperationException("Maintenance mode can only change for a detected installation before approval.");
        }
        maintenanceAction = AlternateAction;
    }
}
