using System.Collections.ObjectModel;

using Kora.Application.Infrastructure;
using Kora.Core;
using Kora.Core.Commands;
using Kora.Core.Dependencies;
using Kora.Core.Platform;
using Kora.Core.Voice;
namespace Kora.Application.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly BuiltInCommandCatalog commandCatalog;
    private readonly BuiltInCommandRouter commandRouter;
    private readonly DependencyBootstrapper dependencyBootstrapper;
    private readonly IVoiceRecognitionService voiceRecognition;
    private readonly ISessionController sessionController;
    private readonly IUiDispatcher uiDispatcher;
    private readonly IApplicationInfo applicationInfo;
    private MicrophoneDevice? selectedMicrophone;
    private AssistantState state = AssistantState.Information;
    private string responseTitle = "A voice-first companion.";
    private string responseBody = "Choose a microphone, enable listening, then say “Kora, what can you do?”";
    private string transcript = "No command heard yet.";
    private string commandText = "Kora, what can you do?";
    private bool isListening;
    private bool isBusy;
    private BuiltInAction? pendingPowerAction;

    public MainViewModel(
        BuiltInCommandCatalog commandCatalog,
        BuiltInCommandRouter commandRouter,
        DependencyBootstrapper dependencyBootstrapper,
        IVoiceRecognitionService voiceRecognition,
        ISessionController sessionController,
        IUiDispatcher uiDispatcher,
        IApplicationInfo applicationInfo)
    {
        this.commandCatalog = commandCatalog;
        this.commandRouter = commandRouter;
        this.dependencyBootstrapper = dependencyBootstrapper;
        this.voiceRecognition = voiceRecognition;
        this.sessionController = sessionController;
        this.uiDispatcher = uiDispatcher;
        this.applicationInfo = applicationInfo;

        ToggleListeningCommand = new AsyncCommand(ToggleListeningAsync, () => SelectedMicrophone is not null && !IsBusy);
        RefreshCommand = new AsyncCommand(RefreshAsync, () => !IsBusy);
        RunTypedCommand = new AsyncCommand(RunTypedCommandAsync, () => !string.IsNullOrWhiteSpace(CommandText) && !IsBusy);

        voiceRecognition.TranscriptRecognized += OnTranscriptRecognized;
        voiceRecognition.RecognitionFailed += OnRecognitionFailed;
    }

    public event EventHandler<WindowAction>? WindowActionRequested;

    public ObservableCollection<MicrophoneDevice> Microphones { get; } = [];

    public ObservableCollection<DependencyStatus> Dependencies { get; } = [];

    public IReadOnlyList<CommandDefinition> Commands => commandCatalog.GetCommands();

    public AsyncCommand ToggleListeningCommand { get; }

    public AsyncCommand RefreshCommand { get; }

    public AsyncCommand RunTypedCommand { get; }

    public MicrophoneDevice? SelectedMicrophone
    {
        get => selectedMicrophone;
        set
        {
            if (SetProperty(ref selectedMicrophone, value))
            {
                ToggleListeningCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public AssistantState State
    {
        get => state;
        private set
        {
            if (SetProperty(ref state, value))
            {
                OnPropertyChanged(nameof(StateLabel));
            }
        }
    }

    public string StateLabel => State.GetLabel();

    public string ResponseTitle
    {
        get => responseTitle;
        private set => SetProperty(ref responseTitle, value);
    }

    public string ResponseBody
    {
        get => responseBody;
        private set => SetProperty(ref responseBody, value);
    }

    public string Transcript
    {
        get => transcript;
        private set => SetProperty(ref transcript, value);
    }

    public string CommandText
    {
        get => commandText;
        set
        {
            if (SetProperty(ref commandText, value))
            {
                RunTypedCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool IsListening
    {
        get => isListening;
        private set
        {
            if (SetProperty(ref isListening, value))
            {
                OnPropertyChanged(nameof(ListeningButtonText));
                OnPropertyChanged(nameof(ListeningStatus));
            }
        }
    }

    public bool IsBusy
    {
        get => isBusy;
        private set
        {
            if (SetProperty(ref isBusy, value))
            {
                ToggleListeningCommand.NotifyCanExecuteChanged();
                RefreshCommand.NotifyCanExecuteChanged();
                RunTypedCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string ListeningButtonText => IsListening ? "Disable listening" : "Enable listening";

    public string ListeningStatus => IsListening
        ? $"Wake listening on {SelectedMicrophone!.Name}"
        : "Microphone closed";

    public async Task InitializeAsync() => await RefreshAsync();

    public async Task DetectMicrophonesAsync() => await RefreshAsync();

    public async Task ExitAsync()
    {
        await StopListeningAsync();
        WindowActionRequested?.Invoke(this, WindowAction.Close);
    }

    private async Task RefreshAsync()
    {
        IsBusy = true;
        try
        {
            var selectedId = SelectedMicrophone?.Id;
            var microphones = voiceRecognition.GetMicrophones();
            Microphones.Clear();
            foreach (var microphone in microphones)
            {
                Microphones.Add(microphone);
            }

            SelectedMicrophone = Microphones.FirstOrDefault(
                item => string.Equals(item.Id, selectedId, StringComparison.Ordinal)) ?? Microphones.FirstOrDefault();

            var statuses = await dependencyBootstrapper.ProbeAsync();
            Dependencies.Clear();
            foreach (var status in statuses)
            {
                Dependencies.Add(status);
            }

            State = AssistantState.Information;
            ResponseTitle = microphones.Count == 0 ? "No microphone detected." : "Environment check complete.";
            ResponseBody = microphones.Count == 0
                ? "Connect a microphone and refresh. Kora has not opened an audio capture device."
                : "Select a microphone and explicitly enable listening. No model or network is required for built-in commands.";
        }
        catch (UnauthorizedAccessException exception)
        {
            ShowFailure("Storage or microphone access was denied.", exception.Message);
        }
        catch (IOException exception)
        {
            ShowFailure("Dependency probing failed.", exception.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ToggleListeningAsync()
    {
        if (IsListening)
        {
            await StopListeningAsync();
            State = AssistantState.Information;
            ResponseTitle = "Listening disabled.";
            ResponseBody = "The microphone capture device has been released.";
            return;
        }

        IsBusy = true;
        try
        {
            var microphone = SelectedMicrophone!;
            var phrases = commandCatalog.GetCommands().SelectMany(command => command.AllPhrases);
            await voiceRecognition.StartAsync(microphone, phrases);
            IsListening = true;
            State = AssistantState.Listening;
            ResponseTitle = "I'm listening.";
            ResponseBody = "Say “Kora” followed by a supported command. Recognition stays local to Windows.";
        }
        catch (ArgumentOutOfRangeException exception)
        {
            ShowFailure("The selected microphone is unavailable.", exception.Message);
        }
        catch (InvalidOperationException exception)
        {
            ShowFailure("Windows speech recognition is unavailable.", exception.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task StopListeningAsync()
    {
        await voiceRecognition.StopAsync();
        IsListening = false;
    }

    private async Task RunTypedCommandAsync() => await HandleTranscriptAsync(CommandText, 1);

    private async void OnTranscriptRecognized(object? sender, VoiceTranscriptEventArgs eventArgs)
    {
        try
        {
            await uiDispatcher.InvokeAsync(() => HandleTranscriptAsync(eventArgs.Transcript, eventArgs.Confidence));
        }
        catch (InvalidOperationException exception)
        {
            ShowFailure("The command could not be completed.", exception.Message);
        }
    }

    private void OnRecognitionFailed(object? sender, VoiceRecognitionFailureEventArgs eventArgs)
    {
        uiDispatcher.Post(() =>
        {
            State = AssistantState.Information;
            ResponseTitle = "Command not recognized.";
            ResponseBody = eventArgs.Message;
        });
    }

    private async Task HandleTranscriptAsync(string spokenText, float confidence)
    {
        Transcript = $"“{spokenText}” · {confidence:P0} confidence";
        State = AssistantState.Calculating;

        var match = commandRouter.Match(spokenText);
        if (!match.IsMatch || match.Command is null)
        {
            State = AssistantState.Information;
            ResponseTitle = "That isn't a supported built-in command.";
            ResponseBody = "Say “Kora, what can you do?” to see the deterministic local command catalogue.";
            return;
        }

        await ExecuteAsync(match.Command);
    }

    internal async Task ExecuteAsync(CommandDefinition command)
    {
        switch (command.Action)
        {
            case BuiltInAction.ShowApplication:
                WindowActionRequested?.Invoke(this, WindowAction.Show);
                ShowSuccess("Kora is visible.", "The existing application instance was shown.");
                break;
            case BuiltInAction.HideApplication:
                State = AssistantState.Hidden;
                WindowActionRequested?.Invoke(this, WindowAction.Hide);
                break;
            case BuiltInAction.ExitApplication:
                await ExitAsync();
                break;
            case BuiltInAction.RestartApplication:
                await StopListeningAsync();
                WindowActionRequested?.Invoke(this, WindowAction.Restart);
                break;
            case BuiltInAction.OpenSettings:
                ShowInformation("Settings", "Microphone selection and listening consent are available in the setup panel.");
                break;
            case BuiltInAction.OpenSetup:
                await DetectMicrophonesAsync();
                break;
            case BuiltInAction.ShowHelp:
                ShowInformation(
                    "Built-in commands are ready.",
                    $"{Commands.Count} deterministic commands are registered. Review the catalogue on the right or try lock, lifecycle, status, and protected power proposals.");
                break;
            case BuiltInAction.ShowVersion:
                ShowInformation(
                    "Kora version",
                    $"{applicationInfo.Version} · local Windows speech · no model configured");
                break;
            case BuiltInAction.ShowStatus:
                ShowInformation(
                    IsListening ? "Waiting for your command." : "Voice is not active.",
                    IsListening ? ListeningStatus : "Enable listening or use the typed proof field.");
                break;
            case BuiltInAction.CancelTask:
                pendingPowerAction = null;
                ShowInformation("Cancelled.", "No pending Kora task or power proposal will continue.");
                break;
            case BuiltInAction.StopSpeaking:
                ShowInformation("Speech is stopped.", "No speech playback is active in this bootstrap.");
                break;
            case BuiltInAction.LockMachine:
                await StopListeningAsync();
                if (!sessionController.LockCurrentSession())
                {
                    ShowFailure("Windows did not accept the lock request.", "The microphone remains disabled.");
                }
                break;
            case BuiltInAction.ProposeShutdown:
            case BuiltInAction.ProposeRestart:
                pendingPowerAction = command.Action;
                State = AssistantState.Waiting;
                ResponseTitle = command.Action == BuiltInAction.ProposeShutdown
                    ? "Shutdown request recognized."
                    : "Restart request recognized.";
                ResponseBody = "This bootstrap proves voice routing without executing disruptive power actions. No OS power request was sent.";
                break;
            case BuiltInAction.CancelPowerAction:
                var hadPendingAction = pendingPowerAction is not null;
                pendingPowerAction = null;
                ShowInformation(
                    hadPendingAction ? "Power proposal cancelled." : "No Kora power action is pending.",
                    "No operating-system action was sent.");
                break;
            case BuiltInAction.ShowPowerStatus:
                ShowInformation(
                    pendingPowerAction switch
                    {
                        BuiltInAction.ProposeShutdown => "Shutdown proposal pending.",
                        BuiltInAction.ProposeRestart => "Computer restart proposal pending.",
                        _ => "No power action is pending.",
                    },
                    "Power execution is intentionally disabled in this bootstrap proof.");
                break;
            default:
                throw new InvalidOperationException($"Unsupported built-in action: {command.Action}.");
        }
    }

    private void ShowInformation(string title, string body)
    {
        State = AssistantState.Information;
        ResponseTitle = title;
        ResponseBody = body;
    }

    private void ShowSuccess(string title, string body)
    {
        State = AssistantState.Success;
        ResponseTitle = title;
        ResponseBody = body;
    }

    private void ShowFailure(string title, string body)
    {
        State = AssistantState.Failure;
        ResponseTitle = title;
        ResponseBody = body;
    }
}