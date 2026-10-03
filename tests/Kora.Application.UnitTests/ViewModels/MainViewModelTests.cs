using AwesomeAssertions;

using Kora.Application;
using Kora.Application.ViewModels;
using Kora.Core;
using Kora.Core.Commands;
using Kora.Core.Dependencies;
using Kora.Core.Platform;
using Kora.Core.Voice;

namespace Kora.Application.UnitTests.ViewModels;

public sealed class MainViewModelTests
{
    [Fact]
    public async Task InitializeAsync_populates_readiness_and_selects_the_first_microphone()
    {
        var fixture = new Fixture();
        fixture.Voice.Microphones =
        [
            new MicrophoneDevice("0", "Headset"),
            new MicrophoneDevice("1", "Webcam"),
        ];

        await fixture.ViewModel.InitializeAsync();

        fixture.ViewModel.Microphones.Should().Equal(fixture.Voice.Microphones);
        fixture.ViewModel.SelectedMicrophone.Should().Be(fixture.Voice.Microphones[0]);
        fixture.ViewModel.Dependencies.Should().ContainSingle()
            .Which.Readiness.Should().Be(DependencyReadiness.Ready);
        fixture.ViewModel.ResponseTitle.Should().Be("Environment check complete.");
        fixture.ViewModel.IsBusy.Should().BeFalse();
    }

    [Fact]
    public async Task InitializeAsync_reports_when_no_microphone_is_available()
    {
        var fixture = new Fixture();

        await fixture.ViewModel.InitializeAsync();

        fixture.ViewModel.SelectedMicrophone.Should().BeNull();
        fixture.ViewModel.ResponseTitle.Should().Be("No microphone detected.");
        fixture.ViewModel.ToggleListeningCommand.CanExecute(null).Should().BeFalse();
        fixture.ViewModel.ListeningButtonText.Should().Be("Enable listening");
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Typed_command_is_disabled_for_blank_input(string text)
    {
        var fixture = new Fixture();

        fixture.ViewModel.CommandText = text;

        fixture.ViewModel.RunTypedCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public async Task Refresh_preserves_a_still_available_microphone_selection()
    {
        var fixture = new Fixture();
        fixture.Voice.Microphones =
        [
            new MicrophoneDevice("0", "Headset"),
            new MicrophoneDevice("1", "Webcam"),
        ];
        await fixture.ViewModel.InitializeAsync();
        fixture.ViewModel.SelectedMicrophone = fixture.Voice.Microphones[1];

        await fixture.ViewModel.RefreshCommand.ExecuteAsync();

        fixture.ViewModel.SelectedMicrophone?.Id.Should().Be("1");
    }

    [Theory]
    [MemberData(nameof(RefreshFailures))]
    public async Task InitializeAsync_surfaces_expected_probe_failures(
        Exception exception,
        string expectedTitle)
    {
        var fixture = new Fixture();
        fixture.Voice.GetMicrophonesException = exception;

        await fixture.ViewModel.InitializeAsync();

        fixture.ViewModel.State.Should().Be(AssistantState.Failure);
        fixture.ViewModel.ResponseTitle.Should().Be(expectedTitle);
        fixture.ViewModel.ResponseBody.Should().Be(exception.Message);
        fixture.ViewModel.IsBusy.Should().BeFalse();
    }

    public static TheoryData<Exception, string> RefreshFailures => new()
    {
        { new UnauthorizedAccessException("denied"), "Storage or microphone access was denied." },
        { new IOException("unavailable"), "Dependency probing failed." },
    };

    [Fact]
    public async Task Enabling_and_disabling_listening_opens_and_releases_the_selected_microphone()
    {
        var fixture = await Fixture.CreateInitializedAsync();

        await fixture.ViewModel.ToggleListeningCommand.ExecuteAsync();

        fixture.Voice.StartedMicrophone.Should().Be(fixture.ViewModel.SelectedMicrophone);
        fixture.Voice.StartedPhrases.Should().BeEquivalentTo(
            fixture.Catalog.GetCommands().SelectMany(command => command.AllPhrases));
        fixture.ViewModel.IsListening.Should().BeTrue();
        fixture.ViewModel.State.Should().Be(AssistantState.Listening);
        fixture.ViewModel.ListeningButtonText.Should().Be("Disable listening");

        await fixture.ViewModel.ToggleListeningCommand.ExecuteAsync();

        fixture.Voice.StopCalls.Should().Be(1);
        fixture.ViewModel.IsListening.Should().BeFalse();
        fixture.ViewModel.ListeningStatus.Should().Be("Microphone closed");
        fixture.ViewModel.ResponseTitle.Should().Be("Listening disabled.");
    }

    [Fact]
    public async Task Other_commands_are_disabled_while_microphone_start_is_in_progress()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        fixture.Voice.StartGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var start = fixture.ViewModel.ToggleListeningCommand.ExecuteAsync();

        fixture.ViewModel.IsBusy.Should().BeTrue();
        fixture.ViewModel.RefreshCommand.CanExecute(null).Should().BeFalse();
        fixture.ViewModel.RunTypedCommand.CanExecute(null).Should().BeFalse();

        fixture.Voice.StartGate.SetResult();
        await start;
    }

    [Fact]
    public async Task Listening_command_is_disabled_while_readiness_refresh_is_in_progress()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        fixture.Probe.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var refresh = fixture.ViewModel.RefreshCommand.ExecuteAsync();

        fixture.ViewModel.IsBusy.Should().BeTrue();
        fixture.ViewModel.ToggleListeningCommand.CanExecute(null).Should().BeFalse();

        fixture.Probe.Gate.SetResult();
        await refresh;
    }

