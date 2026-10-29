using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AwesomeAssertions;
using Kora.NativeUxFixture;
using Kora.Windows.IntegrationTests.Audio;

namespace Kora.Windows.IntegrationTests;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed class HandsFreeSetupNativeTests
{
    [Fact]
    public async Task Actual_accessible_Settings_review_confirm_prepares_and_reads_back_without_enabling_voice()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var assets = new HandsFreeSetupTests.Fixture();
            using var native = new NativeUxFixtureSession(Path.GetDirectoryName(assets.Files.Root)!);
            await native.InitializeAsync(null, speechComponentSetup: assets.Setup());
            var window = new SettingsWindow(native.Main);
            var visible = 0;
            window.PropertyChanged += (_, args) =>
            {
                if (args.Property == Window.IsVisibleProperty) { Volatile.Write(ref visible, window.IsVisible ? 1 : 0); }
            };
            native.Main.BindSpeechComponentNativeLifetime(() => Volatile.Read(ref visible) != 0);
            var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            try
            {
                window.Show();
                window.ShowVoiceRecovery();
                window.UpdateLayout();
                var panel = window.FindControl<StackPanel>("SpeechComponentsPanel")!;
                var status = panel.GetVisualDescendants().OfType<TextBlock>().Single(block =>
                    block.Text?.StartsWith("Inspect to check", StringComparison.Ordinal) == true);
                var prepare = panel.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, "Prepare / retry"));
                ControlAutomationPeer.CreatePeerForElement(prepare)!.GetName().Should().Contain("Prepare");
                status.PropertyChanged += (_, args) =>
                {
                    if (args.Property != TextBlock.TextProperty) { return; }
                    if (status.Text?.StartsWith("Review the exact", StringComparison.Ordinal) == true)
                    {
                        Dispatcher.UIThread.Post(() =>
                        {
                            try
                            {
                                assets.Acquisition.Calls.Should().Be(0);
                                var review = window.OwnedWindows.Single();
                                review.GetVisualDescendants().OfType<TextBlock>()
                                    .Single(block => block.Text?.Contains("Sources:", StringComparison.Ordinal) == true)
                                    .Text.Should().Contain("Sources:").And.Contain("Download:").And.Contain("Licence:")
                                    .And.Contain("No runtime selection");
                                review.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, "Approve Prepare"))
                                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                            }
                            catch (Exception exception) { completed.TrySetException(exception); }
                        });
                    }
                    if (status.Text?.StartsWith("Prepared assets.", StringComparison.Ordinal) == true) { completed.TrySetResult(); }
                    if (status.Text?.StartsWith("Speech data operation did not complete", StringComparison.Ordinal) == true)
                    {
                        completed.TrySetException(new InvalidOperationException(status.Text));
                    }
                };
                prepare.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await completed.Task.WaitAsync(TestContext.Current.CancellationToken);
                assets.Acquisition.Calls.Should().Be(2);
                status.Text.Should().Contain("Unavailable").And.Contain("Prepared");
                native.Main.IsVoiceEnabled.Should().BeFalse();
                native.Main.IsListening.Should().BeFalse();
                window.OwnedWindows.Should().BeEmpty();
            }
            finally { window.Close(); }
        });
    }
}
