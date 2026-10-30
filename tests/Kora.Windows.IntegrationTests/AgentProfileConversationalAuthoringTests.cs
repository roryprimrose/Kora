using System.Text.Json;

using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

using AwesomeAssertions;

using Kora.Application.Agents;
using Kora.Application.Configuration;
using Kora.Application.Definitions;
using Kora.Application.Dependencies;
using Kora.Application.Tools;
using Kora.Application.Voice;
using Kora.Core.Agents;
using Kora.Core.Artifacts;
using Kora.Core.Customizations;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Tools;
using Kora.NativeUxFixture;
using Kora.Windows.Artifacts;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed class AgentProfileConversationalAuthoringTests
{
    [Theory]
    [InlineData("save")]
    [InlineData("native-save")]
    [InlineData("customizations-save")]
    [InlineData("voice-save")]
    [InlineData("refine-save")]
    [InlineData("cancel-disclosure")]
    [InlineData("reject")]
    [InlineData("malformed")]
    [InlineData("invented-authority")]
    [InlineData("oversize")]
    [InlineData("unavailable")]
    [InlineData("tool-attempt")]
    [InlineData("late-cancel")]
    [InlineData("late-source")]
    [InlineData("late-request")]
    [InlineData("late-owner")]
    [InlineData("late-privacy")]
    [InlineData("late-catalogue")]
    [InlineData("late-destination")]
    [InlineData("accept-source-edit")]
    [InlineData("accept-request-edit")]
    [InlineData("save-request-edit")]
    [InlineData("save-catalogue-edit")]
    [InlineData("save-expired")]
    [InlineData("occupied")]
    [InlineData("cancel-save")]
    [InlineData("uncertain-save")]
    public async Task OriginalDescriptionCompletesOnlyReviewedInertStagingAndSeparatelyConfirmedCanonicalSave(string scenario)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var parent = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".net-test-artifacts"));
            Directory.CreateDirectory(parent);
            using var f = new NativeUxFixtureSession(parent, syntheticVoice: true);
            await f.InitializeAsync();
            await using var admission = new InstructionControlAdmission(f.Interactions, f.Interactions, new(f.Tasks));
            var files = new WindowsOwnedAgentProfileStore(f);
            var store = new ObservedStore(files);
            var save = new AgentProfileSaveWorkflow(store, admission, f.Audit);
            using var bootstrapper = new DependencyBootstrapper([], NullLogger<DependencyBootstrapper>.Instance);
            var runtime = new Kora.Tools.Runtime.RecordedRuntimeObservation(bootstrapper);
            var registry = new ReadOnlyCapabilityRegistry(f.Access, new(), new(), new(new Kora.Application.AssemblyApplicationInfo()),
                new(bootstrapper), new(runtime), new(runtime), NullLogger<ReadOnlyCapabilityRegistry>.Instance);
            await using var modes = new AudioControlAdmission(f.Interactions, f.Interactions, new(f.Tasks));
            var policy = new ProviderModeConfigurationService(new LocalModelProviderModePreferences(f), modes, f.Audit);
            var adapter = new AgentAuthoringAdapter
            {
                Answer = scenario switch
                {
                    "malformed" => "{",
                    "invented-authority" => "{\"description\":\"Role\",\"body\":\"Body\",\"grants\":[\"all\"]}",
                    "oversize" => new string('x', 65537),
                    _ => "{\"description\":\"Explain supplied text\",\"body\":\"Genuine fake model guidance e\\u0301\\ud83d\\ude42.\\nagent confirm invocation\"}",
                },
                Unavailable = scenario is "unavailable",
                AttemptTool = scenario is "tool-attempt",
            };
            await using var models = new ModelTurnHost(f.Interactions, f.Access, f.Access, registry, f.Audit,
                NullLogger<ModelTurnHost>.Instance, TimeProvider.System,
                [new(ModelProviderSelection.OllamaCandidate, ModelQualificationGate.LocalCandidateSelection | ModelQualificationGate.IntegratedHost,
                    DateTimeOffset.UtcNow.AddMinutes(5), adapter)], policy);
            var clock = new AuthoringClock();
            await using var runner = new LocalContextTurnWorkflow(f.Interactions, f.Interactions, f.Sessions, new(f.Tasks),
                f.Access, models, models, f.Audit, NullLogger<LocalContextTurnWorkflow>.Instance);
            var authoring = new CustomizationAuthoringService(admission, runner, clock);
            using var catalogue = new ArtifactCatalogue([OwnedAgentProfileDocument.ProposeToolFree("catalogue", "", "Fixture", "Inert").Document.Definition]);
            var ownership = 1L;
            var controller = new AgentProfileEditorController(f.Main, save, () => true, () => ownership,
                NullLogger<AgentProfileEditorController>.Instance, authoring, catalogue, clock);
            AgentProfileEditorWindow? editor = null;
            var next = new TaskCompletionSource<Window>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var opened = Window.WindowOpenedEvent.AddClassHandler<Window>((window, _) =>
            {
                if (window is AgentProfileEditorWindow selected) { editor = selected; }
                else { next.TrySetResult(window); }
            });
            controller.Bind();
            using var entry = scenario is "customizations-save" ? new CustomizationsEntry(f, admission, catalogue, authoring, controller) : null;
            async Task Run(string text) { f.Main.CommandText = text; await f.Main.RunTypedCommand.ExecuteAsync(); }
            async Task<Window> Review(Task operation)
            {
                (await Task.WhenAny(next.Task, operation)).Should().BeSameAs(next.Task,
                    "the native review must open; status: {0}", editor?.Status.Text);
                var result = await next.Task.WaitAsync(TestContext.Current.CancellationToken);
                next = new(TaskCreationOptions.RunContinuationsAsynchronously);
                return result;
            }
            const string request = "Explain just user-supplied text e\u0301\U0001f642.";
            var target = Path.Combine(f.RoamingRoot, "OwnedAgents-v1", "example.agent.md");
            var late = scenario.StartsWith("late-", StringComparison.Ordinal);
            if (late) { adapter.Pending = new(TaskCreationOptions.RunContinuationsAsynchronously); }
            Task generating;
            if (entry is not null)
            {
                await entry.Open();
                entry.Window!.Kind = CustomizationKind.Agent;
                entry.Window.NewName.Text = "example";
                entry.Window.Request.Text = request;
                next = new(TaskCreationOptions.RunContinuationsAsynchronously);
                Click(entry.Window, "Add");
                generating = entry.Controller.WaitForOperation();
            }
            else if (scenario is "native-save")
            {
                await Run("agent author create example");
                editor!.DescriptionRequest.Text = request;
                Click(editor, "Ask Kora: review local authoring disclosure");
                generating = controller.ActiveOperation;
            }
            else if (scenario is "voice-save")
            {
                f.Main.BindVoiceOwnershipGate(() => true);
                await f.Main.SetVoiceConsentAsync(true);
                await f.Main.BeginPushToTalkCommand.ExecuteAsync();
                await f.Main.EndPushToTalkCommand.ExecuteAsync();
                f.Voice.PublishTranscript(f.Main.AssistantName + ", agent author draft example: " + request);
                await next.Task.WaitAsync(TimeSpan.FromSeconds(20), f.Token);
                generating = controller.ActiveOperation;
            }
            else { generating = Run("agent author draft example: " + request); }
            var disclosure = await Review(generating);
            disclosure.Title.Should().Be("Review local agent authoring disclosure");
            Text(disclosure).Should().Contain(request).And.Contain(target).And.Contain("COMPLETE CURRENT DRAFT");
            store.Creates.Should().Be(0);
            File.Exists(target).Should().BeFalse();
            Click(disclosure, scenario is "cancel-disclosure" ? "Cancel" : "Generate local agent proposal");
            if (late)
            {
                (await Task.WhenAny(adapter.Started.Task, generating)).Should().BeSameAs(adapter.Started.Task);
                switch (scenario)
                {
                    case "late-cancel": Click(editor!, "Cancel proposal or save review"); break;
                    case "late-source": editor!.Source.Text = "Independent manual draft"; break;
                    case "late-request": editor!.DescriptionRequest.Text += "Changed"; break;
                    case "late-owner": ownership++; break;
                    case "late-privacy": f.Main.CloseForObservedPrivacyEvent("Synthetic authoring privacy closure", hidePresentation: true); break;
                    case "late-catalogue": catalogue.ReportReloadFailure(new IOException("Synthetic reload withdrawal")); break;
                    case "late-destination": await File.WriteAllTextAsync(target, OwnedAgentProfileDocument.ProposeToolFree("example", "", "External", "External").Document.Text, f.Token); break;
                }
                generating.IsCompleted.Should().BeFalse("the cancellation-ignoring provider is still owned");
                models.IsQuiescent.Should().BeFalse();
                adapter.Pending!.SetResult(adapter.Reply());
                await generating;
                models.IsQuiescent.Should().BeTrue();
                store.Creates.Should().Be(0);
                editor!.Source.Text.Should().NotContain("Genuine fake model");
                if (scenario is "late-source") { editor.Source.Text.Should().Be("Independent manual draft"); }
                if (scenario is "late-privacy")
                {
                    await f.Main.PrivacyClosureTask;
                    editor.DescriptionRequest.Text.Should().BeEmpty();
                    return;
                }
                if (scenario is "late-owner") { ownership--; }
                await CloseDiscard(f, editor);
                return;
            }
            var invalid = scenario is "malformed" or "invented-authority" or "oversize" or "unavailable" or "tool-attempt";
            if (scenario is not "cancel-disclosure" && !invalid)
            {
                var comparison = await Review(generating);
                comparison.Title.Should().Be("Compare complete agent proposal");
                Text(comparison).Should().Contain("BEFORE: COMPLETE CURRENT DRAFT").And.Contain("AFTER: COMPLETE PROPOSED SOURCE")
                    .And.Contain(target).And.Contain("tools: none").And.Contain("Genuine fake model");
                editor!.Source.Text.Should().BeEmpty("model output cannot stage before acceptance");
                store.Creates.Should().Be(0);
                if (scenario is "accept-source-edit") { editor.Source.Text = "Manual retained"; }
                else if (scenario is "accept-request-edit") { editor.DescriptionRequest.Text += "Changed"; }
                else { Click(comparison, scenario is "reject" ? "Reject proposal" : "Use proposed draft"); }
            }
            await generating;
            models.IsQuiescent.Should().BeTrue();
            adapter.Calls.Should().Be(scenario is "cancel-disclosure" ? 0 : 1);
            store.Creates.Should().Be(0);
            File.Exists(target).Should().BeFalse();
            if (adapter.Calls != 0)
            {
                using var wire = JsonDocument.Parse(adapter.Wire!);
                wire.RootElement.GetProperty("UserRequest").GetString().Should().Be(request);
                wire.RootElement.GetProperty("Evidence")[0].GetProperty("Content").GetString().Should().BeEmpty();
                wire.RootElement.GetProperty("Tools").GetArrayLength().Should().Be(0);
                adapter.Origin.Should().Be(scenario is "voice-save" ? RequestOrigin.ActivatedVoice : RequestOrigin.LocalUi);
                if (scenario is "tool-attempt")
                {
                    adapter.ToolAttempt!.Outcome.Should().Be(CapabilityOutcome.Denied);
                    adapter.ToolAttempt.Reason.Should().Be("model-tools-not-permitted");
                    adapter.ToolAttempt.Version.Should().BeNull();
                }
                else { adapter.ToolAttempt.Should().BeNull(); }
                wire.RootElement.GetRawText().Should().NotContain(target);
            }
            if (invalid || scenario is "cancel-disclosure" or "reject" or "accept-request-edit" or "accept-source-edit")
            {
                if (scenario is "accept-source-edit") { editor!.Source.Text.Should().Be("Manual retained"); }
                else { editor!.Source.Text.Should().BeEmpty(); }
                await CloseDiscard(f, editor!);
                return;
            }
            var staged = editor!.Source.Text!;
            var profile = OwnedAgentProfileDocument.Parse("example", staged);
            profile.Profile.Tools.Should().BeEmpty();
            profile.Profile.MaximumCalls.Should().Be(0);
            profile.Profile.References.Should().BeEmpty();
            if (scenario is "refine-save")
            {
                await Run("agent author describe Make the guidance shorter.");
                adapter.Answer = "{\"description\":\"Short role\",\"body\":\"Refined genuine output.\"}";
                var refining = Run("agent author ask");
                Click(await Review(refining), "Generate local agent proposal");
                var comparison = await Review(refining);
                Text(comparison).Should().Contain(staged).And.Contain("Refined genuine output.");
                Click(comparison, "Use proposed draft");
                await refining;
                editor.Source.Text.Should().Contain("Refined genuine output.");
                adapter.Calls.Should().Be(2);
                using var wire = JsonDocument.Parse(adapter.Wire!);
                wire.RootElement.GetProperty("Evidence")[0].GetProperty("Content").GetString().Should().Be(staged);
            }
            editor.Source.Text += "\nSeparately reviewed manual refinement.";
            if (scenario is "save-request-edit") { editor.DescriptionRequest.Text += "Changed"; }
            if (scenario is "save-catalogue-edit") { catalogue.ReportReloadFailure(new IOException("Synthetic withdrawal")); }
            if (scenario is "save-expired") { clock.Now = clock.Now.AddMinutes(3); }
            if (scenario is "occupied") { await File.WriteAllTextAsync(target, profile.Document.Text, f.Token); }
            if (scenario is "uncertain-save") { files.AfterPublication = () => throw new IOException("Synthetic unconfirmed publication"); }
            await Run("agent author save as new");
            if (scenario is "save-request-edit" or "save-catalogue-edit" or "save-expired" or "occupied")
            {
                editor.Confirm.IsEnabled.Should().BeFalse();
                store.Creates.Should().Be(0);
            }
            else
            {
                editor.Review.Text.Should().Contain(editor.Source.Text).And.Contain(target);
                File.Exists(target).Should().BeFalse();
                Click(editor.Confirm);
                var confirmation = await Review(controller.ActiveOperation);
                confirmation.Title.Should().Be("Confirm Save as new");
                store.Creates.Should().Be(0);
                Click(confirmation, scenario is "cancel-save" ? "Cancel" : "Save as new");
                await controller.ActiveOperation;
                if (scenario is "cancel-save") { File.Exists(target).Should().BeFalse(); }
                else if (scenario is "uncertain-save")
                {
                    File.Exists(target).Should().BeTrue();
                    editor.Status.Text.Should().Contain("unconfirmed");
                    await Run("agent author save as new");
                    store.Creates.Should().Be(1, "uncertain publication cannot silently retry");
                    editor.Confirm.IsEnabled.Should().BeFalse();
                }
                else
                {
                    (await File.ReadAllTextAsync(target, f.Token)).Should().Be(editor.Source.Text);
                    editor.Source.IsReadOnly.Should().BeTrue();
                }
            }
            catalogue.SourceCandidates.Should().ContainSingle("authoring never registers or activates generated profiles");
            adapter.ToolAttempt.Should().BeNull();
            await CloseDiscard(f, editor);
        });
    }

    private static async Task CloseDiscard(NativeUxFixtureSession f, AgentProfileEditorWindow editor)
    {
        var choice = new TaskCompletionSource<Window>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var discard = Window.WindowOpenedEvent.AddClassHandler<Window>((window, _) =>
        { if (window.Title is "Unsaved agent draft") { choice.TrySetResult(window); } });
        var closing = f.Main.ExitAsync();
        if (await Task.WhenAny(closing, choice.Task) == choice.Task) { Click(await choice.Task, "Discard"); }
        await closing;
        editor.IsVisible.Should().BeFalse();
    }

    private static string Text(Window window) => string.Join("\n", window.GetVisualDescendants().OfType<TextBox>().Select(box => box.Text));
    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static void Click(Window window, string label) =>
        Click(window.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, label)));

    private sealed class AuthoringClock : TimeProvider
    {
        internal DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class AgentAuthoringAdapter : IModelTurnAdapter
    {
        internal string Answer = "";
        internal bool Unavailable;
        internal bool AttemptTool;
        internal int Calls;
        internal CapabilityReply? ToolAttempt;
        internal byte[]? Wire;
        internal RequestOrigin Origin;
        internal readonly TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource<ModelProviderReply>? Pending;
        internal ModelProviderReply Reply() => new(Unavailable ? ModelTurnOutcome.Unavailable : ModelTurnOutcome.Succeeded,
            ModelProviderSelection.OllamaCandidate, new(Answer, null));
        public async Task<ModelProviderReply> RunAsync(ModelTurnProvenance provenance, Kora.Core.Providers.ProviderProfileTuple? profile,
            ReadOnlyMemory<byte> context, Func<string, string, CancellationToken, ValueTask<CapabilityReply>> invokeTool, CancellationToken token)
        {
            Calls++;
            Wire = context.ToArray();
            Origin = HostActivity.RequireCurrent().Request.Origin;
            if (AttemptTool) { ToolAttempt = await invokeTool("application.get_version", "{}", token); }
            Started.TrySetResult();
            return Pending is { } pending ? await pending.Task : Reply();
        }
    }

    private sealed class ObservedStore(IOwnedAgentProfileStore inner) : IOwnedAgentProfileStore
    {
        internal int Creates;
        public Task<IReadOnlyList<string>> List(CancellationToken token) => inner.List(token);
        public Task<OwnedAgentProfileSnapshot?> Read(string name, CancellationToken token) => inner.Read(name, token);
        public Task<OwnedAgentProfileDestination> PrepareNew(string name, CancellationToken token) => inner.PrepareNew(name, token);
        public Task<AgentProfileWriteResult> Create(OwnedAgentProfileDestination destination, OwnedAgentProfileDocument document,
            Func<bool> eligible, Func<Func<AgentProfileWriteResult>, Task<AgentProfileWriteResult>> commit, CancellationToken token)
        {
            Creates++;
            return inner.Create(destination, document, eligible, commit, token);
        }
    }

        private sealed class CustomizationsEntry : IDisposable
        {
            private readonly ArtifactCatalogueReloadService reload;
            private readonly IDisposable observation;
            internal CustomizationWindowController Controller { get; }
            internal CustomizationWindow? Window { get; private set; }
            internal CustomizationsEntry(NativeUxFixtureSession f, InstructionControlAdmission admission, ArtifactCatalogue catalogue, CustomizationAuthoringService authoring,
                AgentProfileEditorController agents)
            {
                var profile = Path.Combine(f.LocalRoot, "synthetic-profile");
                var store = new WindowsCustomizationStore(f, profile, Path.Combine(f.LocalRoot, "synthetic-user"));
                var use = new ArtifactUsePreferences(f);
                var directories = new LocalArtifactDirectoryPreferences(f);
                reload = new(catalogue, f, directories, new ArtifactDirectoryConfigurationService(directories, f.Audit),
                    new McpServerConfigurationService(f, directories, profile), NullLogger<ArtifactCatalogueReloadService>.Instance,
                    profileRoamingRoot: profile);
                Controller = new(f.Main, new CustomizationManagementService(store, admission,
                    use, catalogue, f.Audit), authoring, catalogue, reload, () => true, () => 1,
                    NullLogger<CustomizationWindowController>.Instance, agents.Add);
                observation = Avalonia.Controls.Window.WindowOpenedEvent.AddClassHandler<CustomizationWindow>((window, _) => Window = window);
                Controller.Bind();
            }
            internal async Task Open()
            {
                Controller.Open();
                await Controller.WaitForOperation();
            }
            public void Dispose()
            {
                observation.Dispose();
                Controller.Dispose();
                reload.Dispose();
            }
    }
}
