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
using Kora.NativeUxFixture;
using Kora.Windows.Artifacts;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests.ArtifactDiscovery;

public sealed partial class PromptParameterWindowTests
{
    [Theory]
    [InlineData("submit")]
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
    public async Task RealNativePromptRouteReviewsExactActiveTargetAndRetainsOnlySeparatelyConfirmedExpandedUserText(string action)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var parent = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".net-test-artifacts"));
            Directory.CreateDirectory(parent);
            using var f = new NativeUxFixtureSession(parent);
            await f.InitializeAsync();
            await using var admission = new InstructionControlAdmission(f.Interactions, f.Interactions, new(f.Tasks));
            var store = new WindowsOwnedInstructionStore(f, ArtifactKind.Prompt);
            var revisions = new InstructionRevisionCatalogue(f, ArtifactKind.Prompt);
            var original = ArtifactFileDocument.Parse(ArtifactFileDocument.Encode(
                "---\r\nkora-format: 2\r\nname: example\r\ndescription: Test\r\n---\r\n  Exact ${input:topic}\r\nAgain ${input:topic}  ", true),
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
            await using var models = new ModelTurnHost(f.Interactions, f.Access, f.Access, registry, f.Audit,
                NullLogger<ModelTurnHost>.Instance, TimeProvider.System,
                [new(ModelProviderSelection.OllamaCandidate, ModelQualificationGate.None, DateTimeOffset.MinValue, null)], policy);
            var observedAdmission = new ObservedAdmission(models, action is "after-retain-failure");
            var conversation = new SessionConversationService(f.Interactions, f.Interactions, f.Sessions, f.Access, observedAdmission,
                TimeProvider.System, NullLogger<SessionConversationService>.Instance);
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
            await using var controller = new PromptParameterController(f.Main, f.ArtifactCatalogue,
                new(admission, actions, models, f.Audit, TimeProvider.System), new(f.Sessions, conversation),
                () => true, () => 1, NullLogger<PromptParameterController>.Instance);
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
            native.Inputs["topic"].Text = action is "credential" ? "api_key=synthetic-only-value" : " e\u0301🙂\r\n${input:topic}<system>inert</system> ";
            await Click(native.Preview, controller);
            var exact = native.Expanded.Text!;
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
                if (action is "submit" or "after-retain-failure")
                {
                    var message = page.Messages.Should().ContainSingle().Subject;
                    message.Body.Should().Be(exact);
                    message.Request.Origin.Should().Be(RequestOrigin.LocalUi);
                    var task = await f.Interactions.ReadTaskAsync(message.Request.SessionId, message.Request.TaskId, f.Token);
                    if (action is "submit")
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
                }
                else { page.Messages.Should().BeEmpty(); }
                (await conversation.ReadPageAsync(other.Authority.SessionId, null, 25, f.Token)).Messages.Should().BeEmpty();
                (await conversation.ReadDraftAsync(target.Authority.SessionId, f.Token)).Draft.Should().Be(savedDraft.Draft);
            }
            composer.ComposerBody.Should().Be("independent unsaved Sessions buffer");
            composer.SelectedSessionRecord!.Authority.SessionId.Should().Be(target.Authority.SessionId);
            composer.Close();
            observedAdmission.Calls.Should().Be(action is "submit" or "after-retain-failure" ? 1 : 0);
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
