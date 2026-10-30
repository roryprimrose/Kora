using AwesomeAssertions;
using System.Text.Json;
using Kora.Application.Voice;
using Kora.Application.Interaction;
using Kora.Application.UnitTests.Interaction;
using Kora.Core.Commands;
using Kora.Core.Communication;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Platform;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    private static async Task<Fixture> ReviewedCommandFixture(bool speechProfile = false)
    {
        var fixture = speechProfile ? new Fixture(enableSpeechInput: true, enableQuestionReplies: true) : CreateVoicePrivacyFixture();
        fixture.VoiceConsent.Consent = true;
        fixture.Voice.Microphones = [new("mic", "Headset")];
        fixture.Voice.DefaultMicrophoneId = "mic";
        fixture.ViewModel.BindVoiceOwnershipGate(static () => true);
        fixture.ViewModel.BindCallOwnershipGate(static () => true);
        BindAdmittedLockResourceOwner(fixture);
        await fixture.ViewModel.InitializeAsync();
        fixture.ViewModel.BindReviewedVoiceCommand(NullLogger<ReviewedVoiceCommandCapture>.Instance, TimeProvider.System);
        return fixture;
    }

    [Fact]
    public async Task ReviewedCommandMissingCompositionIsExplicitAndASecondOwnerIsRejected()
    {
        var f = CreateVoicePrivacyFixture();
        f.ViewModel.IsReviewedVoiceCommandActive.Should().BeFalse();
        f.ViewModel.HasReviewedVoiceCommand.Should().BeFalse();
        f.ViewModel.ReviewedVoiceCommandText.Should().BeEmpty();
        f.ViewModel.ReviewedVoiceCommandStatus.Should().Contain("Review one spoken command");
        await f.ViewModel.Invoking(v => v.BeginReviewedVoiceCommandAsync(static () => true))
            .Should().ThrowAsync<InvalidOperationException>();
        await f.ViewModel.Invoking(v => v.FinishReviewedVoiceCommandAsync())
            .Should().ThrowAsync<InvalidOperationException>();
        var edit = () => f.ViewModel.CaptureReviewedVoiceCommandEdit()("text");
        edit.Should().Throw<InvalidOperationException>();
        var confirm = () => f.ViewModel.CaptureReviewedVoiceCommandConfirmation()();
        await confirm.Should().ThrowAsync<InvalidOperationException>();
        await f.ViewModel.CancelReviewedVoiceCommandAsync();
        f.ViewModel.BindReviewedVoiceCommand(NullLogger<ReviewedVoiceCommandCapture>.Instance, TimeProvider.System);
        f.ViewModel.Invoking(v => v.BindReviewedVoiceCommand(NullLogger<ReviewedVoiceCommandCapture>.Instance, TimeProvider.System))
            .Should().Throw<InvalidOperationException>();
        f.ViewModel.Dispose();
    }

    [Fact]
    public async Task ReviewedCommandProfileSnapshotCannotBeReplacedByAnotherCaptureOwner()
    {
        var f = await ReviewedCommandFixture(speechProfile: true);
        await using var promptInput = f.ViewModel.BindPromptInputVoice(
            NullLogger<Kora.Application.Definitions.PromptInputVoiceController>.Instance)!;
        await f.ViewModel.BeginReviewedVoiceCommandAsync(static () => true);
        f.ViewModel.IsReviewedVoiceCommandActive.Should().BeTrue(f.ViewModel.ReviewedVoiceCommandStatus);
        f.Voice.IsLiteralDictationAvailable = true;
        using var composer = DictationComposer(f);
        await using var dictation = f.ViewModel.BindComposerDictation(NullLogger<ComposerDictationService>.Instance);
        await dictation.BeginAsync(composer, static () => true, TestContext.Current.CancellationToken);
        dictation.IsActive.Should().BeFalse();
        await f.ViewModel.BeginPushToTalkAsync();
        f.Voice.StartCalls.Should().Be(1);
        await f.ViewModel.CancelReviewedVoiceCommandAsync();
        f.Reasoner.Requests.Should().BeEmpty();
        f.ViewModel.Dispose();
    }

    [Fact]
    public async Task ReviewedCommandRequiresTheIndependentControlOwner()
    {
        var f = await ReviewedCommandFixture();
        f.ViewModel.BindCallOwnershipGate(static () => false);
        await f.ViewModel.BeginReviewedVoiceCommandAsync(static () => true);
        f.Voice.StartCalls.Should().Be(0);
        f.ViewModel.IsReviewedVoiceCommandActive.Should().BeFalse();
        f.ViewModel.Dispose();
    }

    [Fact]
    public async Task ReviewedCommandQuestionArrivalRetiresCandidateAndPreservesDedicatedReplyOwner()
    {
        var f = await ReviewedCommandFixture(speechProfile: true);
        using var host = new InteractionFixture();
        await using var replies = f.ViewModel.BindQuestionVoiceReplies(host.Questions, host.Time,
            NullLogger<HostQuestionVoiceReplyService>.Instance)!;
        await f.ViewModel.BeginReviewedVoiceCommandAsync(static () => true);
        var generation = f.Voice.Generation;
        await f.Voice.RaiseTranscriptAsync("Kora, lock the machine", 1);
        await f.ViewModel.FinishReviewedVoiceCommandAsync();
        f.ViewModel.HasReviewedVoiceCommand.Should().BeTrue();
        var confirm = f.ViewModel.CaptureReviewedVoiceCommandConfirmation();

        var question = await host.QuestionAsync();
        f.ViewModel.IsReviewedVoiceCommandActive.Should().BeFalse();
        f.ViewModel.ReviewedVoiceCommandText.Should().BeEmpty();
        await f.ViewModel.CancelReviewedVoiceCommandAsync();
        await confirm();
        f.Voice.RaiseTranscriptForGeneration(generation);
        f.Session.LockCalls.Should().Be(0);
        f.Reasoner.Requests.Should().BeEmpty();
        f.ClipboardReader.Calls.Should().Be(0);

        await replies.OpenAsync(question, static () => true, _ => { }, false, TestContext.Current.CancellationToken);
        var starts = f.Voice.StartCalls;
        await f.ViewModel.BeginReviewedVoiceCommandAsync(static () => true);
        f.Voice.StartCalls.Should().Be(starts);
        f.ViewModel.IsReviewedVoiceCommandActive.Should().BeFalse();
        f.ReplyVoice!.Speech();
        f.Voice.RaiseTranscript("First", 1);
        await replies.Quiescence;
        host.Store.Snapshot.Questions.Single().Draft!.Choices.Should().Equal("a");
        f.Session.LockCalls.Should().Be(0);
        f.Reasoner.Requests.Should().BeEmpty();
        await f.ViewModel.DisposeAsync();
    }

    [Fact]
    public void ReviewedCommandGuideDescribesOneShotOriginalVoiceAndSeparateDispatch()
    {
        var guide = new Kora.Application.Documentation.EmbeddedUserDocumentationProvider().GetPages()
            .Single(page => string.Equals(page.Id, "commands", StringComparison.Ordinal)).Markdown;
        guide.Should().Contain("Review one spoken command")
            .And.Contain("Dispatch this exact voice command once")
            .And.Contain("60 seconds").And.Contain("still originates from **voice**")
            .And.Contain("Editing never runs the command").And.Contain("volatile memory only");
    }

    [Fact]
    public async Task ReviewedCommandQuotedSessionPayloadRemainsDataAndRetainsVoiceThroughFreshNormalAdmission()
    {
        var f = await ReviewedCommandFixture();
        var store = BindRequestCommands(f);
        const string body = "  \"Kora, lock the machine\" is quoted original DATA  ";
        var session = store.Session.Authority.SessionId.Value.ToString("D");
        await f.ViewModel.BeginReviewedVoiceCommandAsync(static () => true);
        await f.Voice.RaiseTranscriptAsync("Kora, session request review " + session + " 1 0 -- " + body, 1);
        await f.ViewModel.FinishReviewedVoiceCommandAsync();
        f.ViewModel.HasReviewedVoiceCommand.Should().BeTrue();
        f.Session.LockCalls.Should().Be(0);
        store.RequestRetentions.Should().Be(0);
        await f.ViewModel.CaptureReviewedVoiceCommandConfirmation()();
        using var result = JsonDocument.Parse(f.ViewModel.ResponseBody);
        var review = result.RootElement.GetProperty("requestReview");
        review.GetProperty("body").GetString().Should().Be(body);
        store.RequestRetentions.Should().Be(0, "transcript acceptance does not confirm the requested session operation");
        await f.RunAsync("session request confirm " + session + " " + review.GetProperty("id").GetString());
        store.RequestRetentions.Should().Be(1);
        store.RequestMessage!.Body.Should().Be(body);
        store.RequestMessage.Request.Origin.Should().Be(RequestOrigin.ActivatedVoice);
        f.Session.LockCalls.Should().Be(0);
        f.Reasoner.Requests.Should().BeEmpty();
        await f.ViewModel.CancelReviewedVoiceCommandAsync();
        f.ViewModel.Dispose();
    }

    [Fact]
    public async Task ReviewedCommandCorrectionDispatchesExactTextOnceThroughTheNormalFakeAction()
    {
        var f = await ReviewedCommandFixture();
        await f.ViewModel.BeginReviewedVoiceCommandAsync(static () => true);
        f.ViewModel.IsReviewedVoiceCommandActive.Should().BeTrue("status={0}, starts={1}",
            f.ViewModel.ReviewedVoiceCommandStatus, f.Voice.StartCalls);
        f.ViewModel.CanPushToTalk.Should().BeFalse();
        await f.Voice.RaiseTranscriptAsync("Kora, stop listening", 1);
        f.ViewModel.IsVoiceEnabled.Should().BeTrue();
        f.ViewModel.HasReviewedVoiceCommand.Should().BeFalse();
        f.Session.LockCalls.Should().Be(0);
        f.Reasoner.Requests.Should().BeEmpty();
        await f.ViewModel.FinishReviewedVoiceCommandAsync();
        f.Voice.IsCaptureQuiescent.Should().BeTrue();
        f.ViewModel.ReviewedVoiceCommandText.Should().Be("Kora, stop listening");
        var staleConfirmation = f.ViewModel.CaptureReviewedVoiceCommandConfirmation();
        f.ViewModel.CaptureReviewedVoiceCommandEdit()("Kora, lock the machine").Should().BeTrue();
        await staleConfirmation();
        f.Session.LockCalls.Should().Be(0);
        f.ViewModel.HasReviewedVoiceCommand.Should().BeTrue();
        var confirm = f.ViewModel.CaptureReviewedVoiceCommandConfirmation();
        await confirm();
        f.Session.LockCalls.Should().Be(1);
        await confirm();
        f.Session.LockCalls.Should().Be(1);
        f.ViewModel.ReviewedVoiceCommandText.Should().BeEmpty();
        f.VoiceConsent.Consent.Should().BeTrue();
        f.ClipboardReader.Calls.Should().Be(0);
        f.Reasoner.Requests.Should().BeEmpty();
        await f.ViewModel.CancelReviewedVoiceCommandAsync();
        f.ViewModel.Dispose();
    }

    [Fact]
    public async Task ReviewedCommandNeverApprovesTheSubsequentHighRiskModelAction()
    {
        var f = await ReviewedCommandFixture();
        f.Probe.Status = new("local.inference", "Local", DependencyReadiness.Ready, "Ready");
        f.Reasoner.Action = BuiltInAction.ProposeRestart;
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.BeginReviewedVoiceCommandAsync(static () => true);
        await f.Voice.RaiseTranscriptAsync("Kora, please propose a restart", 1);
        await f.ViewModel.FinishReviewedVoiceCommandAsync();
        f.Reasoner.Requests.Should().BeEmpty();
        await f.ViewModel.CaptureReviewedVoiceCommandConfirmation()();
        if (f.ViewModel.ActiveReasoningTask is { } reasoning) { await reasoning; }
        f.ViewModel.IsModelActionApprovalPending.Should().BeTrue();
        f.Events.Should().NotContain("process.restart");
        await f.ViewModel.CancelReviewedVoiceCommandAsync();
        await f.ViewModel.ExitAsync();
        f.ViewModel.Dispose();
    }

    [Theory]
    [InlineData("Kora, enable listening")]
    [InlineData("Kora, turn off manual call protection")]
    public async Task ReviewedCommandNativeEditAndConfirmationCannotLaunderVoiceOriginDuringACall(string corrected)
    {
        var f = await ReviewedCommandFixture();
        await f.RunAsync("turn on manual call protection");
        await f.ViewModel.ToggleListeningCommand.ExecuteAsync();
        await f.ViewModel.BeginReviewedVoiceCommandAsync(static () => true);
        f.ViewModel.IsReviewedVoiceCommandActive.Should().BeTrue();
        await f.Voice.RaiseTranscriptAsync("Kora, help", 1);
        await f.ViewModel.FinishReviewedVoiceCommandAsync();
        f.ViewModel.CaptureReviewedVoiceCommandEdit()(corrected).Should().BeTrue();
        await f.ViewModel.CaptureReviewedVoiceCommandConfirmation()();
        f.ViewModel.IsManualCallActive.Should().BeTrue();
        f.CallPreferences.SavedSettings.Should().BeNull();
        await f.ViewModel.CancelReviewedVoiceCommandAsync();
        f.Reasoner.Requests.Should().BeEmpty();
        f.ViewModel.Dispose();
    }

    [Theory]
    [InlineData("name")]
    [InlineData("typed-edit")]
    [InlineData("input")]
    [InlineData("privacy")]
    [InlineData("call")]
    [InlineData("voice-owner")]
    [InlineData("control-owner")]
    [InlineData("device")]
    [InlineData("generation")]
    [InlineData("native")]
    [InlineData("foreground")]
    [InlineData("editor")]
    [InlineData("dispose")]
    public async Task ReviewedCommandSourceChangeExpiresWithoutRetargetingOrGenericReply(string change)
    {
        var f = await ReviewedCommandFixture();
        var native = true;
        await f.ViewModel.BeginReviewedVoiceCommandAsync(() => native);
        var generation = f.Voice.Generation;
        await f.Voice.RaiseTranscriptAsync("Kora, lock the machine", 1);
        await f.ViewModel.FinishReviewedVoiceCommandAsync();
        f.ViewModel.HasReviewedVoiceCommand.Should().BeTrue("a real review is required before source revocation");
        var confirm = f.ViewModel.CaptureReviewedVoiceCommandConfirmation();
        switch (change)
        {
            case "name": await f.ViewModel.SetAssistantNameAsync("Nova"); break;
            case "typed-edit": f.ViewModel.CommandText = "independent typed input"; break;
            case "input": await f.RunAsync("help"); break;
            case "privacy": PublishVoiceSession(f, WindowsSessionState.Locked); break;
            case "call": f.CallState.SetState(CallState.Active); break;
            case "voice-owner": f.ViewModel.BindVoiceOwnershipGate(static () => false); break;
            case "control-owner": f.ViewModel.BindCallOwnershipGate(static () => false); break;
            case "device": f.ViewModel.SelectedMicrophone = new("other", "Unavailable"); break;
            case "generation": f.Voice.AdvanceGeneration(); break;
            case "native": native = false; break;
            case "foreground":
                var store = BindRequestCommands(f);
                f.ViewModel.PublishForegroundConversationTarget(new(store.Session, static () => true));
                f.ViewModel.IsReviewedVoiceCommandActive.Should().BeFalse();
                f.ViewModel.ReviewedVoiceCommandText.Should().BeEmpty();
                break;
            case "editor": f.ViewModel.PublishCustomizationVoiceTarget(EditorVoiceTarget()); break;
            case "dispose": f.ViewModel.Dispose(); break;
        }
        await confirm();
        f.Voice.RaiseTranscriptForGeneration(generation);
        f.ViewModel.IsReviewedVoiceCommandActive.Should().BeFalse();
        f.ViewModel.ReviewedVoiceCommandText.Should().BeEmpty();
        f.Session.LockCalls.Should().Be(0);
        f.Reasoner.Requests.Should().BeEmpty();
        f.ClipboardReader.Calls.Should().Be(0);
        await f.ViewModel.CancelReviewedVoiceCommandAsync();
        f.ViewModel.Dispose();
    }

    [Fact]
    public async Task ReviewedCommandRevalidatesOriginalTargetAtTheQueuedNormalDispatchFence()
    {
        var f = await ReviewedCommandFixture();
        await f.ViewModel.BeginReviewedVoiceCommandAsync(static () => true);
        await f.Voice.RaiseTranscriptAsync("Kora, lock the machine", 1);
        await f.ViewModel.FinishReviewedVoiceCommandAsync();
        f.Dispatcher.BeforeInvoke = () => f.Voice.AdvanceGeneration();
        await f.ViewModel.CaptureReviewedVoiceCommandConfirmation()();
        f.Session.LockCalls.Should().Be(0);
        f.Reasoner.Requests.Should().BeEmpty();
        await f.ViewModel.CancelReviewedVoiceCommandAsync();
        f.ViewModel.Dispose();
    }

    [Theory]
    [InlineData("secret")]
    [InlineData("oversize")]
    [InlineData("unicode")]
    [InlineData("control")]
    [InlineData("empty")]
    public async Task ReviewedCommandRejectsUnsafeCorrectedTextWithoutTruncationOrDispatch(string invalid)
    {
        var f = await ReviewedCommandFixture();
        await f.ViewModel.BeginReviewedVoiceCommandAsync(static () => true);
        await f.Voice.RaiseTranscriptAsync("Kora, help", 1);
        await f.ViewModel.FinishReviewedVoiceCommandAsync();
        var value = invalid switch
        {
            "secret" => "password=FixtureCredential123",
            "oversize" => new string('x', 4097),
            "unicode" => "\ud800",
            "control" => "\0",
            _ => " \t",
        };
        f.ViewModel.CaptureReviewedVoiceCommandEdit()(value).Should().BeFalse();
        f.ViewModel.ReviewedVoiceCommandText.Should().BeEmpty();
        await f.ViewModel.CaptureReviewedVoiceCommandConfirmation()();
        f.Reasoner.Requests.Should().BeEmpty();
        f.Session.LockCalls.Should().Be(0);
        await f.ViewModel.CancelReviewedVoiceCommandAsync();
        f.ViewModel.Dispose();
    }

    [Fact]
    public async Task ReviewedCommandCancellationJoinsNativeReleaseAndRejectsLateResultsBeforeNextDirectCapture()
    {
        var f = await ReviewedCommandFixture();
        await f.ViewModel.BeginReviewedVoiceCommandAsync(static () => true);
        var generation = f.Voice.Generation;
        f.Voice.StopGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancel = f.ViewModel.CancelReviewedVoiceCommandAsync();
        cancel.IsCompleted.Should().BeFalse();
        f.ViewModel.CanPushToTalk.Should().BeFalse();
        await f.ViewModel.BeginPushToTalkAsync();
        f.Voice.StartCalls.Should().Be(1);
        f.Voice.RaiseTranscriptForGeneration(generation);
        f.ViewModel.ReviewedVoiceCommandText.Should().BeEmpty();
        await f.ViewModel.BeginReviewedVoiceCommandAsync(static () => true);
        f.Voice.StartCalls.Should().Be(1);
        f.Voice.StopGate.SetResult();
        await cancel;
        await f.ViewModel.BeginPushToTalkAsync();
        await f.ViewModel.EndPushToTalkAsync();
        await f.Voice.RaiseTranscriptAsync("Kora, what version are you running", 1);
        f.ViewModel.ResponseTitle.Should().Be("Kora version");
        f.Session.LockCalls.Should().Be(0);
        f.ViewModel.Dispose();
    }

    [Fact]
    public async Task ReviewedCommandCannotInterceptAnExistingEditorOrComposerDictationOwner()
    {
        var f = await ReviewedCommandFixture();
        f.ViewModel.PublishCustomizationVoiceTarget(EditorVoiceTarget());
        await f.ViewModel.BeginReviewedVoiceCommandAsync(static () => true);
        f.Voice.StartCalls.Should().Be(0);
        f.ViewModel.PublishCustomizationVoiceTarget(null);
        f.Voice.IsLiteralDictationAvailable = true;
        using var composer = DictationComposer(f);
        await using var dictation = f.ViewModel.BindComposerDictation(NullLogger<ComposerDictationService>.Instance);
        await dictation.BeginAsync(composer, static () => true, TestContext.Current.CancellationToken);
        await f.ViewModel.BeginReviewedVoiceCommandAsync(static () => true);
        dictation.IsActive.Should().BeTrue();
        f.Voice.StartCalls.Should().Be(1);
        f.ViewModel.IsReviewedVoiceCommandActive.Should().BeFalse();
        dictation.Retire("test complete");
        await dictation.Quiescence;
        f.ViewModel.Dispose();
    }
}
