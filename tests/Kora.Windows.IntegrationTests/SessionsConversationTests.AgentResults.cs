using System.Text;
using System.Text.Json;

using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

using AwesomeAssertions;

using Kora.Application.Agents;
using Kora.Application.Dependencies;
using Kora.Application.Presentation;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Presentation;
using Kora.Core.Tools;
using Kora.NativeUxFixture;
using Kora.Windows.IntegrationTests.Storage;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionsConversationTests
{
    [Theory]
    [InlineData(ReadOnlyCapabilityCatalog.List)]
    [InlineData(ReadOnlyCapabilityCatalog.Get)]
    [InlineData(ReadOnlyCapabilityCatalog.Version)]
    [InlineData(ReadOnlyCapabilityCatalog.Readiness)]
    [InlineData(ReadOnlyCapabilityCatalog.RuntimeList)]
    [InlineData(ReadOnlyCapabilityCatalog.RuntimeStatus)]
    public async Task NativeAgentRunRetainsExactObservationForRestartReadbackWithoutReplayOrRenewedAuthority(string id)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            var transport = new ObservationTransport(id);
            ConversationMessage retained;
            await using (var adapter = new BoundedModelTurnAdapter(transport, f.Time, NullLogger<BoundedModelTurnAdapter>.Instance))
            await using (var composed = new Composition(f, adapter: adapter))
            {
                using var native = new NativeUxFixtureSession(NamedAgentScratch());
                await native.InitializeAsync();
                var source = new NativeAgentReader();
                source.Text = source.Text.Replace("tools: none", "tools: " + id, StringComparison.Ordinal)
                    .Replace("maximum-calls: 0", "maximum-calls: 1", StringComparison.Ordinal)
                    .Replace("maximum-rounds: 1", "maximum-rounds: 2", StringComparison.Ordinal);
                await using var workflow = new BoundedAgentWorkflow(source, f.Store, composed.Access,
                    composed.Conversation, composed.Turns, composed.Audit, f.Time);
                var controller = new AgentRunController(native.Main, workflow, composed.Workspace,
                    static () => true, static () => 1, NullLogger<AgentRunController>.Instance);
                controller.Bind();
                AgentRunWindow? opened = null;
                using var observation = Window.WindowOpenedEvent.AddClassHandler<AgentRunWindow>((window, _) => opened = window);
                try
                {
                    await NamedAgentInput(native, "review agent", false);
                    var window = opened ?? throw new InvalidOperationException("No original agent review opened.");
                    await NamedAgentInput(native, "agent targets", false);
                    window.Targets.SelectedIndex = 0;
                    window.Source.Text = source.Metadata.CanonicalPath;
                    window.Input.Text = "Exact original agent task";
                    NamedAgentButton(window, "Read source and review exact invocation")
                        .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    await controller.ActiveOperation;
                    transport.Calls.Should().Be(0);
                    window.Confirm.IsEnabled.Should().BeTrue(window.Status.Text);
                    window.Confirm.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    await controller.ActiveOperation;
                    window.Status.Text.Should().Contain("Retained terminal Succeeded");
                    window.Result.Text.Should().Be("Answer based on the original observation.");
                    window.Input.Text.Should().Be("Exact original agent task");
                    window.Source.Text.Should().Be(source.Metadata.CanonicalPath);
                    retained = (await f.Store.ReadConversationPageAsync(f.Request.SessionId, null, 25, f.Token)).Messages.Single();
                    retained.Observations.Should().NotBeNull("the native reader needs the original callback receipt, not model prose");
                    var item = retained.Observations!.Items.Should().ContainSingle().Subject;
                    item.CapabilityId.Should().Be(id);
                    Encoding.UTF8.GetBytes(item.ResultJson).Should().Equal(transport.ExactResult!);
                    if (string.Equals(id, ReadOnlyCapabilityCatalog.RuntimeStatus, StringComparison.Ordinal))
                    {
                        using var recorded = JsonDocument.Parse(item.ResultJson);
                        recorded.RootElement.GetProperty("runtime").GetProperty("toolLoopQualified").GetBoolean().Should().BeFalse(
                            "a retained observation does not qualify the observed runtime");
                    }
                    retained.Body.Should().Be("Exact original agent task");
                    retained.Request.SessionId.Should().Be(f.Request.SessionId);
                    retained.Request.Origin.Should().Be(RequestOrigin.LocalUi);
                    retained.Validate();
                    composed.Turns.IsQuiescent.Should().BeTrue();
                    source.Released.Should().BeTrue();
                    await NamedAgentInput(native, "agent confirm invocation", false);
                    await controller.ActiveOperation;
                    transport.Calls.Should().Be(2, "readback or consumed consent cannot replay an agent");
                    (await f.Store.ReadConversationMessageAsync(f.Request.SessionId, retained.Request.RequestId, f.Token))
                        .Should().Be(retained);
                    await NamedAgentInput(native, "agent stop", false);
                    using var root = HostActivity.BeginRoot(f.Request, HostActivityLayer.Application, HostOperation.Request);
                    var target = await f.Store.ReadMetadataAsync(f.Request.SessionId, f.Token);
                    await composed.Workspace.ChangeLifecycleAsync(f.Request.SessionId, target.Authority.Generation,
                        false, RequestOrigin.LocalUi, f.Token);
                }
                finally { await native.Main.ExitAsync(); }
            }
            f.Reopen();
            await f.Store.InitializeAsync(f.Token);
            var openedDetails = new List<AdmittedDetailContent>();
            Func<bool>? sourceLive = null;
            Func<CancellationToken, Task<AdmittedDetailContent>>? resolveAgain = null;
            var unusedAdapter = new AnswerAdapter();
            await using var restarted = new Composition(f, async (content, admitted, resolve, token) =>
            {
                sourceLive = admitted;
                resolveAgain = resolve;
                (await resolve(token)).IsSameSnapshot(content).Should().BeTrue();
                openedDetails.Add(content);
                return "Exact retained agent observation opened";
            }, adapter: unusedAdapter);
            var model = restarted.Model;
            await model.RefreshAsync();
            await model.SelectAsync(model.Sessions.Single());
            model.ComposerBody = "Unrelated unsent recovery text";
            await model.ReadConversationAsync();
            var clock = await f.Store.ReadRetentionAsync(f.Request.SessionId, f.Token);
            var audit = WindowsSqliteConversationTests.AuditText(f);
            var tasks = await f.Store.ReadTaskPageAsync(f.Request.SessionId, null, 25, f.Token);
            var owner = new SessionsPresentationOwner(model);
            var sessions = new SessionsWindow(model, owner);
            sessions.Show();
            try
            {
                sessions.GetVisualDescendants().OfType<Button>().Single(button =>
                    string.Equals(button.Name, "OpenObservedResult", StringComparison.Ordinal))
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await owner.DetailOperation;
                var detail = openedDetails.Should().ContainSingle().Subject;
                detail.Source.Should().Be(retained.Observations!.Items[0].ResultJson);
                detail.Origin.Should().Be(DetailContentOrigin.RetainedLocalObservation);
                detail.Provenance.Should().Contain("not current truth, execution, context, speech, export or clipboard authority");
                var viewer = new DetailViewerState(detail, sourceLive);
                viewer.CompleteRender(viewer.Generation, detail.Source, "Native observation only", false).Should().BeTrue();
                viewer.CanInteractWithFinalDocument.Should().BeFalse();
                viewer.TryGetCopySource(true, true, out _).Should().BeFalse();
                model.SelectedSessionRecord!.Authority.IsActive.Should().BeFalse();
                model.ComposerBody.Should().Be("Unrelated unsent recovery text");
                (await f.Store.ReadRetentionAsync(f.Request.SessionId, f.Token)).Should().Be(clock);
                (await f.Store.ReadTaskPageAsync(f.Request.SessionId, null, 25, f.Token)).Should().BeEquivalentTo(tasks);
                (await f.Store.ReadConversationMessageAsync(f.Request.SessionId, retained.Request.RequestId, f.Token))
                    .Should().Be(retained);
                WindowsSqliteConversationTests.AuditText(f).Should().Be(audit).And
                    .NotContain(retained.Body).And.NotContain(detail.Source);
                f.Count("scoped_grants").Should().Be(0);
                f.Count("conversation_drafts").Should().Be(0);
                transport.Calls.Should().Be(2);
                unusedAdapter.Calls.Should().Be(0);
                sessions.ClosePrivate();
                sourceLive!().Should().BeFalse();
                viewer.Content.Should().BeNull();
                Func<Task> late = () => resolveAgain!(f.Token);
                await late.Should().ThrowAsync<Exception>();
            }
            finally { sessions.ClosePrivate(); }
        });
    }

    [Fact]
    public async Task NativeFailedAgentRecoveryRequiresFreshSeparateConsentAndPreservesOriginalTaskAndUnsentInput()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            var adapter = new AgentRecoveryAdapter();
            await using var composed = new Composition(f, adapter: adapter);
            await composed.Model.RefreshAsync();
            await composed.Model.SelectAsync(composed.Model.Sessions.Single());
            composed.Model.ComposerBody = "Unrelated unsent text";
            using var native = new NativeUxFixtureSession(NamedAgentScratch());
            await native.InitializeAsync();
            var source = new NativeAgentReader();
            source.Text = source.Text.Replace("tools: none", "tools: application.get_version", StringComparison.Ordinal)
                .Replace("maximum-calls: 0", "maximum-calls: 1", StringComparison.Ordinal)
                .Replace("maximum-rounds: 1", "maximum-rounds: 2", StringComparison.Ordinal);
            await using var workflow = new BoundedAgentWorkflow(source, f.Store, composed.Access,
                composed.Conversation, composed.Turns, composed.Audit, f.Time);
            var controller = new AgentRunController(native.Main, workflow, composed.Workspace,
                static () => true, static () => 1, NullLogger<AgentRunController>.Instance);
            controller.Bind();
            AgentRunWindow? opened = null;
            using var observation = Window.WindowOpenedEvent.AddClassHandler<AgentRunWindow>((window, _) => opened = window);
            try
            {
                await NamedAgentInput(native, "review agent", false);
                var window = opened ?? throw new InvalidOperationException("No original agent recovery review opened.");
                await NamedAgentInput(native, "agent targets", false);
                window.Targets.SelectedIndex = 0;
                window.Source.Text = source.Metadata.CanonicalPath;
                window.Input.Text = "Recover this original agent task";
                await NamedAgentInput(native, "agent review invocation", false);
                await NamedAgentInput(native, "agent confirm invocation", false);
                await controller.ActiveOperation;
                window.Status.Text.Should().Contain("Retained terminal QuotaExceeded");
                window.Result.Text.Should().Be("No assistant answer was produced.");
                window.Confirm.IsEnabled.Should().BeFalse();
                var failed = (await f.Store.ReadConversationPageAsync(f.Request.SessionId, null, 25, f.Token)).Messages.Single();
                failed.AssistantResponse.Should().BeNull();
                failed.Observations.Should().BeNull();
                await NamedAgentInput(native, "agent confirm invocation", false);
                await controller.ActiveOperation;
                adapter.Calls.Should().Be(1, "consumed failed confirmation is not recovery authority");
                await NamedAgentInput(native, "agent review invocation", false);
                window.Confirm.IsEnabled.Should().BeTrue(window.Status.Text);
                window.Review.Text.Should().Contain(failed.Body).And.Contain(source.Text);
                window.Input.Text.Should().Be(failed.Body);
                window.Source.Text.Should().Be(source.Metadata.CanonicalPath);
                adapter.Calls.Should().Be(1, "fresh review is not automatic recovery");
                await NamedAgentInput(native, "agent confirm invocation", false);
                await controller.ActiveOperation;
                await composed.Model.ReadConversationAsync();
                var recovered = composed.Model.ConversationMessages.Single(message => message.AssistantResponse is not null);
                recovered.Body.Should().Be(failed.Body);
                recovered.Request.SessionId.Should().Be(failed.Request.SessionId);
                recovered.Request.TaskId.Should().NotBe(failed.Request.TaskId);
                recovered.Request.RequestId.Should().NotBe(failed.Request.RequestId);
                recovered.AssistantResponse!.Body.Should().Be("Genuine recovered agent answer");
                recovered.Observations!.Items.Should().ContainSingle();
                (await f.Store.ReadConversationMessageAsync(f.Request.SessionId, failed.Request.RequestId, f.Token))
                    .Should().Be(failed);
                composed.Model.ComposerBody.Should().Be("Unrelated unsent text");
                adapter.Calls.Should().Be(2);
                composed.Turns.IsQuiescent.Should().BeTrue();
                f.Count("scoped_grants").Should().Be(0);
                f.Count("conversation_drafts").Should().Be(0);
                WindowsSqliteConversationTests.AuditText(f).Should().NotContain(failed.Body)
                    .And.NotContain("Genuine recovered agent answer");
            }
            finally { await native.Main.ExitAsync(); }
        });
    }

    private sealed class AgentRecoveryAdapter : IModelTurnAdapter
    {
        internal int Calls { get; private set; }
        public async Task<ModelProviderReply> RunAsync(ModelTurnProvenance provenance,
            Kora.Core.Providers.ProviderProfileTuple? profile, ReadOnlyMemory<byte> context,
            Func<string, string, CancellationToken, ValueTask<CapabilityReply>> invokeTool, CancellationToken token)
        {
            Calls++;
            (await invokeTool(ReadOnlyCapabilityCatalog.Version, "{}", token)).Outcome.Should().Be(CapabilityOutcome.Succeeded);
            return Calls == 1 ? new(ModelTurnOutcome.QuotaExceeded, provenance.Selection)
                : new(ModelTurnOutcome.Succeeded, provenance.Selection, new("Genuine recovered agent answer", null));
        }
    }
}
