using System.Globalization;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Threading;
using Kora.Windows.Audio;
using Kora.Windows.Coordination;
using WixToolset.BootstrapperApplicationApi;

namespace Kora.Setup;

[SuppressMessage("Design", "CA1001",
    Justification = "Burn owns the bootstrapper; Run joins session disposal in finally and clears its callback alias.")]
internal sealed class KoraBootstrapper : BootstrapperApplication, IInstallerEngine
{
    private InstallerSession? session;
    private IBootstrapperCommand? command;
    private bool? installed;
    private SetupWindow? window;
    private readonly HashSet<InstallScope> detectedScopes = [];
    private PackageState? desktopRuntimeState;
    private PackageState? vcRuntimeState;
    private bool relatedUninstall;
    private readonly TaskCompletionSource<int> relatedCompleted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private string? incompatibleRelatedBundle;
    private readonly ConcurrentDictionary<InstallScope, string> relatedProductVersions = new();
    private InstallScope plannedScope;

    protected override void OnCreate(CreateEventArgs args)
    {
        base.OnCreate(args);
        command = args.Command;
    }

    protected override void Run()
    {
        relatedUninstall = command is not null &&
            InstallerRelatedBundlePolicy.IsRelatedUninstall(command.Action, command.Display, command.Relation);
        if (relatedUninstall)
        {
            // Burn's parent already owns shutdown/consent. This child only retires the superseded bundle.
            engine.Detect();
#pragma warning disable VSTHRD002 // Native Burn Run boundary waits for the terminal related-uninstall callback.
            engine.Quit(relatedCompleted.Task.GetAwaiter().GetResult());
#pragma warning restore VSTHRD002
            return;
        }
        using var speechComponents = new InstallerSpeechComponentHost();
        var ownedSession = new InstallerSession(this, new OptionalComponentSetup(
            new BurnLogger<KokoroTextToSpeechProvider>((level, message) => engine.Log(
                level >= Microsoft.Extensions.Logging.LogLevel.Warning ? LogLevel.Error : LogLevel.Standard,
                message))), new WindowsInstallerShutdown());
        session = ownedSession;
        try
        {
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
                window = new SetupWindow(ownedSession, engine.GetVariableString("KoraDisplayVersion"),
                    new InstallerPreflightState(new WindowsInstallerPreflight(
                        CreateLogger<WindowsInstallerPreflight>(), CreateLogger<KokoroTextToSpeechProvider>(),
                        engine.GetVariableString("KoraProductVersion"), RelatedProductVersion),
                        CreateLogger<InstallerPreflightState>()),
                    requestedAction: command.Action, speechComponents: speechComponents.Service);
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
                Task.Run(async () =>
                {
                    await ownedSession.DisposeAsync().ConfigureAwait(false);
                    await ownedSession.LaunchAfterCloseAsync(launcher, CancellationToken.None).ConfigureAwait(false);
                }).GetAwaiter().GetResult();
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
            engine.Quit(ownedSession.ExitCode);
        }
        finally
        {
            try
            {
#pragma warning disable VSTHRD002 // Burn Run owns this session and must join its disposal even if desktop startup or launch fails.
                Task.Run(async () => await ownedSession.DisposeAsync().ConfigureAwait(false)).GetAwaiter().GetResult();
#pragma warning restore VSTHRD002
            }
            finally { session = null; }
        }
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
        if (relatedUninstall)
        {
            if (args.Status < 0) { CompleteRelatedUninstall(args.Status); }
            else { engine.Plan(LaunchAction.Uninstall, command!.Scope); }
            return;
        }
        Dispatch(() =>
        {
            try
            {
                if (incompatibleRelatedBundle is not null)
                {
                    throw new InvalidDataException(incompatibleRelatedBundle);
                }
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
                if (incompatibleRelatedBundle is not null) { Session.ReportFailureDetail(incompatibleRelatedBundle); }
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
            var scope = args.PerMachine ? InstallScope.AllUsers : InstallScope.CurrentUser;
            detectedScopes.Add(scope);
            if (!Version.TryParse(args.Version, out var version) || version.Build < 0 ||
                version.Revision is not (-1 or 0))
            {
                incompatibleRelatedBundle = "The related MSI version cannot be resolved safely. Remove the old installation using its original setup before retrying.";
                return;
            }
            var numeric = version.ToString(3);
            if (!relatedProductVersions.TryAdd(scope, numeric) &&
                !StringComparer.Ordinal.Equals(relatedProductVersions[scope], numeric))
            {
                incompatibleRelatedBundle = "Multiple related MSI versions were detected. Remove conflicting installations before retrying.";
            }
        }
    }

    protected override void OnDetectRelatedBundle(DetectRelatedBundleEventArgs args)
    {
        base.OnDetectRelatedBundle(args);
        if (!relatedUninstall && command?.Action == LaunchAction.Install && args.RelationType == RelationType.Upgrade)
        {
            detectedScopes.Add(args.PerMachine ? InstallScope.AllUsers : InstallScope.CurrentUser);
            try
            {
                InstallerRelatedBundlePolicy.ValidateUpgrade(args.BundleTag, args.MissingFromCache,
                    args.Version, engine.GetVariableString("KoraProductVersion"));
            }
            catch (InvalidDataException exception) { incompatibleRelatedBundle = exception.Message; }
        }
    }

    protected override void OnPlanComplete(PlanCompleteEventArgs args)
    {
        base.OnPlanComplete(args);
        if (relatedUninstall)
        {
            if (args.Status < 0) { CompleteRelatedUninstall(args.Status); }
            else { engine.Apply(IntPtr.Zero); }
            return;
        }
        Dispatch(() => Session.Planned(args.Status));
    }

    protected override void OnProgress(ProgressEventArgs args)
    {
        base.OnProgress(args);
        if (relatedUninstall) { return; }
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
        if (relatedUninstall) { return; }
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
        if (!relatedUninstall && args.ShouldExecute)
        {
            try { WindowsInstallerMaintenanceLease.ValidateUserSession(plannedScope == InstallScope.AllUsers); }
            catch (Exception exception) when (exception is InvalidOperationException or UnauthorizedAccessException
                or System.ComponentModel.Win32Exception or System.Security.SecurityException)
            {
                args.Cancel = true;
                engine.Log(LogLevel.Error, "Package execution refused because installer user/scope eligibility changed. " + exception.Message);
                return;
            }
        }
        engine.Log(LogLevel.Standard, $"Kora setup executing package {args.PackageId}.");
    }

    protected override void OnApplyComplete(ApplyCompleteEventArgs args)
    {
        // Never grant Burn permission to restart Windows automatically.
        args.Action = BOOTSTRAPPER_APPLYCOMPLETE_ACTION.None;
        base.OnApplyComplete(args);
        if (relatedUninstall)
        {
            CompleteRelatedUninstall(args.Status < 0 ? args.Status :
                args.Restart == ApplyRestart.None ? 0 : InstallerSession.RebootRequired);
            return;
        }
        var current = Volatile.Read(ref session);
        if (current is null || !current.RecordNativeApplyComplete())
        {
            engine.Log(LogLevel.Error, "Native Apply completion arrived without a current maintenance reservation; no marker was cleared.");
            return;
        }
        Dispatch(() => current.Applied(args.Status, args.Restart != ApplyRestart.None));
    }

    private void CompleteRelatedUninstall(int status)
    {
        relatedCompleted.TrySetResult(status);
    }

    public void Plan(SetupAction action, InstallScope scope, bool startAtLogin)
    {
        plannedScope = scope;
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
                var startup = WindowsStartupRegistrationProbe.Inspect(scope, engine.GetVariableString("KoraProductVersion"),
                    RelatedProductVersion(scope));
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

    private string? RelatedProductVersion(InstallScope scope)
        => relatedProductVersions.TryGetValue(scope, out var version) ? version : null;

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
