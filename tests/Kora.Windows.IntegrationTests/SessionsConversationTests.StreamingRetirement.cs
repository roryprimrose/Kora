using Avalonia.Controls;
using Avalonia.VisualTree;

using AwesomeAssertions;

using Kora.Core.Dependencies;
using Kora.Windows.IntegrationTests.Storage;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionsConversationTests
{
    [Theory]
    [InlineData("primary", false)]
    [InlineData("sessions", false)]
    [InlineData("compact", false)]
    [InlineData("primary", true)]
    [InlineData("sessions", true)]
    [InlineData("compact", true)]
    public async Task PreviousTurnCompletionCannotRetireTheNextStreamingAnswer(string surface, bool cancel)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            var adapter = new ProvisionalConversationAdapter { BufferedAnswer = "first exact final answer" };
            await using var composed = new Composition(f, adapter: adapter);
            var dispatcher = new DeferredConversationDispatcher();
            var model = CreateDeferredPresentation(f, composed, dispatcher);
            var owner = new SessionsPresentationOwner(model);
            var view = surface is "primary" ? new SessionInteractionView { DataContext = model } : null;
            Window window = surface switch
            {
                "sessions" => new SessionsWindow(model, owner),
                "compact" => new CompactSessionWindow(model, owner),
                _ => new Window { Content = view },
            };
            window.Show();
            Task operation = Task.CompletedTask;
            try
            {
                await model.RefreshAsync();
                await model.SelectAsync(model.Sessions.Single());
                await model.ReadConversationAsync();
                var target = model.SelectedSessionRecord!;
                var previous = await Task.Run(() => composed.Conversation.SubmitAsync(target,
                    "first original question", static () => true, f.Token));
                previous.Message.AssistantResponse!.Body.Should().Be("first exact final answer");
                dispatcher.Count.Should().BeGreaterThan(0);

                model.ComposerBody = "next original local question";
                operation = model.SubmitLocalAnswerAsync();
                var first = await Task.WhenAny(adapter.Started.Task, operation).WaitAsync(f.Token);
                first.Should().BeSameAs(adapter.Started.Task, model.ConversationStatus);
                var provenance = await adapter.Started.Task;
                provenance.Request.Should().NotBe(previous.Message.Request);
                model.ProvisionalAnswer.Should().Be("provisional answer");
                window.UpdateLayout();
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                var displayed = window.GetVisualDescendants().OfType<TextBlock>()
                    .Single(value => string.Equals(value.Text, "provisional answer", StringComparison.Ordinal));

                dispatcher.Drain();
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                model.ProvisionalAnswer.Should().Be("provisional answer",
                    "an earlier request's terminal notification cannot retire the current request's pane");
                displayed.Text.Should().Be("provisional answer");
                model.HasProvisionalAnswer.Should().BeTrue();
                model.CanCancelRunningConversation.Should().BeTrue();
                model.RunningConversationStatus.Should().Contain(provenance.Request.TaskId.Value.ToString("D"))
                    .And.NotContain(previous.Message.Request.TaskId.Value.ToString("D"));
                var pending = await f.Store.ReadConversationMessageAsync(provenance.Request.SessionId,
                    provenance.Request.RequestId, f.Token);
                pending.AssistantResponse.Should().BeNull();
                pending.Outcome.Should().BeNull();
                operation.IsCompleted.Should().BeFalse();
                if (cancel)
                {
                    await model.CancelRunningConversationAsync();
                    model.ProvisionalAnswer.Should().BeEmpty();
                    operation.IsCompleted.Should().BeFalse("cancellation does not terminate the held provider");
                }
                adapter.Release.TrySetResult();
                await operation;
                dispatcher.Drain();
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                model.ProvisionalAnswer.Should().BeEmpty();
                model.HasProvisionalAnswer.Should().BeFalse();
                displayed.Text.Should().BeEmpty();
                var final = await f.Store.ReadConversationMessageAsync(provenance.Request.SessionId,
                    provenance.Request.RequestId, f.Token);
                final.Outcome.Should().Be(cancel ? ModelTurnOutcome.Cancelled : ModelTurnOutcome.Succeeded);
                if (cancel) { final.AssistantResponse.Should().BeNull(); }
                else { final.AssistantResponse!.Body.Should().Be("provisional answer completed"); }
                (await f.Store.ReadConversationMessageAsync(previous.Message.Request.SessionId,
                    previous.Message.Request.RequestId, f.Token)).Should().Be(previous.Message);
                model.RunningConversationStatus.Should().Contain(cancel ? "Terminal Cancelled" : "Terminal Succeeded");
                composed.Turns.IsQuiescent.Should().BeTrue();
                adapter.BufferedCalls.Should().Be(1);
                f.Count("conversation_messages").Should().Be(2);
                f.Count("conversation_drafts").Should().Be(0);
                f.Count("host_questions").Should().Be(0);
                f.Count("scoped_grants").Should().Be(0);
            }
            finally
            {
                adapter.Release.TrySetResult();
                await operation;
                owner.Revoke();
                model.Close();
                window.Close();
                dispatcher.Drain();
            }
        });
    }
}
