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
using Kora.Windows.IntegrationTests.Storage;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests.ArtifactDiscovery;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed class CustomizationDiscoveryWindowTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task PrivateNativeSelectionPreviewAndConfirmCompletesRealRetainedLocalDiscoveryOrFailsClosedOnSourceEdit(bool changeSource) =>
        HeadlessSession.RunAsync(async () =>
        {
            var parent = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".net-test-artifacts"));
            Directory.CreateDirectory(parent);
            var time = new InteractionStorageFixture.Clock { UseFrozenTimers = true };
            using var f = new NativeUxFixtureSession(parent, timeProvider: time);
            await f.InitializeAsync();
            f.Main.BindVoiceOwnershipGate(static () => true);
            var store = new WindowsCustomizationStore(f, Path.Combine(f.LocalRoot, "editor"), Path.Combine(f.LocalRoot, "user"));
            var source = await store.Create(CustomizationKind.Skill, CustomizationScope.Kora, "example", f.Token);
            var bytes = ArtifactFileDocument.Encode(CustomizationLayout.Template(source.Kind, source.Name) + "\nBODY-SECRET", false);
            await store.Save(source, null, [new(source.PrimaryFile, bytes)], static () => true,
                operation => Task.FromResult(operation()), f.Token);
            var binary = Path.Combine(Path.GetDirectoryName(source.Location)!, "never-read.bin");
            await File.WriteAllBytesAsync(binary, [0, 255, 0], f.Token);
            var use = new ArtifactUsePreferences(f);
            var definition = ArtifactFileDocument.Parse(bytes, ArtifactKind.Skill, "kora-profile", source.Name).Definition with { UseControlId = source.Id };
            using var catalogue = new ArtifactCatalogue([definition], use);
            var metadata = new CustomizationDiscoveryService(store, catalogue, use, f.Access, time);
            using var dependencies = new DependencyBootstrapper([], NullLogger<DependencyBootstrapper>.Instance);
            var recorded = new Kora.Tools.Runtime.RecordedRuntimeObservation(dependencies);
            var registry = new ReadOnlyCapabilityRegistry(f.Access, new(), new(), new(new AssemblyApplicationInfo()), new(dependencies),
                new(recorded), new(recorded), NullLogger<ReadOnlyCapabilityRegistry>.Instance);
            await using var admission = new AudioControlAdmission(f.Interactions, f.Interactions, new(f.Tasks));
            var mode = new ProviderModeConfigurationService(new LocalModelProviderModePreferences(f), admission, f.Audit);
            var transport = new MetadataModel();
            await using var adapter = new BoundedModelTurnAdapter(transport, time, NullLogger<BoundedModelTurnAdapter>.Instance);
            await using var host = new ModelTurnHost(f.Interactions, f.Access, f.Access, registry, f.Audit,
                NullLogger<ModelTurnHost>.Instance, time,
                [new(ModelProviderSelection.OllamaCandidate,
                    ModelQualificationGate.LocalCandidateSelection | ModelQualificationGate.IntegratedHost,
                    time.GetUtcNow().AddMinutes(5), adapter)], mode);
            var conversations = new SessionConversationService(f.Interactions, f.Interactions, f.Sessions, f.Access, host,
                time, NullLogger<SessionConversationService>.Instance, host, f.Audit);
            await using var controller = new CustomizationDiscoveryController(f.Main, metadata, f.Sessions, conversations,
                static () => true, static () => 1, NullLogger<CustomizationDiscoveryController>.Instance);
            controller.Bind();
            CustomizationDiscoveryWindow? opened = null;
            using var observe = Window.WindowOpenedEvent.AddClassHandler<CustomizationDiscoveryWindow>((view, _) => opened = view);
            const string question = "Which selected customization can help me?";
            f.Main.CommandText = "discover customizations " + question;
            await f.Main.RunTypedCommand.ExecuteAsync();
            opened.Should().NotBeNull(f.Main.ResponseBody);
            var window = opened!;
            async Task Click(string caption)
            {
                window.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, caption))
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await controller.WaitForOperation();
            }
            await Click("Refresh supported source choices (no definition bodies)");
            window.Sources.SelectedIndex.Should().Be(-1);
            window.Sources.SelectedIndex = 0;
            await Click("Refresh existing Active conversations");
            window.Targets.SelectedIndex.Should().Be(-1);
            var exactSession = f.LifecycleTarget!.SessionId.Value.ToString("D");
            window.Targets.SelectedItem = window.Targets.ItemsSource!.Cast<object>()
                .Single(item => item.ToString()!.Contains(exactSession, StringComparison.Ordinal));
            await Click("Read selected primary metadata and preview exact shared fields");
            transport.Requests.Should().BeEmpty();
            window.Preview.Text.Should().Contain(question).And.Contain("EXACT SHARED METADATA")
                .And.Contain("\"availability\":\"NotObserved\"").And.NotContain(source.Location).And.NotContain("BODY-SECRET");
            window.Confirm.IsEnabled.Should().BeTrue();
            if (changeSource) { await File.WriteAllBytesAsync(source.Location, ArtifactFileDocument.Encode("malformed source", false), f.Token); }
            await Click("Share only this reviewed metadata for ONE local answer");
            if (changeSource)
            {
                transport.Requests.Should().BeEmpty();
                window.Status.Text.Should().Contain("Selected metadata entry 1");
            }
            else
            {
                window.Result.Text.Should().Contain("Genuine metadata recommendation", window.Status.Text + "; model requests: " + transport.Requests.Count)
                    .And.Contain("1 of 1 reviewed metadata entries served");
                transport.Requests.Should().HaveCount(3);
                using var read = HostActivity.BeginOperation(HostActivityLayer.Desktop, HostOperation.Storage, RequestOrigin.LocalUi);
                var sessions = await f.Sessions.ReadMetadataAsync(null, 25, f.Token);
                var messages = new List<Kora.Core.Conversation.ConversationMessage>();
                foreach (var entry in sessions.Records)
                { messages.AddRange((await conversations.ReadPageAsync(entry.Authority.SessionId, null, 25, f.Token)).Messages); }
                messages.Should().ContainSingle(message => string.Equals(message.Body, question, StringComparison.Ordinal));
                messages.Single(message => string.Equals(message.Body, question, StringComparison.Ordinal)).AssistantResponse!.Body
                    .Should().Contain("Genuine metadata recommendation");
                (await File.ReadAllBytesAsync(source.Location, f.Token)).Should().Equal(bytes);
            }
            use.IsDisabled(source.Id).Should().BeFalse();
            (await File.ReadAllBytesAsync(binary, f.Token)).Should().Equal(0, 255, 0);
            host.IsQuiescent.Should().BeTrue();
            f.Access.SetOpen(false);
            await f.Main.PrivacyClosureTask;
            window.IsVisible.Should().BeFalse();
        });

    private sealed class MetadataModel : IModelToolTransport
    {
        internal List<ModelToolRequest> Requests { get; } = [];
        public Task<ModelToolTransportReply> CompleteAsync(ModelToolRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(request);
            var wire = """{"kind":"toolCalls","calls":[{"callId":"list_1","capabilityId":"customizations.list","input":{}}]}""";
            if (Requests.Count == 2)
            {
                var result = (ModelToolResultMessage)request.Messages[1];
                using var document = JsonDocument.Parse(result.Result.AsMemory());
                var reference = document.RootElement.GetProperty("customizations").GetProperty("records")[0].GetProperty("ref").GetString();
                wire = $$$"""{"kind":"toolCalls","calls":[{"callId":"get_1","capabilityId":"customizations.get","input":{"ref":"{{{reference}}}"}}]}""";
            }
            else if (Requests.Count == 3) { wire = """{"kind":"answer","answer":"Genuine metadata recommendation: the selected skill may help; this is not execution."}"""; }
            var message = ModelToolProtocol.ParseAssistant(Encoding.UTF8.GetBytes(wire));
            return Task.FromResult(new ModelToolTransportReply(ModelTurnOutcome.Succeeded, request.Provenance, message,
                new(ModelToolProtocol.SerializeRequest(request).Length, ModelToolProtocol.SerializeMessage(message).Length)));
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
