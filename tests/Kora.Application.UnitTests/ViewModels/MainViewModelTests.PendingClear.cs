using System.Text.Json;

using AwesomeAssertions;

using Kora.Application.UnitTests.Dependencies;
using Kora.Core.Conversation;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task OriginalTypedAndActivatedPendingClearReviewsAndIndependentlyConfirmsActualHostBatch(bool voice, bool cancel)
    {
        var f = await Fixture.CreateInitializedAsync();
        f.ViewModel.HasSessionOperationReview.Should().BeFalse();
        await ModelTurnHostTests.WithPendingClearHost(async (service, workspace, commands, target) =>
        {
            f.ViewModel.BindSessionCommands(workspace, commands);
            var session = target.Authority.SessionId.Value.ToString("D");
            if (voice) { await f.RaiseActivatedTranscriptAsync("Kora, session pending clear " + session, 1); }
            else { await f.RunAsync("session pending clear " + session); }
            using var json = JsonDocument.Parse(f.ViewModel.ResponseBody);
            var id = json.RootElement.GetProperty("pendingClearReview").GetProperty("id").GetString();
            f.ViewModel.PendingClearReviewText.Should().Contain("never-dispatched").And.NotContain("private pending B");
            f.ViewModel.HasSessionOperationReview.Should().BeTrue();
            (await service.ReadPendingConversationAsync(target, () => true, TestContext.Current.CancellationToken))
                .Turns.Count(item => item.Phase == ConversationPendingPhase.Pending).Should().Be(2);
            var decision = "session pending " + (cancel ? "cancel " : "confirm ") + session + " " + id;
            if (voice) { await f.ViewModel.CapturePendingClearDecision(!cancel)(); }
            else { await f.RunAsync(decision); }
            f.ViewModel.ResponseBody.Should().Contain(cancel ? "review-discarded" : "reviewed-pending-cancelled");
            f.ViewModel.PendingClearReviewText.Should().BeEmpty();
            f.ViewModel.HasSessionOperationReview.Should().BeFalse();
            var remaining = await service.ReadPendingConversationAsync(target, () => true, TestContext.Current.CancellationToken);
            remaining.Turns.Count(item => item.Phase == ConversationPendingPhase.Pending).Should().Be(cancel ? 2 : 0);
            remaining.Turns.Should().ContainSingle(item => item.Phase == ConversationPendingPhase.Dispatching);
            f.Reasoner.Requests.Should().BeEmpty();
            await f.RunAsync(decision);
            f.ViewModel.ResponseBody.Should().NotContain("\"outcome\":\"reviewed-pending-cancelled\"");
        });
    }

    [Theory]
    [InlineData("edit-restore", false)]
    [InlineData("unrelated", false)]
    [InlineData("name", false)]
    [InlineData("privacy", false)]
    [InlineData("call", true)]
    [InlineData("generation", true)]
    [InlineData("ownership", true)]
    [InlineData("channel", true)]
    [InlineData("unprefixed", true)]
    public async Task PendingClearCannotRenewOriginalSourceThroughChangedInputChannelOrPrivateControl(string loss, bool voice)
    {
        var f = await Fixture.CreateInitializedAsync();
        await ModelTurnHostTests.WithPendingClearHost(async (service, workspace, commands, target) =>
        {
            f.ViewModel.BindSessionCommands(workspace, commands);
            var session = target.Authority.SessionId.Value.ToString("D");
            var review = "session pending clear " + session;
            if (voice) { await f.RaiseActivatedTranscriptAsync("Kora, " + review, 1); }
            else { await f.RunAsync(review); }
            using var json = JsonDocument.Parse(f.ViewModel.ResponseBody);
            var id = json.RootElement.GetProperty("pendingClearReview").GetProperty("id").GetString();
            var confirm = "session pending confirm " + session + " " + id;
            if (loss is "edit-restore") { f.ViewModel.CommandText = "replacement"; f.ViewModel.CommandText = review; }
            if (loss is "unrelated") { await f.RunAsync("session help"); }
            if (loss is "name") { await f.RunAsync("set assistant name to Nova"); }
            if (loss is "privacy")
            {
                f.ViewModel.CloseForObservedPrivacyEvent("Synthetic private control closure", true);
                await f.ViewModel.PrivacyClosureTask;
            }
            if (loss is "call")
            {
                f.CallState.SetState(Kora.Core.Communication.CallState.Active);
                await f.ViewModel.CallClosureTask;
            }
            if (loss is "generation") { f.Voice.AdvanceGeneration(); }
            if (loss is "ownership") { f.ViewModel.BindCallOwnershipGate(() => false, () => 2); }
            if (voice && loss is not "channel")
            { await f.RaiseActivatedTranscriptAsync((loss is "unprefixed" ? "" : "Kora, ") + confirm, 1); }
            else { await f.RunAsync(confirm); }
            var remaining = await service.ReadPendingConversationAsync(target, () => true, TestContext.Current.CancellationToken);
            remaining.Turns.Count(item => item.Phase == ConversationPendingPhase.Pending).Should().Be(2);
            remaining.Paused.Should().BeFalse();
            f.Reasoner.Requests.Should().BeEmpty();
        });
    }
}
