using System.Text.Json;

using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

using AwesomeAssertions;

using Kora.Application.Configuration;
using Kora.Application.Definitions;
using Kora.Application.Dependencies;
using Kora.Application.Hosting;
using Kora.Application.Tools;
using Kora.Application.Voice;
using Kora.Core.Artifacts;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Providers;
using Kora.Core.Tools;
using Kora.NativeUxFixture;
using Kora.Windows.Artifacts;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionsConversationTests
{
    [Theory]
    [InlineData("complete")]
    [InlineData("recover")]
    [InlineData("voice")]
    [InlineData("consent")]
    [InlineData("v1")]
    [InlineData("cancel-review")]
    [InlineData("cancel-confirm")]
    [InlineData("dirty")]
    [InlineData("input")]
    [InlineData("privacy")]
    [InlineData("occupied")]
    [InlineData("stale-use")]
    [InlineData("stale-target")]
    public async Task OriginalUserRenamesReopensAndIndependentlySubmitsExactPromptToCanonicalRetainedAnswer(string scenario)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var parent = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".net-test-artifacts"));
            Directory.CreateDirectory(parent);
            using var f = new NativeUxFixtureSession(parent, syntheticVoice: scenario is "voice" or "consent");
            await f.InitializeAsync();
            if (scenario is "voice" or "consent")
            {
                f.Main.BindVoiceOwnershipGate(() => true);
                await f.Main.SetVoiceConsentAsync(true);
            }
            await using var admission = new InstructionControlAdmission(f.Interactions, f.Interactions, new(f.Tasks));
            var instructions = new WindowsOwnedInstructionStore(f);
            var instructionRevisions = new InstructionRevisionCatalogue(f);
            var prompts = new WindowsOwnedInstructionStore(f, ArtifactKind.Prompt);
            var promptRevisions = new InstructionRevisionCatalogue(f, ArtifactKind.Prompt);
            var catalogue = f.ArtifactCatalogue;
            var instructionActions = new OwnedInstructionEditorActions(instructions, admission, instructionRevisions, catalogue, f.Audit, new(new()));
            var promptActions = new OwnedInstructionEditorActions(prompts, admission, promptRevisions, catalogue, f.Audit, new(new()), ArtifactKind.Prompt);
            var directories = new LocalArtifactDirectoryPreferences(f);
            var profile = Path.Combine(f.LocalRoot, "no-profile");
            using var reload = new ArtifactCatalogueReloadService(catalogue, f, directories,
                new ArtifactDirectoryConfigurationService(directories, f.Audit),
                new McpServerConfigurationService(f, directories, profile), NullLogger<ArtifactCatalogueReloadService>.Instance,
                instructionRevisions, instructions, profile, promptRevisions, prompts);
            using var editorController = new OwnedInstructionEditorController(f.Main, instructionActions, new(instructionActions), reload, f,
                () => true, () => 1, NullLogger<OwnedInstructionEditorController>.Instance, promptActions, new(promptActions));
            var body = scenario is "v1" ? "  Exact literal saved request.\r\n"
                : "  Exact ${input:second}\r\nThen ${input:first}; again ${input:second}.\r\n";
            var text = $"---\r\nkora-format: {(scenario is "v1" ? "1" : "2")}\r\nname :  'example'  \r\ndescription: Exact saved request\r\nattribution: Original author.\r\n---\r\n" + body;
            var document = ArtifactFileDocument.Parse(ArtifactFileDocument.Encode(text, true), ArtifactKind.Prompt, "kora-profile", "example");
            var source = (await prompts.Write("example", null, document, () => true, operation => Task.FromResult(operation()), f.Token)).Current!;
            reload.ReconcileSavedCopy(source).Should().BeTrue();
            await promptActions.Activate(source, _ => Task.FromResult(true), () => true, f.Token);
            OwnedInstructionEditorWindow? editor = null;
            PromptParameterWindow? parameterWindow = null;
            var nextReview = new TaskCompletionSource<Window>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var observed = Window.WindowOpenedEvent.AddClassHandler<Window>((view, _) =>
            {
                if (view is OwnedInstructionEditorWindow owned) { editor = owned; }
                else if (view is PromptParameterWindow parameters) { parameterWindow = parameters; }
                else { nextReview.TrySetResult(view); }
            });
            editorController.Bind();
            async Task Run(string command) { f.Main.CommandText = command; await f.Main.RunTypedCommand.ExecuteAsync(); }
            static void Choose(Window view, string caption) => view.GetVisualDescendants().OfType<Button>()
                .Single(button => Equals(button.Content, caption)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            async Task<Window> Review(Task operation)
            {
                (await Task.WhenAny(nextReview.Task, operation)).Should().BeSameAs(nextReview.Task,
                    "the operation must reach native review");
                var review = await nextReview.Task;
                nextReview = new(TaskCreationOptions.RunContinuationsAsynchronously);
                return review;
            }
            await Run("prompts inspect example");
            editor!.Source.Text.Should().Be(document.Text);
            if (scenario is "dirty") { editor.Source.Text += "Unsent edit"; }
            Task rename;
            if (scenario is "voice" or "consent")
            {
                await f.Main.BeginPushToTalkCommand.ExecuteAsync();
                await f.Main.EndPushToTalkCommand.ExecuteAsync();
                f.Voice.PublishTranscript("Kora, prompts rename renamed");
                await nextReview.Task.WaitAsync(TimeSpan.FromSeconds(10), f.Token);
                rename = editorController.WaitForOperation();
            }
            else { rename = Run("prompts rename renamed"); }
            if (scenario is "dirty")
            {
                await rename;
                nextReview.Task.IsCompleted.Should().BeFalse();
                editor.Source.Text.Should().EndWith("Unsent edit");
                (await prompts.Read("example", f.Token))!.Revision.Should().Be(source.Revision);
                editor.Source.Text = document.Text;
                return;
            }
            var comparison = await Review(rename);
            comparison.Title.Should().Be("Review owned Prompt rename");
            comparison.GetVisualDescendants().OfType<TextBox>().Select(box => box.Text)
                .Should().Equal(document.Text, new OwnedPromptRename(source, "renamed").Proposed.Text);
            (await prompts.Read("example", f.Token))!.Revision.Should().Be(source.Revision);
            Choose(comparison, scenario is "cancel-review" ? "Cancel" : "Reviewed exact rename");
            if (scenario is not "cancel-review")
            {
                var confirm = await Review(rename);
                confirm.Title.Should().Be("Separately confirm exact Prompt rename");
                (await prompts.Read("example", f.Token))!.Revision.Should().Be(source.Revision);
                if (scenario is "input") { editor.RenameNameInput.Text = "substituted"; }
                if (scenario is "consent") { await f.Main.SetVoiceConsentAsync(false); }
                if (scenario is "occupied") { File.WriteAllBytes(Path.Combine(f.RoamingRoot, "Prompts", "renamed.prompt.md"), [1, 0, 3]); }
                if (scenario is "recover")
                {
                    prompts.AfterRenamePhase = phase =>
                    { if (phase == OwnedInstructionRenamePhase.OriginalHeld) { throw new IOException("Injected interruption"); } };
                }
                if (scenario is "privacy")
                {
                    f.Main.CloseForObservedPrivacyEvent("Synthetic prompt rename revocation", hidePresentation: true);
                    await f.Main.PrivacyClosureTask;
                }
                else { Choose(confirm, scenario is "cancel-confirm" ? "Cancel" : "Confirm rename"); }
            }
            await rename;
            if (scenario is "cancel-review" or "cancel-confirm" or "input" or "privacy" or "occupied" or "consent")
            {
                (await prompts.Read("example", f.Token))!.Revision.Should().Be(source.Revision);
                (await prompts.ReadRenameRecovery(f.Token)).Should().BeNull();
                if (scenario is not "occupied") { (await prompts.Read("renamed", f.Token)).Should().BeNull(); }
                else { File.ReadAllBytes(Path.Combine(f.RoamingRoot, "Prompts", "renamed.prompt.md")).Should().Equal([1, 0, 3]); }
                return;
            }
            if (scenario is "recover")
            {
                (await prompts.ReadRenameRecovery(f.Token))!.Phase.Should().Be(OwnedInstructionRenamePhase.OriginalHeld);
                prompts.AfterRenamePhase = null;
                var recovering = Run("prompts recover-rename");
                Choose(await Review(recovering), "Reviewed exact rename");
                Choose(await Review(recovering), "Confirm rename");
                await recovering;
            }
            (await prompts.Read("example", f.Token)).Should().BeNull();
            var saved = (await prompts.Read("renamed", f.Token))!;
            saved.Document.CopyBytes().Should().Equal(new OwnedPromptRename(source, "renamed").Proposed.CopyBytes());
            saved.Document.Body.Should().Be(body);
            catalogue.Candidates.Should().NotContain(item => item.OwnedInstructionName == "example");
            catalogue.Candidates.Single(item => item.Kind == ArtifactKind.Prompt && item.OwnedInstructionName is "renamed").IsUseEnabled.Should().BeFalse();
            promptRevisions.IsEnabled(source).Should().BeFalse();
            promptRevisions.IsEnabled(saved).Should().BeFalse();
            await Run("prompts inspect renamed");
            editor.Source.Text.Should().Be(saved.Document.Text);
            editor.NameInput.Text.Should().Be("renamed");
            var use = Run("prompts activate");
            var useReview = await Review(use);
            useReview.GetVisualDescendants().OfType<TextBox>().Select(box => box.Text).Should().Equal(saved.Document.Text, saved.Document.Text);
            Choose(useReview, "Permit explicit use");
            await use;
            promptRevisions.IsEnabled(saved).Should().BeTrue();
            if (scenario is "v1") { return; }
            f.Main.BindOwnedInstructionFreshnessWithCancellation((candidate, token) => promptActions.IsCurrent(candidate, () => true, token));
            using var bootstrapper = new DependencyBootstrapper([], NullLogger<DependencyBootstrapper>.Instance);
            var runtime = new Kora.Tools.Runtime.RecordedRuntimeObservation(bootstrapper);
            var registry = new ReadOnlyCapabilityRegistry(f.Access, new(), new(), new(new Kora.Application.AssemblyApplicationInfo()),
                new(bootstrapper), new(runtime), new(runtime), NullLogger<ReadOnlyCapabilityRegistry>.Instance);
            await using var modes = new AudioControlAdmission(f.Interactions, f.Interactions, new(f.Tasks));
            var policy = new ProviderModeConfigurationService(new LocalModelProviderModePreferences(f), modes, f.Audit);
            var adapter = new RenamedPromptAnswerAdapter();
            await using var models = new ModelTurnHost(f.Interactions, f.Access, f.Access, registry, f.Audit,
                NullLogger<ModelTurnHost>.Instance, TimeProvider.System,
                [new(ModelProviderSelection.OllamaCandidate, ModelQualificationGate.LocalCandidateSelection | ModelQualificationGate.IntegratedHost,
                    DateTimeOffset.UtcNow.AddMinutes(5), adapter)], policy);
            await using var conversation = new SessionConversationService(f.Interactions, f.Interactions, f.Sessions, f.Access, models,
                TimeProvider.System, NullLogger<SessionConversationService>.Instance, execution: models,
                cancellationAudit: f.Audit, interactions: f.Interactions);
            var target = await f.Sessions.CreateAsync(new("Exact renamed prompt target"), RequestOrigin.LocalUi, f.Token);
            var unrelated = await f.Sessions.CreateAsync(new("Unrelated target"), RequestOrigin.LocalUi, f.Token);
            await using var parametersController = new PromptParameterController(f.Main, catalogue,
                new(admission, promptActions, models, f.Audit, TimeProvider.System), new(f.Sessions, conversation),
                () => true, () => 1, NullLogger<PromptParameterController>.Instance);
            parametersController.Bind();
            await Run("/prompt example");
            parameterWindow.Should().BeNull();
            adapter.Calls.Should().Be(0);
            await Run("/prompt renamed");
            var inputs = parameterWindow!;
            inputs.Inputs.Keys.Should().Equal("second", "first");
            inputs.Inputs["second"].Text = "${input:first}";
            inputs.Inputs["first"].Text = "Engineers";
            async Task Click(Button button)
            { button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await parametersController.ActiveOperation; }
            await Click(inputs.Preview);
            const string expanded = "  Exact ${input:first}\r\nThen Engineers; again ${input:first}.\r\n";
            inputs.Expanded.Text.Should().Be(expanded);
            await Click(inputs.RefreshTargets);
            inputs.Targets.SelectedItem = inputs.Targets.Items.Cast<PromptConversationTarget>().Single(item => item.Entry == target);
            await Click(inputs.ReviewSubmit);
            inputs.RetentionReview.Text.Should().Contain(expanded).And.Contain(saved.Document.Definition.Id).And.Contain(saved.Document.Definition.Digest);
            adapter.Calls.Should().Be(0);
            if (scenario is "stale-use") { inputs.Inputs["first"].Text = "Changed"; }
            if (scenario is "stale-target")
            { await f.Sessions.RenameAsync(target.Authority.SessionId, target.Authority.Generation, target.Metadata!.Revision.Value, new("Changed target"), RequestOrigin.LocalUi, f.Token); }
            await Click(inputs.ConfirmSubmit);
            using (var read = HostActivity.BeginOperation(HostActivityLayer.Desktop, HostOperation.Storage, RequestOrigin.LocalUi))
            {
                var page = await conversation.ReadPageAsync(target.Authority.SessionId, null, 25, f.Token);
                if (scenario is "stale-use" or "stale-target")
                {
                    page.Messages.Should().BeEmpty();
                    adapter.Calls.Should().Be(0);
                }
                else
                {
                    var message = page.Messages.Should().ContainSingle().Subject;
                    message.Body.Should().Be(expanded);
                    message.Outcome.Should().Be(ModelTurnOutcome.Succeeded);
                    message.AssistantResponse!.Body.Should().Be(RenamedPromptAnswerAdapter.Answer);
                    message.Request.SessionId.Should().Be(target.Authority.SessionId);
                    (await f.Interactions.ReadConversationMessageAsync(target.Authority.SessionId, message.Request.RequestId, f.Token)).Should().Be(message);
                    adapter.Calls.Should().Be(1);
                    adapter.UserRequest.Should().Be(expanded);
                    inputs.Status.Text.Should().Contain("Genuine ASSISTANT answer retained");
                }
                (await conversation.ReadPageAsync(unrelated.Authority.SessionId, null, 25, f.Token)).Messages.Should().BeEmpty();
            }
            File.ReadAllBytes(Path.Combine(f.RoamingRoot, "Prompts", "renamed.prompt.md")).Should().Equal(saved.Document.CopyBytes());
            inputs.Close();
            await parametersController.CloseAndWait();
        });
    }

    private sealed class RenamedPromptAnswerAdapter : IModelTurnAdapter
    {
        internal const string Answer = "Canonical answer to the separately reviewed renamed prompt.";
        internal int Calls;
        internal string? UserRequest;
        public Task<ModelProviderReply> RunAsync(ModelTurnProvenance provenance, ProviderProfileTuple? profile,
            ReadOnlyMemory<byte> context, Func<string, string, CancellationToken, ValueTask<CapabilityReply>> invokeTool, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            using var wire = JsonDocument.Parse(context);
            UserRequest = wire.RootElement.GetProperty("UserRequest").GetString();
            Calls++;
            return Task.FromResult(new ModelProviderReply(ModelTurnOutcome.Succeeded, provenance.Selection, new(Answer, null)));
        }
    }
}
