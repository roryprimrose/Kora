using System.Text.Json;

using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

using AwesomeAssertions;

using Kora.Application.Configuration;
using Kora.Application.Documentation;
using Kora.Application.Presentation;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Presentation;
using Kora.Core.Providers;
using Kora.NativeUxFixture;
using Kora.Windows.IntegrationTests.Storage;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionsConversationTests
{
    [Fact]
    public async Task PrimaryContextInspectorBackControlFocusesExactAnswerWithoutReplacingComposerOrSubmitting()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            new LocalProviderProfilePreferences(f.Paths).Save(new(ProviderService.Local, [HandoffCatalogue.Local]));
            var adapter = new HistoryAdapter();
            await using var composed = new Composition(f, adapter: adapter, localRequestCatalogue: new HandoffCatalogue(f));
            await composed.Model.RefreshAsync();
            await composed.Model.SelectAsync(composed.Model.Sessions.Single());
            composed.Model.ComposerBody = "genuine original request";
            await composed.Model.SubmitConversationAsync();
            var source = composed.Model.ConversationMessages.Single();
            await composed.Model.ReadConversationAsync();
            composed.Model.OpenHistoryContext();
            composed.Model.SelectHistoryContext(composed.Model.HistoryContextCandidates.ToArray());
            composed.Model.ComposerBody = "genuine retained follow-up request";
            await composed.Model.ReviewHistoryContextAsync();
            await composed.Model.ConfirmHistoryContextAsync();
            var retained = composed.Model.ConversationMessages.Single();
            retained.SubmittedContext.Should().NotBeNull();
            using var native = new NativeUxFixtureSession(Path.GetFullPath("."));
            await native.InitializeAsync();
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
            await sessions.ShowInteractionAsync(true, static () => true);
            var state = (SessionsViewModel)window!.SessionView.DataContext!;
            await state.SelectAsync(state.Sessions.Single(entry => entry.Authority.SessionId == f.Request.SessionId));
            await state.ReadConversationAsync();
            var clock = await f.Store.ReadRetentionAsync(f.Request.SessionId, f.Token);
            var audit = WindowsSqliteConversationTests.AuditText(f);
            var tasks = f.Count("host_tasks");
            window.ShowSessions(true);
            window.UpdateLayout();
            var original = state.ConversationMessages.Single(message => message.Request == retained.Request);
            state.ComposerBody = "independent unsent input";
            var button = window.SessionView.GetVisualDescendants().OfType<Button>().Single(control =>
                string.Equals(control.Name, "OpenSubmittedAnswerContext", StringComparison.Ordinal)
                && control.DataContext is ConversationMessage message && message.Request == retained.Request);
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await window.SessionView.Operation;
            window.UpdateLayout();
            window.SessionView.DetailsVisible.Should().BeTrue(state.ConversationStatus);
            var reader = window.SessionView.GetVisualDescendants().OfType<DetailContentView>().Single();
            reader.FindControl<TextBox>("ContentReader")!.Text.Should().Contain("exact source-row revision is retained")
                .And.NotContain(source.Body).And.NotContain(source.AssistantResponse!.Body)
                .And.NotContain(retained.Body).And.NotContain(retained.AssistantResponse!.Body);
            window.SessionView.FindControl<TabControl>("ConversationTabs")!.SelectedIndex = 1;
            var back = window.SessionView.FindControl<Button>("BackToConversation")!;
            back.IsEnabled.Should().BeTrue();
            back.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            button.IsFocused.Should().BeTrue("the return control must target this exact answer, not the latest source");
            window.SessionView.GetVisualDescendants().OfType<DetailContentView>().Single().Should().BeSameAs(reader);
            state.ComposerBody.Should().Be("independent unsent input");
            state.ConversationMessages.Single(message => message.Request == retained.Request).Should().Be(original);
            state.ConversationMessages.Should().HaveCount(2);
            adapter.Calls.Should().Be(2);
            (await f.Store.ReadRetentionAsync(f.Request.SessionId, f.Token)).Should().Be(clock);
            WindowsSqliteConversationTests.AuditText(f).Should().Be(audit);
            f.Count("host_tasks").Should().Be(tasks);
            f.Count("host_questions").Should().Be(0);
            f.Count("scoped_grants").Should().Be(0);
            state.ComposerBody = string.Empty;
            window.Close();
        });
    }

    [Fact]
    public async Task ReviewedHistoryCanonicalDispatchFinalTransactionRestartAndNativeExactInspectionHaveNoReplayOrSourceBodyRead()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            new LocalProviderProfilePreferences(f.Paths).Save(new(ProviderService.Local, [HandoffCatalogue.Local]));
            var adapter = new HistoryAdapter();
            ConversationMessage retained;
            ConversationMessage original;
            await using (var first = new Composition(f, adapter: adapter, localRequestCatalogue: new HandoffCatalogue(f)))
            {
                var model = first.Model;
                await model.RefreshAsync();
                await model.SelectAsync(model.Sessions.Single());
                model.ComposerBody = "original source body must not enter context metadata";
                await model.SubmitConversationAsync();
                original = model.ConversationMessages.Single();
                await model.ReadConversationAsync();
                model.OpenHistoryContext();
                model.SelectHistoryContext(model.HistoryContextCandidates.ToArray());
                model.ComposerBody = "What follows from those exact earlier messages?";
                await model.ReviewHistoryContextAsync();
                await model.ConfirmHistoryContextAsync();
                retained = model.ConversationMessages.Single();
                retained.Outcome.Should().Be(ModelTurnOutcome.Succeeded, model.ConversationStatus);
                retained.SubmittedContext.Should().NotBeNull();
                retained.SubmittedContext!.Profile.Should().Be(HandoffCatalogue.Local);
                retained.SubmittedContext.Sources.Should().HaveCount(2);
                retained.SubmittedContext.CanonicalUtf8Bytes.Should().Be(adapter.Wire!.Length);
                using var wire = JsonDocument.Parse(adapter.Wire);
                wire.RootElement.GetProperty("Evidence")[0].GetProperty("Content").GetString().Should().Contain(original.Body);
                wire.RootElement.GetProperty("Evidence")[0].TryGetProperty("HistorySource", out _).Should().BeFalse();
                JsonSerializer.Serialize(retained.SubmittedContext).Should().NotContain(original.Body);
                model.Close();
            }
            f.Reopen();
            await f.Store.InitializeAsync(f.Token);
            var opened = new List<AdmittedDetailContent>();
            Func<CancellationToken, Task<AdmittedDetailContent>>? revalidate = null;
            Func<bool>? live = null;
            await using var restarted = new Composition(f, (content, admitted, resolve, _) =>
            {
                opened.Add(content);
                live = admitted;
                revalidate = resolve;
                return Task.FromResult("Exact context metadata opened");
            }, adapter: adapter);
            var state = restarted.Model;
            await state.RefreshAsync();
            await state.SelectAsync(state.Sessions.Single());
            await state.ReadConversationAsync();
            var clock = await f.Store.ReadRetentionAsync(f.Request.SessionId, f.Token);
            var audit = WindowsSqliteConversationTests.AuditText(f);
            var owner = new SessionsPresentationOwner(state);
            var parent = new SessionsWindow(state, owner);
            parent.Show();
            try
            {
                var displayed = state.ConversationMessages.Single(message => message.Request == retained.Request);
                parent.FindControl<ListBox>("AnswerSourceRecords")!.ScrollIntoView(displayed);
                parent.UpdateLayout();
                var button = parent.GetVisualDescendants().OfType<Button>().Single(control =>
                    string.Equals(control.Name, "OpenSubmittedAnswerContext", StringComparison.Ordinal)
                    && control.DataContext is ConversationMessage message && message.Request == retained.Request);
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await owner.DetailOperation;
                var context = opened.Should().ContainSingle().Subject;
                context.Origin.Should().Be(DetailContentOrigin.RetainedAnswerContext);
                context.ConversationMessage.Should().Be(retained);
                context.Source.Should().Contain("1. Earlier USER message").And.Contain("2. Earlier ASSISTANT answer")
                    .And.Contain("not transport confirmation").And.Contain("exact source-row revision is retained")
                    .And.NotContain(original.Body).And.NotContain(original.AssistantResponse!.Body)
                    .And.NotContain(retained.AssistantResponse!.Body);
                var reader = new DetailViewerState(context, live);
                reader.CompleteRender(reader.Generation, context.Source, "Native metadata", false).Should().BeTrue();
                reader.TryGetCopySource(true, true, out _).Should().BeFalse();
                reader.CanInteractWithFinalDocument.Should().BeFalse();
                (await revalidate!(f.Token)).IsSameSnapshot(context).Should().BeTrue();
                await state.OpenConversationDetailAsync(displayed, assistant: true);
                opened[^1].Source.Should().Be(retained.AssistantResponse.Body, "return targets the exact original answer, not the newest source");
                adapter.Calls.Should().Be(2);
                state.ComposerBody.Should().BeEmpty();
                (await f.Store.ReadRetentionAsync(f.Request.SessionId, f.Token)).Should().Be(clock);
                WindowsSqliteConversationTests.AuditText(f).Should().Be(audit);
                f.Count("host_questions").Should().Be(0);
                f.Count("scoped_grants").Should().Be(0);
                parent.ClosePrivate();
                live!().Should().BeFalse();
                reader.Content.Should().BeNull();
                await revalidate.Invoking(resolve => resolve!(f.Token)).Should().ThrowAsync<Exception>();
            }
            finally { parent.ClosePrivate(); }
        });
    }

    [Fact]
    public async Task LegacyAnswerInspectionDoesNotInventSourcesFromModelProseAndRejectsCopiedOrStaleSelections()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteConversationTests.FinishInitial(f);
        var adapter = new HistoryAdapter();
        var opened = new List<AdmittedDetailContent>();
        await using var composed = new Composition(f, (content, _, _, _) =>
        {
            opened.Add(content);
            return Task.FromResult("Legacy metadata observed");
        }, adapter: adapter);
        var model = composed.Model;
        await model.RefreshAsync();
        await model.SelectAsync(model.Sessions.Single());
        model.ComposerBody = "ordinary request without explicit reviewed history";
        await model.SubmitConversationAsync();
        var message = model.ConversationMessages.Single();
        await model.OpenConversationDetailAsync(message, submittedContext: true);
        opened.Single().Source.Should().Contain("Unknown / not recorded").And.Contain("no host-recorded");
        await model.OpenConversationDetailAsync(message with { }, submittedContext: true);
        opened.Should().ContainSingle("equal copied data is not a native selection");
        await model.SelectAsync(null);
        await model.OpenConversationDetailAsync(message, submittedContext: true);
        opened.Should().ContainSingle("stale selection cannot publish");
        adapter.Calls.Should().Be(1);
        model.Close();
    }
}
