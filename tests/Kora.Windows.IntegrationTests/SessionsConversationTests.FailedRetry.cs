using System.Text.Json;

using Avalonia.Controls;
using Avalonia.Interactivity;

using AwesomeAssertions;

using Kora.Application.Dependencies;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Providers;
using Kora.Core.Tools;
using Kora.Windows.IntegrationTests.Storage;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionsConversationTests
{
    [Fact]
    public async Task NativeFailedRequestReviewDeclineAndSeparateConfirmationRunOneGenuineNewAttemptWithoutChangingOriginalOrUnsentInput()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            var adapter = new FailedRetryAdapter();
            await using var composed = new Composition(f, adapter: adapter);
            var model = composed.Model;
            var window = new SessionsWindow(model);
            window.Show();
            try
            {
                await model.RefreshAsync();
                await model.SelectAsync(model.Sessions.Single());
                const string body = "  original failed USER e\u0301\r\ninput  ";
                model.ComposerBody = body;
                Click("SubmitConversation");
                await window.ConversationOperation;
                var failed = model.ConversationMessages.Single();
                failed.Outcome.Should().Be(ModelTurnOutcome.QuotaExceeded);
                failed.Execution!.Selection.Should().Be(ModelProviderSelection.OllamaCandidate);
                model.ComposerBody = "unrelated unsent input must survive";
                await model.ReadConversationAsync();
                model.SelectedAnswerRecord = model.ConversationMessages.Single();
                Click("ReviewFailedRetry");
                await window.ConversationOperation;
                window.FindControl<TextBox>("FailedRetryReview")!.Text.Should().Contain(body).And.Contain("Ollama")
                    .And.Contain("NEW attempt");
                model.ComposerBody.Should().Be("unrelated unsent input must survive");
                f.Count("conversation_messages").Should().Be(1);
                adapter.Calls.Should().Be(1);
                Click("CancelFailedRetry");
                model.CanConfirmFailedRetry.Should().BeFalse();
                model.ComposerBody.Should().Be("unrelated unsent input must survive");
                Click("ReviewFailedRetry");
                await window.ConversationOperation;
                await model.RefreshPendingConversationAsync();
                model.CanResumePendingConversation.Should().BeTrue("the failed lane stays held until its existing explicit resume");
                await model.ResumePendingConversationAsync();
                Click("ConfirmFailedRetry");
                await window.ConversationOperation;
                adapter.Calls.Should().Be(2);
                model.ConversationMessages.Single().AssistantResponse!.Body.Should().Be("genuine new answer");
                await model.ReadConversationAsync();
                model.ConversationMessages.Should().HaveCount(2);
                var answer = model.ConversationMessages.Single(item => item.AssistantResponse is not null);
                answer.Body.Should().Be(body);
                answer.AssistantResponse!.Body.Should().Be("genuine new answer");
                answer.Request.SessionId.Should().Be(failed.Request.SessionId);
                answer.Request.Origin.Should().Be(failed.Request.Origin);
                answer.Request.TaskId.Should().NotBe(failed.Request.TaskId);
                answer.Request.RequestId.Should().NotBe(failed.Request.RequestId);
                (await f.Store.ReadConversationMessageAsync(failed.Request.SessionId, failed.Request.RequestId, f.Token))
                    .Should().Be(failed);
                await model.ConfirmFailedRetryAsync();
                adapter.Calls.Should().Be(2, "a consumed confirmation cannot replay");
                model.ComposerBody.Should().Be("unrelated unsent input must survive");
                f.Count("conversation_drafts").Should().Be(0);
                f.Count("scoped_grants").Should().Be(0);
                f.Count("host_questions").Should().Be(0);
                WindowsSqliteConversationTests.AuditText(f).Should().NotContain(body).And.NotContain("genuine new answer");
                composed.Turns.IsQuiescent.Should().BeTrue();
            }
            finally { window.ClosePrivate(); }

            void Click(string name) => window.FindControl<Button>(name)!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        });
    }

    [Fact]
    public async Task NativeLegacyFailureWithoutRecordedProviderIsExplicitlyUnavailableAndCannotReplaceUnsentInput()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            await using var composed = new Composition(f);
            var model = composed.Model;
            var window = new SessionsWindow(model);
            window.Show();
            try
            {
                await model.RefreshAsync();
                await model.SelectAsync(model.Sessions.Single());
                model.ComposerBody = "original unavailable request";
                await model.SubmitConversationAsync();
                await model.ReadConversationAsync();
                model.SelectedAnswerRecord = model.ConversationMessages.Single();
                model.ComposerBody = "still unsent";
                await model.ReviewFailedRetryAsync();
                model.ConversationStatus.Should().Contain("no recorded original provider");
                model.CanConfirmFailedRetry.Should().BeFalse();
                model.ComposerBody.Should().Be("still unsent");
                f.Count("conversation_messages").Should().Be(1);
                f.Count("conversation_drafts").Should().Be(0);
            }
            finally { window.ClosePrivate(); }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativeFailedRetryCancellationOrPrivateWithdrawalJoinsPhysicalProviderAndRejectsIgnoredLateAnswer(bool withdraw)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            var adapter = new FailedRetryAdapter { HoldSecond = true };
            await using var composed = new Composition(f, adapter: adapter);
            var model = composed.Model;
            var window = new SessionsWindow(model);
            window.Show();
            try
            {
                await model.RefreshAsync();
                await model.SelectAsync(model.Sessions.Single());
                model.ComposerBody = "original failure for cancelled retry";
                await model.SubmitConversationAsync();
                var failed = model.ConversationMessages.Single();
                await model.RefreshPendingConversationAsync();
                await model.ResumePendingConversationAsync();
                await model.ReadConversationAsync();
                model.SelectedAnswerRecord = model.ConversationMessages.Single();
                await model.ReviewFailedRetryAsync();
                var operation = model.ConfirmFailedRetryAsync();
                var provenance = await adapter.Started.Task.WaitAsync(f.Token);
                await model.RefreshRunningConversationAsync();
                Task? physicalJoin = null;
                if (withdraw)
                {
                    window.ClosePrivate();
                    physicalJoin = composed.Conversation.DisposeAsync().AsTask();
                    physicalJoin.IsCompleted.Should().BeFalse("private withdrawal does not prove a non-cooperative provider stopped");
                }
                else
                {
                    await model.CancelRunningConversationAsync();
                    model.RunningConversationStatus.Should().Contain("Cancelling");
                }
                operation.IsCompleted.Should().BeFalse();
                composed.Turns.IsQuiescent.Should().BeFalse();
                adapter.Release.TrySetResult(new(ModelTurnOutcome.Succeeded, provenance.Selection, new("forbidden late retry answer", null)));
                await operation;
                if (physicalJoin is not null) { await physicalJoin; }
                var retry = await f.Store.ReadConversationMessageAsync(provenance.Request.SessionId, provenance.Request.RequestId, f.Token);
                retry.AssistantResponse.Should().BeNull();
                (await f.Store.ReadConversationMessageAsync(failed.Request.SessionId, failed.Request.RequestId, f.Token)).Should().Be(failed);
                adapter.Calls.Should().Be(2);
                composed.Turns.IsQuiescent.Should().BeTrue();
                f.Count("scoped_grants").Should().Be(0);
                f.Count("conversation_drafts").Should().Be(0);
                WindowsSqliteConversationTests.AuditText(f).Should().NotContain("forbidden late retry answer");
            }
            finally
            {
                adapter.Release.TrySetResult(new(ModelTurnOutcome.Unknown, ModelProviderSelection.OllamaCandidate));
                window.ClosePrivate();
                await composed.Conversation.DisposeAsync();
            }
        });
    }

    private sealed class FailedRetryAdapter : IModelTurnAdapter
    {
        internal int Calls { get; private set; }
        internal bool HoldSecond { get; init; }
        internal TaskCompletionSource<ModelTurnProvenance> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource<ModelProviderReply> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<ModelProviderReply> RunAsync(ModelTurnProvenance provenance, ProviderProfileTuple? profile,
            ReadOnlyMemory<byte> context, Func<string, string, CancellationToken, ValueTask<CapabilityReply>> invokeTool,
            CancellationToken token)
        {
            Calls++;
            using var wire = JsonDocument.Parse(context);
            wire.RootElement.GetProperty("Evidence").GetArrayLength().Should().Be(0);
            if (Calls == 2 && HoldSecond)
            {
                Started.SetResult(provenance);
                return await Release.Task;
            }
            return Calls == 1
                ? new ModelProviderReply(ModelTurnOutcome.QuotaExceeded, provenance.Selection)
                : new(ModelTurnOutcome.Succeeded, provenance.Selection, new("genuine new answer", null));
        }
    }
}