    [Theory]
    [MemberData(nameof(StartFailures))]
    public async Task Enabling_listening_surfaces_expected_start_failures(
        Exception exception,
        string expectedTitle)
    {
        var fixture = await Fixture.CreateInitializedAsync();
        fixture.Voice.StartException = exception;

        await fixture.ViewModel.ToggleListeningCommand.ExecuteAsync();

        fixture.ViewModel.State.Should().Be(AssistantState.Failure);
        fixture.ViewModel.ResponseTitle.Should().Be(expectedTitle);
        fixture.ViewModel.IsListening.Should().BeFalse();
        fixture.ViewModel.IsBusy.Should().BeFalse();
    }

    public static TheoryData<Exception, string> StartFailures => new()
    {
        { new ArgumentOutOfRangeException("microphone", "gone"), "The selected microphone is unavailable." },
        { new InvalidOperationException("recognizer missing"), "Windows speech recognition is unavailable." },
    };

    [Fact]
    public async Task Unsupported_typed_text_reports_help_without_invoking_platform_actions()
    {
        var fixture = new Fixture();
        fixture.ViewModel.CommandText = "restart";

        await fixture.ViewModel.RunTypedCommand.ExecuteAsync();

        fixture.ViewModel.State.Should().Be(AssistantState.Information);
        fixture.ViewModel.ResponseTitle.Should().Be("That isn't a supported built-in command.");
        fixture.Session.LockCalls.Should().Be(0);
        fixture.WindowActions.Should().BeEmpty();
    }

    [Theory]
    [InlineData("Kora, open settings", "Settings")]
    [InlineData("Kora, what can you do", "Built-in commands are ready.")]
    [InlineData("Kora, what version are you running", "Kora version")]
    [InlineData("Kora, what are you currently working on", "Voice is not active.")]
    [InlineData("Kora, cancel task", "Cancelled.")]
    [InlineData("Kora, stop speaking", "Speech is stopped.")]
    [InlineData("Kora, what power action is pending", "No power action is pending.")]
    public async Task Informational_commands_return_the_expected_response(string phrase, string title)
    {
        var fixture = new Fixture();
        fixture.ViewModel.CommandText = phrase;

        await fixture.ViewModel.RunTypedCommand.ExecuteAsync();

        fixture.ViewModel.State.Should().Be(AssistantState.Information);
        fixture.ViewModel.ResponseTitle.Should().Be(title);
    }

