using Avalonia.Controls;
using Avalonia.Interactivity;

using AwesomeAssertions;

using Kora.Core.Dependencies;
using Kora.Windows.IntegrationTests.Storage;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionsConversationTests
{
    [Theory]
    [InlineData("confirm")]
    [InlineData("cancel")]
    [InlineData("selection")]
    [InlineData("tuple")]
    [InlineData("hidden")]
    public async Task NativeFailedLocalProfileCompletesExactNewAttemptOrDeclineWithoutChangingFailureOrUnsentText(string decision)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            var adapter = new FailedRetryAdapter();
            await using var composed = new Composition(f, adapter: adapter,
                localRequestCatalogue: new SessionsProviderProfileTests.Catalogue(f.Time));
            var model = composed.Model;
            var window = new SessionsWindow(model);
            window.Show();
            try
            {
                await model.RefreshAsync();
                await model.SelectAsync(model.Sessions.Single());
                await model.RefreshProviders();
                const string body = "  unchanged original failed input e\u0301\r\n  ";
                model.ComposerBody = body;
                model.LocalRequestModel = model.LocalRequestModels[0];
                model.LocalRequestReasoning = model.LocalRequestReasonings[0];
                model.LocalRequestContext = model.LocalRequestContexts[0];
                await model.ReviewLocalRequestProfileAsync();
                await model.ConfirmLocalRequestProfileAsync();
                await model.ReadConversationAsync();
                var original = model.ConversationMessages.Single();
                original.Outcome.Should().Be(ModelTurnOutcome.QuotaExceeded);
                var policy = await f.Store.ReadProviderProfile(f.Request.SessionId, f.Token);
                model.SelectedAnswerRecord = original;
                model.LocalRequestReasoning = model.LocalRequestReasonings[^1];
                model.LocalRequestContext = model.LocalRequestContexts[^1];
                model.ComposerBody = body;
                model.CanReviewFailedLocalProfile.Should().BeTrue(model.ConversationStatus);
                window.FindControl<Button>("ReviewFailedLocalProfile")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await window.ConversationOperation;
                model.CanConfirmLocalRequestProfile.Should().BeTrue(model.ConversationStatus);
                model.LocalRequestProfileText.Should().Contain(body).And.Contain("Original provider: Ollama")
                    .And.Contain("effort-next").And.Contain("window-next").And.Contain("NEW LOCAL attempt");
                f.Count("conversation_messages").Should().Be(1);
                adapter.Calls.Should().Be(1);
                var confirm = model.CaptureLocalRequestProfileDecision(true);
                if (decision is "cancel") { model.CancelLocalRequestProfile(); }
                if (decision is "selection") { model.SelectedAnswerRecord = null; }
                if (decision is "tuple") { model.LocalRequestReasoning = model.LocalRequestReasonings[0]; }
                if (decision is "hidden") { model.SetProviderSurfaceOpen(false); }
                await confirm();
                if (decision is "confirm")
                {
                    model.ConversationStatus.Should().Contain("Succeeded").And.Contain("Original failure");
                    adapter.Profiles[1]!.Reasoning.Value.Should().Be("effort-next");
                    adapter.Profiles[1]!.Context.Value.Should().Be("window-next");
                    await model.ReadConversationAsync();
                    var answer = model.ConversationMessages.Single(item => item.AssistantResponse is not null);
                    answer.Body.Should().Be(body);
                    answer.AssistantResponse!.Body.Should().Be("genuine new answer");
                    answer.Request.SessionId.Should().Be(original.Request.SessionId);
                    answer.Request.Origin.Should().Be(original.Request.Origin);
                    answer.Request.TaskId.Should().NotBe(original.Request.TaskId);
                    adapter.Calls.Should().Be(2);
                    f.Count("conversation_messages").Should().Be(2);
                }
                else
                {
                    adapter.Calls.Should().Be(1);
                    f.Count("conversation_messages").Should().Be(1);
                }
                model.ComposerBody.Should().Be(body, "failed-source confirmation must not consume even identical unsent text");
                (await f.Store.ReadConversationMessageAsync(original.Request.SessionId, original.Request.RequestId, f.Token))
                    .Should().Be(original);
                var after = await f.Store.ReadProviderProfile(f.Request.SessionId, f.Token);
                after.Should().Be(policy);
                new Kora.Application.Configuration.LocalProviderProfilePreferences(f.Paths).Load().Should().BeNull();
                f.Count("conversation_drafts").Should().Be(0);
                f.Count("scoped_grants").Should().Be(0);
                f.Count("host_questions").Should().Be(0);
                WindowsSqliteConversationTests.AuditText(f).Should().NotContain(body).And.NotContain("genuine new answer");
                composed.Turns.IsQuiescent.Should().BeTrue();
            }
            finally { window.ClosePrivate(); }
        });
    }
}
