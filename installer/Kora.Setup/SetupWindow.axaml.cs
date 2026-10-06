using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using WixToolset.BootstrapperApplicationApi;

namespace Kora.Setup;

[SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable",
    Justification = "Avalonia owns the window lifetime; Closed disposes the cancellation source after optional work terminates.")]
public sealed partial class SetupWindow : Window
{
    private readonly InstallerSession session;
    private readonly InstallerPreflightState preflight;
    private readonly InstallerPresentation presentation;
    private readonly bool preview;
    private readonly CancellationTokenSource optionalCancellation = new();
    private bool preparingOptional;
    private bool checkingPreflight;
    private bool closed;
    private bool closeRequested;
    private InstallScope? startupScope;
    private InstallerStartupStatus? startupSnapshot;

    public SetupWindow(InstallerSession session, string version, InstallerPreflightState preflight, bool preview = false,
        LaunchAction requestedAction = LaunchAction.Install)
    {
        this.session = session;
        this.preflight = preflight;
        this.preview = preview;
        presentation = new InstallerPresentation(session,
            requestedAction == LaunchAction.Repair ? SetupAction.Repair : SetupAction.Uninstall,
            requiresInstalledPackage: requestedAction is LaunchAction.Repair or LaunchAction.Uninstall);
        InitializeComponent();
        VersionLabel.Text = version;
        Title = $"Kora Setup - {version}";
        BrandMark.AnimationEnabled = WindowsMotionPreference.IsAnimationEnabled();
        WireDetails(RuntimeDisclosure, RuntimeDetailsPanel);
        WireDetails(PowerShellDisclosure, PowerShellDetails);
        WireDetails(InferenceDisclosure, InferenceDetails);
        WireDetails(KokoroDisclosure, KokoroDetails);
        ExpandSelectedOption(PowerShellOption, PowerShellDisclosure);
        ExpandSelectedOption(InferenceOption, InferenceDisclosure);
        ExpandSelectedOption(KokoroOption, KokoroDisclosure);
        CurrentUserScope.IsCheckedChanged += (_, _) => SelectScope(InstallScope.CurrentUser, CurrentUserScope.IsChecked);
        AllUsersScope.IsCheckedChanged += (_, _) => SelectScope(InstallScope.AllUsers, AllUsersScope.IsChecked);
        ActionButton.Click += (_, _) => Start(PrimaryAction);
        RepairButton.Click += (_, _) =>
        {
            if (!closeRequested)
            {
                presentation.SwitchAction();
                Refresh();
            }
        };
        RetryDetectionButton.Click += OnCheckDependencies;
        Opened += OnCheckDependencies;
        CloseButton.Click += (_, _) =>
        {
            if (session.CanCancelOptional)
            {
                optionalCancellation.Cancel();
                CloseButton.IsEnabled = false;
            }
            else
            {
                Close();
            }
        };
        Closing += (_, args) =>
        {
            if (session.CanClose && checkingPreflight)
            {
                args.Cancel = true;
                closeRequested = true;
                optionalCancellation.Cancel();
                Refresh();
                return;
            }
            args.Cancel = !session.CanClose;
            if (!args.Cancel)
            {
                session.Cancel();
            }
        };
        Closed += (_, _) =>
        {
            closed = true;
            session.PropertyChanged -= OnSessionChanged;
            preflight.PropertyChanged -= OnPreflightChanged;
            optionalCancellation.Cancel();
            if (!checkingPreflight) { optionalCancellation.Dispose(); }
            session.AcceptClose(!preview && session.CanLaunchOnClose && LaunchOption.IsChecked == true);
        };
        session.PropertyChanged += OnSessionChanged;
        preflight.PropertyChanged += OnPreflightChanged;
        Refresh();
    }

    private SetupAction PrimaryAction => presentation.Action;

    private void Start(SetupAction action)
    {
        var selection = action == SetupAction.Uninstall ? new OptionalComponents()
            : new OptionalComponents(PowerShellOption.IsChecked == true,
                InferenceOption.IsChecked == true, KokoroOption.IsChecked == true);
        try
        {
            var startAtLogin = action != SetupAction.Uninstall && StartupOption.IsChecked == true;
            if (action != SetupAction.Uninstall) { preflight.ValidateSelection(selection, session.Scope, startAtLogin); }
            session.Start(action, consent: true, optionalComponents: selection, startAtLogin: startAtLogin);
        }
        catch (InvalidOperationException exception)
        {
            session.Fail(exception.HResult);
        }
    }

