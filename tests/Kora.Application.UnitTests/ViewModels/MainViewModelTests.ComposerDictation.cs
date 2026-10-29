using AwesomeAssertions;

using Kora.Application.Hosting;
using Kora.Application.UnitTests.Interaction;
using Kora.Application.Voice;
using Kora.Core.Communication;
using Kora.Core.Platform;
using Kora.Application.ViewModels;
using Kora.Core.Dependencies;
using System.Reflection;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Theory]
    [InlineData("name")]
    [InlineData("input")]
    [InlineData("privacy")]
    [InlineData("call")]
    [InlineData("voice-binding")]
    [InlineData("control-binding")]
    public async Task OwnedBodyDictationFreezesVoiceAdmissionAndCannotRenewItThroughNativeReview(string boundary)
    {
        var f = CreateVoicePrivacyFixture();
        f.ViewModel.BindVoiceOwnershipGate(static () => true);
        await f.ViewModel.InitializeAsync();
        var current = f.ViewModel.CaptureInstructionBodyDictationEligibility();
        current().Should().BeTrue();
        switch (boundary)
        {
            case "name": await f.ViewModel.SetAssistantNameAsync("Nova"); break;
            case "input": await f.RunAsync("help"); break;
            case "privacy": PublishVoiceSession(f, WindowsSessionState.Locked); break;
            case "call": f.CallState.SetState(CallState.Active); break;
            case "voice-binding": f.ViewModel.BindVoiceOwnershipGate(static () => true); break;
            case "control-binding": f.ViewModel.BindCallOwnershipGate(static () => true); break;
        }
        current().Should().BeFalse();
        f.ViewModel.Dispose();
    }

    [Fact]
    public async Task OwnedBodyDictationDoesNotTurnMissingVoiceConsentIntoNativeAuthority()
    {
        var f = CreateVoicePrivacyFixture(consent: false);
        f.ViewModel.BindVoiceOwnershipGate(static () => true);
        await f.ViewModel.InitializeAsync();
        f.ViewModel.CaptureInstructionBodyDictationEligibility()().Should().BeFalse();
        f.ViewModel.Dispose();
    }

    private static NewRequestComposer DictationComposer(Fixture fixture)
    {
        var store = new SessionCommandStore(fixture);
        var workspace = new SessionWorkspaceService(store, new(fixture.HostStore), store, NullLogger<SessionWorkspaceService>.Instance);
        var conversation = new SessionConversationService(store, store, workspace, store, new RequestAdmission(),
            TimeProvider.System, NullLogger<SessionConversationService>.Instance);
        return new(workspace, conversation, store, NullLogger<NewRequestComposer>.Instance);
    }

    [Fact]
    public async Task DictationUsesExistingPttConsentWithoutRoutingEvenReservedStopOrChangingListening()
    {
        var f = CreateVoicePrivacyFixture();
        f.ViewModel.BindVoiceOwnershipGate(static () => true);
        f.Voice.IsLiteralDictationAvailable = true;
        await f.ViewModel.InitializeAsync();
        var editorCalls = 0;
        f.ViewModel.PublishCustomizationVoiceTarget(EditorVoiceTarget());
        f.ViewModel.BindCustomizationVoice(_ => { editorCalls++; return Task.CompletedTask; });
        using var composer = DictationComposer(f);
        await using var service = f.ViewModel.BindComposerDictation(NullLogger<ComposerDictationService>.Instance);
        f.ViewModel.ComposerDictation.Should().BeSameAs(service);
        f.ViewModel.BindComposerDictation(NullLogger<ComposerDictationService>.Instance).Should().BeSameAs(service);
        await service.BeginAsync(composer, static () => true, TestContext.Current.CancellationToken);
        service.IsActiveFor(composer).Should().BeTrue();
        f.Voice.StartCalls.Should().Be(1);
        f.Voice.StartedPhrases.Should().BeEmpty();
        await f.Voice.RaiseTranscriptAsync("Kora, stop listening", 1);
        await service.EndAsync();
        await service.Quiescence;
        composer.Body.Should().Be("Kora, stop listening");
        f.ViewModel.IsVoiceEnabled.Should().BeTrue();
        f.VoiceConsent.Consent.Should().BeTrue();
        f.Reasoner.Requests.Should().BeEmpty();
        f.Session.LockCalls.Should().Be(0);
        f.ClipboardReader.Calls.Should().Be(0);
        editorCalls.Should().Be(0, "literal dictation cannot become editor authoring");
        await f.ViewModel.ExitAsync();
        f.ViewModel.Dispose();
    }

    [Theory]
    [InlineData("stop-request")]
    [InlineData("stop-release")]
    [InlineData("privacy")]
    [InlineData("catalog")]
    [InlineData("catalog-epoch")]
    [InlineData("command-capture")]
    [InlineData("busy")]
    [InlineData("question")]
    [InlineData("native-speech")]
    [InlineData("presented-speech")]
    public async Task EveryCurrentPttAdmissionHoldRemainsFailClosedForLiteralDictation(string hold)
    {
        var f = CreateVoicePrivacyFixture();
        f.ViewModel.BindVoiceOwnershipGate(static () => true);
        f.Voice.IsLiteralDictationAvailable = true;
        if (hold is "question")
        {
            f.Probe.Status = new("local.inference", "Local", DependencyReadiness.Ready, "Ready");
            f.Reasoner.Question = new("Which target?", ["One", "Two"]);
        }
        await f.ViewModel.InitializeAsync();
        using var composer = DictationComposer(f);
        await using var service = f.ViewModel.BindComposerDictation(NullLogger<ComposerDictationService>.Instance);
        if (hold is "stop-request") { SetField("listeningStopRequests", 1); }
        if (hold is "privacy") { PublishVoiceSession(f, WindowsSessionState.Locked); }
        if (hold is "stop-release")
        {
            var stop = typeof(MainViewModel).GetField("listeningStop", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(f.ViewModel)!;
            stop.GetType().GetField("stopping", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(stop, 1);
        }
        if (hold is "catalog") { SetField("microphoneCatalogCurrent", false); }
        if (hold is "catalog-epoch") { SetField("catalogPrivacyRevision", -1L); }
        if (hold is "command-capture") { SetField("pushToTalkHeld", 1); }
        if (hold is "busy") { typeof(MainViewModel).GetProperty(nameof(MainViewModel.IsBusy))!.SetValue(f.ViewModel, true); }
        if (hold is "presented-speech") { typeof(MainViewModel).GetProperty(nameof(MainViewModel.IsSpeaking))!.SetValue(f.ViewModel, true); }
        if (hold is "native-speech") { typeof(FakeTextToSpeechService).GetProperty(nameof(FakeTextToSpeechService.IsSpeaking))!.SetValue(f.TextToSpeech, true); }
        if (hold is "question")
        {
            await f.RunAsync("choose");
            await f.ViewModel.ActiveReasoningTask!;
            f.ViewModel.IsModelQuestionPending.Should().BeTrue();
        }
        await service.BeginAsync(composer, static () => true, TestContext.Current.CancellationToken);
        service.IsActive.Should().BeFalse();
        f.Voice.StartCalls.Should().Be(0);
        composer.Body.Should().BeEmpty();
        f.ViewModel.Dispose();
        void SetField<T>(string field, T value) =>
            typeof(MainViewModel).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(f.ViewModel, value);
    }

    [Theory]
    [InlineData("disable")]
    [InlineData("lock")]
    [InlineData("call")]
    [InlineData("consent")]
    [InlineData("input")]
    [InlineData("calculator")]
    [InlineData("clipboard-status")]
    [InlineData("owner")]
    [InlineData("device")]
    [InlineData("dispose")]
    [InlineData("ptt")]
    public async Task ExistingRuntimeRevocationRetiresDictationWithoutChangingQuestionsOrAcceptingOldGeneration(string boundary)
    {
        var f = CreateVoicePrivacyFixture();
        f.ViewModel.BindVoiceOwnershipGate(static () => true);
        f.Voice.IsLiteralDictationAvailable = true;
        await f.ViewModel.InitializeAsync();
        using var composer = DictationComposer(f);
        await using var service = f.ViewModel.BindComposerDictation(NullLogger<ComposerDictationService>.Instance);
        var source = composer.CaptureDictationTarget();
        var target = new ComposerDictationTarget(source.Source, source.Body, source.Request, source.IsCurrent,
            source.Insert, ComposerDictationDestination.SessionComposer);
        await service.BeginAsync(target, static () => true, TestContext.Current.CancellationToken);
        var generation = f.Voice.Generation;
        service.IsActive.Should().BeTrue();
        var lateHypothesis = f.Voice.DictationObserver!;
        lateHypothesis(new(generation, 1, "PRIVATE_HYPOTHESIS"));
        service.ProvisionalTextFor(target).Should().Be("PRIVATE_HYPOTHESIS");
        switch (boundary)
        {
            case "disable": await f.ViewModel.DisableListeningFromTrayAsync(); break;
            case "lock": PublishVoiceSession(f, WindowsSessionState.Locked); break;
            case "call": f.CallState.SetState(CallState.Unknown); break;
            case "consent": await f.ViewModel.WithdrawVoiceConsentCommand.ExecuteAsync(); break;
            case "input": await f.RunAsync("help"); break;
            case "calculator": await f.RunAsync("calculate 1+1"); break;
            case "clipboard-status": await f.RunAsync("clipboard snapshot status"); break;
            case "owner": f.ViewModel.BindVoiceOwnershipGate(static () => false); service.Revalidate(); break;
            case "device": f.ViewModel.SelectedMicrophone = new("other", "Unavailable"); break;
            case "dispose": f.ViewModel.Dispose(); break;
            case "ptt": await f.ViewModel.BeginPushToTalkAsync(); break;
        }
        service.IsActive.Should().BeFalse();
        service.ProvisionalTextFor(target).Should().BeEmpty();
        f.Voice.DictationObserver.Should().BeNull();
        lateHypothesis(new(generation, 2, "PRIVATE_LATE"));
        service.ProvisionalTextFor(target).Should().BeEmpty();
        f.Voice.RaiseTranscriptForGeneration(generation);
        await service.EndAsync();
        await service.Quiescence;
        composer.Body.Should().BeEmpty();
        f.Reasoner.Requests.Should().BeEmpty();
        if (boundary is "ptt")
        {
            await f.ViewModel.EndPushToTalkAsync();
            await f.Voice.RaiseTranscriptAsync("Kora, what version are you running", 1);
            f.ViewModel.ResponseTitle.Should().Be("Kora version");
        }
        f.ViewModel.Dispose();
    }

    [Fact]
    public async Task UnqualifiedOrUnconsentedCaptureIsExplicitlyUnavailableWithoutConsentDeviceOrListeningMutation()
    {
        var f = CreateVoicePrivacyFixture(consent: false);
        f.ViewModel.BindVoiceOwnershipGate(static () => true);
        await f.ViewModel.InitializeAsync();
        using var composer = DictationComposer(f);
        await using var service = f.ViewModel.BindComposerDictation(NullLogger<ComposerDictationService>.Instance);
        await service.BeginAsync(composer, static () => true, TestContext.Current.CancellationToken);
        f.Voice.StartCalls.Should().Be(0);
        service.Status.Should().Contain("unavailable");
        f.VoiceConsent.Consent.Should().BeFalse();
        f.ViewModel.IsVoiceEnabled.Should().BeFalse();
        composer.Body.Should().BeEmpty();
        f.ViewModel.Dispose();
    }

    [Fact]
    public async Task NativeQuestionRetargetingRevokesLiteralArmAndReplyAdmissionCannotStealItsCapture()
    {
        var f = await ReplyHostAsync();
        f.Voice.IsLiteralDictationAvailable = true;
        using var host = new InteractionFixture();
        using var composer = DictationComposer(f);
        await using var replies = f.ViewModel.BindQuestionVoiceReplies(host.Questions, host.Time,
            NullLogger<Kora.Application.Interaction.HostQuestionVoiceReplyService>.Instance)!;
        await using var service = f.ViewModel.BindComposerDictation(NullLogger<ComposerDictationService>.Instance);
        ((Kora.Core.Voice.IActivatedVoiceRecognitionService)new QuestionReplyFixture.ReplyCapture()).IsLiteralDictationAvailable.Should().BeFalse();
        await service.BeginAsync(composer, static () => true, TestContext.Current.CancellationToken);
        service.IsActive.Should().BeTrue();
        // A fresh reply cannot take the capture owned by a distinct explicit native buffer.
        var question = await host.QuestionAsync();
        service.IsActive.Should().BeFalse();
        await service.Quiescence;
        await replies.OpenAsync(question, static () => true, _ => { }, false, TestContext.Current.CancellationToken);
        replies.IsActive.Should().BeTrue();
        await service.BeginAsync(composer, static () => true, TestContext.Current.CancellationToken);
        service.IsActive.Should().BeFalse();
        f.ViewModel.Dispose();
    }
}
