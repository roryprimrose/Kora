using System.Text.Json;
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
    public async Task NativeOwnedGuidanceRefinementCompletesReviewedProposalThroughExistingPublication(string scenario)
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
            await Run("owned skill create example 1.0.0");
            await Run("owned skill name: Example");
            await Run("owned skill description: Description");
            var owned = editor!;
            var inputs = scenario is "save-v3";
            if (inputs) { await Run("owned skill inputs required-literal"); }
            var initial = inputs ? "CURRENT ${input:topic}" : "  CURRENT e\u0301\U0001f642\r\nowned skill save  ";
            owned.Guidance.Text = initial;
            var draft = owned.Document();
            const string critique = "  Original critique e\u0301\U0001f642\r\nKeep the original input names.  ";
            await Run("owned skill critique: " + critique);
            var late = scenario.StartsWith("late-", StringComparison.Ordinal);
            if (late) { adapter.Pending = new(TaskCreationOptions.RunContinuationsAsynchronously); }
            adapter.Body = inputs ? "NEW ${input:topic}" : "  NEW e\u0301\U0001f642\r\nowned skill save  ";
            var generating = Run("owned skill refine");
            var disclosure = await Review(generating);
            disclosure.Title.Should().Be("Review local skill refinement disclosure");
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
                comparison.Title.Should().Be("Compare complete skill guidance proposal");
                var sources = comparison.GetLogicalDescendants().OfType<TextBox>().ToArray();
                sources.Select(source => source.Text).Should().Equal(initial, adapter.Body);
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
                wire.RootElement.GetProperty("UserRequest").GetString().Should().Be(critique);
                wire.RootElement.GetProperty("Evidence")[0].GetProperty("Content").GetString().Should().Be(initial);
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
            if (scenario is "save" or "save-v3" or "cancel-publication")
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
                    var saved = (await store.Read("example", "1.0.0", f.Token))!;
                    saved.Document.ManifestBytes.ToArray().Should().Equal(draft.ManifestBytes.ToArray());
                    saved.Document.Guidance.Should().Be(adapter.Body);
                    saved.Document.IsAvailableForInvocation.Should().BeFalse();
                    owned.Guidance.Text.Should().Be(adapter.Body);
                    await Run("owned skill reopen example 1.0.0");
                    owned.Guidance.Text.Should().Be(adapter.Body);
                    adapter.Calls.Should().Be(1, "Save and Reopen never invoke or enable a skill");
                }
            }
            models.IsQuiescent.Should().BeTrue();
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
