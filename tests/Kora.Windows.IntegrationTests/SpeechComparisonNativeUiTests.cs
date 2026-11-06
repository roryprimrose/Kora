using System.Reflection;

using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;

using AwesomeAssertions;

using Kora.Application.Configuration;
using Kora.Application.Infrastructure;
using Kora.Application.Presentation;
using Kora.Application.Voice;
using Kora.Core.Voice;
using Kora.NativeUxFixture;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed class SpeechComparisonNativeUiTests
{
    [Fact]
    public async Task ActualSettingsControlsCompleteReviewTestResultsCancelJoinRecoveryAndCloseWithInertFakes()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var runtime = new Runtime();
            await using var comparison = new SpeechSetupComparison(runtime, runtime);
            using var fixture = new NativeUxFixtureSession(Path.GetTempPath(), syntheticVoice: true);
            await using var admission = new AudioControlAdmission(fixture.Interactions, fixture.Interactions, new(fixture.Tasks));
            var detailPreferences = new LocalDetailPresentationPreferences(fixture);
            var details = new DetailPresentationConfigurationService(detailPreferences, admission, fixture.Audit, TimeProvider.System);
            var executionPreferences = new LocalConversationExecutionPreferences(fixture);
            var execution = new ConversationExecutionConfigurationService(executionPreferences, admission, fixture.Audit);
            execution.Observe();
            await using var startupAdmission = new LogonStartupAdmission(fixture.Interactions, fixture.Interactions, new(fixture.Tasks));
            var startupPreferences = new LocalStartupPresentationPreferences(fixture);
            var startup = new StartupPresentationConfigurationService(startupPreferences, startupAdmission, fixture.Audit, TimeProvider.System);
            await fixture.InitializeAsync(null, speechSetupComparison: comparison, detailPresentationConfiguration: details,
                conversationExecution: execution, startupPresentationConfiguration: startup);
            fixture.Main.DetailPresentationStatus.Should().Be(details.Description);
            fixture.Main.ConversationExecutionStatus.Should().Be(execution.Description);
            fixture.Main.StartupPresentationStatus.Should().Be(startup.Status);
            detailPreferences.Load().Should().BeNull();
            executionPreferences.Load().Should().BeNull();
            startupPreferences.Load().Should().BeNull();
            fixture.Main.BindVoiceOwnershipGate(static () => true);
            await fixture.Main.SetVoiceConsentAsync(true);
            await fixture.Main.ToggleListeningCommand.ExecuteAsync();
            fixture.Main.IsVoiceEnabled.Should().BeFalse();
            using var controller = new SettingsWindowController(fixture.Main, NullLogger<SettingsWindowController>.Instance);
            fixture.Main.ShowSettings();
            // Read only the existing singleton window handle; exercise its real bindings and lifecycle.
            var window = (SettingsWindow)typeof(SettingsWindowController).GetField("window",
                BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(controller)!;
            window.ShowVoiceRecovery();
            window.UpdateLayout();
            var controls = window.GetVisualDescendants().ToArray();
            var phrase = controls.OfType<TextBox>().Single(box =>
                string.Equals(ControlAutomationPeer.CreatePeerForElement(box)!.GetName(),
                    "Inert expected comparison phrase", StringComparison.Ordinal));
            phrase.Text = "Kora confirm operation";
            Dispatcher.UIThread.RunJobs();
            fixture.Main.SpeechComparisonPhrase.Should().Be("Kora confirm operation");
            var review = Button(fixture.Main.ReviewSpeechComparisonCommand);
            var test = Button(fixture.Main.TestSpeechComparisonCommand);
            var cancel = Button(fixture.Main.CancelSpeechComparisonCommand);
            ControlAutomationPeer.CreatePeerForElement(test)!.GetName().Should().Be("Test with microphone");
            runtime.Captures.Should().Be(0);
            await ((AsyncCommand)review.Command!).ExecuteAsync();
            Dispatcher.UIThread.RunJobs();
            test.IsEnabled.Should().BeTrue(fixture.Main.SpeechComparisonStatus);
            await ((AsyncCommand)test.Command!).ExecuteAsync();
            Dispatcher.UIThread.RunJobs();
            fixture.Main.SpeechComparisonResults.Should().Contain("correct").And.Contain("12.0 ms").And.Contain("unknown");
            fixture.Main.SpeechComparisonStatus.Should().Contain("complete");
            runtime.Captures.Should().Be(2);
            await ((AsyncCommand)cancel.Command!).ExecuteAsync();
            fixture.Main.SpeechComparisonResults.Should().BeEmpty();

            runtime.Hold = new(TaskCreationOptions.RunContinuationsAsynchronously);
            await ((AsyncCommand)review.Command!).ExecuteAsync();
            var run = ((AsyncCommand)test.Command!).ExecuteAsync();
            await runtime.Entered.Task.WaitAsync(TestContext.Current.CancellationToken);
            var stopping = ((AsyncCommand)cancel.Command!).ExecuteAsync();
            stopping.IsCompleted.Should().BeFalse();
            comparison.IsRunning.Should().BeTrue();
            runtime.Hold.SetResult(new("Late inert result", TimeSpan.FromMilliseconds(12)));
            await Task.WhenAll(run, stopping);
            fixture.Main.SpeechComparisonResults.Should().BeEmpty();
            comparison.IsRunning.Should().BeFalse();
            runtime.Hold = null;
            await ((AsyncCommand)review.Command!).ExecuteAsync();
            await ((AsyncCommand)test.Command!).ExecuteAsync();
            fixture.Main.SpeechComparisonResults.Should().Contain("correct");
            fixture.Voice.StartCalls.Should().Be(0);
            fixture.Main.IsVoiceEnabled.Should().BeFalse();
            fixture.Main.HasVoiceConsent.Should().BeTrue();
            window.Close();
            Dispatcher.UIThread.RunJobs();
            fixture.Main.SpeechComparisonResults.Should().BeEmpty();
            fixture.Main.SpeechComparisonPhrase.Should().NotContain("confirm operation");
            detailPreferences.Load().Should().BeNull();
            executionPreferences.Load().Should().BeNull();
            startupPreferences.Load().Should().BeNull();

            Button Button(AsyncCommand command) => controls.OfType<Button>().Single(button => ReferenceEquals(button.Command, command));
        });
    }

    private sealed class Runtime : ISpeechSetupComparisonRuntime, ISpeechInputProfileRuntime
    {
        public SpeechInputGeneration ProfileGeneration { get; } = new(0, SpeechInputSelection.Default);
        public bool IsProfileAvailable => true;
        public int Captures { get; private set; }
        public TaskCompletionSource<SpeechComparisonObservation>? Hold { get; set; }
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public IReadOnlyList<SpeechComparisonProfile> GetComparisonProfiles() => [new(SpeechInputSelection.Default, "Installed fixture profile")];
        public Task<ISpeechSetupComparisonLease> AcquireComparisonAsync(SpeechInputGeneration generation,
            MicrophoneDevice microphone, Func<bool> eligible, CancellationToken cancellationToken)
        {
            eligible().Should().BeTrue();
            return Task.FromResult<ISpeechSetupComparisonLease>(new Lease(this));
        }
        public void InitializeProfile(SpeechInputSelection? selection) => throw new NotSupportedException();
        public Task<ISpeechInputProfileSwitch> RetireProfileAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        private sealed class Lease(Runtime owner) : ISpeechSetupComparisonLease
        {
            public Task<SpeechComparisonObservation> CaptureAsync(SpeechComparisonProfile profile, Action recordingStarted,
                CancellationToken cancellationToken)
            {
                recordingStarted();
                owner.Captures++;
                if (owner.Hold is { } held) { owner.Entered.TrySetResult(); return held.Task; }
                return Task.FromResult(new SpeechComparisonObservation("Kora confirm operation", TimeSpan.FromMilliseconds(12)));
            }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
