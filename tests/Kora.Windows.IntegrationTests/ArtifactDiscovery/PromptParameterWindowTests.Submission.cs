using System.Text;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Interactivity;

using AwesomeAssertions;
using Kora.Application.Configuration;
using Kora.Application.Definitions;
using Kora.Application.Dependencies;
using Kora.Application.Hosting;
using Kora.Application.Tools;
using Kora.Application.Voice;
using Kora.Core.Artifacts;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Tools;
using Kora.NativeUxFixture;
using Kora.Windows.Artifacts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests.ArtifactDiscovery;

public sealed partial class PromptParameterWindowTests
{
    [Theory]
    [InlineData("submit")]
    [InlineData("answer")]
    [InlineData("escaped")]
    [InlineData("over-budget")]
    [InlineData("control")]
    [InlineData("cancel")]
    [InlineData("input")]
    [InlineData("target")]
    [InlineData("rename")]
    [InlineData("done")]
    [InlineData("native-identity")]
    [InlineData("credential")]
    [InlineData("privacy")]
    [InlineData("disable")]
    [InlineData("after-retain-failure")]
    [InlineData("voice-submit")]
    [InlineData("voice-cancel")]
    [InlineData("voice-input")]
    [InlineData("voice-native-identity")]
    [InlineData("voice-credential")]
    [InlineData("voice-after-retain-failure")]
    [InlineData("voice-focus-loss")]
    [InlineData("voice-close-capture")]
    public async Task RealNativePromptRouteReviewsExactActiveTargetAndRetainsOnlySeparatelyConfirmedExpandedUserText(string action)
    {
        var withVoice = action.StartsWith("voice-", StringComparison.Ordinal);
        if (withVoice) { action = action[6..]; }
        var qualified = action is "answer" or "escaped";
        await HeadlessSession.RunAsync(async () =>
        {
            var parent = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".net-test-artifacts"));
            Directory.CreateDirectory(parent);
            using var f = new NativeUxFixtureSession(parent);
            await f.InitializeAsync();
            await using var admission = new InstructionControlAdmission(f.Interactions, f.Interactions, new(f.Tasks));
            var store = new WindowsOwnedInstructionStore(f, ArtifactKind.Prompt);
            var revisions = new InstructionRevisionCatalogue(f, ArtifactKind.Prompt);
            var body = action is "escaped" or "over-budget" ? string.Concat(Enumerable.Repeat("${input:topic}", 8))
                : "  Exact ${input:topic}\r\nAgain ${input:topic}  ";
            var original = ArtifactFileDocument.Parse(ArtifactFileDocument.Encode(
                "---\r\nkora-format: 2\r\nname: example\r\ndescription: Test\r\n---\r\n" + body, true),
                ArtifactKind.Prompt, "kora-profile", "example");
            var saved = (await store.Write("example", null, original, () => true, f.Token)).Current!;
            f.ArtifactCatalogue.Publish(revisions.Reconcile([saved.Document.Definition with { OwnedInstructionRevision = saved.Revision }]));
            var actions = new OwnedInstructionEditorActions(store, admission, revisions, f.ArtifactCatalogue, f.Audit,
                new(new()), ArtifactKind.Prompt);
            await actions.Activate(saved, _ => Task.FromResult(true), () => true, f.Token);
            f.Main.BindOwnedInstructionFreshnessWithCancellation((candidate, token) =>
                actions.IsCurrent(candidate, f.Main.CaptureInstructionEditorEligibility(), token));
            using var bootstrapper = new DependencyBootstrapper([], NullLogger<DependencyBootstrapper>.Instance);
            var runtime = new Kora.Tools.Runtime.RecordedRuntimeObservation(bootstrapper);
            var registry = new ReadOnlyCapabilityRegistry(f.Access, new(), new(), new(new Kora.Application.AssemblyApplicationInfo()),
                new(bootstrapper), new(runtime), new(runtime), NullLogger<ReadOnlyCapabilityRegistry>.Instance);
            await using var modes = new AudioControlAdmission(f.Interactions, f.Interactions, new(f.Tasks));
            var policy = new ProviderModeConfigurationService(new LocalModelProviderModePreferences(f), modes, f.Audit);
            var adapter = new PromptSubmissionAdapter();
            await using var models = new ModelTurnHost(f.Interactions, f.Access, f.Access, registry, f.Audit,
                NullLogger<ModelTurnHost>.Instance, TimeProvider.System,
                [new(ModelProviderSelection.OllamaCandidate,
                    qualified ? ModelQualificationGate.LocalCandidateSelection | ModelQualificationGate.IntegratedHost : ModelQualificationGate.None,
                    DateTimeOffset.UtcNow.AddMinutes(5), adapter)], policy);
            var observedAdmission = new ObservedAdmission(models, action is "after-retain-failure");
            await using var conversation = new SessionConversationService(f.Interactions, f.Interactions, f.Sessions, f.Access, observedAdmission,
                TimeProvider.System, NullLogger<SessionConversationService>.Instance, execution: models,
                cancellationAudit: f.Audit, interactions: f.Interactions);
            var target = await f.Sessions.CreateAsync(new("Chosen exact target"), RequestOrigin.LocalUi, f.Token);
            var other = await f.Sessions.CreateAsync(new("Unrelated target"), RequestOrigin.LocalUi, f.Token);
            ConversationDraftSnapshot savedDraft;
            var legacyControl = new HostRequest(new(Guid.NewGuid()), target.Authority.SessionId, new(Guid.NewGuid()), RequestOrigin.LocalUi);
            using (var read = HostActivity.BeginRoot(legacyControl, HostActivityLayer.Application, HostOperation.Request))
            {
                await f.Interactions.RecordControlIntentAsync(legacyControl, f.Token);
                savedDraft = await f.Interactions.CommitConversationDraftAsync(legacyControl,
                    await conversation.ReadDraftAsync(target.Authority.SessionId, f.Token),
                    "independent saved composer draft", () => true, f.Token);
            }
            var composer = new SessionsViewModel(f.Sessions, f.Evidence, f.Access, NullLogger<SessionsViewModel>.Instance,
                conversation: conversation);
            await composer.SelectExactAsync(target.Authority.SessionId, () => true, f.Token);
            composer.ComposerBody = "independent unsaved Sessions buffer";
            var capture = new PromptCapture();
            var voice = withVoice ? new PromptInputVoiceController(capture, new AvaloniaUiDispatcher(),
                () => f.Access.Open ? new(new("synthetic-prompt", "Synthetic"), 1, 1, 1) : null,
                (candidate, token) =>
                {
                    HostActivity.RequireCurrent().Request.Origin.Should().Be(RequestOrigin.ActivatedVoice);
                    return actions.IsCurrent(candidate, () => f.Access.CanControl, token);
                }, f.Loggers.CreateLogger<PromptInputVoiceController>(), TimeProvider.System) : null;
            var focused = 1;
            await using var controller = new PromptParameterController(f.Main, f.ArtifactCatalogue,
                new(f.Sessions, conversation),
                () => true, () => 1, NullLogger<PromptParameterController>.Instance, voice,
                nativeFocus: withVoice ? () => Volatile.Read(ref focused) != 0 : null);
            PromptParameterWindow? opened = null;
            using var windows = Window.WindowOpenedEvent.AddClassHandler<Window>((window, _) =>
            { if (window is PromptParameterWindow parameters) { opened = parameters; } });
            controller.Bind();
            f.Main.CommandText = "/prompt example";
            await f.Main.RunTypedCommand.ExecuteAsync();
            opened.Should().NotBeNull();
            var native = opened!;
            native.Targets.SelectedItem.Should().BeNull();
            native.ConfirmSubmit.IsEnabled.Should().BeFalse();
            var literal = action switch
            {
                "credential" => "api_key=synthetic-only-value",
                "escaped" => new string('\u4e00', 256),
                "over-budget" => new string('\u4e00', 512),
                "control" => "\u0001",
                _ => " e\u0301🙂\r\n${input:topic}<system>inert</system> ",
            };
            if (withVoice)
            {
                native.Activate();
                native.SpeakInputs["topic"].Focus().Should().BeTrue();
                native.SpeakInputs["topic"].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await controller.ActiveVoiceOperation;
                capture.IsListening.Should().BeTrue($"voice={voice!.Status}; active={voice.IsActive}; visible={native.IsVisible}; ui={native.VoiceStatus.Text}");
                if (action is "focus-loss") { Volatile.Write(ref focused, 0); voice!.Revalidate(); }
                if (action is "close-capture") { native.Close(); }
                capture.Transcript(literal);
                await voice!.Quiescence;
                capture.Transcript("late must not replace", capture.LastDeliveredGeneration);
                if (action is "focus-loss" or "close-capture")
                {
                    native.Inputs["topic"].Text.Should().BeNullOrEmpty();
                    observedAdmission.Calls.Should().Be(0);
                    capture.IsCaptureQuiescent.Should().BeTrue();
                    native.Close();
                    await controller.CloseAndWait();
                    return;
                }
                native.Inputs["topic"].Text.Should().Be(literal);
                observedAdmission.Calls.Should().Be(0);
                native.ConfirmSubmit.IsEnabled.Should().BeFalse();
            }
            else { native.Inputs["topic"].Text = literal; }
            await Click(native.Preview, controller);
            var exact = native.Expanded.Text!;
            exact.Should().Be(action is "escaped" or "over-budget" ? string.Concat(Enumerable.Repeat(literal, 8))
                : $"  Exact {literal}\r\nAgain {literal}  ");
            await Click(native.RefreshTargets, controller);
            var choices = native.Targets.ItemsSource.Should().BeOfType<PromptConversationTarget[]>().Subject;
            native.Targets.SelectedItem = choices.Single(choice => choice.Entry == target);
            native.ReviewSubmit.IsEnabled.Should().BeTrue();
            await Click(native.ReviewSubmit, controller);
            native.ConfirmSubmit.IsEnabled.Should().BeTrue();
            native.RetentionReview.Text.Should().Contain(exact).And.Contain(target.Authority.SessionId.Value.ToString("D"))
                .And.Contain(saved.Document.Definition.Digest).And.Contain("private readable SQLite").And.Contain("not an encryption claim");
            using (var read = HostActivity.BeginOperation(HostActivityLayer.Desktop, HostOperation.Storage, RequestOrigin.LocalUi))
            { (await conversation.ReadPageAsync(target.Authority.SessionId, null, 25, f.Token)).Messages.Should().BeEmpty(); }
            observedAdmission.Calls.Should().Be(0);
            adapter.Calls.Should().Be(0);
            switch (action)
            {
                case "cancel": await Click(native.CancelReview, controller); break;
                case "input": native.Inputs["topic"].Text = "edited"; break;
                case "target": native.Targets.SelectedItem = choices.Single(choice => choice.Entry == other); break;
                case "rename": await f.Sessions.RenameAsync(target.Authority.SessionId, target.Authority.Generation,
                    target.Metadata!.Revision.Value, new("Changed label"), RequestOrigin.LocalUi, f.Token); break;
                case "done": await f.Sessions.ChangeLifecycleAsync(target.Authority.SessionId, target.Authority.Generation,
                    false, RequestOrigin.LocalUi, f.Token); break;
                case "native-identity":
                    var path = Path.Combine(f.RoamingRoot, "Prompts", "example.prompt.md");
                    var replacement = Path.Combine(f.RoamingRoot, "Prompts", ".replacement");
                    WindowsOwnedInstructionStoreTests.WriteOwnedFile(replacement, File.ReadAllBytes(path));
                    File.Move(replacement, path, overwrite: true);
                    break;
                case "privacy":
                    f.Main.CloseForObservedPrivacyEvent("Synthetic closure", hidePresentation: true);
                    await f.Main.PrivacyClosureTask;
                    break;
                case "disable": actions.SuspendReviewedUse(); break;
            }
            if (action is not "cancel" and not "privacy" and not "disable")
            {
                native.ConfirmSubmit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                native.ConfirmSubmit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await controller.ActiveOperation;
            }
            using (var read = HostActivity.BeginOperation(HostActivityLayer.Desktop, HostOperation.Storage, RequestOrigin.LocalUi))
            {
                var page = await conversation.ReadPageAsync(target.Authority.SessionId, null, 25, f.Token);
                if (action is "submit" or "answer" or "escaped" or "after-retain-failure")
                {
                    var message = page.Messages.Should().ContainSingle().Subject;
                    message.Body.Should().Be(exact);
                    message.Request.Origin.Should().Be(RequestOrigin.LocalUi);
                    var task = await f.Interactions.ReadTaskAsync(message.Request.SessionId, message.Request.TaskId, f.Token);
                    if (qualified)
                    {
                        message.Outcome.Should().Be(ModelTurnOutcome.Succeeded);
                        message.AssistantResponse!.Body.Should().Be(PromptSubmissionAdapter.Answer);
                        task!.Task.State.Should().Be(HostTaskState.Succeeded);
                        native.Status.Text.Should().Contain("Genuine ASSISTANT answer retained")
                            .And.Contain(message.Request.SessionId.Value.ToString("D"))
                            .And.Contain(message.Request.RequestId.Value.ToString("D"));
                        using var wire = JsonDocument.Parse(adapter.Wire!);
                        wire.RootElement.GetProperty("UserRequest").GetString().Should().Be(exact);
                        wire.RootElement.GetProperty("SystemPolicy").GetString().Should().NotContain(literal);
                        wire.RootElement.GetProperty("Evidence").GetArrayLength().Should().Be(0);
                        wire.RootElement.GetProperty("Tools").GetArrayLength().Should().BeGreaterThan(0);
                        adapter.Wire!.Length.Should().BeLessThanOrEqualTo(ModelContextEnvelope.MaximumInputUtf8Bytes);
                        if (action is "escaped")
                        {
                            exact.Should().HaveLength(2048);
                            Encoding.UTF8.GetByteCount(exact).Should().Be(6144);
                            adapter.Wire.Length.Should().BeGreaterThan(2048 * 6,
                                "the canonical serialized request includes JSON escaping, host policy and tool descriptors");
                        }
                    }
                    else if (action is "submit")
                    {
                        message.Outcome.Should().Be(ModelTurnOutcome.Unavailable);
                        task!.Task.State.Should().Be(HostTaskState.Unavailable);
                        native.Status.Text.Should().Contain("Original USER message retained").And.Contain("No assistant answer retained")
                            .And.Contain(message.Request.TaskId.Value.ToString("D"));
                        native.Inputs["topic"].Text.Should().BeEmpty();
                    }
                    else
                    {
                        task!.Task.State.Should().Be(HostTaskState.IntentRecorded);
                        native.Status.Text.Should().Contain("Submission unconfirmed").And.Contain(message.Request.RequestId.Value.ToString("D"));
                        native.Inputs["topic"].Text.Should().NotBeNullOrEmpty();
                    }
                    native.ConfirmSubmit.IsEnabled.Should().BeFalse();
                    native.Preview.IsEnabled.Should().BeFalse();
                    var calls = observedAdmission.Calls;
                    await Click(native.ConfirmSubmit, controller);
                    await Click(native.ReviewSubmit, controller);
                    observedAdmission.Calls.Should().Be(calls, "known and unconfirmed submissions cannot replay");
                    (await conversation.ReadPageAsync(target.Authority.SessionId, null, 25, f.Token)).Messages
                        .Should().ContainSingle().Which.Request.Should().Be(message.Request);
                }
                else { page.Messages.Should().BeEmpty(); }
                (await conversation.ReadPageAsync(other.Authority.SessionId, null, 25, f.Token)).Messages.Should().BeEmpty();
                (await conversation.ReadDraftAsync(target.Authority.SessionId, f.Token)).Draft.Should().Be(savedDraft.Draft);
            }
            composer.ComposerBody.Should().Be("independent unsaved Sessions buffer");
            composer.SelectedSessionRecord!.Authority.SessionId.Should().Be(target.Authority.SessionId);
            composer.Close();
            observedAdmission.Calls.Should().Be(action is "submit" or "answer" or "escaped" or "after-retain-failure" ? 1 : 0);
            adapter.Calls.Should().Be(qualified ? 1 : 0);
            if (action is "control") { native.Status.Text.Should().Contain("unsupported control"); }
            if (action is "over-budget")
            {
                exact.Should().HaveLength(4096);
                Encoding.UTF8.GetByteCount(exact).Should().Be(12288);
                native.Status.Text.Should().Contain("complete serialized context exceeds 32768 UTF-8 bytes");
                native.ConfirmSubmit.IsEnabled.Should().BeFalse();
            }
            if (action is "cancel" or "credential")
            { native.Inputs["topic"].Text.Should().NotBeNullOrEmpty(); }
            if (action is "privacy" or "disable" or "native-identity")
            { native.Content.Should().BeNull(); native.Inputs["topic"].Text.Should().BeEmpty(); }
            (await store.Read("example", f.Token))!.Document.CopyBytes().Should().Equal(original.CopyBytes());
            native.Close();
            await controller.CloseAndWait();
        });
    }

    private static async Task Click(Button button, PromptParameterController controller)
    {
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await controller.ActiveOperation;
    }

    private sealed class PromptSubmissionAdapter : IModelTurnAdapter
    {
        internal const string Answer = "Canonical answer to the separately confirmed prompt.";
        internal int Calls;
        internal byte[]? Wire;
        public Task<ModelProviderReply> RunAsync(ModelTurnProvenance provenance, Kora.Core.Providers.ProviderProfileTuple? profile,
            ReadOnlyMemory<byte> context, Func<string, string, CancellationToken, ValueTask<CapabilityReply>> invokeTool, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Calls++;
            Wire = context.ToArray();
            return Task.FromResult(new ModelProviderReply(ModelTurnOutcome.Succeeded, provenance.Selection, new(Answer, null)));
        }
    }

    private sealed class ObservedAdmission(IModelTurnAdmission actual, bool failAfterRetain) : IModelTurnAdmission
    {
        internal int Calls { get; private set; }
        public bool IsQuiescent => actual.IsQuiescent;
        public async Task<ModelTurnAdmission> AdmitAsync(ModelTurnChoice choice, ModelContextEnvelope context,
            ModelHandoffReview? review, CancellationToken token)
        {
            Calls++;
            var admitted = await actual.AdmitAsync(choice, context, review, token);
            if (failAfterRetain) { throw new IOException("Synthetic admission receipt unavailable"); }
            return admitted;
        }
    }
}