    private async void OnCheckDependencies(object? sender, EventArgs args)
    {
        checkingPreflight = true;
        try
        {
            await preflight.CheckAsync(optionalCancellation.Token);
        }
        catch (OperationCanceledException) when (optionalCancellation.IsCancellationRequested)
        {
            // Closing setup cancels read-only work; it never approves preparation.
        }
        finally
        {
            checkingPreflight = false;
            if (closed) { optionalCancellation.Dispose(); }
            else if (closeRequested) { Close(); }
        }
    }

    private void OnPreflightChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (!closed) { Refresh(); }
    }

    private void SelectScope(InstallScope scope, bool? isChecked)
    {
        if (isChecked == true && session.CanChooseScope && session.Scope != scope)
        {
            session.SelectScope(scope);
        }
    }

    private async void OnSessionChanged(object? sender, PropertyChangedEventArgs args)
    {
        Refresh();
        if (session.CanCancelOptional && !preparingOptional)
        {
            preparingOptional = true;
            try
            {
                await session.PrepareOptionalAsync(optionalCancellation.Token);
            }
            catch (InvalidOperationException exception)
            {
                session.Fail(exception.HResult);
            }
        }
    }

    private void Refresh()
    {
        ActionHeading.Text = presentation.Heading;
        ScopeOptions.IsVisible = presentation.ShowScopeChoice;
        ScopeDescription.IsVisible = presentation.ShowScopeDescription;
        ScopeDescription.Text = presentation.ScopeDescription;
        StartupPanel.IsVisible = presentation.ShowConfiguration;
        RuntimePanel.IsVisible = presentation.ShowConfiguration;
        OptionalPanel.IsVisible = presentation.ShowConfiguration;
        ConsentLabel.Text = presentation.ConsentText;
        ConsentLabel.IsVisible = presentation.ShowAction;
        ActionButton.IsVisible = presentation.ShowAction;
        RetryDetectionButton.IsVisible = presentation.ShowDependencyRetry;
        LaunchOption.IsVisible = session.CanOfferLaunch;
        LaunchOption.IsEnabled = !closeRequested && session.CanLaunchOnClose;
        if (session.CanOfferLaunch && !session.CanLaunchOnClose) { LaunchOption.IsChecked = false; }
        StatusLabel.Text = closeRequested ? "Cancelling dependency checks before closing setup..."
            : preview ? "Visual preview only. Package operations are disabled."
            : session.CanStart && presentation.ShowConfiguration && !preflight.IsComplete
                ? "Checking optional dependencies without installing, starting services, downloading or running inference..."
            : session.CanStart ? presentation.ReadyStatus : session.Status;
        SetupProgress.Value = session.Progress;
        SetupProgress.IsIndeterminate = session.Phase is SetupPhase.Detecting or SetupPhase.Planning or SetupPhase.PreparingOptional
            || (session.CanStart && presentation.ShowConfiguration && preflight.IsChecking);
        ActionButton.Content = PrimaryAction.ToString();
        var startup = preflight.Result.StartupFor(session.Scope);
        if (startupScope != session.Scope || !ReferenceEquals(startupSnapshot, startup))
        {
            startupScope = session.Scope;
            startupSnapshot = startup;
            StartupOption.IsChecked = startup.DefaultEnabled;
        }
        StartupOption.Content = session.Scope == InstallScope.CurrentUser
            ? "Start Kora when I sign in" : "Start Kora when any user signs in";
        StartupOption.IsEnabled = !closeRequested && session.CanStart && preflight.IsComplete && startup.CanConfigure;
        StartupStatus.IsVisible = !startup.CanConfigure;
        StartupStatus.Text = startup.Detail;
        ActionButton.IsEnabled = !preview && !closeRequested && presentation.ShowAction && session.CanStart &&
            (PrimaryAction == SetupAction.Uninstall || (preflight.IsComplete && startup.CanProceed));
        RepairButton.IsVisible = presentation.CanSwitchAction;
        RepairButton.Content = presentation.AlternateAction == SetupAction.Repair ? "Switch to repair" : "Switch to uninstall";
        RepairButton.IsEnabled = !closeRequested && presentation.CanSwitchAction;
        RetryDetectionButton.IsEnabled = !closeRequested && session.CanStart && !preflight.IsChecking;
        DotNetStatus.Text = $".NET: {session.DotNetRuntime.Summary}";
        VCStatus.Text = $"Visual C++: {session.VCRuntime.Summary}";
        RuntimeDetails.Text = $"{session.DotNetRuntime.Display}\n{session.VCRuntime.Display}";
        CurrentUserScope.IsChecked = session.Scope == InstallScope.CurrentUser;
        AllUsersScope.IsChecked = session.Scope == InstallScope.AllUsers;
        CurrentUserScope.IsEnabled = !closeRequested && session.CanChooseScope;
        AllUsersScope.IsEnabled = !closeRequested && session.CanChooseScope;
        UpdateOption(PowerShellOption, PowerShellAvailable, PowerShellStatus, PowerShellDetail, "PowerShell 7",
            preflight.Result.PowerShell, preflight.Result.PowerShell.State == InstallerDependencyState.UpdateRequired
                ? "Update PowerShell 7 (7.4+)" : "Install PowerShell 7 (7.4+)");
        UpdateOption(InferenceOption, InferenceAvailable, InferenceStatus, InferenceDetail, "Local inference",
            preflight.Result.Ollama, preflight.Result.Ollama.State switch
            {
                InstallerDependencyState.Detected => "Verify existing Ollama and pinned qwen3:1.7b",
                InstallerDependencyState.NotRunning => "Start existing Ollama; prepare/verify qwen3:1.7b",
                InstallerDependencyState.NeedsPreparation => "Download/verify qwen3:1.7b (reuse Ollama)",
                _ => "Install Ollama and pinned qwen3:1.7b",
            });
        UpdateOption(KokoroOption, KokoroAvailable, KokoroStatus, KokoroDetail, "Kokoro neural speech",
            preflight.Result.Kokoro, preflight.Result.Kokoro.State switch
            {
                InstallerDependencyState.Detected => "Verify existing Kokoro model and voices",
                InstallerDependencyState.NeedsPreparation => "Repair/prepare Kokoro model and all voices",
                _ => "Install Kokoro model and all voice assets",
            });
        CloseButton.IsEnabled = !closeRequested && (session.CanClose || (session.CanCancelOptional && !optionalCancellation.IsCancellationRequested));
        CloseButton.Content = session.CanCancelOptional ? "Cancel optional setup"
            : session.Phase is SetupPhase.Succeeded or SetupPhase.Failed or SetupPhase.Cancelled ? "Close" : "Cancel";
    }

    private static void WireDetails(ToggleButton disclosure, Control details)
    {
        disclosure.IsCheckedChanged += (_, _) =>
        {
            details.IsVisible = disclosure.IsChecked == true;
            disclosure.Content = details.IsVisible ? "Hide details" : "Details";
        };
    }

    private static void ExpandSelectedOption(CheckBox option, ToggleButton disclosure)
    {
        option.IsCheckedChanged += (_, _) => disclosure.IsChecked = option.IsChecked == true;
    }

    private void UpdateOption(CheckBox option, TextBlock available, TextBlock statusLabel, TextBlock detail,
        string name, InstallerDependencyStatus status, string action)
    {
        option.Content = status.State == InstallerDependencyState.Checking ? $"{name} (checking...)" : action;
        option.IsVisible = status.CanPrepare || status.State == InstallerDependencyState.Checking;
        option.IsEnabled = !closeRequested && session.CanStart && preflight.IsComplete && status.CanPrepare;
        if (!preflight.IsComplete || !status.CanPrepare) { option.IsChecked = false; }
        available.IsVisible = !option.IsVisible;
        available.Text = name;
        statusLabel.Text = status.Summary;
        detail.Text = status.Detail;
    }
}
