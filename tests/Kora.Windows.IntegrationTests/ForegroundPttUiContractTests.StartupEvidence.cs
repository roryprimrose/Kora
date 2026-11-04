using System.Text.Json;

using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

using AwesomeAssertions;

using Kora.Application.Configuration;
using Kora.Application.Voice;
using Kora.Core.Voice;
using Kora.NativeUxFixture;

namespace Kora.Windows.IntegrationTests;

public sealed partial class ForegroundPttUiContractTests
{
    private const string StartupAttachmentName = "foreground-ptt-start.json";

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task StartupEvidenceRetainsObservedAdmissionAndOriginalFailureWithoutContent(bool editorFocused, bool cancelled)
    {
        var context = TestContext.Current;
        await HeadlessSession.RunAsync(async () =>
        {
            using var fixture = new NativeUxFixtureSession(Path.GetTempPath(), syntheticVoice: true);
            await using var admission = new AudioControlAdmission(fixture.Interactions, fixture.Interactions, new(fixture.Tasks));
            var configuration = new ForegroundPttConfigurationService(new(fixture), admission, fixture.Audit);
            await fixture.InitializeAsync(null, foregroundPtt: configuration);
            await SaveShortcut(fixture, ForegroundPttShortcut.ControlShiftSpace);
            fixture.Main.BindVoiceOwnershipGate(static () => true);
            await fixture.Main.SetVoiceConsentAsync(true);
            await fixture.Main.ToggleListeningCommand.ExecuteAsync();
            const string forbidden = "PRIVATE_TRANSCRIPT_RESPONSE_COMMAND_DEVICE_PATH_CREDENTIAL";
            var response = new ResponseWindow(fixture.Main) { Title = forbidden };
            try
            {
                response.Show(); Activate(response); response.UpdateLayout();
                var input = response.FindControl<TextBox>("CommandInput")!;
                input.Text = forbidden;
                var button = response.FindControl<Button>("PushToTalkButton")!;
                button.Content = forbidden;
                (editorFocused ? (Control)input : button).Focus().Should().BeTrue();
                Exception original = cancelled ? new OperationCanceledException(forbidden) : new TimeoutException(forbidden);
                var thrown = await Record.ExceptionAsync(() => PressWithStartupEvidenceAsync(response, fixture,
                    () => editorFocused ? Task.FromException(original)
                        : fixture.Voice.Started.Task.WaitAsync(TimeSpan.FromSeconds(20), context.CancellationToken)));
                if (editorFocused) { thrown.Should().BeSameAs(original); }
                else { thrown.Should().BeNull(); }
                fixture.Voice.StartCalls.Should().Be(editorFocused ? 0 : 1);
                Release(response, Key.Space);
                fixture.Voice.IsCaptureQuiescent.Should().BeTrue();
                fixture.Voice.EndCalls.Should().Be(editorFocused ? 0 : 1);
            }
            finally { response.Close(); }
        });

        var json = context.Attachments![StartupAttachmentName].AsString();
        json.Should().NotContain("PRIVATE_TRANSCRIPT_RESPONSE_COMMAND_DEVICE_PATH_CREDENTIAL");
        using var report = JsonDocument.Parse(json);
        report.RootElement.EnumerateObject().Select(property => property.Name)
            .Should().Equal("Schema", "OpeningCancellation", "Snapshots");
        report.RootElement.GetProperty("Schema").GetInt32().Should().Be(1);
        report.RootElement.GetProperty("OpeningCancellation").GetString().Should().Be("NotExposed");
        var snapshots = report.RootElement.GetProperty("Snapshots").EnumerateArray().ToArray();
        snapshots.Select(snapshot => snapshot.GetProperty("Stage").GetString())
            .Should().Equal("BeforePress", "AfterPress", "StartupExit");
        snapshots[0].EnumerateObject().Select(property => property.Name).Should().Equal(
            "Stage", "WindowVisible", "WindowActive", "ButtonVisible", "ButtonEnabled", "Focus",
            "EditorFocused", "CanPushToTalk", "BeginCanExecute", "EndCanExecute", "ShortcutMatchesSpace",
            "HostInputEligible", "HasVoiceConsent", "MicrophoneCatalogCurrent", "Busy", "Listening",
            "InteractionPending", "PushToTalkVisible", "WindowBubbleKeyHandled", "StartCalls", "EndCalls",
            "StartedCompleted", "CaptureQuiescent", "TestCancellationRequested");
        var before = snapshots[0].Deserialize<StartupSnapshot>()!;
        before.Focus.Should().Be(editorFocused ? "TextBox" : "PushToTalkButton");
        before.EditorFocused.Should().Be(editorFocused);
        before.WindowVisible.Should().BeTrue();
        before.WindowActive.Should().BeTrue();
        before.ButtonVisible.Should().BeTrue();
        before.ButtonEnabled.Should().BeTrue();
        before.CanPushToTalk.Should().BeTrue();
        before.BeginCanExecute.Should().BeTrue();
        before.EndCanExecute.Should().BeTrue();
        before.ShortcutMatchesSpace.Should().BeTrue();
        before.HostInputEligible.Should().BeTrue();
        before.HasVoiceConsent.Should().BeTrue();
        before.MicrophoneCatalogCurrent.Should().BeTrue();
        before.Busy.Should().BeFalse();
        before.Listening.Should().BeFalse();
        before.InteractionPending.Should().BeFalse();
        before.PushToTalkVisible.Should().BeTrue();
        before.TestCancellationRequested.Should().BeFalse();
        before.WindowBubbleKeyHandled.Should().BeNull();
        before.StartCalls.Should().Be(0);
        before.CaptureQuiescent.Should().BeTrue();
        var after = snapshots[1].Deserialize<StartupSnapshot>()!;
        after.WindowBubbleKeyHandled.Should().Be(!editorFocused);
        after.StartCalls.Should().Be(editorFocused ? 0 : 1);
        after.StartedCompleted.Should().Be(!editorFocused);
        var exit = snapshots[2].Deserialize<StartupSnapshot>()!;
        exit.StartCalls.Should().Be(after.StartCalls);
        exit.CaptureQuiescent.Should().Be(editorFocused);
    }

