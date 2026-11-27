using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

using AwesomeAssertions;
using Kora.Application.Hosting;
using Kora.Core.Authorization.SessionPermission;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionRoutingTests
{
    [Theory]
    [InlineData("complete")]
    [InlineData("cancel")]
    [InlineData("native")]
    [InlineData("drift")]
    public async Task ApproveAllForegroundWakeShowsAndSpeaksOriginalThenJoinsCancelCaptureBeforeAutomaticSubmission(string boundary)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var capture = new RoutingReplyCapture();
            await using var f = new WakeNativeComposition(capture);
            var speech = new SendReviewSpeech();
            f.Composition.Ui.Native.SpeechOverride = speech;
            f.Composition.Ui.Native.Interactions.BindSessionPermissionDefault(static () => SessionPermissionLevel.ApproveAll);
            await f.Initialize(expanded: true);
            var native = f.Composition.Ui.Native;
            try
            {
            var entry = f.Composition.Ui.First;
            var policy = await native.Interactions.ReadSessionPermissionAsync(entry.Authority.SessionId, native.Token);
            policy.Level.Should().Be(SessionPermissionLevel.ApproveAll);
            _ = await f.Stage.NextAsync().WaitAsync(TimeSpan.FromSeconds(15), native.Token);
            await native.Main.SetVoiceConsentAsync(false);
            native.Main.PublishForegroundConversationTarget(new ForegroundConversationTarget(entry, static () => true));
            await native.Main.SetVoiceConsentAsync(true);
            var readbackOpening = new TaskCompletionSource<Window>(TaskCreationOptions.RunContinuationsAsynchronously);
            var reviewOpening = new TaskCompletionSource<SessionRoutingWindow>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var opened = Window.WindowOpenedEvent.AddClassHandler<Window>((window, _) =>
            {
                if (window.Content is SessionRoutingReadbackView) { readbackOpening.TrySetResult(window); }
                if (window is SessionRoutingWindow review) { reviewOpening.TrySetResult(review); }
            });
            var original = await f.Stage.NextAsync().WaitAsync(TimeSpan.FromSeconds(15), native.Token);
            original.Wake();
            await original.Capture.Started.Task.WaitAsync(TimeSpan.FromSeconds(15), native.Token);
            original.Capture.Transcript("compare exact original plans");
            original.Capture.Done.SetResult();
            await Task.WhenAny(readbackOpening.Task, f.Voice.Completion)
                .WaitAsync(TimeSpan.FromSeconds(15), native.Token);
            if (f.Voice.Completion.IsCompleted) { await f.Voice.Completion; }
            var readback = await readbackOpening.Task.WaitAsync(TimeSpan.FromSeconds(15), native.Token);
            readback.IsActive.Should().BeFalse();
            readback.GetVisualDescendants().OfType<TextBox>().Single().Text.Should().Be("compare exact original plans");
            await speech.Started.Task.WaitAsync(TimeSpan.FromSeconds(15), native.Token);
            speech.Text.Should().Be("Sending to Alpha: compare exact original plans. Say cancel to stop.");
            capture.Opens.Should().Be(1);
            capture.IsListening.Should().BeTrue();
            f.Composition.Adapter.Calls.Should().Be(0);
            capture.Closing = new(TaskCreationOptions.RunContinuationsAsynchronously);
            if (boundary is "drift")
            {
                using var source = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi),
                    HostActivityLayer.Application, HostOperation.Request);
                await native.Sessions.SetSessionPermissionAsync(policy, SessionPermissionLevel.ReviewAll,
                    static () => true, native.Token);
            }
            if (boundary is "cancel") { capture.Transcript("cancel"); }
            else if (boundary is "native")
            { readback.GetVisualDescendants().OfType<Button>().Single().RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }
            else { speech.Release.SetResult(); }
            await capture.StopStarted.Task.WaitAsync(TimeSpan.FromSeconds(15), native.Token);
            f.Composition.Adapter.Calls.Should().Be(0);
            capture.Closing.SetResult();
            if (boundary is "drift")
            {
                var review = await reviewOpening.Task.WaitAsync(TimeSpan.FromSeconds(15), native.Token);
                f.Composition.Adapter.Calls.Should().Be(0);
                Find<Button>(review, "RoutingSubmit").IsEnabled.Should().BeFalse();
                Click(review, "RoutingCancel");
            }
            var successor = f.Stage.NextAsync();
            await Task.WhenAny(successor, f.Voice.Completion).WaitAsync(TimeSpan.FromSeconds(15), native.Token);
            if (f.Voice.Completion.IsCompleted) { await f.Voice.Completion; }
            f.Composition.Adapter.Calls.Should().Be(boundary is "complete" ? 1 : 0);
            capture.IsCaptureQuiescent.Should().BeTrue();
            if (boundary is "complete")
            {
                f.Composition.Adapter.Originals.Should().Equal("compare exact original plans");
                f.Composition.Adapter.Requests.Single().Origin.Should().Be(RequestOrigin.ActivatedVoice);
            }
            }
            finally
            {
                capture.Closing?.TrySetResult();
                speech.Release.TrySetResult();
                await native.Main.DisableListeningFromTrayAsync();
                await f.Voice.Completion.WaitAsync(TimeSpan.FromSeconds(15), native.Token);
            }
        });
    }
}
