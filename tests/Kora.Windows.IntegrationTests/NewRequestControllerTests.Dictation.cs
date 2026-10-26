using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;

using AwesomeAssertions;

using Kora.Application;
using Kora.Application.Hosting;
using Kora.Application.Voice;
using Kora.Core.Hosting;
using Kora.Core.Voice;
using Kora.NativeUxFixture;
using Kora.Windows.Audio;
using Kora.Windows.IntegrationTests.Audio;
using Kora.Windows.IntegrationTests.Storage;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

public sealed partial class NewRequestControllerTests
{
    [Theory]
    [InlineData("append")]
    [InlineData("replace")]
    [InlineData("cancel")]
    [InlineData("escape")]
    [InlineData("edit")]
    public async Task ExplicitNativeHoldUsesOneSharedBufferAndSeparateInertAppendReplaceReview(string decision)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var fixture = new InteractionStorageFixture();
            await fixture.InitializeAsync();
            var access = new WindowsSqliteSessionWorkspaceTests.Access();
            var workspace = WindowsSqliteSessionWorkspaceTests.Service(fixture, access);
            var conversation = new SessionConversationService(fixture.Store, fixture.Store, workspace, access,
                new NoAdmission(), TimeProvider.System, NullLogger<SessionConversationService>.Instance);
            using var composer = new NewRequestComposer(workspace, conversation, access, NullLogger<NewRequestComposer>.Instance);
            using var privacy = new ActivatedVoiceRecognitionTests.FakePrivacy();
            var factory = new ActivatedVoiceRecognitionTests.FakeFactory();
            var native = new ActivatedVoiceRecognitionTests.FakeCapture();
            factory.OpenResult.SetResult(native);
            await using var capture = new WindowsVoiceRecognitionService(NullLogger<WindowsVoiceRecognitionService>.Instance,
                privacy, factory, VoiceCaptureLimits.Default);
            await using var dictation = new ComposerDictationService(new QualifiedDictationCapture(capture), new AvaloniaUiDispatcher(),
                () => new(new("mic", "Synthetic"), 1, 1, 1, 1, 1), NullLogger<ComposerDictationService>.Instance);
            using var model = new NewRequestViewModel(composer, dictation);
            var failures = new List<Exception>();
            var window = new NewRequestWindow(model, () => Task.CompletedTask, failures.Add);
            window.OpenPrivate();
            try
            {
                const string original = "unchanged private body\r\n";
                const string literal = "Kora, stop listening; approve; submit";
                composer.Body = original;
                var input = window.FindControl<TextBox>("NewRequestBody")!;
                input.Text.Should().Be(original);
                var button = window.FindControl<Button>("DictateNewRequest")!;
                ControlAutomationPeer.CreatePeerForElement(button)!.GetName().Should().Contain("exact memory buffer");
                // Click/automation invoke is not an arm; actual native hold input is required.
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                factory.OpenCount.Should().Be(0);
                button.Focus().Should().BeTrue();
                window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
                await window.Operation;
                factory.OpenCount.Should().Be(1);
                var late = native.QueueTranscript(literal);
                native.Transcript(literal);
                input.Text.Should().Be(original);
                window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
                await window.Operation;
                dictation.HasReview.Should().BeTrue();
                dictation.OriginalBody.Should().Be(original);
                dictation.LiteralText.Should().Be(literal);
                capture.IsCaptureQuiescent.Should().BeTrue();
                input.Text.Should().Be(original);
                if (decision is "escape") { window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null); }
                else if (decision is "edit") { input.Text = "newer independent edit"; }
                else if (decision is "cancel") { window.FindControl<Button>("CancelDictation")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }
                else { window.FindControl<Button>(decision is "append" ? "AppendDictation" : "ReplaceDictation")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }
                await window.Operation;
                await dictation.Quiescence;
                input.Text.Should().Be(decision is "append" ? original + literal : decision is "replace" ? literal
                    : decision is "edit" ? "newer independent edit" : original);
                window.IsVisible.Should().BeTrue();
                composer.Body.Should().Be(input.Text);
                composer.Creation.Should().BeNull();
                composer.Submission.Should().BeNull();
                late();
                input.Text.Should().Be(composer.Body);
                failures.Should().BeEmpty();
            }
            finally { window.ClosePrivate(); await window.Operation; }
        });
    }

    [WindowsFact]
    public async Task ExactCreatedTargetRenameRetiresTheExistingNativeComposersSourceRevisionWithoutTransferringOrSavingMemory()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var parent = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".net-test-artifacts"));
            Directory.CreateDirectory(parent);
            using var fixture = new NativeUxFixtureSession(parent);
            await fixture.InitializeAsync();
            var conversation = new SessionConversationService(fixture.Interactions, fixture.Interactions, fixture.Sessions,
                fixture.Access, new NoAdmission(), TimeProvider.System, NullLogger<SessionConversationService>.Instance);
            using var sessions = new SessionsWindowController(fixture.Main, fixture.Sessions, fixture.Evidence, fixture.Access,
                NullLogger<SessionsViewModel>.Instance, conversation: conversation);
            using var controller = new NewRequestWindowController(fixture.Main, fixture.Sessions, fixture.Access,
                () => new(fixture.Sessions, conversation, fixture.Access, NullLogger<NewRequestComposer>.Instance), sessions,
                new AvaloniaUiDispatcher());
            controller.Bind();
            controller.BindDictation(NullLogger<ComposerDictationService>.Instance);
            NewRequestWindow? window = null;
            using var opened = Window.WindowOpenedEvent.AddClassHandler<NewRequestWindow>((view, _) => window = view);
            controller.Open();
            var composer = ((NewRequestViewModel)window!.DataContext!).Composer;
            composer.Body = "private memory";
            await composer.Create();
            composer.Review();
            composer.CanSubmit.Should().BeTrue();
            var target = composer.Target!;
            await fixture.Sessions.RenameAsync(target.Authority.SessionId, target.Authority.Generation,
                target.Metadata!.Revision.Value, new("Renamed explicitly"), RequestOrigin.LocalUi, fixture.Token);
            composer.CanSubmit.Should().BeFalse();
            composer.ReviewedBody.Should().BeEmpty();
            composer.Body.Should().Be("private memory");
            (await fixture.Interactions.ReadConversationDraftAsync(target.Authority.SessionId, fixture.Token)).Draft.Body.Should().BeNull();
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativePrivateCloseHidesImmediatelyButAwaitsCaptureReleaseAndRejectsLateTranscription(bool unavailable)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var fixture = new InteractionStorageFixture();
            await fixture.InitializeAsync();
            var access = new WindowsSqliteSessionWorkspaceTests.Access();
            var workspace = WindowsSqliteSessionWorkspaceTests.Service(fixture, access);
            var conversation = new SessionConversationService(fixture.Store, fixture.Store, workspace, access,
                new NoAdmission(), TimeProvider.System, NullLogger<SessionConversationService>.Instance);
            using var composer = new NewRequestComposer(workspace, conversation, access, NullLogger<NewRequestComposer>.Instance);
            using var privacy = new ActivatedVoiceRecognitionTests.FakePrivacy();
            var factory = new ActivatedVoiceRecognitionTests.FakeFactory();
            var native = new ActivatedVoiceRecognitionTests.FakeCapture();
            factory.OpenResult.SetResult(native);
            await using var capture = new WindowsVoiceRecognitionService(NullLogger<WindowsVoiceRecognitionService>.Instance,
                privacy, factory, VoiceCaptureLimits.Default);
            await using var dictation = new ComposerDictationService(unavailable ? capture : new QualifiedDictationCapture(capture),
                new AvaloniaUiDispatcher(), () => new(new("mic", "Synthetic"), 1, 1, 1, 1, 1),
                NullLogger<ComposerDictationService>.Instance);
            using var model = new NewRequestViewModel(composer, dictation);
            var failures = new List<Exception>();
            var window = new NewRequestWindow(model, () => Task.CompletedTask, failures.Add);
            window.OpenPrivate();
            composer.Body = "private";
            await window.BeginLiteralDictation();
            factory.OpenCount.Should().Be(unavailable ? 0 : 1);
            var late = native.QueueTranscript("late lock workstation");
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            if (!unavailable) { native.ReleaseResult = release.Task; }
            var closed = false;
            window.Closed += (_, _) => closed = true;
            window.ClosePrivate();
            window.IsVisible.Should().BeFalse();
            composer.Body.Should().BeEmpty();
            if (!unavailable)
            {
                window.Operation.IsCompleted.Should().BeFalse();
                closed.Should().BeFalse();
                release.SetResult();
            }
            await window.Operation;
            closed.Should().BeTrue();
            late();
            composer.Body.Should().BeEmpty();
            capture.IsCaptureQuiescent.Should().BeTrue();
            failures.Should().BeEmpty();
        });
    }

    internal sealed class QualifiedDictationCapture(IActivatedVoiceRecognitionService inner) : IActivatedVoiceRecognitionService, IProvisionalVoiceRecognitionService
    {
        public IDisposable? ObserveProvisionalTranscripts(long generation, Action<ProvisionalTranscriptSnapshot> observer) =>
            (inner as IProvisionalVoiceRecognitionService)?.ObserveProvisionalTranscripts(generation, observer);
        public event EventHandler<VoiceTranscriptEventArgs>? TranscriptRecognized
        { add => inner.TranscriptRecognized += value; remove => inner.TranscriptRecognized -= value; }
        public event EventHandler<VoiceRecognitionFailureEventArgs>? RecognitionFailed
        { add => inner.RecognitionFailed += value; remove => inner.RecognitionFailed -= value; }
        public event EventHandler<VoiceCaptureStateChangedEventArgs>? CaptureStateChanged
        { add => inner.CaptureStateChanged += value; remove => inner.CaptureStateChanged -= value; }
        public event EventHandler<VoiceRecognitionCompletedEventArgs>? RecognitionCompleted
        { add => inner.RecognitionCompleted += value; remove => inner.RecognitionCompleted -= value; }
        public bool IsLiteralDictationAvailable => true;
        public bool IsAmbientListeningAvailable => inner.IsAmbientListeningAvailable;
        public bool IsCaptureQuiescent => inner.IsCaptureQuiescent;
        public bool IsListening => inner.IsListening;
        public long Generation => inner.Generation;
        public long CaptureGeneration => inner.CaptureGeneration;
        public bool AcceptCaptureGeneration(long generation) => inner.AcceptCaptureGeneration(generation);
        public void InvalidateCapture() => inner.InvalidateCapture();
        public Task BeginPushToTalkAsync(MicrophoneDevice microphone, IEnumerable<string> phrases,
            string? assistantName = null, CancellationToken cancellationToken = default) =>
            inner.BeginPushToTalkAsync(microphone, phrases, assistantName, cancellationToken);
        public Task EndPushToTalkAsync(CancellationToken cancellationToken = default) => inner.EndPushToTalkAsync(cancellationToken);
        public Task StopAsync(CancellationToken cancellationToken = default) => inner.StopAsync(cancellationToken);
        public IReadOnlyList<MicrophoneDevice> GetMicrophones() => throw new InvalidOperationException("No device discovery.");
        public MicrophoneDevice? GetDefaultMicrophone() => throw new InvalidOperationException("No device selection.");
        public Task StartAsync(MicrophoneDevice microphone, IEnumerable<string> phrases, string? assistantName = null,
            CancellationToken cancellationToken = default) => throw new InvalidOperationException("No general input.");
        public Task EndCaptureAsync(CancellationToken cancellationToken = default) => throw new InvalidOperationException("No general input.");
        public ValueTask DisposeAsync() => throw new InvalidOperationException("The fixture owns native capture.");
    }
}
