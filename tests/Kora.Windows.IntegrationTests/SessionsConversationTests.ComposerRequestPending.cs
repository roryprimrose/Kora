using AwesomeAssertions;

using Kora.Application.Hosting;
using Kora.Windows.IntegrationTests.Storage;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionsConversationTests
{
    [Fact]
    public async Task ReviewedComposerQueuedAcceptanceSurvivesClearedInputAndSwitchingWithoutRetargeting()
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
            var model = composed.Model;
            var completed = new TaskCompletionSource<ConversationTurnCompletion>(TaskCreationOptions.RunContinuationsAsynchronously);
            void Complete(Kora.Core.Storage.SessionWorkspaceEntry target, ConversationTurnCompletion result)
            {
                if (target.Authority.SessionId == original && result.Receipt?.Message.Body is "reviewed queued input")
                { completed.TrySetResult(result); }
            }
            composed.Conversation.SubmittedConversationCompleted += Complete;
            try
            {
                await model.RefreshAsync();
                await model.SelectAsync(model.Sessions.Single(row => row.Authority.SessionId == original));
                model.ComposerBody = "first ordinary request";
                var firstSubmission = model.SubmitConversationAsync();
                var first = await adapter.NextAsync();
                await WhenModelAsync(model, () => model.CanCompose && !model.ComposerDirty);
                model.ComposerBody = "reviewed queued input";
                await model.ReviewComposerRequestAsync();
                model.CanConfirmComposerRequest.Should().BeTrue(model.ConversationStatus);
                await model.CaptureComposerRequestDecision(true)();
                model.ComposerBody.Should().BeEmpty();
                model.ConversationStatus.Should().Contain("accepted and pending");
                await model.SelectAsync(model.Sessions.Single(row => row.Authority.SessionId != original));
                model.ComposerBody = "another conversation unsent input";
                first.Release();
                await firstSubmission;
                var second = await adapter.NextAsync();
                second.Body.Should().Be("reviewed queued input");
                second.Provenance.Request.SessionId.Should().Be(original);
                second.Release();
                var receipt = (await completed.Task.WaitAsync(f.Token)).RequireReceipt();
                receipt.Message.AssistantResponse!.Body.Should().Be("answer: reviewed queued input");
                model.ComposerBody.Should().Be("another conversation unsent input");
                model.ConversationMessages.Should().BeEmpty();
                (await f.Store.ReadConversationPageAsync(f.Request.SessionId, null, 25, f.Token)).Messages.Should().BeEmpty();
                f.Count("conversation_drafts").Should().Be(0);
                f.Count("host_questions").Should().Be(0);
                f.Count("scoped_grants").Should().Be(0);
            }
            finally
            {
                adapter.ReleaseAll();
                composed.Conversation.SubmittedConversationCompleted -= Complete;
                model.Close();
            }
        });
    }
}
