using Avalonia;
using Avalonia.Styling;
using Microsoft.Extensions.Logging.Abstractions;
using WixToolset.BootstrapperApplicationApi;

namespace Kora.Setup;

internal static class Program
{
    // Burn initializes COM as MTA and creates its own STA thread for Run().
    [MTAThread]
    public static void Main(string[] args)
    {
        if (args is ["--preview", ..])
        {
            var completePreview = args.Contains("--complete", StringComparer.Ordinal);
            var modeIndex = Array.IndexOf(args, "--mode");
            var mode = modeIndex < 0 ? SetupAction.Install
                : modeIndex + 1 < args.Length ? args[modeIndex + 1] switch
                {
                    "install" => SetupAction.Install,
                    "repair" => SetupAction.Repair,
                    "uninstall" => SetupAction.Uninstall,
                    _ => throw new ArgumentException("Preview mode must be install, repair or uninstall.", nameof(args)),
                }
                : throw new ArgumentException("Preview --mode requires a value.", nameof(args));
            var themeArgs = args.Where((argument, index) =>
                !string.Equals(argument, "--complete", StringComparison.Ordinal) &&
                (modeIndex < 0 || (index != modeIndex && index != modeIndex + 1))).ToArray();
            var theme = themeArgs switch
            {
                ["--preview"] => ThemeVariant.Default,
                ["--preview", "--theme", "light"] => ThemeVariant.Light,
                ["--preview", "--theme", "dark"] => ThemeVariant.Dark,
                _ => throw new ArgumentException("Preview supports --preview [--theme light|dark] [--mode install|repair|uninstall] [--complete].", nameof(args)),
            };
            var previewThread = new Thread(() =>
            {
                var session = new InstallerSession(new PreviewEngine(completePreview));
                session.Detected(0, mode != SetupAction.Install,
                    installedScope: mode == SetupAction.Install ? null : InstallScope.CurrentUser);
                session.ReportRuntimeRequirements(
                    new(InstallerDependencyState.Installed, "Sample .NET 10 desktop/base runtimes."),
                    new(InstallerDependencyState.UpdateRequired, "Sample older VC++ runtime."));
                if (completePreview)
                {
                    session.Start(mode, consent: true);
                    session.Planned(0);
                    session.Applied(0, restartRequired: false);
                }
                BuildApplication(() => new SetupWindow(session, "0.1.0 preview",
                    new InstallerPreflightState(new PreviewPreflight(), NullLogger<InstallerPreflightState>.Instance),
                    preview: true, requestedAction: mode switch
                    {
                        SetupAction.Repair => LaunchAction.Repair,
                        SetupAction.Uninstall => LaunchAction.Uninstall,
                        _ => LaunchAction.Install,
                    }), theme)
                    .StartWithClassicDesktopLifetime(args);
            });
            previewThread.SetApartmentState(ApartmentState.STA);
            previewThread.Start();
            previewThread.Join();
            return;
        }

        ManagedBootstrapperApplication.Run(new KoraBootstrapper());
    }

    internal static AppBuilder BuildApplication(Func<SetupWindow> createWindow, ThemeVariant? previewTheme = null)
    {
        return AppBuilder.Configure(() => new SetupApplication(createWindow, previewTheme))
            .UsePlatformDetect()
            .WithInterFont();
    }

    private sealed class PreviewPreflight : IInstallerPreflight
    {
        public Task<InstallerPreflightResult> ProbeAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new InstallerPreflightResult(
                new(InstallerDependencyState.Installed, "Sample PowerShell 7.5 installation, available without new downloads."),
                new(InstallerDependencyState.Detected, "Sample running Ollama and matching qwen3:1.7b digest; inference not tested."),
                new(InstallerDependencyState.Missing, "Sample missing Kokoro model/voices, about 229 MB."),
                new(InstallerStartupState.NotRegistered, "Sample no per-user startup entry."),
                new(InstallerStartupState.DisabledByWindows, "Sample machine startup disabled in Windows Startup apps.")));
        }
    }

    private sealed class PreviewEngine(bool completePreview) : IInstallerEngine
    {
        public void Plan(SetupAction action, InstallScope scope, bool startAtLogin)
        {
            if (!completePreview) { throw new InvalidOperationException("Preview cannot install packages."); }
        }

        public void Apply()
        {
            if (!completePreview) { throw new InvalidOperationException("Preview cannot apply packages."); }
        }

        public void LogFailure(int status) => throw new InvalidOperationException("Preview has no Burn engine.");
    }
}
