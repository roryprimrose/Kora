using System.Text.Json;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using AwesomeAssertions;
using Kora.Application.Configuration;
using Kora.Application.Dependencies;
using Kora.Application.Hosting;
using Kora.Application.Skills;
using Kora.Application.Tools;
using Kora.Application.Voice;
using Kora.Core.Dependencies;
using Kora.Core.Skills;
using Kora.Core.Tools;
using Kora.NativeUxFixture;
using Kora.Windows.Skills;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

public sealed partial class OwnedSkillAuthoringNativeTests
{
    [Theory]
    [InlineData("save")]
    [InlineData("save-v3")]
    [InlineData("reject")]
    [InlineData("cancel-disclosure")]
    [InlineData("cancel-publication")]
    [InlineData("unavailable")]
    [InlineData("draft-edit")]
    [InlineData("critique-edit")]
    [InlineData("cancel-after-validate")]
    [InlineData("late-cancel")]
    [InlineData("late-draft")]
    [InlineData("late-critique")]
    [InlineData("late-owner")]
    [InlineData("late-privacy")]
    [InlineData("initial-save")]
    [InlineData("initial-save-refined")]
    [InlineData("initial-reject")]
    [InlineData("initial-cancel-disclosure")]
    [InlineData("initial-cancel-publication")]
    [InlineData("initial-unavailable")]
    [InlineData("initial-missing-input")]
    [InlineData("initial-missing-output")]
    [InlineData("initial-draft-edit")]
    [InlineData("initial-critique-edit")]
    [InlineData("initial-cancel-after-validate")]
    [InlineData("initial-late-cancel")]
    [InlineData("initial-late-draft")]
    [InlineData("initial-late-critique")]
    [InlineData("initial-late-owner")]
    [InlineData("initial-late-privacy")]
    public async Task NativeOwnedGuidanceRefinementCompletesReviewedProposalThroughExistingPublication(string scenario)
    {
        var initialGeneration = scenario.StartsWith("initial-", StringComparison.Ordinal);
        if (initialGeneration) { scenario = scenario["initial-".Length..]; }
        await HeadlessSession.RunAsync(async () =>
        {
            var parent = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".net-test-artifacts"));
            Directory.CreateDirectory(parent);
            using var f = new NativeUxFixtureSession(parent);
            await f.InitializeAsync();
            await using var admission = new OwnedSkillControlAdmission(f.Interactions, f.Interactions, new(f.Tasks));
            var store = new WindowsOwnedDeclarativeSkillStore(f);
            var save = new OwnedSkillSaveWorkflow(store, admission, f.Audit);
            using var bootstrapper = new DependencyBootstrapper([], NullLogger<DependencyBootstrapper>.Instance);
            var runtime = new Kora.Tools.Runtime.RecordedRuntimeObservation(bootstrapper);
            var registry = new ReadOnlyCapabilityRegistry(f.Access, new(), new(), new(new Kora.Application.AssemblyApplicationInfo()),
                new(bootstrapper), new(runtime), new(runtime), NullLogger<ReadOnlyCapabilityRegistry>.Instance);
            await using var modes = new AudioControlAdmission(f.Interactions, f.Interactions, new(f.Tasks));
            var policy = new ProviderModeConfigurationService(new LocalModelProviderModePreferences(f), modes, f.Audit);
            var adapter = new SkillRefinementAdapter { Unavailable = scenario is "unavailable" };
            await using var models = new ModelTurnHost(f.Interactions, f.Access, f.Access, registry, f.Audit,
                NullLogger<ModelTurnHost>.Instance, TimeProvider.System,
                [new(ModelProviderSelection.OllamaCandidate, ModelQualificationGate.LocalCandidateSelection | ModelQualificationGate.IntegratedHost,
                    DateTimeOffset.UtcNow.AddMinutes(5), adapter)], policy);
            await using var runner = new LocalContextTurnWorkflow(f.Interactions, f.Interactions, f.Sessions, new(f.Tasks),
                f.Access, models, models, f.Audit, NullLogger<LocalContextTurnWorkflow>.Instance);
            var refinement = new OwnedSkillGuidanceRefinementService(store, admission, save, runner, TimeProvider.System);
            var ownership = 1L;
            await using var controller = new OwnedSkillEditorController(f.Main, save, admission, static () => true,
                () => ownership, NullLogger<OwnedSkillEditorController>.Instance, refinement: refinement);
            controller.Bind();
            OwnedSkillEditorWindow? editor = null;
            var next = new TaskCompletionSource<Window>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var opened = Window.WindowOpenedEvent.AddClassHandler<Window>((window, _) =>
            {
                if (window is OwnedSkillEditorWindow skill) { editor = skill; }
                else { next.TrySetResult(window); }
            });
            async Task Run(string command) { f.Main.CommandText = command; await f.Main.RunTypedCommand.ExecuteAsync(); }
            async Task<Window> Review(Task operation)
            {
                (await Task.WhenAny(next.Task, operation)).Should().BeSameAs(next.Task,
                    "native review must be reached: {0}", string.Join("\n", editor!.GetLogicalDescendants().OfType<TextBlock>().Select(block => block.Text)));
                var result = await next.Task;
                next = new(TaskCreationOptions.RunContinuationsAsynchronously);
                return result;
            }
            static void Choose(Window dialog, string caption) => dialog.GetLogicalDescendants().OfType<Button>()
                .Single(button => Equals(button.Content, caption)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            const string purpose = "explain a deployment failure";
            if (initialGeneration)
            {
                await Run("create a skill that " + purpose);
                if (scenario is not "missing-input") { await Run("owned skill expected-input: User supplied failure text"); }
                if (scenario is not "missing-output") { await Run("owned skill expected-output: Explanation and questions"); }
            }
            else
            {
                await Run("owned skill create example 1.0.0");
                await Run("owned skill name: Example");
                await Run("owned skill description: Description");
            }
            var owned = editor!;
            var inputs = scenario is "save-v3";
            if (inputs) { await Run("owned skill inputs required-literal"); }
            var initial = initialGeneration ? owned.Guidance.Text! : inputs ? "CURRENT ${input:topic}" : "  CURRENT e\u0301\U0001f642\r\nowned skill save  ";
            if (!initialGeneration) { owned.Guidance.Text = initial; }
            var draft = owned.Document();
            const string critique = "  Original critique e\u0301\U0001f642\r\nKeep the original input names.  ";
            await Run("owned skill critique: " + critique);
            var late = scenario.StartsWith("late-", StringComparison.Ordinal);
            if (late) { adapter.Pending = new(TaskCreationOptions.RunContinuationsAsynchronously); }
            adapter.Body = inputs ? "NEW ${input:topic}" : "  NEW e\u0301\U0001f642\r\nowned skill save  ";
            var generating = Run(initialGeneration ? "owned skill generate" : "owned skill refine");
            if (scenario is "missing-input" or "missing-output")
            {
                await generating;
                await Run("owned skill validate");
                await Run("owned skill save");
                next.Task.IsCompleted.Should().BeFalse();
                adapter.Calls.Should().Be(0);
                (await store.List(f.Token)).Should().BeEmpty();
                return;
            }
            var disclosure = await Review(generating);
            disclosure.Title.Should().Be(initialGeneration ? "Review initial skill generation disclosure" : "Review local skill refinement disclosure");
            Choose(disclosure, scenario is "cancel-disclosure" ? "Cancel" : "Generate local guidance proposal");
            if (late)
            {
                (await Task.WhenAny(adapter.Started.Task, generating)).Should().BeSameAs(adapter.Started.Task);
                switch (scenario)
                {
                    case "late-cancel": Choose(owned, "Cancel refinement"); break;
                    case "late-draft": owned.Guidance.Text += "MANUAL"; break;
                    case "late-critique": owned.Critique.Text += "MANUAL"; break;
                    case "late-owner": ownership++; break;
                    case "late-privacy": f.Main.CloseForObservedPrivacyEvent("Synthetic refinement closure", hidePresentation: true); break;
                }
                generating.IsCompleted.Should().BeFalse("the actual provider is still owned");
                models.IsQuiescent.Should().BeFalse();
                adapter.Pending!.SetResult(new(ModelTurnOutcome.Succeeded, ModelProviderSelection.OllamaCandidate, new("{\"body\":\"LATE\"}", null)));
                await generating;
                models.IsQuiescent.Should().BeTrue();
                (await store.List(f.Token)).Should().BeEmpty();
                owned.Guidance.Text.Should().NotContain("LATE");
                owned.Close();
                return;
            }
            if (scenario is not ("cancel-disclosure" or "unavailable"))
            {
                var comparison = await Review(generating);
                comparison.Title.Should().Be(initialGeneration ? "Review complete generated skill source" : "Compare complete skill guidance proposal");
                var sources = comparison.GetLogicalDescendants().OfType<TextBox>().ToArray();
                sources.Select(source => source.Text).Should().Equal(initialGeneration ? draft.ManifestText : initial, adapter.Body);
                if (initialGeneration)
                {
                    string.Join("\n", comparison.GetLogicalDescendants().OfType<TextBlock>().Select(block => block.Text))
                        .Should().Contain(store.Destination(draft));
                }
                sources.Should().OnlyContain(source => source.IsReadOnly);
                Choose(comparison, scenario is "reject" ? "Reject proposal" : "Use proposal for next Save");
            }
            await generating;
            owned.Guidance.Text.Should().Be(initial, "proposal acceptance does not edit or autosave the draft");
            owned.Critique.Text.Should().Be(critique);
            (await store.List(f.Token)).Should().BeEmpty();
            if (scenario is "cancel-disclosure")
            { adapter.Calls.Should().Be(0); }
            else
            {
                adapter.Calls.Should().Be(1);
                using var wire = JsonDocument.Parse(adapter.Wire!);
                wire.RootElement.GetProperty("UserRequest").GetString().Should().Be(initialGeneration ? purpose : critique);
                if (!initialGeneration) { wire.RootElement.GetProperty("Evidence")[0].GetProperty("Content").GetString().Should().Be(initial); }
                wire.RootElement.GetProperty("Tools").GetArrayLength().Should().Be(0);
            }
            if (scenario is "draft-edit" or "critique-edit" or "cancel-after-validate")
            {
                await Run("owned skill validate");
                if (scenario is "draft-edit") { owned.Guidance.Text += "MANUAL"; }
                if (scenario is "critique-edit") { owned.Critique.Text += "MANUAL"; }
                if (scenario is "cancel-after-validate") { await Run("owned skill cancel-refinement"); }
                await Run("owned skill save");
                next.Task.IsCompleted.Should().BeFalse("revocation must clear the validated replacement, not fall back to publishing it");
                (await store.List(f.Token)).Should().BeEmpty();
            }
            if (scenario is "save-refined")
            {
                await Run("owned skill critique: Make the generated guidance clearer.");
                var previous = adapter.Body;
                adapter.Body = "Complete refined generated guidance";
                var refining = Run("owned skill refine");
                Choose(await Review(refining), "Generate local guidance proposal");
                var comparison = await Review(refining);
                comparison.GetLogicalDescendants().OfType<TextBox>().Select(source => source.Text)
                    .Should().Equal(previous, adapter.Body);
                Choose(comparison, "Use proposal for next Save");
                await refining;
            }
            if (scenario is "save" or "save-v3" or "save-refined" or "cancel-publication")
            {
                await Run("owned skill validate");
                var saving = Run("owned skill save");
                var package = await Review(saving);
                package.Title.Should().Be("Review complete skill package");
                package.GetLogicalDescendants().OfType<TextBox>().Select(source => source.Text)
                    .Should().Equal(draft.ManifestText, adapter.Body);
                Choose(package, "Continue to separate native publication confirmation");
                var publication = await Review(saving);
                publication.Title.Should().Be("Confirm exact new skill publication");
                (await store.List(f.Token)).Should().BeEmpty();
                Choose(publication, scenario is "cancel-publication" ? "Cancel" : "Publish exact new version");
                await saving;
                if (scenario is "cancel-publication") { (await store.List(f.Token)).Should().BeEmpty(); }
                else
                {
                    var saved = (await store.Read(draft.Id, draft.Version, f.Token))!;
                    saved.Document.ManifestBytes.ToArray().Should().Equal(draft.ManifestBytes.ToArray());
                    saved.Document.Guidance.Should().Be(adapter.Body);
                    saved.Document.IsAvailableForInvocation.Should().BeFalse();
                    owned.Guidance.Text.Should().Be(adapter.Body);
                    await Run($"owned skill reopen {draft.Id} {draft.Version}");
                    owned.Guidance.Text.Should().Be(adapter.Body);
                    adapter.Calls.Should().Be(scenario is "save-refined" ? 2 : 1, "Save and Reopen never invoke or enable a skill");
                }
            }
            models.IsQuiescent.Should().BeTrue();
        });
    }

    [Theory]
    [InlineData("clean")]
    [InlineData("discard")]
    [InlineData("cancel")]
    [InlineData("save")]
    public async Task PurposeCreationAfterReopenedMarkdownPackageResetsOnlyExplicitlyDiscardedDraftState(string scenario)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var parent = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".net-test-artifacts"));
            Directory.CreateDirectory(parent);
            using var f = new NativeUxFixtureSession(parent);
            await f.InitializeAsync();
            await using var admission = new OwnedSkillControlAdmission(f.Interactions, f.Interactions, new(f.Tasks));
            var store = new WindowsOwnedDeclarativeSkillStore(f);
            var save = new OwnedSkillSaveWorkflow(store, admission, f.Audit);
            using var bootstrapper = new DependencyBootstrapper([], NullLogger<DependencyBootstrapper>.Instance);
            var runtime = new Kora.Tools.Runtime.RecordedRuntimeObservation(bootstrapper);
            var registry = new ReadOnlyCapabilityRegistry(f.Access, new(), new(), new(new Kora.Application.AssemblyApplicationInfo()),
                new(bootstrapper), new(runtime), new(runtime), NullLogger<ReadOnlyCapabilityRegistry>.Instance);
            await using var modes = new AudioControlAdmission(f.Interactions, f.Interactions, new(f.Tasks));
            var policy = new ProviderModeConfigurationService(new LocalModelProviderModePreferences(f), modes, f.Audit);
            var adapter = new SkillRefinementAdapter { Body = "Complete new literal guidance" };
            await using var models = new ModelTurnHost(f.Interactions, f.Access, f.Access, registry, f.Audit,
                NullLogger<ModelTurnHost>.Instance, TimeProvider.System,
                [new(ModelProviderSelection.OllamaCandidate, ModelQualificationGate.LocalCandidateSelection | ModelQualificationGate.IntegratedHost,
                    DateTimeOffset.UtcNow.AddMinutes(5), adapter)], policy);
            await using var runner = new LocalContextTurnWorkflow(f.Interactions, f.Interactions, f.Sessions, new(f.Tasks),
                f.Access, models, models, f.Audit, NullLogger<LocalContextTurnWorkflow>.Instance);
            var refinement = new OwnedSkillGuidanceRefinementService(store, admission, save, runner, TimeProvider.System);
            await using var controller = new OwnedSkillEditorController(f.Main, save, admission, static () => true,
                static () => 1, NullLogger<OwnedSkillEditorController>.Instance, refinement: refinement);
            controller.Bind();
            OwnedSkillEditorWindow? editor = null;
            var next = new TaskCompletionSource<Window>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var opened = Window.WindowOpenedEvent.AddClassHandler<Window>((window, _) =>
            {
                if (window is OwnedSkillEditorWindow skill) { editor = skill; }
                else { next.TrySetResult(window); }
            });
            async Task Run(string command) { f.Main.CommandText = command; await f.Main.RunTypedCommand.ExecuteAsync(); }
            async Task<Window> Review(Task operation)
            {
                (await Task.WhenAny(next.Task, operation)).Should().BeSameAs(next.Task,
                    "native review must be reached: {0}", string.Join("\n", editor!.GetLogicalDescendants().OfType<TextBlock>().Select(block => block.Text)));
                var result = await next.Task;
                next = new(TaskCreationOptions.RunContinuationsAsynchronously);
                return result;
            }
            static void Choose(Window dialog, string caption) => dialog.GetLogicalDescendants().OfType<Button>()
                .Single(button => Equals(button.Content, caption)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            await Run("owned skill create previous 1.0.0");
            await Run("owned skill name: Previous package");
            await Run("owned skill description: Previous description");
            await Run("owned skill guidance: PRIVATE previous primary guidance");
            await Run("owned skill format markdown-package");
            await Run(@"owned skill reference references\guide.md: PRIVATE previous component");
            var owned = editor!;
            var previous = owned.Document();
            await Run("owned skill validate");
            var savingPrevious = Run("owned skill save");
            Choose(await Review(savingPrevious), "Continue to separate native publication confirmation");
            (await store.List(f.Token)).Should().BeEmpty();
            Choose(await Review(savingPrevious), "Publish exact new version");
            await savingPrevious;
            var saved = (await store.Read(previous.Id, previous.Version, f.Token))!;
            saved.Document.References.Should().ContainSingle();
            var reopening = Run("owned skill reopen previous 1.0.0");
            var readConsent = await Review(reopening);
            readConsent.Title.Should().Be("Review exact owned component declaration");
            readConsent.GetLogicalDescendants().OfType<TextBox>().Select(box => box.Text)
                .Should().NotContain("PRIVATE previous component");
            Choose(readConsent, "Read only the exact declared owned Markdown components");
            await reopening;
            owned.BasePackage!.IsSameRevision(saved).Should().BeTrue();
            owned.Document().References.Should().ContainSingle();
            adapter.Calls.Should().Be(0);
            const string purpose = "explain a new deployment failure";
            if (scenario is "discard" or "cancel")
            {
                owned.ComponentPath.Text = @"references\pending.md";
                owned.ComponentBody.Text = "PRIVATE unapplied component edit";
                Action readPending = () => owned.Document();
                readPending.Should().Throw<InvalidOperationException>();
                var creating = Run("create a skill that " + purpose);
                var dirtyChoice = await Review(creating);
                dirtyChoice.Title.Should().Be("Unsaved skill draft");
                if (scenario is "cancel")
                {
                    Choose(dirtyChoice, "Cancel");
                    await creating;
                    owned.BasePackage!.IsSameRevision(saved).Should().BeTrue();
                    owned.InputFormat.SelectedIndex.Should().Be(2);
                    owned.ComponentPath.Text.Should().Be(@"references\pending.md");
                    owned.ComponentBody.Text.Should().Be("PRIVATE unapplied component edit");
                    readPending.Should().Throw<InvalidOperationException>();
                    adapter.Calls.Should().Be(0);
                    creating = Run("create a skill that " + purpose);
                    dirtyChoice = await Review(creating);
                }
                Choose(dirtyChoice, "Discard");
                await creating;
            }
            else { await Run("create a skill that " + purpose); }

            editor.Should().BeSameAs(owned);
            owned.BasePackage.Should().BeNull();
            owned.ComponentPath.Text.Should().BeEmpty();
            owned.ComponentBody.Text.Should().BeEmpty();
            owned.GetLogicalDescendants().OfType<ComboBox>()
                .Single(box => string.Equals(AutomationProperties.GetName(box), "Declared component tree paths", StringComparison.Ordinal))
                .Items.Should().BeEmpty();
            owned.InputFormat.SelectedIndex.Should().Be(0);
            owned.Tool.SelectedIndex.Should().Be(0);
            owned.Purpose.Text.Should().Be(purpose);
            owned.ExpectedInput.Text.Should().BeEmpty();
            owned.ExpectedOutput.Text.Should().BeEmpty();
            var draft = owned.Document();
            draft.Id.Should().NotBe(previous.Id);
            draft.ManifestText.Should().Contain("kora-skill-format: 1\n");
            draft.ReferenceDeclaration.Should().BeNull();
            draft.References.Should().BeEmpty();
            draft.InputTemplate.Should().BeNull();
            draft.Tool.Should().Be(OwnedSkillToolSelection.None);
            adapter.Calls.Should().Be(0);
            await Run("owned skill validate");
            await Run("owned skill save");
            next.Task.IsCompleted.Should().BeFalse("a fresh purpose requires separately reviewed generation before Save");
            (await store.List(f.Token)).Should().ContainSingle();
            await Run("owned skill expected-input: Original new failure text");
            await Run("owned skill expected-output: Explanation and questions");
            var generating = Run("owned skill generate");
            var disclosure = await Review(generating);
            disclosure.Title.Should().Be("Review initial skill generation disclosure");
            string.Join("\n", disclosure.GetLogicalDescendants().OfType<TextBlock>().Select(block => block.Text))
                .Should().Contain(purpose).And.NotContain("PRIVATE");
            adapter.Calls.Should().Be(0);
            Choose(disclosure, "Generate local guidance proposal");
            var proposal = await Review(generating);
            proposal.Title.Should().Be("Review complete generated skill source");
            proposal.GetLogicalDescendants().OfType<TextBox>().Select(box => box.Text)
                .Should().Equal(draft.ManifestText, adapter.Body);
            Choose(proposal, "Use proposal for next Save");
            await generating;
            owned.Guidance.Text.Should().Be(draft.Guidance);
            adapter.Calls.Should().Be(1);
            using var wire = JsonDocument.Parse(adapter.Wire!);
            wire.RootElement.GetProperty("UserRequest").GetString().Should().Be(purpose);
            wire.RootElement.GetProperty("Tools").GetArrayLength().Should().Be(0);
            wire.RootElement.GetProperty("Evidence").GetArrayLength().Should().Be(1);
            using var evidence = JsonDocument.Parse(wire.RootElement.GetProperty("Evidence")[0].GetProperty("Content").GetString()!);
            evidence.RootElement.GetProperty("input").GetString().Should().Be("Original new failure text");
            evidence.RootElement.GetProperty("output").GetString().Should().Be("Explanation and questions");
            evidence.RootElement.EnumerateObject().Count().Should().Be(2);
            wire.RootElement.GetRawText().Should().NotContain("PRIVATE");
            await Run("owned skill validate");
            var savingNew = Run("owned skill save");
            var packageReview = await Review(savingNew);
            packageReview.GetLogicalDescendants().OfType<TextBox>().Select(box => box.Text)
                .Should().Equal(draft.ManifestText, adapter.Body);
            Choose(packageReview, "Continue to separate native publication confirmation");
            var publication = await Review(savingNew);
            publication.Title.Should().Be("Confirm exact new skill publication");
            (await store.List(f.Token)).Should().ContainSingle("generation and validation cannot implicitly save");
            Choose(publication, scenario is "save" ? "Publish exact new version" : "Cancel");
            await savingNew;
            if (scenario is "save")
            {
                var savedNew = (await store.Read(draft.Id, draft.Version, f.Token))!;
                savedNew.Document.ManifestText.Should().Be(draft.ManifestText);
                savedNew.Document.Guidance.Should().Be(adapter.Body);
                savedNew.Document.ReferenceDeclaration.Should().BeNull();
                savedNew.Document.References.Should().BeEmpty();
                savedNew.Document.InputTemplate.Should().BeNull();
                savedNew.Document.IsAvailableForInvocation.Should().BeFalse();
                owned.BasePackage!.IsSameRevision(savedNew).Should().BeTrue();
                owned.Document().References.Should().BeEmpty();
            }
            var unchanged = (await store.Read(previous.Id, previous.Version, f.Token))!;
            unchanged.IsSameRevision(saved).Should().BeTrue();
            unchanged.Document.ManifestBytes.ToArray().Should().Equal(previous.ManifestBytes.ToArray());
            unchanged.Document.InstructionBytes.ToArray().Should().Equal(previous.InstructionBytes.ToArray());
            unchanged.Document.References.Single().Bytes.ToArray().Should().Equal(previous.References.Single().Bytes.ToArray());
            unchanged.Document.IsAvailableForInvocation.Should().BeFalse();
            (await store.List(f.Token)).Should().HaveCount(scenario is "save" ? 2 : 1);
            adapter.Calls.Should().Be(1, "publication review never enables or invokes either skill");
            models.IsQuiescent.Should().BeTrue();
            var closing = controller.PrepareClose();
            if (scenario is not "save") { Choose(await Review(closing), "Discard"); }
            (await closing).Should().BeTrue();
        });
    }

    private sealed class SkillRefinementAdapter : IModelTurnAdapter
    {
        internal string Body = "NEW";
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
            return Pending?.Task ?? Task.FromResult(Unavailable ? new ModelProviderReply(ModelTurnOutcome.Unavailable, provenance.Selection)
                : new(ModelTurnOutcome.Succeeded, provenance.Selection, new(JsonSerializer.Serialize(new { body = Body }), null)));
        }
    }
}
