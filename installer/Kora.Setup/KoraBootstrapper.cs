using System.Globalization;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Threading;
using Kora.Windows.Audio;
using Kora.Windows.Coordination;
using WixToolset.BootstrapperApplicationApi;

namespace Kora.Setup;

internal sealed class KoraBootstrapper : BootstrapperApplication, IInstallerEngine
{
    private InstallerSession? session;
    private IBootstrapperCommand? command;
    private bool? installed;
    private SetupWindow? window;
    private readonly HashSet<InstallScope> detectedScopes = [];
    private PackageState? desktopRuntimeState;
    private PackageState? vcRuntimeState;

    protected override void OnCreate(CreateEventArgs args)
    {
        base.OnCreate(args);
        command = args.Command;
    }

    protected override void Run()
    {
        session = new InstallerSession(this, new OptionalComponentSetup(
            new BurnLogger<KokoroTextToSpeechProvider>((level, message) => engine.Log(
                level >= Microsoft.Extensions.Logging.LogLevel.Warning ? LogLevel.Error : LogLevel.Standard,
                message))));
        // Interactive consent is required by this proof; /quiet and layout are not implemented.
        if (command?.Display != Display.Full ||
            command.Action is not (LaunchAction.Install or LaunchAction.Repair or LaunchAction.Uninstall))
        {
            engine.Log(LogLevel.Error, "Kora setup proof requires full interactive display and install, repair or uninstall.");
            engine.Quit(unchecked((int)0x80070032));
            return;
        }

        Program.BuildApplication(() =>
        {
            window = new SetupWindow(session, engine.GetVariableString("KoraDisplayVersion"),
                new InstallerPreflightState(new WindowsInstallerPreflight(
                    CreateLogger<WindowsInstallerPreflight>(), CreateLogger<KokoroTextToSpeechProvider>(),
                    engine.GetVariableString("KoraProductVersion")),
                    CreateLogger<InstallerPreflightState>()),
                requestedAction: command.Action);
            window.Opened += (_, _) => engine.Detect();
            return window;
        }).StartWithClassicDesktopLifetime([]);
        try
        {
            var launcher = new InstallerApplicationLauncher(new WindowsInstallerLaunchPlatform(),
                engine.GetVariableString("KoraProductVersion"),
                engine.GetVariableString("KoraApplicationExeHash"), engine.GetVariableString("KoraApplicationAssemblyHash"),
                CreateLogger<InstallerApplicationLauncher>());
            // The Avalonia dispatcher has stopped; launch I/O must not capture its context.
#pragma warning disable VSTHRD002 // Burn's synchronous Run boundary waits on thread-pool work after the UI dispatcher has stopped.
            Task.Run(() => session.LaunchAfterCloseAsync(launcher, CancellationToken.None)).GetAwaiter().GetResult();
#pragma warning restore VSTHRD002
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException
            or InvalidOperationException or System.ComponentModel.Win32Exception or COMException
            or System.Security.SecurityException)
        {
            InstallerLog.ApplicationLaunchFailed(CreateLogger<KoraBootstrapper>(), exception);
            WindowsInstanceCoordinator.ReportStartupFailure(
                $"Kora remains installed, but it could not be started: {exception.Message}");
            engine.Quit(exception.HResult);
            return;
        }
        engine.Quit(session.ExitCode);
    }

    protected override void OnDetectPackageComplete(DetectPackageCompleteEventArgs args)
    {
        base.OnDetectPackageComplete(args);
        if (args.Status >= 0)
        {
            if (StringComparer.Ordinal.Equals(args.PackageId, "DesktopRuntime")) { desktopRuntimeState = args.State; }
            if (StringComparer.Ordinal.Equals(args.PackageId, "VCRuntime")) { vcRuntimeState = args.State; }
        }
        if (StringComparer.Ordinal.Equals(args.PackageId, "KoraMsi") && args.Status >= 0)
        {
            installed = args.State switch
            {
                PackageState.Absent => false,
                PackageState.Present => true,
                _ => null,
            };
        }
    }

