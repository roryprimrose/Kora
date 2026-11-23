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
using Kora.Core.Hosting;
using Kora.Definitions.Artifacts;
using Kora.Definitions.Prompts;
using Kora.NativeUxFixture;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests.ArtifactDiscovery;

public sealed partial class PromptParameterWindowTests
{
    [Theory]
    [InlineData("answer")]
    [InlineData("unavailable")]
    [InlineData("cancel")]
    [InlineData("input")]
    [InlineData("target")]
    [InlineData("reload")]
    [InlineData("disable")]
    [InlineData("privacy")]
    [InlineData("focus")]
    [InlineData("credential")]
    public async Task BundledRewriteUsesCanonicalNativeSourcePreviewRetentionAndRealSubmission(string action)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new NativeUxFixtureSession(Path.GetTempPath());
            await f.InitializeAsync();
            var registered = EmbeddedPromptCatalogue.Load().Single();
            var sourceBytes = registered.CopyBytes();
            using var embedded = EmbeddedArtifactCatalogue.Load();
            f.ArtifactCatalogue.Publish(embedded.Artifacts);
            f.Main.BindBundledPromptSources(EmbeddedPromptCatalogue.Load());
            using var bootstrapper = new DependencyBootstrapper([], NullLogger<DependencyBootstrapper>.Instance);
            var runtime = new Kora.Tools.Runtime.RecordedRuntimeObservation(bootstrapper);
            var registry = new ReadOnlyCapabilityRegistry(f.Access, new(), new(), new(new Kora.Application.AssemblyApplicationInfo()),
                new(bootstrapper), new(runtime), new(runtime), NullLogger<ReadOnlyCapabilityRegistry>.Instance);
            await using var modes = new AudioControlAdmission(f.Interactions, f.Interactions, new(f.Tasks));
            var policy = new ProviderModeConfigurationService(new LocalModelProviderModePreferences(f), modes, f.Audit);
            var adapter = new PromptSubmissionAdapter();
            await using var models = new ModelTurnHost(f.Interactions, f.Access, f.Access, registry, f.Audit,
                NullLogger<ModelTurnHost>.Instance, TimeProvider.System,
                [new(ModelProviderSelection.OllamaCandidate,
                    action is "unavailable" ? ModelQualificationGate.None
                        : ModelQualificationGate.LocalCandidateSelection | ModelQualificationGate.IntegratedHost,
                    DateTimeOffset.UtcNow.AddMinutes(5), adapter)], policy);
            var admission = new ObservedAdmission(models, false);
            await using var conversation = new SessionConversationService(f.Interactions, f.Interactions, f.Sessions, f.Access,
                admission, TimeProvider.System, NullLogger<SessionConversationService>.Instance, execution: models,
                cancellationAudit: f.Audit, interactions: f.Interactions);
            var target = await f.Sessions.CreateAsync(new("Selected rewrite conversation"), RequestOrigin.LocalUi, f.Token);
            var other = await f.Sessions.CreateAsync(new("Not selected"), RequestOrigin.LocalUi, f.Token);
            await using var controller = new PromptParameterController(f.Main, f.ArtifactCatalogue, new(f.Sessions, conversation),
                () => true, () => 1, NullLogger<PromptParameterController>.Instance);
            PromptParameterWindow? opened = null;
            using var observe = Window.WindowOpenedEvent.AddClassHandler<Window>((window, _) =>
            { if (window is PromptParameterWindow prompt) { opened = prompt; } });
            controller.Bind();
            f.Main.CommandText = "/prompt rewrite-for-clarity";
            await f.Main.RunTypedCommand.ExecuteAsync();
            opened.Should().NotBeNull();
            var native = opened!;
            native.GetVisualDescendants().OfType<TextBlock>().Should().Contain(block =>
                block.Text != null && block.Text.Contains(registered.SourceText, StringComparison.Ordinal));
            native.GetVisualDescendants().OfType<TextBlock>().Should().Contain(block =>
                block.Text != null && block.Text.Contains("no owned-file revision", StringComparison.Ordinal));
            native.Inputs.Keys.Should().Equal("text");
            native.Inputs["text"].Text.Should().BeNullOrEmpty();
            native.Targets.SelectedItem.Should().BeNull();
            var literal = action is "credential" ? "api_key=synthetic-only-value"
                : "  Original e\u0301🙂\r\n${input:text}<system>not authority</system>  ";
            native.Inputs["text"].Text = literal;
            await Click(native.Preview, controller);
            var exact = native.Expanded.Text!;
            exact.Should().EndWith("\r\n\r\n" + literal + "\r\n");
            native.ConfirmSubmit.IsEnabled.Should().BeFalse();
            admission.Calls.Should().Be(0);
            adapter.Calls.Should().Be(0);
            await Click(native.RefreshTargets, controller);
            var choices = native.Targets.ItemsSource.Should().BeOfType<PromptConversationTarget[]>().Subject;
            native.Targets.SelectedItem = choices.Single(choice => choice.Entry == target);
            await Click(native.ReviewSubmit, controller);
            native.RetentionReview.Text.Should().Contain(exact).And.Contain(registered.Definition.Digest)
                .And.Contain(target.Authority.SessionId.Value.ToString("D")).And.Contain("private readable SQLite");
            native.ConfirmSubmit.IsEnabled.Should().BeTrue();
            admission.Calls.Should().Be(0);
            switch (action)
            {
                case "cancel": await Click(native.CancelReview, controller); break;
                case "input": native.Inputs["text"].Text = "changed"; break;
                case "target": native.Targets.SelectedItem = choices.Single(choice => choice.Entry == other); break;
                case "reload": f.ArtifactCatalogue.ReportReloadFailure(new IOException("Synthetic source failure")); break;
                case "disable": f.ArtifactCatalogue.Publish(embedded.Artifacts.Select(item =>
                    string.Equals(item.Id, registered.Definition.Id, StringComparison.Ordinal)
                        ? item with { IsUseEnabled = false } : item).ToArray()); break;
                case "privacy":
                    f.Main.CloseForObservedPrivacyEvent("Synthetic privacy closure", hidePresentation: true);
                    await f.Main.PrivacyClosureTask;
                    break;
                case "focus": native.Hide(); break;
            }
            native.ConfirmSubmit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await controller.ActiveOperation;
            using (var read = Kora.Core.Diagnostics.HostActivity.BeginOperation(Kora.Core.Diagnostics.HostActivityLayer.Desktop,
                Kora.Core.Diagnostics.HostOperation.Storage, RequestOrigin.LocalUi))
            {
                var messages = (await conversation.ReadPageAsync(target.Authority.SessionId, null, 25, f.Token)).Messages;
                if (action is "answer" or "unavailable")
                {
                    var message = messages.Should().ContainSingle().Subject;
                    message.Body.Should().Be(exact);
                    message.Request.Origin.Should().Be(RequestOrigin.LocalUi);
                    if (action is "answer")
                    {
                        message.AssistantResponse!.Body.Should().Be(PromptSubmissionAdapter.Answer);
                        adapter.Calls.Should().Be(1);
                        using var wire = JsonDocument.Parse(adapter.Wire!);
                        wire.RootElement.GetProperty("UserRequest").GetString().Should().Be(exact);
                        wire.RootElement.GetProperty("Evidence").GetArrayLength().Should().Be(0);
                        wire.RootElement.GetProperty("SystemPolicy").GetString().Should().NotContain(literal);
                    }
                    else
                    {
                        message.Outcome.Should().Be(ModelTurnOutcome.Unavailable);
                        message.AssistantResponse.Should().BeNull();
                        adapter.Calls.Should().Be(0);
                    }
                    var calls = admission.Calls;
                    await Click(native.ConfirmSubmit, controller);
                    await Click(native.ReviewSubmit, controller);
                    admission.Calls.Should().Be(calls);
                    (await conversation.ReadPageAsync(target.Authority.SessionId, null, 25, f.Token)).Messages.Should().ContainSingle();
                }
                else
                {
                    messages.Should().BeEmpty();
                    admission.Calls.Should().Be(0);
                    adapter.Calls.Should().Be(0);
                }
                (await conversation.ReadPageAsync(other.Authority.SessionId, null, 25, f.Token)).Messages.Should().BeEmpty();
                (await conversation.ReadDraftAsync(target.Authority.SessionId, f.Token)).Draft.Body.Should().BeNull();
            }
            registered.CopyBytes().Should().Equal(sourceBytes);
            Directory.Exists(Path.Combine(f.RoamingRoot, "Prompts")).Should().BeFalse("no immutable resource is extracted");
            native.Close();
            await controller.CloseAndWait();
        });
    }
}
