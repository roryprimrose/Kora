using Avalonia.Controls;
using AwesomeAssertions;
using Kora.Application.Configuration;
using Kora.Application.Hosting;
using Kora.Application.Voice;
using Kora.Core.Presentation;
using Kora.Core.Storage;
using Kora.Windows.IntegrationTests.Storage;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionsConversationTests
{
    [Theory]
    [InlineData("current")]
    [InlineData("aba")]
    [InlineData("arrive-other")]
    [InlineData("revision")]
    [InlineData("privacy")]
    [InlineData("close")]
    public async Task DeferredSubmittedAcceptanceCannotReattachToRetiredComposerSource(string retirement)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            var original = f.Request.SessionId;
            f.Request = InteractionStorageFixture.NewRequest();
            await f.AdmitAsync(newSession: true);
            await WindowsSqliteConversationTests.FinishInitial(f);
            var adapter = new PendingTurnAdapter();
            await using var composed = new Composition(f, adapter: adapter);
            var dispatcher = new DeferredConversationDispatcher();
            var model = CreateDeferredPresentation(f, composed, dispatcher);
            var accepted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void Changed(Kora.Core.Conversation.ConversationPendingSnapshot snapshot)
            {
                if (snapshot.Turns.Any(turn => turn.Phase == Kora.Core.Conversation.ConversationPendingPhase.Pending))
                { accepted.TrySetResult(); }
            }
            try
            {
                await model.RefreshAsync();
                await model.SelectAsync(model.Sessions.Single(row => row.Authority.SessionId == original));
                var target = model.SelectedSessionRecord!;
                model.ComposerBody = "independent unsent input";
                if (retirement is "arrive-other")
                { await model.SelectAsync(model.Sessions.Single(row => row.Authority.SessionId != original)); }
                composed.Conversation.PendingConversationChanged += Changed;
                await Task.Run(() => composed.Conversation.SubmitQueuedAsync(target, "active original", static () => true, f.Token));
                await adapter.NextAsync();
                const string body = "  pending original e\u0301\r\n unchanged  ";
                var queued = await Task.Run(() => composed.Conversation.SubmitQueuedAsync(target, body, static () => true, f.Token));
                await accepted.Task.WaitAsync(f.Token);
                queued.Position.Should().Be(1);
                dispatcher.Count.Should().BeGreaterThan(0);
                if (retirement is "aba")
                {
                    await model.SelectAsync(model.Sessions.Single(row => row.Authority.SessionId != original));
                    model.ComposerBody = "foreign composer";
                    await model.SelectAsync(model.Sessions.Single(row => row.Authority.SessionId == original));
                }
                else if (retirement is "arrive-other") { await model.SelectAsync(target); }
                else if (retirement is "revision") { composed.Access.ControlRevision++; }
                else if (retirement is "privacy")
                {
                    composed.Access.CanInspect = false;
                    composed.Access.CanControl = false;
                    composed.Access.ControlRevision++;
                }
                else if (retirement is "close") { model.Close(); }
                dispatcher.Drain();
                if (retirement is "current")
                { model.PendingConversationTurns.Should().ContainSingle().Which.Submission.Message.Body.Should().Be(body); }
                else { model.PendingConversationTurns.Should().BeEmpty("a late pending snapshot is not fresh native source admission"); }
                adapter.Calls.Should().Be(1, "presentation cannot dispatch the submitted pending request");
                f.Count("conversation_messages").Should().Be(2);
                f.Count("conversation_drafts").Should().Be(0);
                f.Count("host_questions").Should().Be(0);
                f.Count("scoped_grants").Should().Be(0);
                if (retirement is "current" or "aba" or "arrive-other")
                {
                    model.ComposerBody.Should().Be("independent unsent input");
                    await model.RefreshPendingConversationAsync();
                    model.PendingConversationTurns.Should().ContainSingle().Which.Submission.Message.Body.Should().Be(body);
                    model.SelectedPendingConversation = model.PendingConversationTurns.Single();
                    await model.CancelPendingConversationAsync();
                    (await f.Store.ReadConversationMessageAsync(original, queued.Receipt.Message.Request.RequestId, f.Token))
                        .Outcome.Should().Be(Kora.Core.Dependencies.ModelTurnOutcome.Cancelled);
                    model.ComposerBody.Should().Be("independent unsent input");
                    if (retirement is "aba")
                    {
                        await model.SelectAsync(model.Sessions.Single(row => row.Authority.SessionId != original));
                        model.ComposerBody.Should().Be("foreign composer");
                    }
                    adapter.Calls.Should().Be(1);
                }
            }
            finally
            {
                composed.Conversation.PendingConversationChanged -= Changed;
                model.Close();
                adapter.ReleaseAll();
                await composed.Conversation.DisposeAsync();
                dispatcher.Drain();
            }
        });
    }

    [Theory]
    [InlineData("current", null)]
    [InlineData("switch", null)]
    [InlineData("aba", null)]
    [InlineData("arrive-other", null)]
    [InlineData("generation", null)]
    [InlineData("revision", null)]
    [InlineData("privacy", null)]
    [InlineData("close", null)]
    [InlineData("current", DetailPresentationMode.Offer)]
    [InlineData("switch", DetailPresentationMode.Offer)]
    [InlineData("aba", DetailPresentationMode.Offer)]
    [InlineData("arrive-other", DetailPresentationMode.Offer)]
    [InlineData("generation", DetailPresentationMode.Offer)]
    [InlineData("revision", DetailPresentationMode.Offer)]
    [InlineData("privacy", DetailPresentationMode.Offer)]
    [InlineData("close", DetailPresentationMode.Offer)]
    [InlineData("current", DetailPresentationMode.LinkOnly)]
    [InlineData("switch", DetailPresentationMode.LinkOnly)]
    [InlineData("aba", DetailPresentationMode.LinkOnly)]
    [InlineData("arrive-other", DetailPresentationMode.LinkOnly)]
    [InlineData("generation", DetailPresentationMode.LinkOnly)]
    [InlineData("revision", DetailPresentationMode.LinkOnly)]
    [InlineData("privacy", DetailPresentationMode.LinkOnly)]
    [InlineData("close", DetailPresentationMode.LinkOnly)]
    public async Task DeferredSubmittedTerminalCannotReattachToRetiredComposerSource(string retirement,
        DetailPresentationMode? detailMode)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            var original = f.Request.SessionId;
            f.Request = InteractionStorageFixture.NewRequest();
            await f.AdmitAsync(newSession: true);
            await WindowsSqliteConversationTests.FinishInitial(f);
            var adapter = detailMode is null ? null : new DetailAnswerAdapter();
            await using var composed = new Composition(f, adapter: adapter);
            var dispatcher = new DeferredConversationDispatcher();
            var model = CreateDeferredPresentation(f, composed, dispatcher);
            await using var admission = new AudioControlAdmission(f.Store, f.Store, new(f.Tasks));
            var preferences = new DetailPresentationSettingsTests.Preferences { Value = detailMode };
            var settings = new DetailPresentationConfigurationService(preferences, admission, new AuditLog(), f.Time);
            settings.Observe();
            model.BindDetailPresentation(settings);
            var interaction = new SessionInteractionView { DataContext = model };
            var window = new Window { Content = interaction };
            window.Show();
            var terminal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void Completed(SessionWorkspaceEntry target, ConversationTurnCompletion completion) => terminal.TrySetResult();
            try
            {
                await model.RefreshAsync();
                await model.SelectAsync(model.Sessions.Single(row => row.Authority.SessionId == original));
                var target = model.SelectedSessionRecord!;
                const string body = "  original USER e\u0301\r\n unchanged  ";
                const string unsent = "  independent unsent e\u0301\r\n next  ";
                model.ComposerBody = unsent;
                if (retirement is "arrive-other")
                { await model.SelectAsync(model.Sessions.Single(row => row.Authority.SessionId != original)); }
                composed.Conversation.SubmittedConversationCompleted += Completed;
                var submission = await Task.Run(() => composed.Conversation.SubmitAsync(target, body, static () => true, f.Token));
                await terminal.Task.WaitAsync(f.Token);
                dispatcher.Count.Should().BeGreaterThan(0);
                submission.Message.Outcome.Should().Be(detailMode is null
                    ? Kora.Core.Dependencies.ModelTurnOutcome.Unavailable : Kora.Core.Dependencies.ModelTurnOutcome.Succeeded);
                if (retirement is "switch" or "aba")
                {
                    await model.SelectAsync(model.Sessions.Single(row => row.Authority.SessionId != original));
                    model.ComposerBody = "foreign composer";
                    if (retirement is "aba")
                    { await model.SelectAsync(model.Sessions.Single(row => row.Authority.SessionId == original)); }
                }
                else if (retirement is "arrive-other") { await model.SelectAsync(target); }
                else if (retirement is "generation")
                {
                    await composed.Workspace.ChangeLifecycleAsync(original, target.Authority.Generation, false,
                        Kora.Core.Hosting.RequestOrigin.LocalUi, f.Token);
                    await model.RefreshWorkAsync();
                    await composed.Workspace.ChangeLifecycleAsync(original, model.SelectedSessionRecord!.Authority.Generation, true,
                        Kora.Core.Hosting.RequestOrigin.LocalUi, f.Token);
                    await model.RefreshWorkAsync();
                }
                else if (retirement is "revision") { composed.Access.ControlRevision++; }
                else if (retirement is "privacy")
                {
                    composed.Access.CanInspect = false;
                    composed.Access.CanControl = false;
                    composed.Access.ControlRevision++;
                }
                else if (retirement is "close") { model.Close(); }
                var status = model.ConversationStatus;
                var audit = WindowsSqliteConversationTests.AuditText(f);
                dispatcher.Drain();
                if (retirement is "current")
                {
                    model.ConversationMessages.Should().ContainSingle().Which.Should().Be(submission.Message);
                    if (detailMode is null)
                    { model.ConversationStatus.Should().Contain("HOST Unavailable").And.Contain("UnregisteredModel"); }
                    else
                    { model.ConversationStatus.Should().Contain("HOST Succeeded").And.Contain("Genuine ASSISTANT answer retained"); }
                    model.DetailsOfferMessage.Should().Be(detailMode is DetailPresentationMode.Offer ? submission.Message : null);
                }
                else
                {
                    model.ConversationMessages.Should().BeEmpty("an old terminal callback cannot attach to a later native source");
                    model.ConversationStatus.Should().Be(status);
                    model.DetailsOfferMessage.Should().BeNull("a retired callback cannot capture a future offer epoch");
                }
                WindowsSqliteConversationTests.AuditText(f).Should().Be(audit);
                preferences.Writes.Should().Be(0);
                if (adapter is not null) { adapter.Calls.Should().Be(1); }
                if (retirement is "current" or "aba" or "arrive-other" or "generation")
                {
                    model.ComposerBody.Should().Be(unsent);
                    interaction.FindControl<TextBox>("SessionComposer")!.Text.Should().Be(unsent);
                }
                if (retirement is "switch") { model.ComposerBody.Should().Be("foreign composer"); }
                var retained = (await f.Store.ReadConversationPageAsync(original, null, 25, f.Token)).Messages.Single();
                retained.Body.Should().Be(body);
                retained.Request.Should().Be(submission.Message.Request);
                retained.Generation.Should().Be(submission.Message.Generation);
                retained.Revision.Should().Be(submission.Message.Revision);
                if (detailMode is not null) { retained.AssistantResponse!.Body.Should().Be(DetailAnswerAdapter.Body); }
                f.Count("conversation_drafts").Should().Be(0);
                f.Count("host_questions").Should().Be(0);
                f.Count("scoped_grants").Should().Be(0);
                if (retirement is "aba" or "arrive-other" or "generation")
                {
                    await model.ReadConversationAsync();
                    model.ConversationMessages.Should().ContainSingle().Which.Body.Should().Be(body);
                    model.ComposerBody.Should().Be(unsent);
                    model.DetailsOfferMessage.Should().BeNull("explicit retained reading never renews a retired offer");
                    if (detailMode is not null)
                    { model.ConversationMessages.Single().AssistantResponse!.Body.Should().Be(DetailAnswerAdapter.Body); }
                }
            }
            finally
            {
                composed.Conversation.SubmittedConversationCompleted -= Completed;
                model.Close();
                window.Close();
                dispatcher.Drain();
            }
        });
    }
}
