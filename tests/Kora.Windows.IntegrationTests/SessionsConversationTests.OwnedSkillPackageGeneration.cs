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
    [InlineData(1)]
    [InlineData(4)]
    public async Task GeneratedFixedPackageCompletesNativeReviewDisabledSaveReopenAndSeparateCanonicalAnswer(int count)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            var answer = new HistoryAdapter();
            await using var composed = new Composition(f, adapter: answer);
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
            var generator = new PackageGenerationAdapter();
            await using var models = new ModelTurnHost(native.Interactions, native.Access, native.Access, registry, native.Audit,
                NullLogger<ModelTurnHost>.Instance, TimeProvider.System,
                [new(ModelProviderSelection.OllamaCandidate, ModelQualificationGate.LocalCandidateSelection | ModelQualificationGate.IntegratedHost,
                    DateTimeOffset.UtcNow.AddMinutes(5), generator)], policy);
            await using var runner = new LocalContextTurnWorkflow(native.Interactions, native.Interactions, native.Sessions, new(native.Tasks),
                native.Access, models, models, native.Audit, NullLogger<LocalContextTurnWorkflow>.Instance);
            var refinement = new OwnedSkillGuidanceRefinementService(store, admission, save, runner, TimeProvider.System);
            var invocation = new OwnedSkillInvocationWorkflow(store, admission, composed.Access, f.Store, composed.Workspace,
                composed.Conversation, native.Audit, f.Time, new MarkdownUsePolicy());
            await using var use = new OwnedSkillInvocationController(native.Main, invocation, static () => true, static () => 1,
                NullLogger<OwnedSkillInvocationController>.Instance);
            use.Bind();
            await using var editor = new OwnedSkillEditorController(native.Main, save, admission,
                static () => true, static () => 1, NullLogger<OwnedSkillEditorController>.Instance, use, refinement);
            editor.Bind();
            OwnedSkillEditorWindow? editing = null;
            OwnedSkillInvocationWindow? invoking = null;
            OwnedDeclarativeSkillDocument? original = null;
            var reviews = new List<string>();
            using var opened = Window.WindowOpenedEvent.AddClassHandler<Window>((window, _) =>
            {
                if (window is OwnedSkillEditorWindow authoring) { editing = authoring; }
                else if (window is OwnedSkillInvocationWindow usingSkill) { invoking = usingSkill; }
                else if (window.Title is "Review exact owned component declaration")
                {
                    var source = window.GetLogicalDescendants().OfType<TextBox>().ToArray();
                    source[0].Text.Should().Be(original!.ManifestText);
                    source[1].Text.Should().Be(PackageGenerationAdapter.Body).And.NotContain("Original component");
                    var consent = window.GetLogicalDescendants().OfType<Button>().First();
                    Avalonia.Threading.Dispatcher.UIThread.Post(() => consent.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)));
                }
                else if (window.Title is "Review initial skill generation disclosure" or "Review complete generated skill source"
                    or "Review complete skill package" or "Confirm exact new skill publication")
                {
                    reviews.Add(window.Title);
                    var controls = window.GetLogicalDescendants().ToArray();
                    var source = controls.OfType<TextBox>().Select(box => box.Text).ToArray();
                    var description = string.Join("\n", controls.OfType<TextBlock>().Select(block => block.Text));
                    if (window.Title is "Review initial skill generation disclosure")
                    {
                        description.Should().Contain(original!.CompleteText).And.Contain(store.Destination(original))
                            .And.Contain("explains original prose").And.Contain("Exact supplied prose").And.Contain("Reusable explanation");
                        source.Should().Equal(original.CompleteText, original.CompleteText);
                        generator.Calls.Should().Be(0);
                    }
                    else if (window.Title is "Review complete generated skill source")
                    {
                        source.Should().Equal(original!.CompleteText, Proposed().CompleteText);
                    }
                    else
                    {
                        source.Last().Should().Contain(Proposed().CompleteText);
                    }
                    controls.OfType<TextBox>().Should().OnlyContain(box => box.IsReadOnly);
                    answer.Calls.Should().Be(0);
                    f.Count("conversation_messages").Should().Be(0);
                    var accept = controls.OfType<Button>().First();
                    Avalonia.Threading.Dispatcher.UIThread.Post(() => accept.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)));
                }
            });
            async Task Command(string text) { native.Main.CommandText = text; await native.Main.RunTypedCommand.ExecuteAsync(); }
            await Command("create a skill that explains original prose");
            editing!.IdInput.Text = "example";
            await Command("owned skill name: Example");
            await Command("owned skill description: Description");
            await Command("owned skill expected-input: Exact supplied prose");
            await Command("owned skill expected-output: Reusable explanation");
            await Command("owned skill guidance: Original primary.");
            await Command("owned skill format markdown-package");
            for (var index = 0; index < count; index++)
            {
                await Command($@"owned skill reference references\notes{index}.md: Original component {index}" + "\r\ne\u0301 ${input:literal}");
            }
            original = editing.Document();
            await Command("owned skill generate");
            reviews.Should().Equal("Review initial skill generation disclosure", "Review complete generated skill source");
            editing.Document().RevisionDigest.Should().Be(original.RevisionDigest, "adoption is volatile, not a draft edit");
            (await store.List(native.Token)).Should().BeEmpty();
            answer.Calls.Should().Be(0);
            generator.Calls.Should().Be(1);
            await Command("owned skill validate");
            await Command("owned skill save");
            reviews.Should().Equal("Review initial skill generation disclosure", "Review complete generated skill source",
                "Review complete skill package", "Confirm exact new skill publication");
            var restartedStore = new WindowsOwnedDeclarativeSkillStore(native);
            var saved = (await restartedStore.Read("example", "1.0.0", native.Token))!;
            saved.Document.CompleteText.Should().Be(Proposed().CompleteText);
            saved.Document.IsAvailableForInvocation.Should().BeFalse();
            await Command("owned skill reopen example 1.0.0");
            editing.Document().CompleteText.Should().Be(saved.Document.CompleteText);
            await Command("owned skill use example 1.0.0");
            invoking!.Instructions.Text.Should().NotContain("Original component");
            await Command("owned skill read-references");
            invoking.Instructions.Text.Should().Contain(saved.Document.CompleteText);
            await Command("owned skill enable");
            await Command("owned skill targets");
            invoking.Targets.SelectedIndex = 0;
            const string request = "  Original unchanged task e\u0301  ";
            await Command("owned skill request: " + request);
            await Command("owned skill review-use");
            invoking.Confirm.IsEnabled.Should().BeTrue();
            answer.Calls.Should().Be(0);
            var previewText = invoking.Confirmation.Text!;
            using (var preview = JsonDocument.Parse(previewText[previewText.IndexOf('{')..]))
            {
                preview.RootElement.GetProperty("UserRequest").GetString().Should().Be(request);
                preview.RootElement.GetProperty("Evidence").GetArrayLength().Should().Be(count + 2);
                preview.RootElement.GetProperty("Tools").GetArrayLength().Should().Be(0);
            }
            await Command("owned skill invoke");
            answer.Calls.Should().Be(1);
            generator.Calls.Should().Be(1);
            var retained = (await f.Store.ReadConversationPageAsync(f.Request.SessionId, null, 25, f.Token)).Messages.Single();
            retained.Request.SessionId.Should().Be(f.Request.SessionId);
            retained.Body.Should().Be(request);
            retained.AssistantResponse!.Body.Should().Be("earlier ASSISTANT");
            using var wire = JsonDocument.Parse(answer.Wire!);
            wire.RootElement.GetProperty("Tools").GetArrayLength().Should().Be(0);
            wire.RootElement.GetProperty("ToolMode").GetInt32().Should().Be((int)ModelToolMode.None);
            var evidence = wire.RootElement.GetProperty("Evidence");
            evidence.GetArrayLength().Should().Be(count + 2);
            evidence[0].GetProperty("Content").GetString().Should().Be(original.ManifestText);
            evidence[1].GetProperty("Content").GetString().Should().Be(PackageGenerationAdapter.Body);
            for (var index = 0; index < count; index++)
            {
                using var component = JsonDocument.Parse(evidence[index + 2].GetProperty("Content").GetString()!);
                component.RootElement.GetProperty("RelativePath").GetString().Should().Be(original.References[index].RelativePath);
                component.RootElement.GetProperty("Content").GetString().Should().Be(original.References[index].Text);
                evidence[index + 2].GetProperty("Disclosure").GetInt32().Should().Be((int)ModelEvidenceDisclosure.LocalOnly);
            }
            await Command("owned skill invoke");
            answer.Calls.Should().Be(1, "reviewed use is one-shot");
            await Command("owned skill close-use");
            (await editor.PrepareClose()).Should().BeTrue();

            OwnedDeclarativeSkillDocument Proposed() => OwnedDeclarativeSkillDocument.Parse(original!.ManifestBytes.Span,
                Kora.Core.Artifacts.ArtifactFileDocument.Encode(PackageGenerationAdapter.Body, false),
                original.References.ToDictionary(item => item.RelativePath, item => item.Bytes.ToArray(), StringComparer.Ordinal));
        });
    }

    private sealed class PackageGenerationAdapter : IModelTurnAdapter
    {
        internal const string Body = "Explain original prose with [notes](references\\notes0.md).";
        internal int Calls;
        public Task<ModelProviderReply> RunAsync(ModelTurnProvenance provenance, Kora.Core.Providers.ProviderProfileTuple? profile,
            ReadOnlyMemory<byte> context, Func<string, string, CancellationToken, ValueTask<CapabilityReply>> invokeTool, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Calls++;
            using var wire = JsonDocument.Parse(context);
            wire.RootElement.GetProperty("UserRequest").GetString().Should().Be("explains original prose");
            wire.RootElement.GetProperty("Tools").GetArrayLength().Should().Be(0);
            wire.RootElement.GetProperty("ToolMode").GetInt32().Should().Be((int)ModelToolMode.None);
            using var evidence = JsonDocument.Parse(wire.RootElement.GetProperty("Evidence")[0].GetProperty("Content").GetString()!);
            evidence.RootElement.GetProperty("manifest").GetString().Should().Contain("kora-skill-format: 5\n");
            evidence.RootElement.GetProperty("components").EnumerateArray().Should().NotBeEmpty();
            return Task.FromResult(new ModelProviderReply(ModelTurnOutcome.Succeeded, provenance.Selection,
                new(JsonSerializer.Serialize(new { body = Body }), null)));
        }
    }
}
