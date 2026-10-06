using System.ComponentModel;
using System.Globalization;
using System.Text.Json;

namespace Kora.Setup;

public sealed class InstallerSession(IInstallerEngine engine, IOptionalComponentSetup? optionalSetup = null) : INotifyPropertyChanged
{
    public const int UserCancelled = 1602;
    public const int RebootRequired = 3010;
    private const int UnexpectedState = unchecked((int)0x8000FFFF);
    private SetupAction action;
    private OptionalComponents components = new();
    private bool restartRequired;
    private bool optionalPreparationStarted;
    private bool scopeLocked;
    private bool closeAccepted;
    private bool launchApproved;
    private bool launchAttempted;

    public event PropertyChangedEventHandler? PropertyChanged;

    public SetupPhase Phase { get; private set; } = SetupPhase.Detecting;

    public string Status { get; private set; } = "Checking the installed package...";

    public bool Installed { get; private set; }

    public bool CanStart => Phase == SetupPhase.Ready && !closeAccepted;

    public bool CanOfferLaunch => Phase == SetupPhase.Succeeded && action is SetupAction.Install or SetupAction.Repair;

    public bool CanLaunchOnClose => CanOfferLaunch && !restartRequired;

    public InstallScope Scope { get; private set; } = InstallScope.CurrentUser;

    public bool StartAtLogin { get; private set; }

    public bool CanChooseScope => CanStart && !scopeLocked;

    public bool CanClose => Phase is not (SetupPhase.Planning or SetupPhase.Applying or SetupPhase.PreparingOptional);

    public bool CanCancelOptional => Phase == SetupPhase.PreparingOptional;

    public int Progress { get; private set; }

    public int ExitCode { get; private set; } = UserCancelled;

    public InstallerDependencyStatus DotNetRuntime { get; private set; } = InstallerDependencyStatus.Checking;

    public InstallerDependencyStatus VCRuntime { get; private set; } = InstallerDependencyStatus.Checking;

    public void ReportRuntimeRequirements(InstallerDependencyStatus dotNet, InstallerDependencyStatus visualCpp)
    {
        ArgumentNullException.ThrowIfNull(dotNet);
        ArgumentNullException.ThrowIfNull(visualCpp);
        DotNetRuntime = dotNet;
        VCRuntime = visualCpp;
        Notify();
    }

    public void Detected(int status, bool? installed, bool supportedPlatform = true,
        InstallScope? installedScope = null)
    {
        if (Phase != SetupPhase.Detecting)
        {
            throw new InvalidOperationException("Detection has already completed.");
        }

        if (status < 0 || !installed.HasValue)
        {
            Fail(status < 0 ? status : UnexpectedState);
            return;
        }

        if (!supportedPlatform)
        {
            Fail(unchecked((int)0x80070032));
            Status = "Kora setup requires Windows 11 x64. No prerequisites or application changes were started.";
            Notify();
            return;
        }

        if ((installed.Value && !installedScope.HasValue) ||
            (installedScope.HasValue && !Enum.IsDefined(installedScope.Value)))
        {
            Fail(UnexpectedState);
            Status = "The existing Kora installation scope is unknown. Use the original setup or Windows Installed apps to repair/remove it before retrying.";
            Notify();
            return;
        }

        if (installedScope.HasValue)
        {
            Scope = installedScope.Value;
            scopeLocked = true;
        }

        Installed = installed.Value;
        Phase = SetupPhase.Ready;
        Status = Installed ? "Kora is installed. Choose repair or uninstall." : "Ready to install Kora.";
        Notify();
    }

    public void SelectScope(InstallScope scope)
    {
        if (!CanChooseScope || !Enum.IsDefined(scope))
        {
            throw new InvalidOperationException("Scope can only be chosen for a new installation before planning.");
        }

        Scope = scope;
        Notify();
    }

    public void Start(SetupAction requestedAction, bool consent, OptionalComponents? optionalComponents = null,
        bool startAtLogin = false)
    {
        if (!CanStart || !consent)
        {
            throw new InvalidOperationException("Setup requires completed detection and explicit consent.");
        }

        if (!Enum.IsDefined(requestedAction) || (!Installed && requestedAction != SetupAction.Install))
        {
            throw new InvalidOperationException("The requested action does not match the installed state.");
        }
        if (requestedAction == SetupAction.Uninstall && startAtLogin)
        {
            throw new InvalidOperationException("Uninstall cannot request a startup registration.");
        }

        var selection = optionalComponents ?? new OptionalComponents();
        if (selection.Any && (requestedAction == SetupAction.Uninstall || optionalSetup is null))
        {
            throw new InvalidOperationException("Optional preparation requires install/repair and an available setup service.");
        }

        components = selection;
        action = requestedAction;
        StartAtLogin = startAtLogin;
        Phase = SetupPhase.Planning;
        Status = "Preparing the package plan...";
        Notify();
        engine.Plan(action, Scope, StartAtLogin);
    }