    private static async Task PressWithStartupEvidenceAsync(Window window, NativeUxFixtureSession fixture,
        Func<Task> waitForStartup)
    {
        var context = TestContext.Current;
        var evidence = NativeFixturePhaseTimings.Capture(context);
        var snapshots = new List<StartupSnapshot>(3);
        bool? keyHandled = null;
        void ObserveKey(object? sender, KeyEventArgs args)
        {
            if (args.Key == Key.Space && args.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Shift))
            { keyHandled = args.Handled; }
        }
        window.AddHandler(InputElement.KeyDownEvent, ObserveKey, RoutingStrategies.Bubble, handledEventsToo: true);
        try
        {
            evidence.PublishAttachment(() => snapshots.Add(CaptureStartup("BeforePress", window, fixture, keyHandled)));
            Press(window, Key.Space);
            evidence.PublishAttachment(() => snapshots.Add(CaptureStartup("AfterPress", window, fixture, keyHandled)));
            await waitForStartup();
        }
        finally
        {
            window.RemoveHandler(InputElement.KeyDownEvent, ObserveKey);
            evidence.PublishAttachment(() =>
            {
                snapshots.Add(CaptureStartup("StartupExit", window, fixture, keyHandled));
                context.AddAttachment(StartupAttachmentName, JsonSerializer.Serialize(new
                {
                    Schema = 1,
                    OpeningCancellation = "NotExposed",
                    Snapshots = snapshots,
                }), replaceExistingValue: true);
            });
        }
    }

    private static StartupSnapshot CaptureStartup(string stage, Window window, NativeUxFixtureSession fixture,
        bool? keyHandled)
    {
        var main = fixture.Main;
        var button = window.FindControl<Button>("PushToTalkButton")!;
        var focused = window.FocusManager?.GetFocusedElement() as Control;
        return new(stage, window.IsVisible, window.IsActive, button.IsVisible, button.IsEnabled,
            focused is null ? "None" : ReferenceEquals(focused, button) ? "PushToTalkButton"
                : focused is TextBox ? "TextBox" : focused is Button ? "Button" : "Other",
            focused is not null && focused.GetVisualAncestors().Prepend(focused).Any(control =>
                control is TextBox or SelectingItemsControl or NumericUpDown or RangeBase or AutoCompleteBox),
            main.CanPushToTalk, main.BeginPushToTalkCommand.CanExecute(null), main.EndPushToTalkCommand.CanExecute(null),
            main.EffectiveForegroundPttShortcut == ForegroundPttShortcut.ControlShiftSpace,
            main.CanRevealPrivatePresentation, main.HasVoiceConsent, main.IsMicrophoneCatalogCurrent,
            main.IsBusy, main.IsListening, main.IsResponseInteractionPending, main.IsPushToTalkVisible,
            keyHandled, fixture.Voice.StartCalls, fixture.Voice.EndCalls,
            fixture.Voice.Started.Task.IsCompletedSuccessfully, fixture.Voice.IsCaptureQuiescent,
            TestContext.Current.CancellationToken.IsCancellationRequested);
    }

    private sealed record StartupSnapshot(string Stage, bool WindowVisible, bool WindowActive,
        bool ButtonVisible, bool ButtonEnabled, string Focus, bool EditorFocused, bool CanPushToTalk,
        bool BeginCanExecute, bool EndCanExecute, bool ShortcutMatchesSpace, bool HostInputEligible,
        bool HasVoiceConsent, bool MicrophoneCatalogCurrent, bool Busy, bool Listening,
        bool InteractionPending, bool PushToTalkVisible, bool? WindowBubbleKeyHandled,
        int StartCalls, int EndCalls, bool StartedCompleted, bool CaptureQuiescent, bool TestCancellationRequested);
}