    [Fact]
    public async Task Status_command_reports_the_active_microphone_while_listening()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        await fixture.ViewModel.ToggleListeningCommand.ExecuteAsync();

        await fixture.RunAsync("Kora, what are you currently working on");

        fixture.ViewModel.ResponseTitle.Should().Be("Waiting for your command.");
        fixture.ViewModel.ResponseBody.Should().Contain("Headset");
    }

    [Fact]
    public async Task Open_setup_refreshes_microphones_and_dependencies()
    {
        var fixture = new Fixture();
        fixture.Voice.Microphones = [new MicrophoneDevice("0", "First")];
        await fixture.ViewModel.InitializeAsync();
        fixture.Voice.Microphones = [new MicrophoneDevice("1", "Replacement")];

        await fixture.RunAsync("Kora, open setup");

        fixture.ViewModel.Microphones.Should().ContainSingle()
            .Which.Name.Should().Be("Replacement");
        fixture.ViewModel.ResponseTitle.Should().Be("Environment check complete.");
    }

    [Fact]
    public async Task DetectMicrophonesAsync_refreshes_the_device_list_for_the_tray()
    {
        var fixture = new Fixture();
        fixture.Voice.Microphones = [new MicrophoneDevice("2", "Tray microphone")];

        await fixture.ViewModel.DetectMicrophonesAsync();

        fixture.ViewModel.Microphones.Should().ContainSingle()
            .Which.Name.Should().Be("Tray microphone");
    }

    [Fact]
    public async Task ExitAsync_releases_listening_before_closing_from_the_tray()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        await fixture.ViewModel.ToggleListeningCommand.ExecuteAsync();

        await fixture.ViewModel.ExitAsync();

        fixture.Events.Should().ContainInOrder("voice.stop", "window.Close");
        fixture.ViewModel.IsListening.Should().BeFalse();
    }

    [Fact]
    public async Task Show_command_requests_show_and_reports_success()
    {
        var fixture = new Fixture();
        fixture.ViewModel.CommandText = "Kora, show Kora";

        await fixture.ViewModel.RunTypedCommand.ExecuteAsync();

        fixture.WindowActions.Should().ContainSingle().Which.Should().Be(WindowAction.Show);
        fixture.ViewModel.State.Should().Be(AssistantState.Success);
        fixture.ViewModel.StateLabel.Should().Be("COMPLETE");
    }

    [Fact]
    public async Task Repeating_a_command_that_keeps_the_same_state_is_supported()
    {
        var fixture = new Fixture();

        await fixture.RunAsync("Kora, show Kora");
        await fixture.RunAsync("Kora, show Kora");

        fixture.WindowActions.Should().Equal(WindowAction.Show, WindowAction.Show);
        fixture.ViewModel.State.Should().Be(AssistantState.Success);
    }

    [Fact]
    public async Task Hide_command_uses_the_tray_recovery_path_without_requiring_listening()
    {
        var fixture = new Fixture();
        fixture.ViewModel.CommandText = "Kora, hide Kora";

        await fixture.ViewModel.RunTypedCommand.ExecuteAsync();

        fixture.WindowActions.Should().ContainSingle().Which.Should().Be(WindowAction.Hide);
        fixture.ViewModel.State.Should().Be(AssistantState.Hidden);
    }

    [Fact]
    public async Task Hide_command_hides_when_listening_is_active()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        await fixture.ViewModel.ToggleListeningCommand.ExecuteAsync();
        fixture.ViewModel.CommandText = "Kora, hide Kora";

        await fixture.ViewModel.RunTypedCommand.ExecuteAsync();

        fixture.WindowActions.Should().ContainSingle().Which.Should().Be(WindowAction.Hide);
        fixture.ViewModel.State.Should().Be(AssistantState.Hidden);
        fixture.ViewModel.IsListening.Should().BeTrue();
    }

    [Theory]
    [InlineData("Kora, exit Kora", WindowAction.Close)]
    [InlineData("Kora, restart Kora", WindowAction.Restart)]
    public async Task Lifecycle_commands_release_audio_before_requesting_window_action(
        string phrase,
        WindowAction expectedAction)
    {
        var fixture = await Fixture.CreateInitializedAsync();
        await fixture.ViewModel.ToggleListeningCommand.ExecuteAsync();
        fixture.ViewModel.CommandText = phrase;

        await fixture.ViewModel.RunTypedCommand.ExecuteAsync();

        fixture.Events.Should().ContainInOrder("voice.stop", $"window.{expectedAction}");
        fixture.ViewModel.IsListening.Should().BeFalse();
    }

    [Fact]
    public async Task Lock_command_releases_audio_before_requesting_session_lock()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        await fixture.ViewModel.ToggleListeningCommand.ExecuteAsync();
        fixture.ViewModel.CommandText = "Kora, lock the machine";

        await fixture.ViewModel.RunTypedCommand.ExecuteAsync();

        fixture.Events.Should().ContainInOrder("voice.stop", "session.lock");
        fixture.ViewModel.IsListening.Should().BeFalse();
    }

    [Fact]
    public async Task Failed_lock_request_is_reported_without_reopening_audio()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        fixture.Session.LockResult = false;
        await fixture.ViewModel.ToggleListeningCommand.ExecuteAsync();
        fixture.ViewModel.CommandText = "Kora, lock the machine";

        await fixture.ViewModel.RunTypedCommand.ExecuteAsync();

        fixture.ViewModel.State.Should().Be(AssistantState.Failure);
        fixture.ViewModel.ResponseTitle.Should().Be("Windows did not accept the lock request.");
        fixture.ViewModel.IsListening.Should().BeFalse();
    }

    [Theory]
    [InlineData("Kora, shut down the computer", "Shutdown request recognized.")]
    [InlineData("Kora, restart the computer", "Restart request recognized.")]
    public async Task Power_commands_create_only_a_non_destructive_proposal(string phrase, string title)
    {
        var fixture = new Fixture();
        fixture.ViewModel.CommandText = phrase;

        await fixture.ViewModel.RunTypedCommand.ExecuteAsync();

        fixture.ViewModel.State.Should().Be(AssistantState.Waiting);
        fixture.ViewModel.ResponseTitle.Should().Be(title);
        fixture.Session.LockCalls.Should().Be(0);
        fixture.WindowActions.Should().BeEmpty();
    }

    [Fact]
    public async Task Power_proposal_can_be_inspected_and_cancelled()
    {
        var fixture = new Fixture();
        await fixture.RunAsync("Kora, restart the computer");

        await fixture.RunAsync("Kora, what power action is pending");
        fixture.ViewModel.ResponseTitle.Should().Be("Computer restart proposal pending.");

        await fixture.RunAsync("Kora, cancel computer restart");
        fixture.ViewModel.ResponseTitle.Should().Be("Power proposal cancelled.");

        await fixture.RunAsync("Kora, what power action is pending");
        fixture.ViewModel.ResponseTitle.Should().Be("No power action is pending.");
    }

    [Fact]
    public async Task Shutdown_proposal_can_be_inspected()
    {
        var fixture = new Fixture();
        await fixture.RunAsync("Kora, shut down the computer");

        await fixture.RunAsync("Kora, what power action is pending");

        fixture.ViewModel.ResponseTitle.Should().Be("Shutdown proposal pending.");
    }

    [Fact]
    public async Task Cancelling_power_with_no_proposal_reports_that_nothing_is_pending()
    {
        var fixture = new Fixture();

        await fixture.RunAsync("Kora, cancel shutdown");

        fixture.ViewModel.ResponseTitle.Should().Be("No Kora power action is pending.");
    }

    [Fact]
    public async Task Recognized_voice_transcript_uses_the_same_command_pipeline()
    {
        var fixture = new Fixture();

        await fixture.Voice.RaiseTranscriptAsync("Kora, show Kora", 0.87f);

        fixture.ViewModel.Transcript.Should().Contain("87%");
        fixture.WindowActions.Should().ContainSingle().Which.Should().Be(WindowAction.Show);
    }

    [Fact]
    public void Recognition_failure_is_dispatched_to_the_information_surface()
    {
        var fixture = new Fixture();

        fixture.Voice.RaiseFailure("not recognized");

        fixture.ViewModel.State.Should().Be(AssistantState.Information);
        fixture.ViewModel.ResponseTitle.Should().Be("Command not recognized.");
        fixture.ViewModel.ResponseBody.Should().Be("not recognized");
    }

    [Fact]
    public void Voice_dispatch_failure_is_reported()
    {
        var fixture = new Fixture();
        fixture.Dispatcher.InvokeException = new InvalidOperationException("dispatch failed");

        fixture.Voice.RaiseTranscript("Kora, show Kora", 0.9f);

        fixture.ViewModel.State.Should().Be(AssistantState.Failure);
        fixture.ViewModel.ResponseTitle.Should().Be("The command could not be completed.");
        fixture.ViewModel.ResponseBody.Should().Be("dispatch failed");
    }

    [Fact]
    public async Task ExecuteAsync_rejects_an_unknown_registered_action()
    {
        var fixture = new Fixture();
        var command = new CommandDefinition(
            (BuiltInAction)int.MaxValue,
            "invalid",
            "invalid",
            []);

        var action = () => fixture.ViewModel.ExecuteAsync(command);

        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Unsupported built-in action*");
    }

    [Fact]
    public async Task Show_command_is_safe_without_a_window_action_subscriber()
    {
        var fixture = new Fixture(subscribeToWindowActions: false);

        await fixture.RunAsync("Kora, show Kora");

        fixture.ViewModel.State.Should().Be(AssistantState.Success);
    }

    [Theory]
    [InlineData(BuiltInAction.ExitApplication)]
    [InlineData(BuiltInAction.RestartApplication)]
    public async Task Lifecycle_commands_are_safe_without_a_window_action_subscriber(BuiltInAction action)
    {
        var fixture = new Fixture(subscribeToWindowActions: false);
        var command = fixture.Catalog.GetCommands().Single(item => item.Action == action);

        await fixture.ViewModel.ExecuteAsync(command);

        fixture.Voice.StopCalls.Should().Be(1);
    }

    [Fact]
    public async Task Hide_is_safe_without_a_window_action_subscriber()
    {
        var fixture = await Fixture.CreateInitializedAsync(subscribeToWindowActions: false);
        await fixture.ViewModel.ToggleListeningCommand.ExecuteAsync();
        var command = fixture.Catalog.GetCommands().Single(item => item.Action == BuiltInAction.HideApplication);

        await fixture.ViewModel.ExecuteAsync(command);

        fixture.ViewModel.State.Should().Be(AssistantState.Hidden);
    }

    private sealed class Fixture
    {
        public Fixture(bool subscribeToWindowActions = true)
        {
            Catalog = new BuiltInCommandCatalog();
            Dispatcher = new ImmediateDispatcher();
            Voice = new FakeVoiceRecognitionService(Dispatcher, Events);
            Session = new FakeSessionController(Events);
            Probe = new StubProbe(new DependencyStatus(
                "storage",
                "Storage",
                DependencyReadiness.Ready,
                "ready"));
            var bootstrapper = new DependencyBootstrapper([Probe]);
            ViewModel = new MainViewModel(
                Catalog,
                new BuiltInCommandRouter(Catalog),
                bootstrapper,
                Voice,
                Session,
                Dispatcher,
                new FakeApplicationInfo());
            if (subscribeToWindowActions)
            {
                ViewModel.WindowActionRequested += (_, action) =>
                {
                    WindowActions.Add(action);
                    Events.Add($"window.{action}");
                };
            }
        }

        public BuiltInCommandCatalog Catalog { get; }

        public ImmediateDispatcher Dispatcher { get; }

        public FakeVoiceRecognitionService Voice { get; }

        public FakeSessionController Session { get; }

        public StubProbe Probe { get; }

        public MainViewModel ViewModel { get; }

        public List<WindowAction> WindowActions { get; } = [];

        public List<string> Events { get; } = [];

        public static async Task<Fixture> CreateInitializedAsync(bool subscribeToWindowActions = true)
        {
            var fixture = new Fixture(subscribeToWindowActions);
            fixture.Voice.Microphones = [new MicrophoneDevice("0", "Headset")];
            await fixture.ViewModel.InitializeAsync();
            return fixture;
        }

        public async Task RunAsync(string command)
        {
            ViewModel.CommandText = command;
            await ViewModel.RunTypedCommand.ExecuteAsync();
        }
    }

    private sealed class StubProbe(DependencyStatus status) : IDependencyProbe
    {
        public TaskCompletionSource? Gate { get; set; }

        public async ValueTask<DependencyStatus> ProbeAsync(CancellationToken cancellationToken)
        {
            if (Gate is not null)
            {
                await Gate.Task.WaitAsync(cancellationToken);
            }

            return status;
        }
    }

    private sealed class ImmediateDispatcher : IUiDispatcher
    {
        public Task LastInvocation { get; private set; } = Task.CompletedTask;

        public InvalidOperationException? InvokeException { get; set; }

        public Task InvokeAsync(Func<Task> action)
        {
            if (InvokeException is not null)
            {
                throw InvokeException;
            }

            LastInvocation = action();
            return LastInvocation;
        }

        public void Post(Action action) => action();
    }

    private sealed class FakeVoiceRecognitionService(
        ImmediateDispatcher dispatcher,
        List<string> events) : IVoiceRecognitionService
    {
        public event EventHandler<VoiceTranscriptEventArgs>? TranscriptRecognized;

        public event EventHandler<VoiceRecognitionFailureEventArgs>? RecognitionFailed;

        public bool IsListening { get; private set; }

        public IReadOnlyList<MicrophoneDevice> Microphones { get; set; } = [];

        public Exception? GetMicrophonesException { get; set; }

        public Exception? StartException { get; set; }

        public TaskCompletionSource? StartGate { get; set; }

        public MicrophoneDevice? StartedMicrophone { get; private set; }

        public IReadOnlyList<string> StartedPhrases { get; private set; } = [];

        public int StopCalls { get; private set; }

        public IReadOnlyList<MicrophoneDevice> GetMicrophones()
        {
            if (GetMicrophonesException is not null)
            {
                throw GetMicrophonesException;
            }

            return Microphones;
        }

        public async Task StartAsync(
            MicrophoneDevice microphone,
            IEnumerable<string> phrases,
            CancellationToken cancellationToken = default)
        {
            if (StartException is not null)
            {
                throw StartException;
            }

            StartedMicrophone = microphone;
            StartedPhrases = phrases.ToArray();
            if (StartGate is not null)
            {
                await StartGate.Task.WaitAsync(cancellationToken);
            }

            IsListening = true;
        }

        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            events.Add("voice.stop");
            StopCalls++;
            IsListening = false;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public async Task RaiseTranscriptAsync(string transcript, float confidence)
        {
            TranscriptRecognized?.Invoke(this, new VoiceTranscriptEventArgs(transcript, confidence));
            await dispatcher.LastInvocation;
        }

        public void RaiseTranscript(string transcript, float confidence) =>
            TranscriptRecognized?.Invoke(this, new VoiceTranscriptEventArgs(transcript, confidence));

        public void RaiseFailure(string message) =>
            RecognitionFailed?.Invoke(this, new VoiceRecognitionFailureEventArgs(message));
    }

    private sealed class FakeSessionController(List<string> events) : ISessionController
    {
        public bool LockResult { get; set; } = true;

        public int LockCalls { get; private set; }

        public bool LockCurrentSession()
        {
            events.Add("session.lock");
            LockCalls++;
            return LockResult;
        }
    }

    private sealed class FakeApplicationInfo : IApplicationInfo
    {
        public string Version => "1.2.3";
    }
}