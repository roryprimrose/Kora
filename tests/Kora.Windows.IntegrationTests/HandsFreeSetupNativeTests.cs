using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

using AwesomeAssertions;

using Kora.NativeUxFixture;
using Kora.Windows.IntegrationTests.Audio;
using Kora.Application.Hosting;
using Kora.Application.Dependencies;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;

namespace Kora.Windows.IntegrationTests;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed class HandsFreeSetupNativeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ActualSettingsRemovalRequiresSeparateNativeConfirmationAndReportsUnavailable(bool confirm)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var assets = new HandsFreeSetupTests.Fixture();
            var setup = assets.Setup();
            using var native = new NativeUxFixtureSession(Path.GetDirectoryName(assets.Files.Root)!);
            await native.InitializeAsync(null, speechComponentSetup: setup);
            await HostRequestRunner.RunAsync(RequestOrigin.LocalUi, async () =>
            {
                var preparation = await setup.ReviewAsync(assets.Component.Identity, SpeechComponentOperation.Prepare,
                    () => true, TestContext.Current.CancellationToken);
                await setup.ApplyAsync(preparation, true, assets.Progress, TestContext.Current.CancellationToken);
            });
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
                var remove = panel.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, "Remove"));
                ControlAutomationPeer.CreatePeerForElement(remove)!.GetName().Should().Contain("Remove");
                status.PropertyChanged += (_, args) =>
                {
                    if (args.Property != TextBlock.TextProperty) { return; }
                    if (status.Text?.StartsWith("Review the exact", StringComparison.Ordinal) == true)
                    {
                        Dispatcher.UIThread.Post(() =>
                        {
                            try
                            {
                                Directory.GetFiles(assets.FinalPath).Should().HaveCount(3);
                                var review = window.OwnedWindows.Single();
                                var disclosure = review.GetVisualDescendants().OfType<TextBlock>().Single(block =>
                                    block.Text?.Contains("Independent privacy and consent", StringComparison.Ordinal) == true);
                                disclosure.Text.Should().Contain("Exact remaining inventoried bytes: 72")
                                    .And.Contain("Saved / desired / effective input:").And.Contain("model.data: 4 bytes");
                                var decision = review.GetVisualDescendants().OfType<Button>().Single(button =>
                                    Equals(button.Content, confirm ? "Confirm exact model-data removal" : "Decline"));
                                ControlAutomationPeer.CreatePeerForElement(decision)!.GetName().Should().NotBeNullOrEmpty();
                                decision.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                            }
                            catch (Exception exception) { completed.TrySetException(exception); }
                        });
                    }
                    if (status.Text?.StartsWith(confirm ? "Assets unavailable." : "Speech data operation did not complete", StringComparison.Ordinal) == true)
                    { completed.TrySetResult(); }
                };
                remove.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await completed.Task.WaitAsync(TestContext.Current.CancellationToken);
                Directory.Exists(assets.FinalPath).Should().Be(!confirm);
                native.Main.IsVoiceEnabled.Should().BeFalse();
                native.Main.IsListening.Should().BeFalse();
                assets.Acquisition.Calls.Should().Be(2);
                File.Exists(assets.Native).Should().BeTrue();
            }
            finally { window.Close(); }
        });
    }

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