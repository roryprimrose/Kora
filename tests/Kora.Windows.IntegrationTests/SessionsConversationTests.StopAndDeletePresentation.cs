using AwesomeAssertions;

using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Storage;
using Kora.Windows.IntegrationTests.Storage;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionsConversationTests
{
    [Theory]
    [InlineData("selection")]
    [InlineData("selection-return")]
    [InlineData("control-return")]
    public async Task StopAndDeleteCompletionCannotReattachRetiredNativePresentation(string change)
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
            var other = f.Request.SessionId;
            var adapter = new GateableConversationAdapter();
            await using var composed = new Composition(f, adapter: adapter);
            var model = composed.Model;
            var window = new SessionsWindow(model);
            window.Show();
            Task? submission = null;
            Task? confirmation = null;
            try
            {
                await model.RefreshAsync();
                await model.SelectAsync(model.Sessions.Single(row => row.Authority.SessionId == other));
                model.ComposerBody = "independent unsent input";
                await model.SelectAsync(model.Sessions.Single(row => row.Authority.SessionId == original));
                model.ComposerBody = "original unsent input";
                var target = model.SelectedSessionRecord!;
                submission = composed.Conversation.SubmitAsync(target, "original running request", static () => true, f.Token);
                var provenance = await adapter.Started.Task;
                var stopping = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                composed.Conversation.RunningConversationChanged += observed =>
                {
                    if (observed.Target.Request == provenance.Request && observed.Phase == ConversationExecutionPhase.Cancelling)
                    { stopping.TrySetResult(); }
                };
                await model.ReviewStopAndDeleteAsync();
                model.CanConfirmStopAndDelete.Should().BeTrue();
                confirmation = model.ConfirmStopAndDeleteAsync();
                await stopping.Task.WaitAsync(f.Token);
                confirmation.IsCompleted.Should().BeFalse("physical provider completion is still owned");

                if (change is "control-return")
                {
                    composed.Access.CanControl = false;
                    composed.Access.ControlRevision++;
                    composed.Access.CanControl = true;
                }
                else
                {
                    await model.SelectAsync(model.Sessions.Single(row => row.Authority.SessionId == other));
                    if (change is "selection-return")
                    { await model.SelectAsync(model.Sessions.Single(row => row.Authority.SessionId == original)); }
                }
                var selected = model.SelectedSessionRecord;
                var input = model.ComposerBody;
                var status = model.StopAndDeleteStatus;
                confirmation.IsCompleted.Should().BeFalse("retirement cannot abandon the original join");
                adapter.Release.SetResult(new(ModelTurnOutcome.Succeeded, provenance.Selection, new("forbidden late answer", null)));
                try { await submission; }
                catch (ConversationSubmissionUnconfirmedException) { }
                await confirmation;

                model.SelectedSessionRecord.Should().Be(selected);
                model.ComposerBody.Should().Be(input);
                model.StopAndDeleteStatus.Should().Be(status, "an old completion cannot reattach to another selection or private-control lifetime");
                model.CanConfirmStopAndDelete.Should().BeFalse();
                (await f.Store.ReadHistoryAsync(original, null, 50, f.Token)).Disposed.Should().BeFalse();
                (await f.Store.ReadMetadataAsync(other, f.Token)).Authority.IsActive.Should().BeTrue();
                (await f.Store.ReadConversationMessageAsync(original, provenance.Request.RequestId, f.Token))
                    .AssistantResponse.Should().BeNull();
                (await f.Store.ReadConversationPageAsync(other, null, 25, f.Token)).Messages.Should().BeEmpty();
                (await f.Store.ReadConversationDraftAsync(other, f.Token)).Draft.Body.Should().BeNull();
                if (change is not "control-return")
                {
                    await model.SelectAsync(model.Sessions.Single(row => row.Authority.SessionId == original));
                    model.ComposerBody.Should().Be("original unsent input");
                    await model.ReviewStopAndDeleteAsync();
                    model.StopAndDeleteStatus.Should().Contain("previously reviewed work physically stopped");
                    await model.ConfirmStopAndDeleteAsync();
                    model.StopAndDeleteStatus.Should().Contain("logically deleted");
                    model.ComposerBody.Should().BeEmpty();
                    (await f.Store.ReadHistoryAsync(original, null, 50, f.Token)).Disposed.Should().BeTrue();
                    await model.RefreshAsync();
                    await model.SelectAsync(model.Sessions.Single(row => row.Authority.SessionId == other));
                    model.ComposerBody.Should().Be("independent unsent input");
                }
            }
            finally
            {
                adapter.Release.TrySetResult(new(ModelTurnOutcome.Unknown, ModelProviderSelection.OllamaCandidate));
                if (submission is not null)
                {
                    try { await submission; }
                    catch (ConversationSubmissionUnconfirmedException) { }
                }
                if (confirmation is not null) { await confirmation; }
                window.ClosePrivate();
            }
        });
    }
}
