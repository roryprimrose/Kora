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
using Kora.Windows.IntegrationTests.Storage;
using Kora.Windows.Skills;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionsConversationTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task RequiredLiteralSkillNativeCreateSaveReopenReviewAndConfirmCompletesCanonicalAnswer(bool editAfterPreview, bool generate)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            var adapter = new HistoryAdapter();
            await using var composed = new Composition(f, adapter: adapter);
            var parent = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".net-test-artifacts"));
            Directory.CreateDirectory(parent);
            using var native = new NativeUxFixtureSession(parent);
            await native.InitializeAsync();
            var store = new WindowsOwnedDeclarativeSkillStore(native);
            await using var admission = new OwnedSkillControlAdmission(native.Interactions, native.Interactions, new(native.Tasks));
            var save = new OwnedSkillSaveWorkflow(store, admission, native.Audit);
            using var bootstrapper = new DependencyBootstrapper([], NullLogger<DependencyBootstrapper>.Instance);
            var runtime = new Kora.Tools.Runtime.RecordedRuntimeObservation(bootstrapper);
            var registry = new ReadOnlyCapabilityRegistry(native.Access, new(), new(), new(new Kora.Application.AssemblyApplicationInfo()),
                new(bootstrapper), new(runtime), new(runtime), NullLogger<ReadOnlyCapabilityRegistry>.Instance);
            await using var modes = new AudioControlAdmission(native.Interactions, native.Interactions, new(native.Tasks));
            var policy = new ProviderModeConfigurationService(new LocalModelProviderModePreferences(native), modes, native.Audit);
            var generationAdapter = new LiteralGenerationAdapter();
            await using var models = new ModelTurnHost(native.Interactions, native.Access, native.Access, registry, native.Audit,
                NullLogger<ModelTurnHost>.Instance, TimeProvider.System,
                [new(ModelProviderSelection.OllamaCandidate, ModelQualificationGate.LocalCandidateSelection | ModelQualificationGate.IntegratedHost,
                    DateTimeOffset.UtcNow.AddMinutes(5), generationAdapter)], policy);
            await using var runner = new LocalContextTurnWorkflow(native.Interactions, native.Interactions, native.Sessions, new(native.Tasks),
                native.Access, models, models, native.Audit, NullLogger<LocalContextTurnWorkflow>.Instance);
            var refinement = new OwnedSkillGuidanceRefinementService(store, admission, save, runner, TimeProvider.System);
            var workflow = new OwnedSkillInvocationWorkflow(store, admission, composed.Access, f.Store, composed.Workspace,
                composed.Conversation, native.Audit, f.Time, new MarkdownUsePolicy());
            await using var use = new OwnedSkillInvocationController(native.Main, workflow, static () => true, static () => 1,
                NullLogger<OwnedSkillInvocationController>.Instance);
            use.Bind();
            await using var editor = new OwnedSkillEditorController(native.Main, save, admission,
                static () => true, static () => 1, NullLogger<OwnedSkillEditorController>.Instance, use, refinement);
            editor.Bind();
            OwnedSkillEditorWindow? editing = null;
            OwnedSkillInvocationWindow? invoking = null;
            using var editOpened = Window.WindowOpenedEvent.AddClassHandler<OwnedSkillEditorWindow>((window, _) => editing = window);
            using var useOpened = Window.WindowOpenedEvent.AddClassHandler<OwnedSkillInvocationWindow>((window, _) => invoking = window);
            var publicationReviews = new List<string>();
            var generationReviews = new List<string>();
            using var publication = Window.WindowOpenedEvent.AddClassHandler<Window>((window, _) =>
            {
                if (window.Title is "Review initial skill generation disclosure" or "Review complete generated skill source")
                {
                    generationReviews.Add(window.Title);
                    var proposalControls = window.GetLogicalDescendants().ToArray();
                    string.Join("\n", proposalControls.OfType<TextBlock>().Select(block => block.Text)).Should().Contain("native v3")
                        .And.Contain("FROZEN REQUIRED INPUT NAMES (FIRST-USE ORDER)\ntopic");
                    if (window.Title is "Review complete generated skill source")
                    {
                        proposalControls.OfType<TextBox>().First().Text.Should().Contain("kora-skill-format: 3\n");
                        proposalControls.OfType<TextBox>().Last().Text.Should().Be("Compare ${input:topic} and ${input:topic}.");
                    }
                    adapter.Calls.Should().Be(0);
                    f.Count("conversation_messages").Should().Be(0);
                    var acceptProposal = proposalControls.OfType<Button>().First();
                    Avalonia.Threading.Dispatcher.UIThread.Post(() => acceptProposal.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)));
                    return;
                }
                if (window.Title is not ("Review complete skill package" or "Confirm exact new skill publication")) { return; }
                publicationReviews.Add(window.Title);
                var controls = window.GetLogicalDescendants().ToArray();
                var sources = controls.OfType<TextBox>().ToArray();
                sources[0].Text.Should().Contain("kora-skill-format: 3\n").And.Contain("inputs: required-literal\n");
                sources[1].Text.Should().Be("Compare ${input:topic} and ${input:topic}.");
                var accept = controls.OfType<Button>().First();
                Avalonia.Threading.Dispatcher.UIThread.Post(() => accept.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)));
            });
            async Task Command(string command)
            { native.Main.CommandText = command; await native.Main.RunTypedCommand.ExecuteAsync(); }
            if (generate)
            {
                await Command("create a skill that compares a topic");
                await Command("owned skill expected-input: A topic, not a model-selected schema");
                await Command("owned skill expected-output: Reusable comparison guidance");
                editing!.IdInput.Text = "example";
                editing.VersionInput.Text = "1.0.0";
            }
            else { await Command("owned skill create example 1.0.0"); }
            await Command("owned skill name: Example");
            await Command("owned skill description: Description");
            await Command(generate ? "owned skill guidance: ${input:topic}" : "owned skill guidance: Compare ${input:topic} and ${input:topic}.");
            await Command("owned skill inputs required-literal");
            if (generate)
            {
                await Command("owned skill generate");
                generationReviews.Should().Equal("Review initial skill generation disclosure", "Review complete generated skill source");
                generationAdapter.Calls.Should().Be(1);
                models.IsQuiescent.Should().BeTrue();
                (await store.List(native.Token)).Should().BeEmpty();
                editing!.Guidance.Text.Should().Be("${input:topic}", "review stages a volatile proposal, not a draft edit");
            }
            await Command("owned skill validate");
            await Command("owned skill save");
            generationAdapter.Calls.Should().Be(generate ? 1 : 0, "Save cannot generate, enable or invoke");
            publicationReviews.Should().Equal("Review complete skill package", "Confirm exact new skill publication");
            var saved = (await store.Read("example", "1.0.0", native.Token))!;
            saved.Document.InputTemplate!.RequiredNames.Should().Equal("topic");
            saved.Document.IsAvailableForInvocation.Should().BeFalse();
            await Command("owned skill reopen example 1.0.0");
            editing!.InputFormat.SelectedIndex.Should().Be(1);
            editing.Document().RevisionDigest.Should().Be(saved.Document.RevisionDigest);
            await Command("owned skill use example 1.0.0");
            var opened = invoking ?? throw new InvalidOperationException("No canonical owned skill use window.");
            opened.LiteralInputs.Inputs.Keys.Should().Equal("topic");
            opened.LiteralInputs.Inputs["topic"].IsUndoEnabled.Should().BeFalse();
            await Command("owned skill enable");
            await Command("owned skill targets");
            opened.Targets.SelectedIndex = 0;
            const string original = "  Original user task e\u0301  ";
            const string value = "  literal ${input:never-expanded} e\u0301  ";
            await Command("owned skill request: " + original);
            await Command("owned skill input topic: " + value);
            opened.LiteralInputs.Inputs["topic"].Text.Should().Be(value);
            await Command("owned skill review-use");
            opened.Confirm.IsEnabled.Should().BeTrue();
            using (var preview = JsonDocument.Parse(opened.Confirmation.Text![opened.Confirmation.Text!.IndexOf('{')..]))
            {
                preview.RootElement.GetProperty("UserRequest").GetString().Should().Be(original);
                var evidence = preview.RootElement.GetProperty("Evidence");
                evidence.GetArrayLength().Should().Be(3);
                evidence[1].GetProperty("Content").GetString().Should().Be(saved.Document.Guidance);
                using var inputs = JsonDocument.Parse(evidence[2].GetProperty("Content").GetString()!);
                inputs.RootElement.GetProperty("RequiredLiteralInputs").GetProperty("topic").GetString().Should().Be(value);
                inputs.RootElement.GetProperty("ExpandedGuidance").GetString().Should().Be($"Compare {value} and {value}.");
            }
            adapter.Calls.Should().Be(0);
            f.Count("conversation_messages").Should().Be(0);
            if (editAfterPreview)
            {
                opened.LiteralInputs.Inputs["topic"].Text = "new unreviewed value";
                opened.Confirm.IsEnabled.Should().BeFalse();
                await Command("owned skill invoke");
                adapter.Calls.Should().Be(0);
                f.Count("conversation_messages").Should().Be(0);
            }
            else
            {
                opened.Confirm.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await use.ActiveOperation;
                adapter.Calls.Should().Be(1);
                var retained = (await f.Store.ReadConversationPageAsync(f.Request.SessionId, null, 25, f.Token)).Messages.Single();
                retained.Body.Should().Be(original);
                retained.AssistantResponse!.Body.Should().Be("earlier ASSISTANT");
                retained.Request.SessionId.Should().Be(f.Request.SessionId);
            }
            await Command("owned skill close-use");
            opened.LiteralInputs.Inputs.Should().BeEmpty();
            opened.Instructions.Text.Should().BeEmpty();
            opened.Confirmation.Text.Should().BeEmpty();
            (await editor.PrepareClose()).Should().BeTrue();
        });
    }

    private sealed class LiteralGenerationAdapter : IModelTurnAdapter
    {
        internal int Calls;
        public Task<ModelProviderReply> RunAsync(ModelTurnProvenance provenance, Kora.Core.Providers.ProviderProfileTuple? profile,
            ReadOnlyMemory<byte> context, Func<string, string, CancellationToken, ValueTask<CapabilityReply>> invokeTool, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Calls++;
            using var wire = JsonDocument.Parse(context);
            wire.RootElement.GetProperty("UserRequest").GetString().Should().Be("compares a topic");
            wire.RootElement.GetProperty("Tools").GetArrayLength().Should().Be(0);
            wire.RootElement.GetProperty("ToolMode").GetInt32().Should().Be((int)ModelToolMode.None);
            using var evidence = JsonDocument.Parse(wire.RootElement.GetProperty("Evidence")[0].GetProperty("Content").GetString()!);
            evidence.RootElement.GetProperty("requiredInputs").EnumerateArray().Select(name => name.GetString()).Should().Equal("topic");
            return Task.FromResult(new ModelProviderReply(ModelTurnOutcome.Succeeded, provenance.Selection,
                new(JsonSerializer.Serialize(new { body = "Compare ${input:topic} and ${input:topic}." }), null)));
        }
    }
}
