using AwesomeAssertions;

using Kora.Application.Hosting;
using Kora.Core.Hosting;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TypedAndCurrentNameVoiceReviewUseOriginalTargetBodyAndNativeConfirmation(bool voice)
    {
        var f = await Fixture.CreateInitializedAsync();
        var store = BindRequestCommands(f);
        await f.RunAsync("set assistant name to Nova");
        f.ViewModel.ForegroundRequestReviewText.Should().BeEmpty();
        if (voice)
        {
            await f.RunAsync("session rename " + store.Session.Authority.SessionId.Value.ToString("D")
                + " 1 0 \"Readable topic\"");
        }
        var target = new ForegroundConversationTarget(store.Session, () => true);
        f.ViewModel.PublishForegroundConversationTarget(target);
        f.ViewModel.PublishForegroundConversationTarget(target);
        const string body = "  shutdown e\u0301\U0001f642\r\n\tunchanged  ";
        if (voice) { await f.RaiseActivatedTranscriptAsync("Nova, ask this conversation " + body, 1); }
        else { await f.RunAsync("ask this conversation " + body); }
        f.ViewModel.HasForegroundRequestReview.Should().BeTrue();
        f.ViewModel.ForegroundRequestReviewText.Should().Contain(body).And.Contain("Default / local only");
        store.RequestRetentions.Should().Be(0);
        Kora.Core.Conversation.ConversationSubmissionReceipt? submitted = null;
        if (voice) { f.ViewModel.ForegroundRequestSubmitted += value => submitted = value; }
        await f.ViewModel.ConfirmForegroundRequestAsync();
        f.ViewModel.HasForegroundRequestReview.Should().BeFalse();
        store.RequestMessage!.Body.Should().Be(body);
        store.RequestMessage.Request.Origin.Should().Be(voice ? RequestOrigin.ActivatedVoice : RequestOrigin.LocalUi);
        store.RequestRetentions.Should().Be(1);
        f.ViewModel.ResponseBody.Should().Contain("HOST outcome: Unavailable")
            .And.NotContain(store.Session.Authority.SessionId.Value.ToString("D"));
        if (voice) { submitted!.Message.Should().Be(store.RequestMessage); }
        f.ViewModel.ForegroundRequestReviewText.Should().BeEmpty();
        f.Reasoner.Requests.Should().BeEmpty();
    }

    [Fact]
    public void ForegroundOriginalRequestGuideIsEmbeddedWithCompleteOperationAndExclusions()
    {
        var guide = new Kora.Application.Documentation.EmbeddedUserDocumentationProvider().GetPages()
            .Single(page => string.Equals(page.Id, "session-routing", StringComparison.Ordinal)).Markdown;
        guide.Should().Contain("ask this conversation <original body>").And.Contain("Confirm original request once")
            .And.Contain("60 seconds").And.Contain("unsent buffer").And.Contain("never Copilot/Claude fallback");
    }

    [Theory]
    [InlineData("absent")]
    [InlineData("focus")]
    [InlineData("cancel")]
    [InlineData("name")]
    [InlineData("queue")]
    [InlineData("unprefixed")]
    [InlineData("malformed")]
    public async Task ForegroundAdmissionRefusesWithoutFallbackOrRetainingWhenOriginalSourceChanges(string loss)
    {
        var f = await Fixture.CreateInitializedAsync();
        var store = BindRequestCommands(f);
        var target = new ForegroundConversationTarget(store.Session, () => true);
        if (loss is not "absent") { f.ViewModel.PublishForegroundConversationTarget(target); }
        if (loss is "queue")
        {
            f.Dispatcher.BeforeInvoke = () =>
                f.ViewModel.PublishForegroundConversationTarget(new(store.Session, () => true));
        }
        if (loss is "unprefixed" or "queue")
        { await f.RaiseActivatedTranscriptAsync((loss is "unprefixed" ? "" : "Kora, ") + "ask this conversation original", 1); }
        else { await f.RunAsync(loss is "malformed" ? "ask this conversation" : "ask this conversation original"); }
        if (loss is "focus") { f.ViewModel.PublishForegroundConversationTarget(null); }
        if (loss is "cancel") { await f.ViewModel.CancelForegroundRequestAsync(); }
        if (loss is "name") { await f.RunAsync("set assistant name to Nova"); }
        await f.ViewModel.ConfirmForegroundRequestAsync();
        store.RequestRetentions.Should().Be(0);
        f.ViewModel.HasForegroundRequestReview.Should().BeFalse();
        f.Reasoner.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task LateNativeDecisionCannotConfirmAReplacementReview()
    {
        var f = await Fixture.CreateInitializedAsync();
        var store = BindRequestCommands(f);
        f.ViewModel.PublishForegroundConversationTarget(new(store.Session, () => true));
        await f.RunAsync("ask this conversation first original");
        var original = f.ViewModel.CaptureForegroundRequestDecision(confirm: true);
        f.ViewModel.PublishForegroundConversationTarget(null);
        f.ViewModel.PublishForegroundConversationTarget(new(store.Session, () => true));
        await f.RunAsync("ask this conversation replacement original");
        await original();
        store.RequestRetentions.Should().Be(0);
        f.ViewModel.HasForegroundRequestReview.Should().BeFalse();
        f.Reasoner.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task AssistantNameOverlappingRequestGrammarStillNeedsASeparateVoiceActivation()
    {
        var f = await Fixture.CreateInitializedAsync();
        var store = BindRequestCommands(f);
        await f.RunAsync("set assistant name to Ask");
        f.ViewModel.PublishForegroundConversationTarget(new(store.Session, () => true));
        await f.RaiseActivatedTranscriptAsync("ask this conversation original", 1);
        f.ViewModel.HasForegroundRequestReview.Should().BeFalse();
        store.RequestRetentions.Should().Be(0);
        await f.RaiseActivatedTranscriptAsync("Ask, ask this conversation original", 1);
        f.ViewModel.HasForegroundRequestReview.Should().BeTrue();
        await f.ViewModel.ConfirmForegroundRequestAsync();
        store.RequestRetentions.Should().Be(1);
        store.RequestMessage!.Request.Origin.Should().Be(RequestOrigin.ActivatedVoice);
        f.Reasoner.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData("generation")]
    [InlineData("device")]
    [InlineData("voice")]
    [InlineData("call")]
    [InlineData("privacy")]
    [InlineData("ownership")]
    public async Task OriginalForegroundVoiceAdmissionCannotBeCleansedByNativeConfirmation(string loss)
    {
        var f = await Fixture.CreateInitializedAsync();
        var store = BindRequestCommands(f);
        f.ViewModel.PublishForegroundConversationTarget(new(store.Session, () => true));
        await f.RaiseActivatedTranscriptAsync("Kora, ask this conversation original private request", 1);
        f.ViewModel.HasForegroundRequestReview.Should().BeTrue();
        if (loss is "generation") { f.Voice.AdvanceGeneration(); }
        if (loss is "device") { f.ViewModel.SelectedMicrophone = null; }
        if (loss is "voice") { await f.ViewModel.ToggleListeningCommand.ExecuteAsync(); }
        if (loss is "ownership") { f.ViewModel.BindCallOwnershipGate(() => false, () => 2); }
        if (loss is "call")
        {
            f.CallState.SetState(Kora.Core.Communication.CallState.Active);
            await f.ViewModel.CallClosureTask;
        }
        if (loss is "privacy")
        {
            f.ViewModel.CloseForObservedPrivacyEvent("Synthetic privacy closure", true);
            await f.ViewModel.PrivacyClosureTask;
            f.ViewModel.ForegroundRequestReviewText.Should().BeEmpty();
        }
        await f.ViewModel.ConfirmForegroundRequestAsync();
        store.RequestRetentions.Should().Be(0);
        f.Reasoner.Requests.Should().BeEmpty();
    }
}
