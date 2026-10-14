using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;

using AwesomeAssertions;

using Kora.NativeUxFixture;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SpeechCaptionDisplayTests
{
    [Theory]
    [InlineData(75)]
    [InlineData(100)]
    [InlineData(200)]
    public async Task ConfirmedCaptionScaleIsContentOnlyAbsoluteAndKeepsNativeGeometryAndAccessibleLabels(int percent)
    {
        await HeadlessSession.RunAsync(() =>
        {
            var caption = new Caption { ReaderTextScalePercent = percent,
                SpeechCaptionText = string.Concat(Enumerable.Repeat("Exact observed text α 🙂.\n", 20)) };
            var primary = Display(0, 0, 1920, 1080, 1, primary: true);
            var other = Display(-1920, 0, 1920, 1080, 1.5);
            var window = new SpeechCaptionWindow(caption);
            var body = window.FindControl<TextBox>("CaptionText")!;
            var baseline = body.FontSize;
            using var controller = new SpeechCaptionWindowController(caption, window, new Displays(primary, other));
            controller.Refresh();
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            controller.Refresh();
            window.UpdateLayout();
            var geometry = (window.Width, window.Height, window.Position);
            var chrome = window.GetVisualDescendants().OfType<TextBlock>()
                .Where(control => !body.IsVisualAncestorOf(control))
                .ToDictionary(control => control, control => (control.FontSize, control.LineHeight, AutomationProperties.GetName(control)));
            var controls = window.GetVisualDescendants().OfType<Button>()
                .Where(control => !body.IsVisualAncestorOf(control))
                .ToDictionary(control => control, control => (control.FontSize, AutomationProperties.GetName(control)));
            var text = caption.SpeechCaptionText;
            var name = AutomationProperties.GetName(body);
            var help = AutomationProperties.GetHelpText(body);
            foreach (var scale in new[] { percent, 200, 75, 100, percent })
            {
                caption.ReaderTextScalePercent = scale;
                window.UpdateLayout();
                body.FontSize.Should().Be(baseline * scale / 100d);
                body.Text.Should().Be(text);
                (window.Width, window.Height, window.Position).Should().Be(geometry);
                (AutomationProperties.GetName(body), AutomationProperties.GetHelpText(body)).Should().Be((name, help));
                foreach (var (control, original) in chrome)
                {
                    (control.FontSize, control.LineHeight, AutomationProperties.GetName(control)).Should().Be(original);
                }
                foreach (var (control, original) in controls)
                {
                    (control.FontSize, AutomationProperties.GetName(control)).Should().Be(original);
                }
            }
            name.Should().Be("Current spoken utterance");
            controls.Values.Should().Contain(value => string.Equals(value.Item2, "Pin or unpin current speech text", StringComparison.Ordinal));
            caption.SpeechCaptionPinLabel = "Unpin";
            caption.SpeechCaptionLabel = "PREVIOUS SPEECH";
            Choose(controller, window, other);
            AssertContained(window, other);
            Dispatcher.UIThread.RunJobs();
            controller.Refresh();
            window.UpdateLayout();
            var moved = (window.Width, window.Height, window.Position);
            caption.ReaderTextScalePercent = percent == 75 ? 200 : 75;
            window.UpdateLayout();
            (window.Width, window.Height, window.Position).Should().Be(moved);
            caption.SpeechCaptionPinLabel.Should().Be("Unpin");
            caption.SpeechCaptionLabel.Should().Be("PREVIOUS SPEECH");
            body.Text.Should().Be(text);
            caption.Failures.Should().BeEmpty();
            caption.Retirements.Should().Be(0);
        });
    }

    [Theory]
    [InlineData(75)]
    [InlineData(100)]
    [InlineData(200)]
    public async Task ProductionCaptionPresentationUsesConfirmedWorkflowForNewAndOpenWindowsWithoutAudio(int percent)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var fixture = new NativeUxFixtureSession(Path.GetTempPath(), silentCaption: true);
            await fixture.InitializeAsync();
            fixture.Main.ReaderTextScalePercent = percent;
            Dispatcher.UIThread.RunJobs();
            var primary = Display(0, 0, 1920, 1080, 1, primary: true);
            var window = new SpeechCaptionWindow(fixture.Main);
            var body = window.FindControl<TextBox>("CaptionText")!;
            var baseline = body.FontSize;
            using var controller = new SpeechCaptionWindowController(fixture.Main, window, new Displays(primary));
            await fixture.QueueSyntheticCaptionAsync();
            controller.Refresh();
            body.Text.Should().BeNullOrEmpty("queued text has no presentation authority");
            fixture.ObserveSyntheticCaption();
            controller.Refresh();
            body.Text.Should().Be(fixture.SilentSpeech!.ExactText);
            body.FontSize.Should().Be(baseline * percent / 100d);
            var identity = fixture.SilentSpeech.PlaybackId;
            await fixture.Main.ToggleSpeechCaptionPinCommand.ExecuteAsync();
            foreach (var scale in new[] { 200, 75, 100, percent })
            {
                fixture.Main.ReaderTextScalePercent = scale;
                Dispatcher.UIThread.RunJobs();
                body.FontSize.Should().Be(baseline * scale / 100d);
                body.Text.Should().Be(fixture.SilentSpeech.ExactText);
                fixture.SilentSpeech.PlaybackId.Should().Be(identity);
                fixture.Main.IsSpeechCaptionPinned.Should().BeTrue();
            }
            await fixture.CompleteSyntheticCaptionAsync();
            fixture.Main.ReaderTextScalePercent = percent == 75 ? 200 : 75;
            Dispatcher.UIThread.RunJobs();
            body.Text.Should().Be(fixture.SilentSpeech.ExactText);
            fixture.Main.SpeechCaptionLabel.Should().Be("PREVIOUS SPEECH");
            fixture.Main.IsSpeechCaptionPinned.Should().BeTrue();
            await fixture.StopSyntheticCaptionAsync();
            body.Text.Should().BeNullOrEmpty("source retirement must clear without waiting for a frame");
            body.FontSize.Should().Be(baseline);
            fixture.Main.ReaderTextScalePercent = 100;
            Dispatcher.UIThread.RunJobs();
            controller.Refresh();
            body.Text.Should().BeNullOrEmpty();
            window.IsVisible.Should().BeFalse();
            fixture.SilentSpeech.AdmittedRequests.Should().Be(1);
        });
    }

    [Theory]
    [InlineData("privacy")]
    [InlineData("call")]
    [InlineData("interruption")]
    [InlineData("replacement")]
    [InlineData("cancellation")]
    [InlineData("failure")]
    [InlineData("closure")]
    public async Task CaptionRetirementClearsScaledControlAndLateFramesOrPreferenceChangesCannotRestoreIt(string reason)
    {
        await HeadlessSession.RunAsync(() =>
        {
            var caption = new Caption { ReaderTextScalePercent = 200, SpeechCaptionPinLabel = "Unpin" };
            var source = new Displays(Display(0, 0, 1920, 1080, 1, primary: true));
            var window = new SpeechCaptionWindow(caption);
            var body = window.FindControl<TextBox>("CaptionText")!;
            var baseline = body.FontSize;
            using var controller = new SpeechCaptionWindowController(caption, window, source);
            controller.Refresh();
            body.FontSize.Should().Be(baseline * 2);
            body.Text.Should().NotBeNullOrEmpty();
            if (reason is "privacy") { caption.ClosePrivacy(); }
            else if (reason is "closure") { window.Close(); }
            else
            {
                caption.ReportSpeechCaptionDisplayUnavailable();
                controller.Refresh();
            }
            body.Text.Should().BeNullOrEmpty();
            body.FontSize.Should().Be(baseline);
            window.IsVisible.Should().BeFalse();
            caption.ObserveLateFrame();
            caption.ReaderTextScalePercent = 75;
            controller.Refresh();
            source.Change(Display(0, 0, 1920, 1080, 1, primary: true));
            body.Text.Should().BeNullOrEmpty();
            body.FontSize.Should().Be(baseline);
            window.IsVisible.Should().BeFalse();
            if (reason is "replacement")
            {
                caption.ObserveFreshUtterance();
                controller.Refresh();
                body.Text.Should().Be("Fresh observed utterance.");
                body.FontSize.Should().Be(baseline * .75);
            }
        });
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("74")]
    [InlineData("201")]
    [InlineData("100\n")]
    [InlineData("unconfirmed")]
    [InlineData("external")]
    public async Task RealPreferenceEvidenceFailureRetiresOpenAndNewCaptionsWithoutDefaultOrResurrection(string state)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var fixture = new NativeUxFixtureSession(Path.GetTempPath());
            await fixture.InitializeAsync();
            fixture.Main.ReaderTextScalePercent = 200;
            Dispatcher.UIThread.RunJobs();
            var caption = new Caption { OnTypographyAdmission = fixture.Main.EnsureReaderTypographyAvailable,
                ReaderTextScalePercent = fixture.Main.ReaderTextScalePercent };
            var primary = Display(0, 0, 1920, 1080, 1, primary: true);
            var window = new SpeechCaptionWindow(caption);
            var body = window.FindControl<TextBox>("CaptionText")!;
            var baseline = body.FontSize;
            using var controller = new SpeechCaptionWindowController(caption, window, new Displays(primary));
            controller.Refresh();
            body.FontSize.Should().Be(baseline * 2);
            var file = state is "unconfirmed" ? "reader-text-scale-unconfirmed.txt" : "reader-text-scale-percent.txt";
            File.WriteAllText(Path.Combine(fixture.LocalRoot, "Preferences", file),
                state switch { "unconfirmed" => "1", "external" => "75", _ => state });
            controller.Refresh();
            caption.Failures.Should().Equal(nameof(InvalidOperationException));
            caption.SpeechCaptionText.Should().BeNull();
            body.Text.Should().BeNullOrEmpty();
            body.FontSize.Should().Be(baseline);
            window.IsVisible.Should().BeFalse();
            caption.ReaderTextScalePercent = 75;
            caption.ObserveLateFrame();
            controller.Refresh();
            body.Text.Should().BeNullOrEmpty();
            var next = new Caption { OnTypographyAdmission = fixture.Main.EnsureReaderTypographyAvailable };
            var second = new SpeechCaptionWindow(next);
            using var secondController = new SpeechCaptionWindowController(next, second, new Displays(primary));
            secondController.Refresh();
            next.Failures.Should().Equal(nameof(InvalidOperationException));
            second.FindControl<TextBox>("CaptionText")!.Text.Should().BeNullOrEmpty();
            second.IsVisible.Should().BeFalse();
        });
    }

    [Fact]
    public async Task LatePrivacyRevocationDuringTypographyAdmissionNeverPublishesText()
    {
        await HeadlessSession.RunAsync(() =>
        {
            var caption = new Caption { ReaderTextScalePercent = 200 };
            var window = new SpeechCaptionWindow(caption);
            var body = window.FindControl<TextBox>("CaptionText")!;
            var baseline = body.FontSize;
            using var controller = new SpeechCaptionWindowController(caption, window,
                new Displays(Display(0, 0, 1920, 1080, 1, primary: true)));
            caption.OnTypographyAdmission = caption.ClosePrivacy;
            controller.Refresh();
            window.IsVisible.Should().BeFalse();
            body.Text.Should().BeNullOrEmpty();
            body.FontSize.Should().Be(baseline);
            caption.Failures.Should().BeEmpty();
        });
    }
}
