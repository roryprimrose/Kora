using System.Text;

using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

using AwesomeAssertions;

using Kora.Application;
using Kora.Application.Dependencies;
using Kora.Application.Voice;
using Kora.Core.Dependencies;
using Kora.Core.Providers;
using Kora.Core.Tools;
using Kora.Core.Voice;
using Kora.Windows.Audio;
using Kora.Windows.IntegrationTests.Audio;
using Kora.Windows.IntegrationTests.Storage;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionsConversationTests
{
    [Fact]
    public async Task NativeRecognizerHypothesisIsPrivateNoncopyableFeedbackThenOnlyFinalOriginalInputReachesActualSubmissionAndRetainedAnswer()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            var adapter = new DictationAnswerAdapter();
            await using var composed = new Composition(f, adapter: adapter);
            var model = composed.Model;
            await model.RefreshAsync();
            await model.SelectAsync(model.Sessions.Single());
            var host = new SessionsWindow(model);
            host.Show();
            using var privacy = new ActivatedVoiceRecognitionTests.FakePrivacy();
            var factory = new ActivatedVoiceRecognitionTests.FakeFactory();
            var native = new ActivatedVoiceRecognitionTests.FakeCapture();
            factory.OpenResult.SetResult(native);
            await using var capture = new WindowsVoiceRecognitionService(NullLogger<WindowsVoiceRecognitionService>.Instance,
                privacy, factory, VoiceCaptureLimits.Default);
            await using var dictation = new ComposerDictationService(new NewRequestControllerTests.QualifiedDictationCapture(capture),
                new AvaloniaUiDispatcher(), () => new(new("mic", "Synthetic"), 1, 1, 1, 1, 1),
                NullLogger<ComposerDictationService>.Instance);
            model.BindDictation(dictation);
            var window = new SessionDictationWindow(model);
            window.Show();
            var preview = window.GetVisualDescendants().OfType<ProvisionalDictationText>().Single();
            try
            {
                var retention = await f.Store.ReadRetentionAsync(f.Request.SessionId, f.Token);
                var audit = WindowsSqliteConversationTests.AuditText(f);
                var button = window.GetVisualDescendants().OfType<Button>().Single(control => control.Name is "DictateSession");
                button.Focus().Should().BeTrue();
                window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
                await window.Operation;
                native.Hypothesis("PRIVATE_UNSTABLE submit approve");
                native.Hypothesis("PRIVATE_LATEST", 2);
                Dispatcher.UIThread.RunJobs();
                preview.Text.Should().Be("PRIVATE_LATEST");
                model.ComposerBody.Should().BeEmpty();
                host.FindControl<TextBox>("ConversationComposer")!.Text.Should().BeEmpty();
                adapter.Calls.Should().Be(0);
                f.Count("conversation_messages").Should().Be(0);
                f.Count("conversation_drafts").Should().Be(0);
                f.Count("host_questions").Should().Be(0);
                WindowsSqliteConversationTests.AuditText(f).Should().Be(audit);
                (await f.Store.ReadRetentionAsync(f.Request.SessionId, f.Token)).Should().Be(retention);
                var peer = ControlAutomationPeer.CreatePeerForElement(preview)!;
                peer.GetName().Should().Contain("may change").And.NotContain("PRIVATE");
                peer.GetProvider<IValueProvider>().Should().BeNull();
                AutomationProperties.GetLiveSetting(preview).Should().Be(AutomationLiveSetting.Off);
                preview.ContextMenu.Should().BeNull();
                preview.Should().NotBeAssignableTo<TextBox>().And.NotBeAssignableTo<SelectableTextBlock>();
                var late = native.QueueHypothesis("PRIVATE_LATE", 4);
                native.Hypothesis("PRIVATE_PENDING", 3);
                native.Transcript("accepted final original");
                Dispatcher.UIThread.RunJobs();
                preview.Text.Should().BeEmpty("final completion clears pending and visible hypotheses");
                model.ComposerBody.Should().BeEmpty("capture release still owns final admission");
                window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
                await window.Operation;
                await dictation.Quiescence;
                late();
                Dispatcher.UIThread.RunJobs();
                preview.Text.Should().BeEmpty();
                model.ComposerBody.Should().Be("accepted final original");
                capture.IsCaptureQuiescent.Should().BeTrue();
                native.Disposed.Task.IsCompletedSuccessfully.Should().BeTrue();
                adapter.Calls.Should().Be(0);
                window.Close();
                await window.CloseOperation;
                host.FindControl<Button>("SubmitConversation")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await host.ConversationOperation;
                adapter.Calls.Should().Be(1);
                adapter.Context.Should().Contain("accepted final original").And.NotContain("PRIVATE");
                var message = model.ConversationMessages.Single();
                message.Body.Should().Be("accepted final original");
                message.AssistantResponse!.Body.Should().Be("Genuine final-only answer");
                var retained = await f.Store.ReadConversationMessageAsync(f.Request.SessionId, message.Request.RequestId, f.Token);
                retained.Should().Be(message);
                WindowsSqliteConversationTests.AuditText(f).Should().NotContain("PRIVATE");
                f.Count("host_questions").Should().Be(0);
                f.Count("scoped_grants").Should().Be(0);
            }
            finally
            {
                window.Close();
                await window.CloseOperation;
                host.ClosePrivate();
            }
        });
    }

    [Theory]
    [InlineData("edit")]
    [InlineData("source")]
    [InlineData("switch")]
    [InlineData("cancel")]
    [InlineData("lock")]
    [InlineData("device")]
    [InlineData("invalid")]
    public async Task NativePrivatePreviewRetiresWithExactSourceOrWindowsCaptureAndJoinsLateRecognizer(string boundary)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            var session = f.Request.SessionId;
            if (boundary is "switch")
            {
                f.Request = InteractionStorageFixture.NewRequest();
                await f.AdmitAsync(newSession: true);
                await WindowsSqliteConversationTests.FinishInitial(f);
            }
            await using var composed = new Composition(f);
            var model = composed.Model;
            await model.RefreshAsync();
            await model.SelectAsync(model.Sessions.Single(row => row.Authority.SessionId == session));
            using var privacy = new ActivatedVoiceRecognitionTests.FakePrivacy();
            var factory = new ActivatedVoiceRecognitionTests.FakeFactory();
            var native = new ActivatedVoiceRecognitionTests.FakeCapture
            { DisposeGate = new(TaskCreationOptions.RunContinuationsAsynchronously) };
            factory.OpenResult.SetResult(native);
            await using var capture = new WindowsVoiceRecognitionService(NullLogger<WindowsVoiceRecognitionService>.Instance,
                privacy, factory, VoiceCaptureLimits.Default);
            await using var dictation = new ComposerDictationService(new NewRequestControllerTests.QualifiedDictationCapture(capture),
                new AvaloniaUiDispatcher(), () => new(new("mic", "Synthetic"), 1, 1, 1, 1, 1),
                NullLogger<ComposerDictationService>.Instance);
            model.BindDictation(dictation);
            var window = new SessionDictationWindow(model);
            window.Show();
            try
            {
                await window.BeginAsync();
                native.Hypothesis("PRIVATE_VISIBLE");
                Dispatcher.UIThread.RunJobs();
                var preview = window.GetVisualDescendants().OfType<ProvisionalDictationText>().Single();
                preview.Text.Should().Be("PRIVATE_VISIBLE");
                var late = native.QueueHypothesis("PRIVATE_LATE", 3);
                native.Hypothesis("PRIVATE_PENDING", 2);
                switch (boundary)
                {
                    case "edit": model.ComposerBody = "independent edit"; break;
                    case "source": model.RevokeDictationSource(session); break;
                    case "switch": await model.SelectAsync(model.Sessions.Single(row => row.Authority.SessionId != session)); break;
                    case "cancel": window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null); break;
                    case "lock": privacy.Set(privacy.Current with { SessionState = Kora.Core.Platform.WindowsSessionState.Locked }); break;
                    case "device": privacy.Set(privacy.Current with { ActiveMicrophoneIds = [] }); break;
                    case "invalid":
                        native.Hypothesis("\ud800", 3);
                        native.Hypothesis("\udc00", 4);
                        Dispatcher.UIThread.RunJobs();
                        preview.Text.Should().BeEmpty();
                        window.GetVisualDescendants().OfType<TextBlock>().Should().Contain(control =>
                            control.Text != null && control.Text.Contains("invalid or oversized", StringComparison.Ordinal));
                        dictation.IsActive.Should().BeTrue("invalid feedback must not disable final-only recognition");
                        window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
                        break;
                }
                Dispatcher.UIThread.RunJobs();
                preview.Text.Should().BeEmpty();
                dictation.IsActive.Should().BeFalse();
                dictation.Quiescence.IsCompleted.Should().BeFalse("cancellation is not resource release");
                late();
                native.Transcript("PRIVATE_FINAL_TOO_LATE");
                native.DisposeGate.SetResult();
                await dictation.Quiescence;
                Dispatcher.UIThread.RunJobs();
                preview.Text.Should().BeEmpty();
                capture.IsCaptureQuiescent.Should().BeTrue();
                model.ComposerBody.Should().Be(boundary is "edit" ? "independent edit" : "");
                f.Count("conversation_messages").Should().Be(0);
                WindowsSqliteConversationTests.AuditText(f).Should().NotContain("PRIVATE");
            }
            finally
            {
                native.DisposeGate.TrySetResult();
                window.Close();
                await window.CloseOperation;
            }
        });
    }

    private sealed class DictationAnswerAdapter : IModelTurnAdapter
    {
        public int Calls { get; private set; }
        public string Context { get; private set; } = string.Empty;
        public Task<ModelProviderReply> RunAsync(ModelTurnProvenance provenance, ProviderProfileTuple? profile,
            ReadOnlyMemory<byte> context, Func<string, string, CancellationToken, ValueTask<CapabilityReply>> invokeTool,
            CancellationToken token)
        {
            Calls++;
            Context = Encoding.UTF8.GetString(context.Span);
            return Task.FromResult(new ModelProviderReply(ModelTurnOutcome.Succeeded, provenance.Selection,
                new("Genuine final-only answer", null)));
        }
    }
}
