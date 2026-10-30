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
using Kora.Core.Customizations;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Tools;
using Kora.NativeUxFixture;
using Kora.Windows.Artifacts;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests.ArtifactDiscovery;

public sealed partial class CustomizationWindowTests
{
    [Fact]
    public Task PromptAndMcpControlsRetainSeparateAuthoringAuthorityAndClearAllPrivateFields() =>
        HeadlessSession.RunAsync(async () =>
        {
            var parent = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".net-test-artifacts"));
            Directory.CreateDirectory(parent);
            using var f = new NativeUxFixtureSession(parent);
            await f.InitializeAsync();
            var window = new CustomizationWindow(_ => { }, f.Main);
            try
            {
                window.Show();
                var source = new CustomizationSource("file.example", CustomizationKind.Prompt, CustomizationScope.Kora,
                    "example", "owned", "example.prompt.md");
                var refine = window.GetVisualDescendants().OfType<Button>()
                    .Single(button => Equals(button.Content, "Ask Kora (review local disclosure)"));
                var cancel = window.GetVisualDescendants().OfType<Button>()
                    .Single(button => Equals(button.Content, "Cancel prompt authoring"));
                window.SetSource(source, false);
                refine.IsEnabled.Should().BeTrue();
                window.RequiredPromptInputs.IsVisible.Should().BeTrue();
                window.RequiredPromptInputs.IsEnabled.Should().BeTrue();
                cancel.IsVisible.Should().BeTrue();
                window.RequiredPromptInputs.Text = "topic,audience";
                window.Editor.Text = "Private prompt source";
                window.Request.Text = "Private original request";

                window.SetSource(source with { Scope = CustomizationScope.User }, false);
                refine.IsEnabled.Should().BeFalse();
                window.RequiredPromptInputs.IsEnabled.Should().BeFalse();
                window.SetSource(source, true);
                refine.IsEnabled.Should().BeFalse();
                window.RequiredPromptInputs.IsEnabled.Should().BeFalse();

                window.SetSource(source with { Kind = CustomizationKind.McpServer }, false);
                refine.IsEnabled.Should().BeFalse();
                window.Request.IsEnabled.Should().BeFalse();
                window.RequiredPromptInputs.IsVisible.Should().BeFalse();
                cancel.IsVisible.Should().BeFalse();
                window.Editor.IsVisible.Should().BeFalse();
                window.McpUrl.Text = "https://private.invalid/mcp";
                window.McpHttpType.IsChecked = true;
                window.ClearPrivateText();
                window.Editor.Text.Should().BeEmpty();
                window.Request.Text.Should().BeEmpty();
                window.RequiredPromptInputs.Text.Should().BeEmpty();
                window.McpUrl.Text.Should().BeEmpty();
                window.McpHttpType.IsChecked.Should().BeFalse();
            }
            finally { window.Close(); }
        });

    [Theory]
    [InlineData("save")]
    [InlineData("voice")]
    [InlineData("existing")]
    [InlineData("source-save")]
    [InlineData("cancel-disclosure")]
    [InlineData("cancel-proposal")]
    [InlineData("cancel-save")]
    [InlineData("invalid-placeholder")]
    [InlineData("oversize")]
    [InlineData("unsupported")]
    [InlineData("occupied-before")]
    [InlineData("occupied-save")]
    [InlineData("source-review")]
    [InlineData("late-cancel")]
    [InlineData("late-close")]
    [InlineData("late-private")]
    [InlineData("late-owner")]
    [InlineData("late-fields")]
    [InlineData("late-request")]
    [InlineData("late-draft")]
    [InlineData("late-input")]
    [InlineData("late-catalogue")]
    [InlineData("late-source")]
    public async Task PromptAuthoringCompletesDescriptionProposalLiteralValidationDisabledSaveAndReviewedSubmission(string action)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var parent = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".net-test-artifacts"));
            Directory.CreateDirectory(parent);
            using var f = new NativeUxFixtureSession(parent, syntheticVoice: action is "voice");
            await f.InitializeAsync();
            if (action is "voice")
            {
                f.Main.BindVoiceOwnershipGate(static () => true);
                await f.Main.SetVoiceConsentAsync(true);
            }
            await using var admission = new InstructionControlAdmission(f.Interactions, f.Interactions, new(f.Tasks));
            var profile = Path.Combine(f.LocalRoot, "prompt-authoring-profile");
            var store = new WindowsCustomizationStore(f, profile, Path.Combine(f.LocalRoot, "prompt-authoring-user"));
            var use = new ArtifactUsePreferences(f);
            var catalogue = f.ArtifactCatalogue;
            var promptStore = new WindowsOwnedInstructionStore(f, ArtifactKind.Prompt);
            var revisions = new InstructionRevisionCatalogue(f, ArtifactKind.Prompt);
            var actions = new OwnedInstructionEditorActions(promptStore, admission, revisions, catalogue, f.Audit, new(new()), ArtifactKind.Prompt);
            var management = new CustomizationManagementService(store, admission, use, catalogue, f.Audit);
            var directories = new LocalArtifactDirectoryPreferences(f);
            using var reload = new ArtifactCatalogueReloadService(catalogue, f, directories,
                new ArtifactDirectoryConfigurationService(directories, f.Audit),
                new McpServerConfigurationService(f, directories, profile),
                NullLogger<ArtifactCatalogueReloadService>.Instance, profileRoamingRoot: profile,
                promptRevisions: revisions, ownedPrompts: promptStore, use: use);
            using var bootstrapper = new DependencyBootstrapper([], NullLogger<DependencyBootstrapper>.Instance);
            var runtime = new Kora.Tools.Runtime.RecordedRuntimeObservation(bootstrapper);
            var registry = new ReadOnlyCapabilityRegistry(f.Access, new(), new(), new(new Kora.Application.AssemblyApplicationInfo()),
                new(bootstrapper), new(runtime), new(runtime), NullLogger<ReadOnlyCapabilityRegistry>.Instance);
            await using var modes = new AudioControlAdmission(f.Interactions, f.Interactions, new(f.Tasks));
            var policy = new ProviderModeConfigurationService(new LocalModelProviderModePreferences(f), modes, f.Audit);
            var adapter = new PromptAuthoringAdapter
            {
                Answer = action switch
                {
                    "invalid-placeholder" => "{\"body\":\"${env:topic}\"}",
                    "oversize" => JsonSerializer.Serialize(new { body = new string('x', 65536) }),
                    "unsupported" => "{\"unsupported\":\"Read secret files\"}",
                    _ => JsonSerializer.Serialize(new { body = PromptAuthoringAdapter.Body }),
                },
            };
            await using var models = new ModelTurnHost(f.Interactions, f.Access, f.Access, registry, f.Audit,
                NullLogger<ModelTurnHost>.Instance, TimeProvider.System,
                [new(ModelProviderSelection.OllamaCandidate, ModelQualificationGate.LocalCandidateSelection | ModelQualificationGate.IntegratedHost,
                    DateTimeOffset.UtcNow.AddMinutes(5), adapter)], policy);
            await using var runner = new LocalContextTurnWorkflow(f.Interactions, f.Interactions, f.Sessions, new(f.Tasks),
                f.Access, models, models, f.Audit, NullLogger<LocalContextTurnWorkflow>.Instance);
            var authoring = new CustomizationAuthoringService(admission, models, models, TimeProvider.System, runner);
            var ownership = 1L;
            using var controller = new CustomizationWindowController(f.Main, management, authoring, catalogue, reload,
                () => true, () => ownership, NullLogger<CustomizationWindowController>.Instance);
            CustomizationWindow? editor = null;
            var nextReview = new TaskCompletionSource<Window>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var observe = Window.WindowOpenedEvent.AddClassHandler<Window>((view, _) =>
            {
                if (view is CustomizationWindow selected) { editor = selected; }
                else if (view is not PromptParameterWindow) { nextReview.TrySetResult(view); }
            });
            controller.Bind();
            async Task Run(string command) { f.Main.CommandText = command; await f.Main.RunTypedCommand.ExecuteAsync(); }
            static void Choose(Window view, string caption) => view.GetVisualDescendants().OfType<Button>()
                .Single(button => Equals(button.Content, caption)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            async Task<Window> Review(Task operation)
            {
                (await Task.WhenAny(nextReview.Task, operation)).Should().BeSameAs(nextReview.Task,
                    "the operation must reach native review: {0}", editor!.Status.Text);
                var review = await nextReview.Task;
                nextReview = new(TaskCreationOptions.RunContinuationsAsynchronously);
                return review;
            }
            OwnedInstructionSnapshot? original = null;
            if (action is "existing" or "source-save" or "late-source")
            {
                original = (await promptStore.Write("example", null, ArtifactFileDocument.Parse(
                    ArtifactFileDocument.Encode("---\r\nkora-format: 2\r\nname: 'example'  \r\ndescription: \"Keep exact metadata\"\r\nattribution: Exact\r\n---\r\nOld ${input:topic}\r\n", true),
                    ArtifactKind.Prompt, "kora-profile", "example"), () => true, f.Token)).Current!;
                reload.ReloadNow().Should().BeTrue();
                await Run("edit example prompt");
            }
            else { await Run("add a prompt named example"); }
            var native = editor!;
            native.Kind.Should().Be(CustomizationKind.Prompt);
            native.RequiredPromptInputs.IsVisible.Should().BeTrue();
            native.RequiredPromptInputs.Text = "topic,audience";
            const string description = "  Create a repeatable REQUEST summarizing a topic for an audience.\r\nDo not submit it.  ";
            native.Request.Text = description;
            var draft = native.Editor.Text!;
            var path = Path.Combine(f.RoamingRoot, "Prompts", "example.prompt.md");
            var late = action.StartsWith("late-", StringComparison.Ordinal);
            if (late) { adapter.Pending = new(TaskCreationOptions.RunContinuationsAsynchronously); }
            Task generation;
            if (action is "voice")
            {
                await ActivateEditor(native);
                controller.VoiceTarget.Should().NotBeNull(native.VoiceContext.Text);
                generation = controller.RouteVoice(new(controller.VoiceTarget!, description,
                    HostRequest.Create(RequestOrigin.ActivatedVoice), () => true, () => f.Voice.Generation));
                var routing = await Review(generation);
                adapter.Calls.Should().Be(0);
                Choose(routing, "Interpret editor intent");
            }
            else
            {
                Choose(native, "Ask Kora (review local disclosure)");
                generation = controller.WaitForOperation();
            }
            var disclosure = await Review(generation);
            disclosure.Title.Should().Be("Review model authoring disclosure");
            var disclosed = disclosure.GetVisualDescendants().OfType<TextBox>().Single().Text!;
            disclosed.Should().Contain(draft).And.Contain(description).And.Contain("topic,audience").And.Contain("SAVED USER REQUEST");
            adapter.Calls.Should().Be(action is "voice" ? 1 : 0);
            if (action is "occupied-before")
            {
                await promptStore.Write("example", null, ArtifactFileDocument.Parse(ArtifactFileDocument.Encode(draft, false),
                    ArtifactKind.Prompt, "kora-profile", "example"), () => true, f.Token);
            }
            Choose(disclosure, action is "cancel-disclosure" ? "Cancel" : "Send for local authoring");
            if (late)
            {
                (await Task.WhenAny(adapter.Started.Task, generation)).Should().BeSameAs(adapter.Started.Task, native.Status.Text);
                switch (action)
                {
                    case "late-cancel": Choose(native, "Cancel prompt authoring"); break;
                    case "late-close": native.Close(); break;
                    case "late-private": f.Main.CloseForObservedPrivacyEvent("Synthetic prompt closure", hidePresentation: true); break;
                    case "late-owner": ownership++; break;
                    case "late-fields": native.RequiredPromptInputs.Text = "changed"; break;
                    case "late-request": native.Request.Text += "Changed"; break;
                    case "late-draft": native.Editor.Text += "Manual"; break;
                    case "late-input": f.Main.CommandText = "unrelated replacement input"; break;
                    case "late-catalogue": catalogue.ReportReloadFailure(new IOException("Synthetic unavailable")); break;
                    case "late-source":
                        await promptStore.Write("example", original, original!.Document.WithPromptBody("External request"), () => true, f.Token);
                        break;
                }
                generation.IsCompleted.Should().BeFalse("ownership must remain until the ignored cancellation joins");
                models.IsQuiescent.Should().BeFalse();
                adapter.Pending!.SetResult(new(ModelTurnOutcome.Succeeded, ModelProviderSelection.OllamaCandidate, new(adapter.Answer, null)));
                await generation;
                models.IsQuiescent.Should().BeTrue();
                nextReview.Task.IsCompleted.Should().BeFalse("late output cannot open proposal review");
                File.Exists(path).Should().Be(action is "late-source");
                if (action is "late-source") { File.ReadAllText(path).Should().EndWith("External request"); }
                if (action is not "late-private")
                { native.Editor.Text.Should().Be(action is "late-draft" ? draft + "Manual" : draft); }
            }
            else if (action is "cancel-disclosure" or "occupied-before" or "invalid-placeholder" or "oversize" or "unsupported")
            {
                await generation;
                native.Editor.Text.Should().Be(draft);
                nextReview.Task.IsCompleted.Should().BeFalse();
                adapter.Calls.Should().Be(action is "cancel-disclosure" or "occupied-before" ? 0 : 1);
                File.Exists(path).Should().Be(action is "occupied-before");
            }
            else
            {
                var proposal = await Review(generation);
                proposal.Title.Should().Be("Review proposed draft");
                proposal.GetVisualDescendants().OfType<TextBox>().Single().Text.Should().Contain(PromptAuthoringAdapter.Body).And.Contain("topic, audience");
                native.Editor.Text.Should().Be(draft);
                if (original is null) { File.Exists(path).Should().BeFalse(); }
                else { File.ReadAllBytes(path).Should().Equal(original.Document.CopyBytes()); }
                if (action is "source-review")
                {
                    await promptStore.Write("example", null, ArtifactFileDocument.Parse(ArtifactFileDocument.Encode(draft, false),
                        ArtifactKind.Prompt, "kora-profile", "example"), () => true, f.Token);
                }
                Choose(proposal, action is "cancel-proposal" ? "Cancel" : "Use proposed draft");
                await generation;
                if (action is "cancel-proposal" or "source-review") { native.Editor.Text.Should().Be(draft); }
                else
                {
                    native.Editor.Text.Should().Contain(PromptAuthoringAdapter.Body);
                    var proposed = native.Editor.Text!;
                    using var wire = JsonDocument.Parse(adapter.Wire!);
                    wire.RootElement.GetProperty("UserRequest").GetString().Should().Be(description);
                    wire.RootElement.GetProperty("Tools").GetArrayLength().Should().Be(0);
                    if (action is "occupied-save")
                    {
                        await promptStore.Write("example", null, ArtifactFileDocument.Parse(ArtifactFileDocument.Encode(draft, false),
                            ArtifactKind.Prompt, "kora-profile", "example"), () => true, f.Token);
                    }
                    if (action is "source-save")
                    {
                        await promptStore.Write("example", original, original!.Document.WithPromptBody("External request"), () => true, f.Token);
                    }
                    Choose(native, "Review and save");
                    var save = controller.WaitForOperation();
                    var confirmation = await Review(save);
                    confirmation.GetVisualDescendants().OfType<TextBox>().Single().Text.Should().Contain(proposed);
                    Choose(confirmation, action is "cancel-save" ? "Cancel" : "Save disabled");
                    await save;
                    if (action is "cancel-save") { File.Exists(path).Should().BeFalse(); }
                    else if (action is "occupied-save") { File.ReadAllText(path).Should().Be(draft); }
                    else if (action is "source-save") { File.ReadAllText(path).Should().EndWith("External request"); }
                    else
                    {
                        var saved = (await promptStore.Read("example", f.Token))!;
                        saved.Document.CopyBytes().Should().Equal(ArtifactFileDocument.Encode(proposed, original is not null));
                        saved.Document.Metadata.Should().BeEquivalentTo(ArtifactFileDocument.Parse(
                            ArtifactFileDocument.Encode(draft, original is not null), ArtifactKind.Prompt, "kora-profile", "example").Metadata);
                        var selected = (await store.List(f.Token)).Single(item => item.Kind == CustomizationKind.Prompt);
                        management.IsDisabled(selected).Should().BeTrue();
                        catalogue.Artifacts.Should().NotContain(item => item.OwnedInstructionName == "example");
                        await Run("edit example prompt");
                        native.RequiredPromptInputs.Text.Should().Be("topic,audience");
                        await management.SetDisabled(selected, false, (_, _) => Task.FromResult(true), () => true, f.Token);
                        await actions.Activate(saved, _ => Task.FromResult(true), () => true, f.Token);
                        f.Main.BindOwnedInstructionFreshnessWithCancellation((candidate, token) => actions.IsCurrent(candidate, () => true, token));
                        await using var unavailableModels = new ModelTurnHost(f.Interactions, f.Access, f.Access, registry, f.Audit,
                            NullLogger<ModelTurnHost>.Instance, TimeProvider.System,
                            [new(ModelProviderSelection.OllamaCandidate, ModelQualificationGate.None, DateTimeOffset.MinValue, null)], policy);
                        var conversation = new SessionConversationService(f.Interactions, f.Interactions, f.Sessions, f.Access, unavailableModels,
                            TimeProvider.System, NullLogger<SessionConversationService>.Instance);
                        var target = await f.Sessions.CreateAsync(new("Prompt authored target"), RequestOrigin.LocalUi, f.Token);
                        await using var parameters = new PromptParameterController(f.Main, catalogue,
                            new(admission, actions, models, f.Audit, TimeProvider.System), new(f.Sessions, conversation),
                            () => true, () => ownership, NullLogger<PromptParameterController>.Instance);
                        PromptParameterWindow? parameterWindow = null;
                        using var observeParameters = Window.WindowOpenedEvent.AddClassHandler<PromptParameterWindow>((view, _) => parameterWindow = view);
                        parameters.Bind();
                        await Run("/prompt example");
                        var inputs = parameterWindow!;
                        inputs.Inputs["topic"].Text = "${input:audience}";
                        inputs.Inputs["audience"].Text = "Engineers";
                        async Task Click(Button button)
                        { button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await parameters.ActiveOperation; }
                        await Click(inputs.Preview);
                        inputs.Expanded.Text.Should().Be("Summarize ${input:audience} for Engineers.\r\nAgain ${input:audience}.\r\n");
                        await Click(inputs.RefreshTargets);
                        inputs.Targets.SelectedItem = inputs.Targets.Items.Cast<PromptConversationTarget>().Single(item => item.Entry == target);
                        await Click(inputs.ReviewSubmit);
                        inputs.RetentionReview.Text.Should().Contain(inputs.Expanded.Text!);
                        await Click(inputs.ConfirmSubmit);
                        using var read = HostActivity.BeginOperation(HostActivityLayer.Desktop, HostOperation.Storage, RequestOrigin.LocalUi);
                        (await conversation.ReadPageAsync(target.Authority.SessionId, null, 25, f.Token)).Messages
                            .Should().ContainSingle().Which.Body.Should().Be("Summarize ${input:audience} for Engineers.\r\nAgain ${input:audience}.\r\n");
                        adapter.Calls.Should().Be(action is "voice" ? 2 : 1,
                            "submission is retained through existing unavailable generation, never another authoring call");
                        File.ReadAllBytes(path).Should().Equal(saved.Document.CopyBytes());
                        inputs.Close();
                        await parameters.CloseAndWait();
                    }
                }
            }
            models.IsQuiescent.Should().BeTrue();
            f.Main.CloseForObservedPrivacyEvent("Synthetic prompt cleanup", hidePresentation: true);
            await f.Main.PrivacyClosureTask;
            native.RequiredPromptInputs.Text.Should().BeEmpty();
            native.Request.Text.Should().BeEmpty();
        });
    }

    private sealed class PromptAuthoringAdapter : IModelTurnAdapter
    {
        internal const string Body = "Summarize ${input:topic} for ${input:audience}.\r\nAgain ${input:topic}.\r\n";
        internal string Answer = string.Empty;
        internal int Calls;
        internal byte[]? Wire;
        internal readonly TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource<ModelProviderReply>? Pending;
        public Task<ModelProviderReply> RunAsync(ModelTurnProvenance provenance, Kora.Core.Providers.ProviderProfileTuple? profile,
            ReadOnlyMemory<byte> context, Func<string, string, CancellationToken, ValueTask<CapabilityReply>> invokeTool, CancellationToken token)
        {
            Calls++; Wire = context.ToArray(); Started.TrySetResult();
            string answer;
            using (var json = JsonDocument.Parse(context))
            {
                answer = json.RootElement.GetProperty("SystemPolicy").GetString()!.StartsWith("Kora editor intent routing", StringComparison.Ordinal)
                    ? "{\"intent\":\"edit\"}" : Answer;
            }
            return Pending?.Task ?? Task.FromResult(new ModelProviderReply(ModelTurnOutcome.Succeeded, provenance.Selection, new(answer, null)));
        }
    }
}
