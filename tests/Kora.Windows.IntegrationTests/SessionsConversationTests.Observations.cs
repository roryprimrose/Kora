using System.Text;
using System.Text.Json;

using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

using AwesomeAssertions;

using Kora.Application.Dependencies;
using Kora.Application.Documentation;
using Kora.Application.Presentation;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Presentation;
using Kora.Core.Tools;
using Kora.Windows.IntegrationTests.Storage;
using Kora.NativeUxFixture;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionsConversationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OriginalTypedAndCurrentNameVoiceOpenExactForegroundObservationAndCannotReviveRetiredPage(bool voice)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            var adapter = new AnswerAdapter();
            await using var composed = new Composition(f, adapter: adapter);
            await composed.Model.RefreshAsync();
            await composed.Model.SelectAsync(composed.Model.Sessions.Single());
            composed.Model.ComposerBody = "genuine host observation";
            await composed.Model.SubmitConversationAsync();
            composed.Model.ComposerBody = "another genuine host observation";
            await composed.Model.SubmitConversationAsync();
            using var native = new NativeUxFixtureSession(Path.GetFullPath("."), syntheticVoice: voice);
            await native.InitializeAsync();
            native.Main.BindVoiceOwnershipGate(static () => true);
            native.Main.CommandText = "set assistant name to Nova";
            await native.Main.RunTypedCommand.ExecuteAsync();
            if (voice)
            {
                await native.Main.SetVoiceConsentAsync(true);
                await native.Main.BeginPushToTalkAsync();
                await native.Main.EndPushToTalkAsync();
            }
            using var details = new DetailWindowController(new EmbeddedUserDocumentationProvider(), native.Main,
                NullLogger<DetailWindowController>.Instance, NullLogger<NativeDetailRenderer>.Instance,
                NullLogger<StaticHtmlDetailRenderer>.Instance);
            using var responses = new ResponseWindowController(native.Main, NullLogger<ResponseWindowController>.Instance);
            using var sessions = new SessionsWindowController(native.Main, composed.Workspace, native.Evidence,
                composed.Access, NullLogger<SessionsViewModel>.Instance, details: details, conversation: composed.Conversation);
            ResponseWindow? window = null;
            sessions.CreateInteraction = (model, owner) =>
                window = responses.PrepareSessions(model, owner, sessions.Open, static () => { });
            sessions.Bind();
            using var all = new AllWorkWindowController(native.Main, composed.Workspace, composed.Access,
                NullLogger<AllWorkViewModel>.Instance, sessions);
            using var commands = new WorkspaceSurfaceCommandController(native.Main, composed.Access, all, sessions);
            commands.Bind();
            await sessions.ShowInteractionAsync(true, static () => true);
            var state = (SessionsViewModel)window!.SessionView.DataContext!;
            await state.SelectAsync(state.Sessions.Single(entry => entry.Authority.SessionId == f.Request.SessionId));
            await state.ReadConversationAsync();
            window.ShowSessions(true);
            await ActivateOriginalRequestWindow(window);
            sessions.CaptureObservedResultEligibility()().Should().BeTrue(
                $"the original page must be foreground; active={window.IsActive}, visible={window.IsVisible}, sessionList={window.IsSessionListPresented}, conversation={window.SessionView.IsConversationPresented}");
            var choice = state.ObservationChoices[0];
            var opened = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            state.PropertyChanged += (_, _) =>
            {
                if (state.LastDetailObservation == choice.Observation.Id) { opened.TrySetResult(); }
            };
            if (voice) { native.Voice.PublishTranscript("Nova open observed result " + choice.Name); }
            else
            {
                native.Main.CommandText = "open observed result " + choice.Name;
                await native.Main.RunTypedCommand.ExecuteAsync();
                state.LastDetailObservation.Should().Be(choice.Observation.Id,
                    "typed result: " + native.Main.ResponseBody + " | conversation: " + state.ConversationStatus);
            }
            try { await opened.Task.WaitAsync(TimeSpan.FromSeconds(20), f.Token); }
            catch (TimeoutException exception)
            {
                throw new InvalidOperationException("Observation opening did not complete: "
                    + native.Main.ResponseBody + " | " + state.ConversationStatus, exception);
            }
            window.SessionView.DetailsVisible.Should().BeTrue(state.ConversationStatus);
            state.LastDetailObservation.Should().Be(choice.Observation.Id);
            adapter.Calls.Should().Be(2);
            state.ComposerBody.Should().BeEmpty();
            native.Main.CommandText = "open observed result " + ReadOnlyCapabilityCatalog.Version;
            await native.Main.RunTypedCommand.ExecuteAsync();
            state.ConversationStatus.Should().Contain("Several observed results");
            var chooser = window.SessionView.FindControl<ContentControl>("DetailsContent")!.Content.Should().BeOfType<StackPanel>().Which;
            chooser.Children.OfType<Button>().Count(button => string.Equals(button.Name, "OpenObservedResult", StringComparison.Ordinal))
                .Should().Be(2, "ambiguity must show exact choices, not reopen the remembered prior result");
            var originalAdmission = sessions.CaptureObservedResultEligibility();
            originalAdmission().Should().BeTrue();
            var tabs = window.SessionView.FindControl<TabControl>("ConversationTabs")!;
            tabs.SelectedIndex = 1;
            tabs.SelectedIndex = 0;
            originalAdmission().Should().BeFalse("a returned foreground page cannot renew the original choice authority");
            window.Close();
        });
    }

    [Theory]
    [InlineData(ReadOnlyCapabilityCatalog.List)]
    [InlineData(ReadOnlyCapabilityCatalog.Get)]
    [InlineData(ReadOnlyCapabilityCatalog.Version)]
    [InlineData(ReadOnlyCapabilityCatalog.Readiness)]
    [InlineData(ReadOnlyCapabilityCatalog.RuntimeList)]
    [InlineData(ReadOnlyCapabilityCatalog.RuntimeStatus)]
    public async Task ActualHostToolLoopRetainsExactResultAndRestartOpensNativeLinkWithoutRequeryOrResumingDone(string id)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            var transport = new ObservationTransport(id);
            ConversationMessage retained;
            await using (var adapter = new BoundedModelTurnAdapter(transport, f.Time, NullLogger<BoundedModelTurnAdapter>.Instance))
            await using (var first = new Composition(f, adapter: adapter))
            {
                var model = first.Model;
                await model.RefreshAsync();
                await model.SelectAsync(model.Sessions.Single());
                model.ComposerBody = "Use an admitted host observation";
                await model.SubmitConversationAsync();
                retained = model.ConversationMessages.Single();
                retained.Outcome.Should().Be(ModelTurnOutcome.Succeeded, model.ConversationStatus);
                retained.Observations!.Items.Should().ContainSingle();
                var observation = retained.Observations.Items[0];
                observation.CapabilityId.Should().Be(id);
                Encoding.UTF8.GetBytes(observation.ResultJson).Should().Equal(transport.ExactResult!);
                observation.Id.ToString("D").Should().NotBe("provider-call-1");
                retained.AssistantResponse!.Body.Should().Be("Answer based on the original observation.");
                transport.Calls.Should().Be(2);
                using var root = HostActivity.BeginRoot(f.Request, HostActivityLayer.Application, HostOperation.Request);
                await first.Workspace.ChangeLifecycleAsync(f.Request.SessionId,
                    model.SelectedSessionRecord!.Authority.Generation, false, RequestOrigin.LocalUi, f.Token);
            }
            f.Reopen();
            await f.Store.InitializeAsync(f.Token);
            var opened = new List<AdmittedDetailContent>();
            Func<CancellationToken, Task<AdmittedDetailContent>>? resolveAgain = null;
            Func<bool>? sourceLive = null;
            await using var restarted = new Composition(f, async (content, admitted, resolve, token) =>
            {
                sourceLive = admitted;
                resolveAgain = resolve;
                (await resolve(token)).IsSameSnapshot(content).Should().BeTrue();
                opened.Add(content);
                return "Exact original observation opened";
            }, adapter: new AnswerAdapter());
            var state = restarted.Model;
            await state.RefreshAsync();
            await state.SelectAsync(state.Sessions.Single());
            await state.ReadConversationAsync();
            var clock = await f.Store.ReadRetentionAsync(f.Request.SessionId, f.Token);
            var audit = WindowsSqliteConversationTests.AuditText(f);
            var owner = new SessionsPresentationOwner(state);
            var window = new SessionsWindow(state, owner);
            window.Show();
            try
            {
                var button = window.GetVisualDescendants().OfType<Button>().Single(control =>
                    string.Equals(control.Name, "OpenObservedResult", StringComparison.Ordinal));
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await owner.DetailOperation;
                var exact = opened.Should().ContainSingle().Subject;
                exact.Origin.Should().Be(DetailContentOrigin.RetainedLocalObservation);
                exact.Kind.Should().Be(DetailContentKind.PlainText);
                exact.Source.Should().Be(retained.Observations!.Items[0].ResultJson);
                exact.Provenance.Should().Contain(retained.Observations.Items[0].ObservedAt.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
                var reader = new DetailViewerState(exact, sourceLive);
                reader.CompleteRender(reader.Generation, exact.Source, "Inert native source", false).Should().BeTrue();
                reader.CanInteractWithFinalDocument.Should().BeFalse("opening is not speech or model-context permission");
                reader.TryGetCopySource(true, true, out _).Should().BeFalse();
                state.SelectedSessionRecord!.Authority.IsActive.Should().BeFalse();
                state.ComposerBody.Should().BeEmpty();
                (await f.Store.ReadRetentionAsync(f.Request.SessionId, f.Token)).Should().Be(clock);
                WindowsSqliteConversationTests.AuditText(f).Should().Be(audit);
                f.Count("scoped_grants").Should().Be(0);
                transport.Calls.Should().Be(2);
                window.ClosePrivate();
                sourceLive!().Should().BeFalse();
                reader.Content.Should().BeNull();
                Func<Task> late = () => resolveAgain!(f.Token);
                await late.Should().ThrowAsync<Exception>();
            }
            finally { window.ClosePrivate(); }
        });
    }

    [Fact]
    public async Task DuplicateToolNamesRequireExactPresentedMessageResultChoiceAndRefuseStaleOrRetiredChoices()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteConversationTests.FinishInitial(f);
        var opened = new List<AdmittedDetailContent>();
        await using var composed = new Composition(f, (content, _, _, _) =>
        {
            opened.Add(content);
            return Task.FromResult("Exact observation opened");
        }, adapter: new AnswerAdapter());
        var model = composed.Model;
        await model.RefreshAsync();
        await model.SelectAsync(model.Sessions.Single());
        foreach (var body in new[] { "first observation", "second observation" })
        { model.ComposerBody = body; await model.SubmitConversationAsync(); }
        await model.ReadConversationAsync();
        var choices = model.ObservationChoices.ToArray();
        choices.Should().HaveCount(2);
        (await model.OpenNamedObservationAsync(ReadOnlyCapabilityCatalog.Version, static () => true)).Should().BeFalse();
        opened.Should().BeEmpty();
        model.ConversationStatus.Should().Contain("Several observed results");
        foreach (var choice in choices)
        {
            (await model.OpenNamedObservationAsync(choice.Name, static () => true)).Should().BeTrue();
            opened[^1].Reference.ItemId.Value.Should().Be(choice.Observation.Id);
        }
        (await model.OpenNamedObservationAsync("missing", static () => true)).Should().BeFalse();
        Func<Task> retired = () => model.OpenNamedObservationAsync(choices[0].Name, static () => false);
        await retired.Should().ThrowAsync<OperationCanceledException>();
        model.Close();
        Func<Task> stale = () => model.OpenObservationAsync(choices[0]);
        await stale.Should().ThrowAsync<InvalidOperationException>();
    }

    private sealed class ObservationTransport(string id) : IModelToolTransport
    {
        internal int Calls { get; private set; }
        internal byte[]? ExactResult { get; private set; }

        public Task<ModelToolTransportReply> CompleteAsync(ModelToolRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            var input = string.Equals(id, ReadOnlyCapabilityCatalog.Get, StringComparison.Ordinal) ? "{\"id\":\"capabilities.list\"}"
                : string.Equals(id, ReadOnlyCapabilityCatalog.RuntimeStatus, StringComparison.Ordinal) ? "{\"id\":\"local.inference\"}" : "{}";
            ModelAssistantMessage message;
            if (Calls == 1)
            {
                message = ModelToolProtocol.ParseAssistant(Encoding.UTF8.GetBytes(
                    "{\"kind\":\"toolCalls\",\"calls\":[{\"callId\":\"provider-call-1\",\"capabilityId\":"
                    + JsonSerializer.Serialize(id) + ",\"input\":" + input + "}]}"));
            }
            else
            {
                ExactResult = request.Messages.OfType<ModelToolResultMessage>().Single().Result.ToArray();
                message = ModelToolProtocol.ParseAssistant("{\"kind\":\"answer\",\"answer\":\"Answer based on the original observation.\"}"u8);
            }
            return Task.FromResult(new ModelToolTransportReply(ModelTurnOutcome.Succeeded, request.Provenance, message,
                new(ModelToolProtocol.SerializeRequest(request).Length, ModelToolProtocol.SerializeMessage(message).Length)));
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
