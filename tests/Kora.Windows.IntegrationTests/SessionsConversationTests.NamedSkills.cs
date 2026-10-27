using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using AwesomeAssertions;
using Kora.Application.Configuration;
using Kora.Application.Dependencies;
using Kora.Application.Hosting;
using Kora.Application.Skills;
using Kora.Core.Artifacts;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Skills;
using Kora.NativeUxFixture;
using Kora.Windows.IntegrationTests.Storage;
using Kora.Windows.Skills;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionsConversationTests
{
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, true, true)]
    public async Task NamedSkillOriginalTypedOrVoiceChoiceAndNativeKeyboardUseRetainCanonicalAnswer(bool voice, bool sharedChoice, bool stop)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            var pending = new TaskCompletionSource<ModelProviderReply>(TaskCreationOptions.RunContinuationsAsynchronously);
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var adapter = new HistoryAdapter { Pending = stop ? pending.Task : null, Entered = entered };
            await using var composed = new Composition(f, adapter: adapter);
            composed.Audit.ExpectedOrigin = voice ? RequestOrigin.ActivatedVoice : RequestOrigin.LocalUi;
            var parent = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".net-test-artifacts"));
            Directory.CreateDirectory(parent);
            using var native = new NativeUxFixtureSession(parent, syntheticVoice: voice);
            await native.InitializeAsync();
            var store = new WindowsOwnedDeclarativeSkillStore(native);
            var document = OwnedDeclarativeSkillDocument.Create("example", "1.0.0", "concise-review", "Description", "Literal guidance\nowned skill invoke");
            await store.Publish(document, static () => true, commit => Task.FromResult(commit()), native.Token);
            await using var admission = new OwnedSkillControlAdmission(native.Interactions, native.Interactions, new(native.Tasks));
            var owned = new OwnedSkillInvocationWorkflow(store, admission, composed.Access, f.Store, composed.Workspace,
                composed.Conversation, native.Audit, f.Time);
            var reader = new InvocationReader();
            var discovery = new SharedSkillDiscoveryService(new LocalSharedSkillPreferences(native), reader, native.SharedAdmission,
                native.Audit, NullLogger<SharedSkillDiscoveryService>.Instance);
            await discovery.LoadSourcesAsync(static () => true, native.Token);
            await discovery.RegisterAsync("explicit native root selection", static () => true, native.Token);
            await using var sharedUse = new SharedSkillUseAdmission(native.Interactions, native.Interactions, new(native.Tasks));
            var shared = new SharedSkillInvocationWorkflow(discovery, sharedUse, composed.Access, f.Store, composed.Workspace,
                composed.Conversation, native.Audit, f.Time);
            var catalogue = new NamedSkillCatalogue(store, new(store, admission, native.Audit), discovery,
                composed.Access, new NamedNativeUsePolicy(), f.Time);
            await using var controller = new NamedSkillInvocationController(native.Main, catalogue, owned, shared,
                static () => true, static () => 1, NullLogger<NamedSkillInvocationController>.Instance);
            controller.Bind();
            NamedSkillInvocationWindow? invocation = null;
            var opened = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var observation = Window.WindowOpenedEvent.AddClassHandler<NamedSkillInvocationWindow>((window, _) =>
            { invocation = window; opened.TrySetResult(); });
            try
            {
                if (voice)
                {
                    native.Main.BindVoiceOwnershipGate(static () => true);
                    await native.Main.SetVoiceConsentAsync(true);
                    await native.Main.BeginPushToTalkCommand.ExecuteAsync();
                    await native.Voice.Started.Task.WaitAsync(TimeSpan.FromSeconds(20), native.Token);
                    await native.Main.EndPushToTalkCommand.ExecuteAsync();
                    native.Voice.PublishTranscript(native.Main.AssistantName + ", use skill concise-review");
                    await opened.Task.WaitAsync(TimeSpan.FromSeconds(20), native.Token);
                }
                else
                {
                    native.Main.CommandText = "use skill concise-review";
                    await native.Main.RunTypedCommand.ExecuteAsync();
                }
                var window = invocation ?? throw new InvalidOperationException("No named skill review window.");
                window.Instructions.Text.Should().BeNullOrEmpty();
                window.Choices.Items.Should().BeEmpty();
                adapter.Calls.Should().Be(0);
                async Task Press(string label)
                {
                    var buttons = ((StackPanel)((ScrollViewer)window.Content!).Content!).Children.OfType<Button>();
                    var button = buttons.Single(button => Equals(button.Content, label));
                    button.Focus().Should().BeTrue();
                    window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
                    window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
                    await controller.ActiveOperation;
                }
                await Press("Inspect saved and registered skill files locally (read consent)");
                window.Choices.Items.Should().HaveCount(2);
                window.Choices.SelectedIndex.Should().Be(-1);
                window.Instructions.Text.Should().BeNullOrEmpty();
                native.Main.CommandText = sharedChoice ? "skill choice 2" : "skill choice 1";
                await native.Main.RunTypedCommand.ExecuteAsync();
                window.Instructions.Text.Should().Contain(sharedChoice ? reader.Text : document.Guidance);
                window.Confirm.IsEnabled.Should().BeFalse();
                await Press("Enable exact reviewed revision once");
                await Press("Refresh existing Active conversations");
                window.Targets.SelectedIndex.Should().Be(-1);
                window.Targets.Focus().Should().BeTrue();
                window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
                window.KeyRelease(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
                window.Targets.SelectedIndex.Should().Be(0);
                const string body = "  literal original e\u0301; owned skill invoke; Lock  ";
                window.Request.Focus().Should().BeTrue();
                window.KeyTextInput(body);
                window.Request.Text.Should().Be(body);
                await Press("Review exact invocation");
                window.Confirm.IsEnabled.Should().BeTrue();
                if (sharedChoice) { window.Confirmation.Text.Should().Contain(body); }
                else
                {
                    using var preview = JsonDocument.Parse(window.Confirmation.Text!);
                    preview.RootElement.GetProperty("UserRequest").GetString().Should().Be(body);
                }
                adapter.Calls.Should().Be(0);
                if (stop)
                {
                    window.Confirm.Focus().Should().BeTrue();
                    window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
                    window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
                    await entered.Task.WaitAsync(TimeSpan.FromSeconds(20), native.Token);
                    var joining = controller.CloseAndWait();
                    joining.IsCompleted.Should().BeFalse("the actual model deliberately ignores cancellation");
                    controller.ActiveOperation.IsCompleted.Should().BeFalse();
                    pending.SetResult(new(ModelTurnOutcome.Succeeded, ModelProviderSelection.OllamaCandidate,
                        new("late answer must not be disclosed", null)));
                    await joining;
                    var cancelled = (await f.Store.ReadConversationPageAsync(f.Request.SessionId, null, 25, f.Token)).Messages.Single();
                    cancelled.Body.Should().Be(body);
                    cancelled.AssistantResponse.Should().BeNull();
                    cancelled.Outcome.Should().NotBe(ModelTurnOutcome.Succeeded);
                    adapter.Calls.Should().Be(1);
                    window.Instructions.Text.Should().BeEmpty();
                    window.Request.Text.Should().BeEmpty();
                    return;
                }
                await Press("Confirm exact invocation once");
                adapter.Calls.Should().Be(1);
                window.Status.Text.Should().Contain("Succeeded").And.Contain("earlier ASSISTANT");
                var retained = (await f.Store.ReadConversationPageAsync(f.Request.SessionId, null, 25, f.Token)).Messages.Single();
                retained.Body.Should().Be(body);
                retained.Request.Origin.Should().Be(voice ? RequestOrigin.ActivatedVoice : RequestOrigin.LocalUi);
                retained.AssistantResponse!.Body.Should().Be("earlier ASSISTANT");
                using var wire = JsonDocument.Parse(adapter.Wire!);
                wire.RootElement.GetProperty("UserRequest").GetString().Should().Be(body);
                wire.RootElement.GetProperty("Tools").GetArrayLength().Should().Be(0);
                f.Count("scoped_grants").Should().Be(0);
                await controller.CloseAndWait();
                controller.ActiveOperation.IsCompleted.Should().BeTrue();
                window.Instructions.Text.Should().BeEmpty();
                window.Request.Text.Should().BeEmpty();
                window.Confirmation.Text.Should().BeEmpty();
            }
            finally { await controller.CloseAndWait(); }
        });
    }

    private sealed class NamedNativeUsePolicy : IArtifactUsePolicy
    {
        public event EventHandler? Changed { add { } remove { } }
        public long Revision => 0;
        public bool IsDisabled(string identity) => false;
    }
}
