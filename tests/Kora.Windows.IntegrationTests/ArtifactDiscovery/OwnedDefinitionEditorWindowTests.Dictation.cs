using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

using AwesomeAssertions;

using Kora.Application.Configuration;
using Kora.Application.Definitions;
using Kora.Application.Hosting;
using Kora.Application.Voice;
using Kora.Core.Artifacts;
using Kora.Core.Communication;
using Kora.Core.Voice;
using Kora.NativeUxFixture;
using Kora.Windows.Artifacts;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests.ArtifactDiscovery;

public sealed partial class OwnedDefinitionEditorWindowTests
{
    [Theory]
    [InlineData("insert")]
    [InlineData("replace")]
    [InlineData("save")]
    [InlineData("cancel")]
    [InlineData("empty")]
    [InlineData("caret")]
    [InlineData("range")]
    [InlineData("draft")]
    [InlineData("metadata-caret")]
    [InlineData("name")]
    [InlineData("kind")]
    [InlineData("source")]
    [InlineData("catalogue")]
    [InlineData("owner")]
    [InlineData("channel")]
    [InlineData("assistant-name")]
    [InlineData("call")]
    [InlineData("privacy")]
    [InlineData("device")]
    [InlineData("foreground")]
    [InlineData("bounds")]
    [InlineData("metadata")]
    [InlineData("opening")]
    [InlineData("release")]
    [InlineData("review-device")]
    [InlineData("review-caret")]
    [InlineData("reentrant")]
    [InlineData("new-save")]
    [InlineData("prompt")]
    [InlineData("readonly")]
    [InlineData("disabled")]
    [InlineData("transcript-bound")]
    [InlineData("source-bound")]
    [InlineData("source-overflow")]
    [InlineData("close-release")]
    public async Task NativeOwnedBodyDictationUsesFrozenRangeAndSeparateNormalSaveWithoutCommandOrOtherFieldEffects(string boundary)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var parent = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".net-test-artifacts"));
            Directory.CreateDirectory(parent);
            using var f = new NativeUxFixtureSession(parent, syntheticVoice: true);
            var calls = new DictationCallState();
            await f.InitializeAsync(null, callState: calls);
            f.Main.BindVoiceOwnershipGate(static () => true);
            await f.Main.SetVoiceConsentAsync(true);
            var service = f.Main.BindComposerDictation(NullLogger<ComposerDictationService>.Instance);
            await using var admission = new InstructionControlAdmission(f.Interactions, f.Interactions, new(f.Tasks));
            var store = new WindowsOwnedInstructionStore(f);
            var revisions = new InstructionRevisionCatalogue(f);
            using var catalogue = Kora.Definitions.Artifacts.EmbeddedArtifactCatalogue.Load();
            var actions = new OwnedInstructionEditorActions(store, admission, revisions, catalogue, f.Audit, new(new()));
            var directories = new LocalArtifactDirectoryPreferences(f);
            using var reload = new ArtifactCatalogueReloadService(catalogue, f, directories,
                new ArtifactDirectoryConfigurationService(directories, f.Audit),
                new McpServerConfigurationService(f, directories, Path.Combine(f.LocalRoot, "no-profile")),
                NullLogger<ArtifactCatalogueReloadService>.Instance, revisions, store, Path.Combine(f.LocalRoot, "no-profile"));
            const string initial = "---\r\nkora-format: 1\r\nname: example\r\ndescription: Frozen metadata\r\n---\r\nBefore SELECT After e\u0301\U0001f642\r\n";
            if (boundary is not "new-save")
            {
                var original = (await store.Write("example", null,
                    ArtifactFileDocument.Parse(ArtifactFileDocument.Encode(initial, true), ArtifactKind.Instruction, "kora-profile", "example"),
                    () => true, f.Token)).Current!;
                reload.ReconcileSavedCopy(original).Should().BeTrue();
            }
            var ownership = 1L;
            var promptActions = new OwnedInstructionEditorActions(new WindowsOwnedInstructionStore(f, ArtifactKind.Prompt),
                admission, new(f, ArtifactKind.Prompt), catalogue, f.Audit, new(new()), ArtifactKind.Prompt);
            using var controller = new OwnedInstructionEditorController(f.Main, actions, new(actions), reload, f,
                () => true, () => ownership, NullLogger<OwnedInstructionEditorController>.Instance,
                promptActions, new(promptActions), dictation: service);
            OwnedInstructionEditorWindow? editor = null;
            var nextReview = new TaskCompletionSource<Window>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var opened = Window.WindowOpenedEvent.AddClassHandler<Window>((view, _) =>
            {
                if (view is OwnedInstructionEditorWindow selected) { editor = selected; }
                else { nextReview.TrySetResult(view); }
            });
            controller.Bind();
            async Task Run(string text) { f.Main.CommandText = text; await f.Main.RunTypedCommand.ExecuteAsync(); }
            await Run(boundary is "new-save" ? "instructions create example"
                : boundary is "prompt" ? "prompts create example" : "instructions inspect example");
            var window = editor!;
            const string phrase = "Kora, stop listening; approve; save; enable";
            var frozen = boundary is "source-bound" or "source-overflow"
                ? initial + new string('x', 65536 - ArtifactFileDocument.Encode(initial, true).Length - phrase.Length
                    + (boundary is "source-overflow" ? 1 : 0)) : initial;
            if (boundary is "new-save" or "prompt" or "source-bound" or "source-overflow") { window.Source.Text = frozen; }
            window.Activate();
            Dispatcher.UIThread.RunJobs();
            var start = initial.IndexOf("SELECT", StringComparison.Ordinal);
            window.Source.CaretIndex = boundary is "metadata" ? 0 : start;
            window.Source.SelectionStart = boundary is "metadata" ? 0 : start;
            window.Source.SelectionEnd = boundary is "metadata" ? 0 : boundary is "replace" ? start + 6 : start;
            var hold = window.GetVisualDescendants().OfType<Button>().Single(button => button.Name is "DictateInstructionBody");
            ControlAutomationPeer.CreatePeerForElement(hold)!.GetName().Should().Contain("exact owned Instruction body");
            hold.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            f.Voice.StartCalls.Should().Be(0, "Invoke is not a hold");
            hold.Focus().Should().BeTrue();
            if (boundary is "opening") { f.Voice.PendingLiteralOpen = new(TaskCreationOptions.RunContinuationsAsynchronously); }
            window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
            if (boundary is "metadata" or "prompt")
            {
                var attempt = () => window.BodyDictation!.Operation;
                await attempt.Should().ThrowAsync<InvalidOperationException>();
                f.Voice.StartCalls.Should().Be(0);
                window.Source.Text.Should().Be(initial);
                return;
            }
            if (boundary is "opening")
            {
                var beginning = window.BodyDictation!.Operation;
                await f.Voice.Started.Task.WaitAsync(f.Token);
                window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
                var ending = window.BodyDictation.Operation;
                ending.IsCompleted.Should().BeFalse("native open ignores cancellation until the seam completes");
                service.IsActive.Should().BeFalse();
                f.Voice.PendingLiteralOpen!.SetResult();
                await beginning;
                await ending;
            }
            await window.BodyDictation!.Operation;
            service.IsActive.Should().Be(boundary is not "opening",
                string.Join("\n", window.GetVisualDescendants().OfType<TextBlock>().Select(block => block.Text)));
            f.Voice.StartCalls.Should().Be(1);
            var literal = boundary is "bounds" ? new string('x', VoiceInputLimits.MaximumTranscriptCharacters + 1)
                : boundary is "transcript-bound" ? new string('x', VoiceInputLimits.MaximumTranscriptCharacters) : phrase;
            var late = f.Voice.QueueLiteralTranscript(literal);
            switch (boundary)
            {
                case "caret": window.Source.CaretIndex++; break;
                case "range": window.Source.SelectionEnd++; break;
                case "draft": window.Source.Text += "INDEPENDENT"; break;
                case "readonly": window.Source.IsReadOnly = true; break;
                case "disabled": window.Source.IsEnabled = false; break;
                case "name": window.NameInput.Text = "another"; break;
                case "kind": window.KindInput.SelectedItem = ArtifactKind.Prompt; break;
                case "source":
                    await File.WriteAllBytesAsync(Path.Combine(f.RoamingRoot, "Instructions", "example.instructions.md"),
                        ArtifactFileDocument.Encode(initial + "EXTERNAL", true), f.Token); break;
                case "catalogue": catalogue.Publish(catalogue.Candidates.Where(candidate => candidate.OwnedInstructionName is null).ToArray()); break;
                case "owner": ownership++; break;
                case "channel": await f.Main.WithdrawVoiceConsentCommand.ExecuteAsync(); break;
                case "assistant-name": await f.Main.SetAssistantNameAsync("Nova"); break;
                case "call": calls.Set(CallState.Active); break;
                case "privacy": f.Main.CloseForObservedPrivacyEvent("Synthetic body dictation closure", hidePresentation: true); break;
                case "device": f.Voice.InvalidateCapture(); break;
                case "foreground": window.Hide(); break;
            }
            if (boundary is not "empty") { late(); }
            if (boundary is "release" or "close-release") { f.Voice.PendingLiteralStop = new(TaskCreationOptions.RunContinuationsAsynchronously); }
            if (boundary is not "privacy") { window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " "); }
            if (boundary is "release" or "close-release")
            {
                var releasing = window.BodyDictation.Operation;
                releasing.IsCompleted.Should().BeFalse();
                window.GetVisualDescendants().OfType<Button>().Single(button => button.Name is "CancelInstructionDictation")
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                service.Quiescence.IsCompleted.Should().BeFalse();
                window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
                f.Voice.StartCalls.Should().Be(1, "cleanup retains exclusive capture ownership");
                var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                if (boundary is "close-release")
                {
                    window.Closed += (_, _) => closed.TrySetResult();
                    window.Close();
                    window.IsVisible.Should().BeTrue("clean closing must join cancelled native release");
                    closed.Task.IsCompleted.Should().BeFalse();
                }
                f.Voice.PendingLiteralStop!.SetResult();
                await releasing;
                if (boundary is "close-release") { await closed.Task.WaitAsync(f.Token); }
            }
            if (boundary is "bounds" or "source-overflow")
            {
                var finished = () => window.BodyDictation.Operation;
                await finished.Should().ThrowAsync<InvalidDataException>();
            }
            else { await window.BodyDictation.Operation; }
            Dispatcher.UIThread.RunJobs();
            if (boundary is "review-device") { f.Voice.InvalidateCapture(); }
            if (boundary is "review-caret") { window.Source.CaretIndex++; }
            if (boundary is "reentrant")
            {
                window.Source.PropertyChanged += (_, change) =>
                {
                    if (change.Property == TextBox.TextProperty && window.Source.Text?.Contains(literal, StringComparison.Ordinal) == true)
                    { window.Source.Text = initial + "INDEPENDENT"; }
                };
            }
            var accepted = boundary is "insert" or "replace" or "save" or "new-save" or "transcript-bound" or "source-bound";
            var accept = window.GetVisualDescendants().OfType<Button>().SingleOrDefault(button => button.Name is "AcceptInstructionDictation");
            if (accepted || boundary is "cancel" or "metadata-caret" or "reentrant")
            {
                service.HasReview.Should().BeTrue();
                f.Voice.IsCaptureQuiescent.Should().BeTrue();
                window.Source.Text.Should().Be(frozen);
                if (boundary is "metadata-caret") { window.Source.CaretIndex = 0; }
                if (accepted || boundary is "reentrant") { accept!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }
                else
                {
                    window.GetVisualDescendants().OfType<Button>().Single(button => button.Name is "CancelInstructionDictation")
                        .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }
                await window.BodyDictation.Operation;
            }
            await window.BodyDictation.CancelAndRelease();
            late();
            var expected = boundary is "replace" ? frozen[..start] + literal + frozen[(start + 6)..]
                : accepted ? frozen[..start] + literal + frozen[start..]
                : boundary is "draft" or "reentrant" ? frozen + "INDEPENDENT"
                : boundary is "privacy" or "kind" or "close-release" ? "" : frozen;
            window.Source.Text.Should().Be(expected);
            if (boundary is "source-bound") { ArtifactFileDocument.Encode(expected, true).Should().HaveCount(65536); }
            if (boundary is "transcript-bound") { literal.Length.Should().Be(4096); }
            window.Critique.Text.Should().BeNullOrEmpty();
            window.CopyNameInput.Text.Should().BeNullOrEmpty();
            f.Main.IsVoiceEnabled.Should().Be(boundary is not ("channel" or "assistant-name" or "privacy"),
                "literal command words cannot change listening; only explicit boundary revocation may");
            var path = Path.Combine(f.RoamingRoot, "Instructions", "example.instructions.md");
            if (boundary is "new-save") { File.Exists(path).Should().BeFalse(); }
            else
            {
                var disk = await File.ReadAllBytesAsync(path, f.Token);
                disk.Should().Equal(ArtifactFileDocument.Encode(boundary is "source" ? initial + "EXTERNAL" : initial, true));
            }
            service.IsActive.Should().BeFalse();
            if (boundary is "save" or "new-save")
            {
                var saving = Run("instructions save");
                (await Task.WhenAny(saving, nextReview.Task)).Should().BeSameAs(nextReview.Task);
                var review = await nextReview.Task;
                review.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, "Confirm save"))
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await saving;
                (await File.ReadAllBytesAsync(path, f.Token)).Should().Equal(ArtifactFileDocument.Encode(expected, boundary is not "new-save"));
                catalogue.Candidates.Should().Contain(candidate => candidate.OwnedInstructionName == "example" && !candidate.IsUseEnabled);
            }
        });
    }

    private sealed class DictationCallState : ICallStateService
    {
        public event EventHandler<CallStateChangedEventArgs>? StateChanged;
        public CallState CurrentState { get; private set; } = CallState.Clear;
        internal void Set(CallState value) { CurrentState = value; StateChanged?.Invoke(this, new(value)); }
    }
}
