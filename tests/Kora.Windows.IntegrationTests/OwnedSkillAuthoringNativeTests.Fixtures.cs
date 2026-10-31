using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using AwesomeAssertions;
using Kora.Application.Configuration;
using Kora.Application.Dependencies;
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
    [InlineData("pass")]
    [InlineData("fail")]
    [InlineData("saved")]
    [InlineData("saved-delete")]
    [InlineData("reject")]
    [InlineData("unknown")]
    [InlineData("mocked-tool")]
    [InlineData("v2")]
    [InlineData("v3")]
    [InlineData("unavailable")]
    [InlineData("review-edit")]
    [InlineData("other-cancel")]
    [InlineData("late-cancel")]
    [InlineData("late-fixture")]
    [InlineData("late-draft")]
    [InlineData("late-expected-input")]
    [InlineData("late-critique")]
    [InlineData("late-selection")]
    [InlineData("late-delete")]
    [InlineData("late-owner")]
    [InlineData("late-privacy")]
    [InlineData("dispose")]
    public async Task NativeSimulatedFixtureCompletesExactReviewedOperationWithoutOrdinaryInvocation(string scenario)
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
            var adapter = new SkillSimulationAdapter { Unavailable = scenario is "unavailable" };
            await using var models = new ModelTurnHost(f.Interactions, f.Access, f.Access, registry, f.Audit,
                NullLogger<ModelTurnHost>.Instance, TimeProvider.System,
                [new(ModelProviderSelection.OllamaCandidate, ModelQualificationGate.LocalCandidateSelection | ModelQualificationGate.IntegratedHost,
                    DateTimeOffset.UtcNow.AddMinutes(5), adapter)], policy);
            await using var runner = new LocalContextTurnWorkflow(f.Interactions, f.Interactions, f.Sessions, new(f.Tasks),
                f.Access, models, models, f.Audit, NullLogger<LocalContextTurnWorkflow>.Instance);
            var flow = new OwnedSkillFixtureWorkflow(store, admission, runner, TimeProvider.System);
            var ownership = 1L;
            await using var controller = new OwnedSkillEditorController(f.Main, save, admission, static () => true,
                () => ownership, NullLogger<OwnedSkillEditorController>.Instance,
                deletion: new(store, admission, f.Audit, TimeProvider.System), fixtures: flow);
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
            const string guidance = "  Complete e\u0301\U0001f642 guidance\r\nowned skill save; shell.run  ";
            owned.Guidance.Text = guidance;
            if (scenario is "v2") { await Run("owned skill tool web-page-get"); }
            if (scenario is "v3")
            {
                owned.Guidance.Text += "${input:topic}";
                await Run("owned skill inputs required-literal");
            }
            var document = owned.Document();
            var savedSource = scenario is "saved" or "saved-delete" or "late-delete";
            if (savedSource)
            {
                var saving = Run("owned skill save");
                await saving;
                next.Task.IsCompleted.Should().BeFalse("testing must not implicitly validate Save");
                await Run("owned skill validate");
                saving = Run("owned skill save");
                Choose(await Review(saving), "Continue to separate native publication confirmation");
                Choose(await Review(saving), "Publish exact new version");
                await saving;
                await Run("owned skill list");
                await Run("owned skill reopen example 1.0.0");
            }
            const string input = "  example e\u0301\U0001f642\r\nowned skill invoke  ";
            const string output = "  SIMULATED e\u0301\U0001f642\r\nowned skill save; shell.run  ";
            var fixture = JsonSerializer.Serialize(new
            {
                input,
                assertions = new[] { new { kind = scenario is "unknown" ? "eval" : "equals", value = scenario is "fail" ? "other" : output } },
            });
            if (scenario is "mocked-tool") { fixture = fixture[..^1] + ",\"mockedToolResponse\":{\"tool\":\"WebPageGet\",\"result\":\"counterfeit\"}}"; }
            await Run("owned skill fixture: " + fixture);
            adapter.Output = output;
            var late = scenario.StartsWith("late-", StringComparison.Ordinal) || scenario is "dispose";
            if (late) { adapter.Pending = new(TaskCreationOptions.RunContinuationsAsynchronously); }
            var testing = Run("owned skill test");
            if (scenario is "unknown" or "mocked-tool" or "v2" or "v3")
            {
                await testing;
                next.Task.IsCompleted.Should().BeFalse();
                adapter.Calls.Should().Be(0);
                owned.GetLogicalDescendants().OfType<TextBlock>().Should().Contain(block => block.Text != null
                    && block.Text.Contains("Skill authoring failed", StringComparison.Ordinal));
                return;
            }
            var disclosure = await Review(testing);
            disclosure.Title.Should().Be("Review SIMULATED skill example");
            var reviewed = disclosure.GetLogicalDescendants().OfType<TextBox>().ToArray();
            reviewed.Should().HaveCount(2).And.OnlyContain(source => source.IsReadOnly);
            reviewed[0].Text.Should().Contain(document.ManifestText).And.Contain(guidance).And.Contain(fixture);
            using (var wire = JsonDocument.Parse(reviewed[1].Text!))
            {
                wire.RootElement.GetProperty("UserRequest").GetString().Should().Be(input);
                wire.RootElement.GetProperty("Tools").GetArrayLength().Should().Be(0);
                wire.RootElement.GetProperty("ToolMode").GetInt32().Should().Be((int)ModelToolMode.None);
            }
            adapter.Calls.Should().Be(0, "preparation and validation cannot run inference");
            if (scenario is "other-cancel")
            {
                Choose(owned, "Cancel refinement");
                disclosure.IsVisible.Should().BeTrue("Cancel refinement must not close a simulated test review");
                Choose(owned, "Generate initial guidance");
                disclosure.IsVisible.Should().BeTrue("a busy editor cannot start a second model operation");
                adapter.Calls.Should().Be(0);
            }
            if (scenario is "review-edit")
            {
                owned.Fixture.Text += " ";
                await testing;
                adapter.Calls.Should().Be(0);
                return;
            }
            Choose(disclosure, scenario is "reject" ? "Cancel" : "Start SIMULATED test");
            if (late)
            {
                (await Task.WhenAny(adapter.Started.Task, testing)).Should().BeSameAs(adapter.Started.Task);
                Task? disposing = null;
                try
                {
                switch (scenario)
                {
                    case "late-cancel": Choose(owned, "Cancel test"); break;
                    case "late-fixture": owned.Fixture.Text += " "; break;
                    case "late-draft": owned.Guidance.Text += "EDIT"; break;
                    case "late-expected-input": owned.ExpectedInput.Text = "Different generation input"; break;
                    case "late-critique": owned.Critique.Text = "Different refinement critique"; break;
                    case "late-selection":
                        owned.Saved.ItemsSource = new[] { new OwnedDeclarativeSkillSnapshot(document, "d", "m", "i") };
                        owned.Saved.SelectedIndex = 0;
                        break;
                    case "late-delete":
                        await Run("owned skill delete example 1.0.0");
                        next.Task.IsCompleted.Should().BeFalse("deletion cannot start a review while simulation owns work");
                        var heldSource = (await store.Read("example", "1.0.0", f.Token))!.Document;
                        heldSource.ManifestBytes.ToArray().Should().Equal(document.ManifestBytes.ToArray());
                        heldSource.InstructionBytes.ToArray().Should().Equal(document.InstructionBytes.ToArray());
                        Choose(owned, "Cancel test");
                        break;
                    case "late-owner": ownership++; break;
                    case "late-privacy": f.Main.CloseForObservedPrivacyEvent("Synthetic simulated test closure", hidePresentation: true); break;
                    case "dispose": disposing = controller.DisposeAsync().AsTask(); break;
                }
                testing.IsCompleted.Should().BeFalse();
                models.IsQuiescent.Should().BeFalse();
                if (disposing is not null) { disposing.IsCompleted.Should().BeFalse(); }
                }
                finally
                { adapter.Pending!.TrySetResult(new(ModelTurnOutcome.Succeeded, ModelProviderSelection.OllamaCandidate, new("LATE", null))); }
                await testing;
                if (disposing is not null) { await disposing; }
                next.Task.IsCompleted.Should().BeFalse("no late simulated output may become a result window");
            }
            else if (scenario is "reject" or "unavailable")
            {
                await testing;
                next.Task.IsCompleted.Should().BeFalse();
                adapter.Calls.Should().Be(scenario is "reject" ? 0 : 1);
            }
            else
            {
                var result = await Review(testing);
                result.Title.Should().Be("SIMULATED skill example result");
                var results = result.GetLogicalDescendants().OfType<TextBox>().ToArray();
                results.Should().HaveCount(3).And.OnlyContain(source => source.IsReadOnly);
                results[0].Text.Should().Contain(document.ManifestText).And.Contain(guidance).And.Contain(fixture);
                results[1].Text.Should().Be(output);
                using var assertions = JsonDocument.Parse(results[2].Text!);
                assertions.RootElement[0].GetProperty("Passed").GetBoolean().Should().Be(scenario is not "fail");
                var expectedStatus = scenario is "fail" ? "SIMULATED / FAIL" : "SIMULATED / PASS";
                result.GetLogicalDescendants().OfType<TextBlock>().Should().Contain(block => block.Text != null
                    && block.Text.Contains(expectedStatus, StringComparison.Ordinal));
                Choose(result, "Dismiss simulated result");
                await testing;
                adapter.Wire.Should().Equal(System.Text.Encoding.UTF8.GetBytes(reviewed[1].Text!));
            }
            owned.Guidance.Text.Should().Be(scenario is "late-draft" ? guidance + "EDIT" : guidance);
            (await store.List(f.Token)).Count.Should().Be(savedSource ? 1 : 0);
            document.IsAvailableForInvocation.Should().BeFalse();
            models.IsQuiescent.Should().BeTrue();
            if (savedSource)
            {
                var exactSource = (await store.Read("example", "1.0.0", f.Token))!;
                exactSource.Document.ManifestBytes.ToArray().Should().Equal(document.ManifestBytes.ToArray());
                exactSource.Document.InstructionBytes.ToArray().Should().Equal(document.InstructionBytes.ToArray());
                if (scenario is "saved-delete")
                {
                    var calls = adapter.Calls;
                    var deleting = Run("owned skill delete example 1.0.0");
                    var sourceReview = await Review(deleting);
                    sourceReview.Title.Should().Be("Review exact owned skill source deletion");
                    sourceReview.GetLogicalDescendants().OfType<TextBox>().Select(box => box.Text)
                        .Should().Equal(document.ManifestText, document.Guidance);
                    (await store.Read("example", "1.0.0", f.Token))!.IsSameRevision(exactSource).Should().BeTrue();
                    Choose(sourceReview, "Continue to separate deletion confirmation");
                    var confirmation = await Review(deleting);
                    confirmation.Title.Should().Be("Confirm exact owned skill source deletion");
                    (await store.Read("example", "1.0.0", f.Token))!.IsSameRevision(exactSource).Should().BeTrue();
                    Choose(confirmation, "Delete only this exact owned source");
                    await deleting;
                    (await store.Read("example", "1.0.0", f.Token)).Should().BeNull();
                    (await store.List(f.Token)).Should().BeEmpty();
                    owned.Saved.Items.Should().BeEmpty();
                    adapter.Calls.Should().Be(calls, "source deletion grants no model or tool invocation authority");
                    models.IsQuiescent.Should().BeTrue();
                    next.Task.IsCompleted.Should().BeFalse("deletion cannot open a simulated result or another operation");
                }
            }
        });
    }

    private sealed class SkillSimulationAdapter : IModelTurnAdapter
    {
        internal string Output = "example";
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
                : new(ModelTurnOutcome.Succeeded, provenance.Selection, new(Output, null)));
        }
    }
}
