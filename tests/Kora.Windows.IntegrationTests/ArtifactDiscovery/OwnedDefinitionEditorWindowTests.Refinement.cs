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
using Kora.Core.Tools;
using Kora.Definitions.Artifacts;
using Kora.NativeUxFixture;
using Kora.Windows.Artifacts;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests.ArtifactDiscovery;

public sealed partial class OwnedDefinitionEditorWindowTests
{
    [Theory]
    [InlineData("save")]
    [InlineData("save-enabled")]
    [InlineData("cancel-disclosure")]
    [InlineData("cancel-proposal")]
    [InlineData("unavailable")]
    [InlineData("manual-edit")]
    [InlineData("stale-source")]
    [InlineData("cancel-save")]
    [InlineData("late-cancel")]
    [InlineData("late-close")]
    [InlineData("late-privacy")]
    [InlineData("late-owner")]
    [InlineData("late-draft")]
    [InlineData("late-critique")]
    [InlineData("new-save")]
    [InlineData("new-cancel-disclosure")]
    [InlineData("new-cancel-proposal")]
    [InlineData("new-cancel-save")]
    [InlineData("new-unavailable")]
    [InlineData("new-late-cancel")]
    [InlineData("new-late-close")]
    [InlineData("new-late-privacy")]
    [InlineData("new-late-owner")]
    [InlineData("new-late-draft")]
    [InlineData("new-late-critique")]
    [InlineData("new-late-destination")]
    [InlineData("new-late-kind")]
    [InlineData("new-late-call")]
    [InlineData("new-late-input")]
    [InlineData("new-occupied-target")]
    public async Task NativeRefinementUsesRealModelHostCompleteReviewAndConfirmedAuthoritativeSaveReadback(string action)
    {
        var creating = action.StartsWith("new-", StringComparison.Ordinal);
        if (creating) { action = action[4..]; }
        await HeadlessSession.RunAsync(async () =>
        {
            var parent = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".net-test-artifacts"));
            Directory.CreateDirectory(parent);
            using var f = new NativeUxFixtureSession(parent);
            var calls = new DictationCallState();
            await f.InitializeAsync(null, callState: calls);
            await using var admission = new InstructionControlAdmission(f.Interactions, f.Interactions, new(f.Tasks));
            var store = new WindowsOwnedInstructionStore(f);
            var revisions = new InstructionRevisionCatalogue(f);
            using var catalogue = EmbeddedArtifactCatalogue.Load();
            var actions = new OwnedInstructionEditorActions(store, admission, revisions, catalogue, f.Audit, new(new()));
            var directories = new LocalArtifactDirectoryPreferences(f);
            using var reload = new ArtifactCatalogueReloadService(catalogue, f, directories,
                new ArtifactDirectoryConfigurationService(directories, f.Audit),
                new McpServerConfigurationService(f, directories, Path.Combine(f.LocalRoot, "no-profile")),
                NullLogger<ArtifactCatalogueReloadService>.Instance, revisions, store,
                Path.Combine(f.LocalRoot, "no-profile"));
            var initial = "---\r\nkora-format: 1\r\nname: example\r\ndescription: Exact host metadata\r\n---\r\n  OLD_BODY e\u0301\U0001f642\r\n";
            var original = (await store.Write("example", null, ArtifactFileDocument.Parse(ArtifactFileDocument.Encode(initial, true),
                ArtifactKind.Instruction, "kora-profile", "example"), () => true, f.Token)).Current!;
            reload.ReconcileSavedCopy(original).Should().BeTrue();
            using var bootstrapper = new DependencyBootstrapper([], NullLogger<DependencyBootstrapper>.Instance);
            var runtime = new Kora.Tools.Runtime.RecordedRuntimeObservation(bootstrapper);
            var registry = new ReadOnlyCapabilityRegistry(f.Access, new(), new(), new(new Kora.Application.AssemblyApplicationInfo()),
                new(bootstrapper), new(runtime), new(runtime), NullLogger<ReadOnlyCapabilityRegistry>.Instance);
            await using var modes = new AudioControlAdmission(f.Interactions, f.Interactions, new(f.Tasks));
            var policy = new ProviderModeConfigurationService(new LocalModelProviderModePreferences(f), modes, f.Audit);
            var adapter = new RefinementAdapter { Unavailable = action is "unavailable" };
            await using var models = new ModelTurnHost(f.Interactions, f.Access, f.Access, registry, f.Audit,
                NullLogger<ModelTurnHost>.Instance, TimeProvider.System,
                [new(ModelProviderSelection.OllamaCandidate, ModelQualificationGate.LocalCandidateSelection | ModelQualificationGate.IntegratedHost,
                    DateTimeOffset.UtcNow.AddMinutes(5), adapter)], policy);
            var observed = new RefinementExecution(models);
            await using var runner = new LocalContextTurnWorkflow(f.Interactions, f.Interactions, f.Sessions, new(f.Tasks),
                f.Access, models, observed, f.Audit, NullLogger<LocalContextTurnWorkflow>.Instance);
            var refinement = new OwnedInstructionRefinementService(actions, store, admission, runner, TimeProvider.System);
            var ownership = 1L;
            using var controller = new OwnedInstructionEditorController(f.Main, actions, new(actions), reload, f,
                () => true, () => ownership, NullLogger<OwnedInstructionEditorController>.Instance, refinement: refinement);
            OwnedInstructionEditorWindow? editor = null;
            var nextReview = new TaskCompletionSource<Window>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var opened = Window.WindowOpenedEvent.AddClassHandler<Window>((window, _) =>
            {
                if (window is OwnedInstructionEditorWindow owned) { editor = owned; }
                else { nextReview.TrySetResult(window); }
            });
            controller.Bind();
            async Task Run(string command) { f.Main.CommandText = command; await f.Main.RunTypedCommand.ExecuteAsync(); }
            async Task<Window> Review(Task operation)
            {
                (await Task.WhenAny(operation, nextReview.Task)).Should().BeSameAs(nextReview.Task,
                    "the operation must reach native review: execution calls {0}, admitted {1}, access {2}, policy {3}; {4}",
                    observed.Calls, observed.Admitted, f.Access.Open, policy.Get(),
                    string.Join("\n", editor!.GetVisualDescendants().OfType<TextBlock>().Select(block => block.Text)));
                var review = await nextReview.Task;
                nextReview = new(TaskCreationOptions.RunContinuationsAsynchronously);
                return review;
            }
            static void Choose(Window window, string caption) => window.GetVisualDescendants().OfType<Button>()
                .Single(button => Equals(button.Content, caption)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (action is "save-enabled") { await actions.Activate(original, _ => Task.FromResult(true), () => true, f.Token); }
            await Run("instructions list");
            var targetName = creating ? "new-example" : "example";
            await Run(creating ? "instructions create new-example" : "instructions inspect example");
            var owned = editor!;
            if (creating) { initial = owned.Source.Text!; }
            var draft = initial.Replace("OLD_BODY", "CURRENT_DRAFT", StringComparison.Ordinal);
            owned.Source.Text = draft;
            const string critique = "  Make this clearer e\u0301\U0001f642.\r\nKeep all body lines.  ";
            owned.Critique.Text = critique;
            var late = action.StartsWith("late-", StringComparison.Ordinal);
            if (late) { adapter.Pending = new(TaskCreationOptions.RunContinuationsAsynchronously); }
            var generation = Run("instructions refine");
            var disclosure = await Review(generation);
            disclosure.Title.Should().Be(creating ? "Review new instruction generation disclosure" : "Review local instruction refinement disclosure");
            if (creating)
            {
                adapter.Calls.Should().Be(0);
                (await store.Read(targetName, f.Token)).Should().BeNull();
                string.Join("\n", disclosure.GetVisualDescendants().OfType<TextBlock>().Select(block => block.Text))
                    .Should().Contain(critique).And.Contain("destination kind: Instruction").And.Contain(targetName + ".instructions.md");
            }
            Choose(disclosure, action is "cancel-disclosure" ? "Cancel" : "Generate local body proposal");
            if (late)
            {
                (await Task.WhenAny(adapter.Started.Task, generation)).Should().BeSameAs(adapter.Started.Task);
                switch (action)
                {
                    case "late-cancel": Choose(owned, "Cancel refinement"); break;
                    case "late-close": owned.Close(); break;
                    case "late-privacy": f.Main.CloseForObservedPrivacyEvent("Synthetic late refinement closure", hidePresentation: true); break;
                    case "late-owner": ownership++; break;
                    case "late-draft": owned.Source.Text += "MANUAL_WORK"; break;
                    case "late-critique": owned.Critique.Text += "NEW_CRITIQUE"; break;
                    case "late-destination": owned.NameInput.Text = "another-target"; break;
                    case "late-kind": owned.KindInput.SelectedItem = ArtifactKind.Prompt; break;
                    case "late-call": calls.Set(Kora.Core.Communication.CallState.Active); break;
                    case "late-input": f.Main.CommandText = "unrelated replacement input"; break;
                }
                generation.IsCompleted.Should().BeFalse("cancellation must not release an adapter which ignores it");
                models.IsQuiescent.Should().BeFalse();
                adapter.Pending!.SetResult(new(ModelTurnOutcome.Succeeded, ModelProviderSelection.OllamaCandidate,
                    new(JsonSerializer.Serialize(new { body = RefinementAdapter.Body }), null)));
                await generation;
                nextReview.Task.IsCompleted.Should().BeFalse("late output must not open proposal review");
                (await store.Read("example", f.Token))!.Document.CopyBytes().Should().Equal(original.Document.CopyBytes());
                if (creating) { (await store.Read(targetName, f.Token)).Should().BeNull(); }
                models.IsQuiescent.Should().BeTrue();
                if (action is not "late-privacy") { owned.Source.Text.Should().Be(action is "late-draft" ? draft + "MANUAL_WORK" : draft); }
                f.Main.CloseForObservedPrivacyEvent("Synthetic cleanup", hidePresentation: true);
                await f.Main.PrivacyClosureTask;
                return;
            }
            if (action is "cancel-disclosure" or "unavailable")
            {
                await generation;
                owned.Source.Text.Should().Be(draft);
                owned.Critique.Text.Should().Be(critique);
                (await store.Read("example", f.Token))!.Document.CopyBytes().Should().Equal(original.Document.CopyBytes());
                adapter.Calls.Should().Be(action is "unavailable" ? 1 : 0, "execution calls {0}, admitted {1}, policy {2}", observed.Calls, observed.Admitted, policy.Get());
                models.IsQuiescent.Should().BeTrue();
                f.Main.CloseForObservedPrivacyEvent("Synthetic cleanup", hidePresentation: true);
                await f.Main.PrivacyClosureTask;
                return;
            }
            var proposal = await Review(generation);
            proposal.Title.Should().Be(creating ? "Review complete new instruction proposal" : "Compare complete instruction body proposal");
            var panes = proposal.GetVisualDescendants().OfType<TextBox>().Where(box => box.IsReadOnly).ToArray();
            panes.Should().HaveCount(2);
            panes[0].Text.Should().Be(draft);
            var proposed = panes[1].Text!;
            proposed.Should().Be(creating
                ? actions.PrepareSave(targetName, null, draft).WithInstructionBody(RefinementAdapter.Body).Text
                : initial.Replace("  OLD_BODY e\u0301\U0001f642\r\n", RefinementAdapter.Body, StringComparison.Ordinal));
            owned.Source.Text.Should().Be(draft);
            (await store.Read("example", f.Token))!.Document.CopyBytes().Should().Equal(original.Document.CopyBytes());
            using var wire = JsonDocument.Parse(adapter.Wire!);
            wire.RootElement.GetProperty("UserRequest").GetString().Should().Be(critique);
            if (creating) { wire.RootElement.GetProperty("Evidence")[0].GetProperty("Content").GetString().Should().Be(OwnedInstructionRefinementService.InitialContext); }
            else { wire.RootElement.GetProperty("Evidence")[0].GetProperty("Content").GetString().Should().Be("  CURRENT_DRAFT e\u0301\U0001f642\r\n"); }
            wire.RootElement.GetProperty("Tools").GetArrayLength().Should().Be(0);
            Choose(proposal, action is "cancel-proposal" ? "Cancel" : "Use proposal for next Save");
            await generation;
            owned.Source.Text.Should().Be(creating && action is not "cancel-proposal" ? proposed : draft);
            owned.Critique.Text.Should().Be(critique);
            if (action is "cancel-proposal")
            {
                f.Main.CloseForObservedPrivacyEvent("Synthetic cleanup", hidePresentation: true);
                await f.Main.PrivacyClosureTask;
                return;
            }
            if (action is "manual-edit") { owned.Source.Text = draft + "MANUAL_WORK"; }
            if (action is "occupied-target")
            {
                var occupied = (await store.Write(targetName, null, actions.PrepareSave(targetName, null, draft),
                    () => true, f.Token)).Current!;
                await Run("instructions save");
                nextReview.Task.IsCompleted.Should().BeFalse("occupied new targets cannot offer overwrite confirmation");
                (await store.Read(targetName, f.Token))!.Document.CopyBytes().Should().Equal(occupied.Document.CopyBytes());
                owned.Source.Text.Should().Be(proposed);
                f.Main.CloseForObservedPrivacyEvent("Synthetic cleanup", hidePresentation: true);
                await f.Main.PrivacyClosureTask;
                return;
            }
            if (action is "stale-source")
            {
                await store.Write("example", original, original.Document.WithInstructionBody("EXTERNAL_EDIT"), () => true, f.Token);
                await Run("instructions save");
                owned.Source.Text.Should().Be(draft);
                (await store.Read("example", f.Token))!.Document.Body.Should().Be("EXTERNAL_EDIT");
            }
            else
            {
                var save = Run("instructions save");
                var confirmation = await Review(save);
                confirmation.Title.Should().Be("Save exact owned Instruction");
                confirmation.GetVisualDescendants().OfType<TextBox>().Where(box => box.IsReadOnly).Last().Text.Should()
                    .Be(action is "manual-edit" ? draft + "MANUAL_WORK" : proposed);
                Choose(confirmation, action is "cancel-save" ? "Cancel" : "Confirm save");
                await save;
                var readback = await store.Read(targetName, f.Token);
                if (creating && action is "cancel-save") { readback.Should().BeNull(); }
                else { readback!.Document.CopyBytes().Should().Equal(action is "cancel-save"
                    ? original.Document.CopyBytes() : ArtifactFileDocument.Encode(action is "manual-edit" ? draft + "MANUAL_WORK" : proposed, !creating)); }
                if (action is not "cancel-save")
                {
                    owned.Source.Text.Should().Be(readback!.Document.Text);
                    catalogue.Artifacts.Should().NotContain(item => item.OwnedInstructionName == targetName);
                    revisions.Reconcile([readback.Document.Definition with { OwnedInstructionRevision = readback.Revision }])
                        .Single().IsUseEnabled.Should().BeFalse();
                    if (creating)
                    {
                        await Run("instructions inspect " + targetName);
                        owned.Source.Text.Should().Be(proposed);
                        var observedSource = catalogue.Candidates.Single(item => string.Equals(item.OwnedInstructionName, targetName, StringComparison.Ordinal));
                        observedSource.Id.Should().Be("disk.kora-profile.instruction." + targetName);
                        observedSource.OwnedInstructionRevision.Should().Be(readback.Revision);
                        observedSource.IsUseEnabled.Should().BeFalse();
                    }
                }
            }
            models.IsQuiescent.Should().BeTrue();
            f.Main.CloseForObservedPrivacyEvent("Synthetic cleanup", hidePresentation: true);
            await f.Main.PrivacyClosureTask;
            owned.Critique.Text.Should().BeEmpty();
        });
    }

    private sealed class RefinementAdapter : IModelTurnAdapter
    {
        internal const string Body = "  NEW_BODY e\u0301\U0001f642\r\nname: inert body data\r\n";
        internal bool Unavailable;
        internal int Calls;
        internal byte[]? Wire;
        internal readonly TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource<ModelProviderReply>? Pending;
        public Task<ModelProviderReply> RunAsync(ModelTurnProvenance provenance, Kora.Core.Providers.ProviderProfileTuple? profile,
            ReadOnlyMemory<byte> context, Func<string, string, CancellationToken, ValueTask<CapabilityReply>> invokeTool, CancellationToken token)
        {
            Calls++;
            Wire = context.ToArray();
            Started.TrySetResult();
            if (Pending is { } pending) { return pending.Task; }
            return Task.FromResult(Unavailable ? new ModelProviderReply(ModelTurnOutcome.Unavailable, provenance.Selection)
                : new(ModelTurnOutcome.Succeeded, provenance.Selection, new(JsonSerializer.Serialize(new { body = Body }), null)));
        }

    }

    private sealed class RefinementExecution(IModelTurnExecution inner) : IModelTurnExecution
        {
            internal int Calls;
            internal bool Admitted;
            public Task<ModelTurnResult> RunAsync(ModelTurn turn, Func<bool> admitted, CancellationToken token, Func<CancellationToken, Task>? validateSource = null)
            {
                Calls++;
                Admitted = admitted();
                return inner.RunAsync(turn, admitted, token, validateSource);
            }
            public Task WaitForQuiescenceAsync(CancellationToken token) => inner.WaitForQuiescenceAsync(token);
    }
}
