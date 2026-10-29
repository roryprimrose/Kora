using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using AwesomeAssertions;
using Kora.Application.Diagnostics;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.Windows.IntegrationTests.Dependencies;
using Kora.Windows.IntegrationTests.Storage;
using Kora.Windows.Storage;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

public sealed partial class ClaudeUiContractTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task ExplicitNativeHostedQuestionReusesRealQuestionWindowAndCanonicalAnswer(bool compact, bool cancel)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            f.Time.UseFrozenTimers = true;
            await f.InitializeAsync(); await WindowsSqliteConversationTests.FinishInitial(f);
            await using var c = new HostedClaudeSessionRequestTests.Composition(f); await c.InitializeAsync();
            var sink = new WindowsSqliteEvidenceSink(f.Paths); sink.Initialize();
            var model = new SessionsViewModel(c.Workspace,
                new DurableEvidenceQuery(new WindowsSqliteEvidenceReader(sink), c.Access, f.Time,
                    NullLogger<DurableEvidenceQuery>.Instance), c.Access, NullLogger<SessionsViewModel>.Instance,
                conversation: c.Conversation);
            var owner = new SessionsPresentationOwner(model);
            var workspace = compact ? null : new SessionsWindow(model, owner);
            var small = compact ? new CompactSessionWindow(model, owner) : null;
            Window window = workspace ?? (Window)small!;
            window.Show();
            using var host = new NativeQuestionHost(f.Store, f.Time, NullLogger<NativeQuestionViewModel>.Instance);
            host.BindGate(() => true); host.BindAdmissionRevision(() => 1); host.BindPrivateRevision(() => 1);
            var messages = 0;
            c.Http.Respond = request => ClaudeAccountBrokerTests.Fixture.Json(request.RequestUri!.AbsolutePath.EndsWith("count_tokens", StringComparison.Ordinal)
                ? """{"input_tokens":25}""" : ++messages == 1
                    ? HostedClaudeSessionRequestTests.QuestionProposal() : ClaudeMessagesRuntimeTests.Answer);
            c.Reviews.ClaudeOffered += review =>
            { using var presentation = review.Presentation(); c.Reviews.Confirm(review, true, RequestOrigin.LocalUi).Should().BeTrue(); };
            QuestionWindow? question = null;
            model.QuestionPresenter = async (request, generation, spec, expiry, admitted, token) =>
                await Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    try
                    {
                        return await host.AskConversationAsync(request, generation, spec, expiry, admitted, async state =>
                        {
                            question = new(state, new NativeDetailRenderer(NullLogger<NativeDetailRenderer>.Instance));
                            question.Show(window);
                            await state.ReviewAsync();
                            if (cancel) { await state.CancelAsync(); }
                            else { state.Edit(new(["option-1"])); await state.SubmitAsync(); }
                        }, token);
                    }
                    finally { question?.Close(); }
                });
            try
            {
                await model.RefreshAsync(); await model.SelectAsync(model.Sessions.Single()); await model.ReadConversationAsync();
                model.ComposerBody = "original native hosted question";
                var control = window.FindControl<Button>("SubmitClaudeWithClarification")!;
                control.IsEffectivelyEnabled.Should().BeTrue();
                control.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await (workspace?.ConversationOperation ?? small!.ConversationOperation);
                model.ConversationStatus.Should().Contain(cancel ? "Denied" : "Succeeded");
                var stored = (await f.Store.ReadConversationPageAsync(f.Request.SessionId, null, 25, f.Token)).Messages.Single();
                stored.Body.Should().Be("original native hosted question");
                stored.AssistantResponse?.Body.Should().Be(cancel ? null : "genuine exact answer");
                c.Http.Requests.Count(item => item.Method is "POST").Should().Be(cancel ? 2 : 4);
                c.Boundary.VersionReads.Should().Be(0);
                c.Turns.IsQuiescent.Should().BeTrue();
            }
            finally { question?.Close(); owner.Revoke(); window.Close(); }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HostedQuestionControlFailsClosedWithoutTheComposedQuestionPresenter(bool compact)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync(); await WindowsSqliteConversationTests.FinishInitial(f);
            await using var c = new HostedClaudeSessionRequestTests.Composition(f); await c.InitializeAsync();
            var sink = new WindowsSqliteEvidenceSink(f.Paths); sink.Initialize();
            var model = new SessionsViewModel(c.Workspace,
                new DurableEvidenceQuery(new WindowsSqliteEvidenceReader(sink), c.Access, f.Time,
                    NullLogger<DurableEvidenceQuery>.Instance), c.Access, NullLogger<SessionsViewModel>.Instance,
                conversation: c.Conversation);
            var owner = new SessionsPresentationOwner(model);
            Window window = compact ? new CompactSessionWindow(model, owner) : new SessionsWindow(model, owner);
            window.Show();
            try
            {
                await model.RefreshAsync(); await model.SelectAsync(model.Sessions.Single()); await model.ReadConversationAsync();
                model.ComposerBody = "must not submit";
                window.FindControl<Button>("SubmitClaudeWithClarification")!.IsEffectivelyEnabled.Should().BeFalse();
                await model.SubmitClaudeWithClarificationAsync();
                (await f.Store.ReadConversationPageAsync(f.Request.SessionId, null, 25, f.Token)).Messages.Should().BeEmpty();
                c.Http.Requests.Should().OnlyContain(item => string.Equals(item.Method, "GET", StringComparison.Ordinal));
            }
            finally { owner.Revoke(); window.Close(); }
        });
    }
}