    public void Planned(int status)
    {
        if (Phase != SetupPhase.Planning)
        {
            throw new InvalidOperationException("No plan is in progress.");
        }

        if (status < 0)
        {
            Fail(status);
            return;
        }

        Phase = SetupPhase.Applying;
        Status = "Applying changes. Windows may request administrator approval.";
        Notify();
        engine.Apply();
    }

    public void ReportProgress(int percentage)
    {
        if (Phase != SetupPhase.Applying)
        {
            throw new InvalidOperationException("Progress requires an active apply operation.");
        }

        ArgumentOutOfRangeException.ThrowIfNegative(percentage);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(percentage, 100);
        Progress = percentage;
        Notify();
    }

    public void Applied(int status, bool restartRequired)
    {
        if (Phase != SetupPhase.Applying)
        {
            throw new InvalidOperationException("No apply is in progress.");
        }

        if (status < 0)
        {
            Fail(status);
            return;
        }

        this.restartRequired = restartRequired;
        if (components.Any)
        {
            Phase = SetupPhase.PreparingOptional;
            Status = "Kora is installed. Preparing only the optional components you selected...";
            Notify();
            return;
        }

        Complete();
    }

    public async Task PrepareOptionalAsync(CancellationToken cancellationToken)
    {
        if (Phase != SetupPhase.PreparingOptional || optionalSetup is null || optionalPreparationStarted)
        {
            throw new InvalidOperationException("No optional setup is pending.");
        }

        optionalPreparationStarted = true;
        try
        {
            await optionalSetup.PrepareAsync(components, new Progress<string>(message =>
            {
                if (Phase == SetupPhase.PreparingOptional)
                {
                    Status = message;
                    Notify();
                }
            }), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            Complete();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Phase = SetupPhase.Cancelled;
            ExitCode = UserCancelled;
            Status = "Kora remains installed. Optional setup was cancelled; partially prepared components are retained. Reopen setup or use Kora Settings to continue.";
            Notify();
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            Fail(exception.HResult);
            Status = "Kora remains installed, but optional setup timed out. Check network/dependency health and retry through Repair or Kora Settings.";
            Notify();
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException
            or InvalidOperationException or HttpRequestException or JsonException or Win32Exception or TimeoutException)
        {
            Fail(exception.HResult);
            Status = $"Kora remains installed, but optional setup failed: {exception.Message} Reopen setup or use Kora Settings to retry.";
            Notify();
        }
    }

    private void Complete()
    {
        Phase = SetupPhase.Succeeded;
        Progress = 100;
        ExitCode = restartRequired ? RebootRequired : 0;
        Status = action == SetupAction.Uninstall
            ? "Kora was removed. Your device-local data was not deleted."
            : "Kora setup completed.";
        if (restartRequired)
        {
            Status += " Restart Windows before using Kora; setup will not restart it.";
        }

        Notify();
    }

    public void AcceptClose(bool launchKora)
    {
        if (!CanClose || closeAccepted || (launchKora && !CanLaunchOnClose))
        {
            throw new InvalidOperationException("Completion launch requires successful install/repair, no restart and one quiescent close.");
        }
        closeAccepted = true;
        launchApproved = launchKora;
    }

    public async Task LaunchAfterCloseAsync(IInstallerApplicationLauncher launcher, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(launcher);
        if (!closeAccepted) { throw new InvalidOperationException("Wait for setup to close before launching Kora."); }
        if (!launchApproved) { return; }
        if (!CanLaunchOnClose || launchAttempted)
        {
            throw new InvalidOperationException("Completion launch is no longer eligible or has already been attempted.");
        }
        launchAttempted = true;
        await launcher.LaunchAsync(Scope, cancellationToken);
    }

    public void Cancel()
    {
        if (!CanClose)
        {
            throw new InvalidOperationException("Wait for the package operation and rollback to finish.");
        }

        if (Phase is SetupPhase.Detecting or SetupPhase.Ready)
        {
            Phase = SetupPhase.Cancelled;
            Status = "Setup cancelled without changing the installed package.";
            ExitCode = UserCancelled;
            Notify();
        }
    }

    public void Fail(int status)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(status, 0);
        engine.LogFailure(status);
        Phase = SetupPhase.Failed;
        ExitCode = status;
        Status = string.Create(CultureInfo.InvariantCulture,
            $"Setup failed (0x{status:X8}). See the Burn log in your temporary folder. Close setup and retry after correcting the problem.");
        Notify();
    }

    private void Notify()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    }
}