    protected override void OnDetectComplete(DetectCompleteEventArgs args)
    {
        base.OnDetectComplete(args);
        Dispatch(() =>
        {
            try
            {
                if (args.Status >= 0)
                {
                    var desktopVersion = ReadVariable("DesktopRuntimeVersion");
                    var coreVersion = ReadVariable("CoreRuntimeVersion");
                    var vcVersion = ReadVariable("VCRuntimeVersion");
                    Session.ReportRuntimeRequirements(
                        RuntimeStatus(desktopRuntimeState, desktopVersion is not null || coreVersion is not null,
                            $".NET Desktop {desktopVersion ?? "not detected"}; base {coreVersion ?? "not detected"}."),
                        RuntimeStatus(vcRuntimeState, StringComparer.Ordinal.Equals(ReadVariable("VCRuntimeInstalled"), "1"),
                            $"VC++ x64 {vcVersion ?? "not detected"}."));
                }
                if (engine.ContainsVariable("WixBundleDetectedScope"))
                {
                    switch (engine.GetVariableNumeric("WixBundleDetectedScope"))
                    {
                        case 0:
                            break;
                        case 1:
                            detectedScopes.Add(InstallScope.AllUsers);
                            break;
                        case 2:
                            detectedScopes.Add(InstallScope.CurrentUser);
                            break;
                        default:
                            throw new InvalidDataException("Burn reported an unknown installed bundle scope.");
                    }
                }

                if (detectedScopes.Count > 1)
                {
                    throw new InvalidDataException("Kora installations with conflicting user/machine scopes were detected. Remove the conflicting installation before retrying.");
                }

                Session.Detected(args.Status, installed,
                    command?.Action == LaunchAction.Uninstall ||
                    (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000) &&
                     RuntimeInformation.OSArchitecture == Architecture.X64),
                    detectedScopes.Count == 1 ? detectedScopes.Single() : null);
            }
            catch (Exception exception) when (exception is COMException or InvalidDataException)
            {
                engine.Log(LogLevel.Error, exception.Message);
                Session.Fail(exception.HResult);
            }
        });
    }

    private string? ReadVariable(string name) => engine.ContainsVariable(name) ? engine.GetVariableString(name) : null;

    private static InstallerDependencyStatus RuntimeStatus(PackageState? state, bool existing, string detail)
        => new(state switch
        {
            PackageState.Present => InstallerDependencyState.Installed,
            PackageState.Absent when existing => InstallerDependencyState.UpdateRequired,
            PackageState.Absent => InstallerDependencyState.Missing,
            _ => throw new InvalidDataException("Required runtime detection returned an unknown state."),
        }, detail);

    private BurnLogger<T> CreateLogger<T>() => new((level, message) => engine.Log(
        level >= Microsoft.Extensions.Logging.LogLevel.Warning ? LogLevel.Error : LogLevel.Standard, message));

    protected override void OnDetectRelatedMsiPackage(DetectRelatedMsiPackageEventArgs args)
    {
        base.OnDetectRelatedMsiPackage(args);
        if (StringComparer.Ordinal.Equals(args.PackageId, "KoraMsi"))
        {
            detectedScopes.Add(args.PerMachine ? InstallScope.AllUsers : InstallScope.CurrentUser);
        }
    }

    protected override void OnPlanComplete(PlanCompleteEventArgs args)
    {
        base.OnPlanComplete(args);
        Dispatch(() => Session.Planned(args.Status));
    }

    protected override void OnProgress(ProgressEventArgs args)
    {
        base.OnProgress(args);
        Dispatch(() =>
        {
            if (Session.Phase == SetupPhase.Applying)
            {
                Session.ReportProgress(args.OverallPercentage);
            }
        });
    }

    protected override void OnCacheAcquireProgress(CacheAcquireProgressEventArgs args)
    {
        base.OnCacheAcquireProgress(args);
        Dispatch(() =>
        {
            if (Session.Phase == SetupPhase.Applying)
            {
                Session.ReportProgress(args.OverallPercentage);
            }
        });
    }

    protected override void OnExecutePackageBegin(ExecutePackageBeginEventArgs args)
    {
        base.OnExecutePackageBegin(args);
        engine.Log(LogLevel.Standard, $"Kora setup executing package {args.PackageId}.");
    }

    protected override void OnApplyComplete(ApplyCompleteEventArgs args)
    {
        // Never grant Burn permission to restart Windows automatically.
        args.Action = BOOTSTRAPPER_APPLYCOMPLETE_ACTION.None;
        base.OnApplyComplete(args);
        Dispatch(() => Session.Applied(args.Status, args.Restart != ApplyRestart.None));
    }

    public void Plan(SetupAction action, InstallScope scope, bool startAtLogin)
    {
        var bundleScope = scope switch
        {
            InstallScope.CurrentUser => BundleScope.PerUser,
            InstallScope.AllUsers => BundleScope.PerMachine,
            _ => throw new ArgumentOutOfRangeException(nameof(scope)),
        };
        InvokeEngine(() =>
        {
            if (action != SetupAction.Uninstall)
            {
                var startup = WindowsStartupRegistrationProbe.Inspect(scope, engine.GetVariableString("KoraProductVersion"));
                if (!startup.CanProceed || (startAtLogin && !startup.CanConfigure))
                {
                    InstallerLog.StartupReported(CreateLogger<KoraBootstrapper>(), scope, startup.State, startup.Detail);
                    throw new InvalidOperationException("Startup registration changed; check dependencies again before approving setup.");
                }
            }
            engine.SetVariableNumeric("KoraStartAtLogin", startAtLogin ? 1 : 0);
            engine.Plan(action switch
            {
                SetupAction.Install => LaunchAction.Install,
                SetupAction.Repair => LaunchAction.Repair,
                SetupAction.Uninstall => LaunchAction.Uninstall,
                _ => throw new ArgumentOutOfRangeException(nameof(action)),
            }, bundleScope);
        });
    }

    public void Apply()
    {
        var handle = window?.TryGetPlatformHandle()?.Handle
            ?? throw new InvalidOperationException("Setup has no native window handle.");
        InvokeEngine(() => engine.Apply(handle));
    }

    public void LogFailure(int status)
    {
        engine.Log(LogLevel.Error,
            string.Create(CultureInfo.InvariantCulture, $"Kora setup failed with status 0x{status:X8}."));
    }

    private InstallerSession Session => session ?? throw new InvalidOperationException("Setup has not started.");

    private static void Dispatch(Action callback) => Dispatcher.UIThread.Post(callback);

    private void InvokeEngine(Action callback)
    {
        try
        {
            callback();
        }
        catch (Exception exception) when (exception is COMException or IOException or InvalidDataException
            or UnauthorizedAccessException or System.Security.SecurityException)
        {
            InstallerLog.ComponentFailed(CreateLogger<KoraBootstrapper>(), "Native setup configuration", exception);
            Session.Fail(exception.HResult);
        }
    }
}
