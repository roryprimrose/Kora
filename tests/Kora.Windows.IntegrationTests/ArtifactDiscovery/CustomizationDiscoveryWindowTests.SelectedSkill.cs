using System.Text;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using AwesomeAssertions;
using Kora.Application;
using Kora.Application.Configuration;
using Kora.Application.Dependencies;
using Kora.Application.Hosting;
using Kora.Application.Skills;
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
using Kora.Windows.Skills;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests.ArtifactDiscovery;

public sealed partial class CustomizationDiscoveryWindowTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task SelectedSkillNativeSeparateReviewsRetainActualCanonicalAnswerOrRetireOnTaskEdit(bool editTask) =>
        HeadlessSession.RunAsync(async () =>
        {
            var parent = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".net-test-artifacts"));
            Directory.CreateDirectory(parent);
            using var f = new NativeUxFixtureSession(parent);
            await f.InitializeAsync();
            f.Main.BindVoiceOwnershipGate(static () => true);
            var profile = Path.Combine(f.LocalRoot, "profile");
            var store = new WindowsCustomizationStore(f, Path.Combine(f.LocalRoot, "editor"), profile);
            var source = await store.Create(CustomizationKind.Skill, CustomizationScope.User, "concise-review", f.Token);
            const string text = "---\nname: concise-review\nversion: 1.0.0\ndescription: Review prose\n---\nReview the original prose concisely.";
            var bytes = Encoding.UTF8.GetBytes(text);
            await store.Save(source, null, [new(source.PrimaryFile, bytes)], static () => true,
                operation => Task.FromResult(operation()), f.Token);
            var use = new ArtifactUsePreferences(f);
            var definition = ArtifactFileDocument.Parse(bytes, ArtifactKind.Skill, "user-profile", source.Name).Definition with { UseControlId = source.Id };
            use.SetDisabled(source.Id, false, definition.Digest);
            using var catalogue = new ArtifactCatalogue([definition], use);
            var discovery = new SharedSkillDiscoveryService(new(f), new WindowsProfileSkillReader(profile),
                f.SharedAdmission, f.Audit, NullLogger<SharedSkillDiscoveryService>.Instance, use);
            await HostRequestRunner.RunAsync(RequestOrigin.LocalUi, async () =>
            {
                await discovery.LoadSourcesAsync(static () => true, f.Token);
                await discovery.RegisterAsync(Path.GetDirectoryName(Path.GetDirectoryName(source.Location))!, static () => true, f.Token);
            });
            var metadata = new CustomizationDiscoveryService(store, catalogue, use, f.Access, TimeProvider.System, discovery);
            using var dependencies = new DependencyBootstrapper([], NullLogger<DependencyBootstrapper>.Instance);
            var recorded = new Kora.Tools.Runtime.RecordedRuntimeObservation(dependencies);
            var registry = new ReadOnlyCapabilityRegistry(f.Access, new(), new(), new(new AssemblyApplicationInfo()), new(dependencies),
                new(recorded), new(recorded), NullLogger<ReadOnlyCapabilityRegistry>.Instance);
            await using var admission = new AudioControlAdmission(f.Interactions, f.Interactions, new(f.Tasks));
            var mode = new ProviderModeConfigurationService(new LocalModelProviderModePreferences(f), admission, f.Audit);
            var transport = new SelectedSkillModel();
            await using var adapter = new BoundedModelTurnAdapter(transport, TimeProvider.System, NullLogger<BoundedModelTurnAdapter>.Instance);
            await using var host = new ModelTurnHost(f.Interactions, f.Access, f.Access, registry, f.Audit,
                NullLogger<ModelTurnHost>.Instance, TimeProvider.System,
                [new(ModelProviderSelection.OllamaCandidate, ModelQualificationGate.LocalCandidateSelection | ModelQualificationGate.IntegratedHost,
                    DateTimeOffset.UtcNow.AddMinutes(5), adapter)], mode);
            var conversation = new SessionConversationService(f.Interactions, f.Interactions, f.Sessions, f.Access, host,
                TimeProvider.System, NullLogger<SessionConversationService>.Instance, host, f.Audit);
            await using var sharedUse = new SharedSkillUseAdmission(f.Interactions, f.Interactions, new(f.Tasks));
            var workflow = new SharedSkillInvocationWorkflow(discovery, sharedUse, f.Access, f.Interactions, f.Sessions,
                conversation, f.Audit, TimeProvider.System);
            await using var controller = new CustomizationDiscoveryController(f.Main, metadata, f.Sessions, conversation,
                static () => true, static () => 1, NullLogger<CustomizationDiscoveryController>.Instance, skills: workflow);
            controller.Bind();
            CustomizationDiscoveryWindow? opened = null;
            using var observe = Window.WindowOpenedEvent.AddClassHandler<CustomizationDiscoveryWindow>((view, _) => opened = view);
            const string task = "Review this exact original prose.";
            f.Main.CommandText = "discover customizations " + task;
            await f.Main.RunTypedCommand.ExecuteAsync();
            var window = opened ?? throw new InvalidOperationException(f.Main.ResponseBody);
            async Task Click(string caption)
            {
                window.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, caption))
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await controller.WaitForOperation();
            }
            await Click("Refresh supported source choices (no definition bodies)");
            window.Sources.SelectedItem = window.Sources.ItemsSource!.Cast<object>()
                .Single(item => item.ToString()!.Contains(source.Location, StringComparison.Ordinal));
            await Click("Refresh existing Active conversations");
            window.Targets.SelectedItem = window.Targets.ItemsSource!.Cast<object>()
                .Single(item => item.ToString()!.Contains(f.LifecycleTarget!.SessionId.Value.ToString("D"), StringComparison.Ordinal));
            window.ProposeSkill.IsChecked = true;
            await Click("Read selected primary metadata and preview exact shared fields");
            window.Preview.Text.Should().Contain(task, window.Status.Text).And.NotContain(text);
            transport.Requests.Should().BeEmpty();
            await Click("Share only this reviewed metadata for ONE local answer");
            host.IsQuiescent.Should().BeTrue();
            window.Result.Text.Should().Contain("No skill guidance was read, enabled or invoked", window.Status.Text);
            window.ReadSkill.IsEnabled.Should().BeTrue(window.Status.Text);
            transport.Requests.Should().HaveCount(2);
            await Click("Authorize exact selected skill source read and revision review");
            window.SkillReview.Text.Should().Contain(text).And.Contain(task).And.Contain("LocalOnly").And.Contain("ZERO tools");
            window.EnableSkill.IsEnabled.Should().BeTrue(window.Status.Text);
            await Click("Enable only this reviewed skill revision once");
            window.PrepareSkill.IsEnabled.Should().BeTrue(window.Status.Text);
            await Click("Review frozen original task invocation");
            window.InvokeSkill.IsEnabled.Should().BeTrue(window.Status.Text);
            transport.Requests.Should().HaveCount(2);
            if (editTask)
            {
                window.Request.Text = task + " changed";
                window.InvokeSkill.IsEnabled.Should().BeFalse();
                window.SkillReview.Text.Should().BeEmpty();
                transport.Requests.Should().HaveCount(2);
            }
            else
            {
                await Click("Confirm exact selected skill for the unchanged original task");
                window.Result.Text.Should().Be("Actual skill-produced original-task answer.", window.Status.Text);
                transport.Requests.Should().HaveCount(3);
                transport.Requests[2].Context.ToolMode.Should().Be(ModelToolMode.None);
                transport.Requests[2].Context.Evidence.Single().Content.Should().Be(text);
                transport.Requests[2].Context.UserRequest.Should().Be(task);
            }
            using var read = HostActivity.BeginOperation(HostActivityLayer.Desktop, HostOperation.Storage, RequestOrigin.LocalUi);
            var retained = await conversation.ReadPageAsync(f.LifecycleTarget!.SessionId, null, 25, f.Token);
            retained.Messages.Should().HaveCount(editTask ? 1 : 2);
            retained.Messages.Should().OnlyContain(message => string.Equals(message.Body, task, StringComparison.Ordinal));
            if (!editTask)
            { retained.Messages.Should().Contain(message => message.AssistantResponse != null
                && string.Equals(message.AssistantResponse.Body, "Actual skill-produced original-task answer.", StringComparison.Ordinal)); }
            File.ReadAllBytes(source.Location).Should().Equal(bytes);
            host.IsQuiescent.Should().BeTrue();
            f.Access.SetOpen(false);
            await f.Main.PrivacyClosureTask;
            window.IsVisible.Should().BeFalse();
            window.SkillReview.Text.Should().BeEmpty();
        });

    private sealed class SelectedSkillModel : IModelToolTransport
    {
        internal List<ModelToolRequest> Requests { get; } = [];
        public Task<ModelToolTransportReply> CompleteAsync(ModelToolRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(request);
            string wire;
            if (request.Context.ToolMode == ModelToolMode.None)
            { wire = """{"kind":"answer","answer":"Actual skill-produced original-task answer."}"""; }
            else if (request.Messages.IsEmpty)
            { wire = """{"kind":"toolCalls","calls":[{"callId":"list_1","capabilityId":"customizations.list","input":{}}]}"""; }
            else
            {
                using var result = JsonDocument.Parse(((ModelToolResultMessage)request.Messages[1]).Result.AsMemory());
                var reference = result.RootElement.GetProperty("customizations").GetProperty("records")[0].GetProperty("ref").GetString();
                wire = JsonSerializer.Serialize(new { kind = "answer", answer = JsonSerializer.Serialize(new { skillRef = reference }) });
            }
            var message = ModelToolProtocol.ParseAssistant(Encoding.UTF8.GetBytes(wire));
            return Task.FromResult(new ModelToolTransportReply(ModelTurnOutcome.Succeeded, request.Provenance, message,
                new(ModelToolProtocol.SerializeRequest(request).Length, ModelToolProtocol.SerializeMessage(message).Length)));
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
