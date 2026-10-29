using AwesomeAssertions;

using Kora.Application.Hosting;
using Kora.Application.Interaction;
using Kora.Application.UnitTests.Interaction;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Interaction;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Fact]
    public async Task CompletionNoticeMissingCompositionIsHonestlyUnavailableAndCannotSpeak()
    {
        var f = await Fixture.CreateInitializedAsync();
        f.ViewModel.CompletionNoticeStatus.Should().Contain("Off").And.Contain("NotRequested");
        f.ViewModel.CanEnableCompletionNotices.Should().BeFalse();
        f.ViewModel.RetireCompletionNotices();
        await f.ViewModel.DeliverCompletionNoticesAsync(new([], 0, LocalEventReason.Eligible), () => true, TestContext.Current.CancellationToken);
        var unavailable = () => f.ViewModel.ChangeCompletionNoticesNativeAsync(new(Guid.NewGuid()), new(1), true,
            () => true, TestContext.Current.CancellationToken);
        await unavailable.Should().ThrowAsync<InvalidOperationException>();
        await f.ViewModel.DisposeAsync();
    }

    [Theory]
    [InlineData("session")]
    [InlineData("generation")]
    [InlineData("inactive")]
    [InlineData("stale")]
    [InlineData("output")]
    [InlineData("text-only")]
    [InlineData("other-speech")]
    [InlineData("prompt")]
    [InlineData("model-prompt")]
    [InlineData("request-review")]
    public async Task CompletionNoticeNativeReviewRejectsUnavailableOrMisboundVoiceTargets(string change)
    {
        var f = change is "model-prompt" ? await CreateModelActionReviewFixtureAsync() : await Fixture.CreateInitializedAsync();
        using var source = new LocalEventBrokerTests.Fixture();
        await using var broker = source.Create();
        var target = change is "request-review" ? BindRequestCommands(f).Session : BindSessions(f).Session;
        source.Session = target.Authority.SessionId;
        f.ViewModel.BindLocalEvents(broker);
        var duplicate = () => f.ViewModel.BindLocalEvents(broker);
        duplicate.Should().Throw<InvalidOperationException>();
        if (f.ViewModel.IsVoiceEnabled) { await f.ViewModel.ToggleListeningCommand.ExecuteAsync(); }
        await f.ViewModel.ToggleListeningCommand.ExecuteAsync();
        f.ViewModel.PublishForegroundConversationTarget(new(change is "inactive"
            ? target with { Authority = target.Authority with { IsActive = false } } : target, () => change is not "stale"));
        if (change is "output")
        {
            f.TextToSpeech.OutputDevices = [];
            f.TextToSpeech.DefaultOutputDeviceId = null;
            await f.ViewModel.DetectMicrophonesAsync();
        }
        if (change is "text-only") { f.ViewModel.DefaultResponseMode = Kora.Core.Voice.ResponseOutputMode.VisualOnly; }
        if (change is "other-speech")
        {
            f.TextToSpeech.ReturnWhileSpeaking = true;
            await f.TextToSpeech.SpeakAsync("unrelated", f.TextToSpeech.Voices[0], f.TextToSpeech.OutputDevices[0], source.Token);
        }
        if (change is "prompt") { await f.RunAsync("Kora, shut down the computer"); }
        if (change is "model-prompt") { await ProposeModelActionReviewAsync(f); }
        if (change is "request-review") { await f.RunAsync("ask this conversation original unsent request"); }
        if (change is not ("session" or "generation")) { f.ViewModel.CanEnableCompletionNotices.Should().BeFalse(); }
        using var root = HostActivity.BeginRoot(new(new(Guid.NewGuid()), source.Session, new(Guid.NewGuid()), RequestOrigin.LocalUi),
            HostActivityLayer.Desktop, HostOperation.Policy);
        var denied = () => f.ViewModel.ChangeCompletionNoticesNativeAsync(
            change is "session" ? new(Guid.NewGuid()) : source.Session,
            change is "generation" ? new(2) : target.Authority.Generation, true, () => true, source.Token);
        await denied.Should().ThrowAsync<InvalidOperationException>();
        f.TextToSpeech.InvalidateOutput();
        await f.ViewModel.DisposeAsync();
    }

    [Theory]
    [InlineData("lock")]
    [InlineData("question")]
    [InlineData("exit")]
    public async Task CompletionNoticeBoundOwnerJoinsTheExistingPrivacyQuestionAndShutdownLifecycles(string change)
    {
        var f = await ReplyHostAsync();
        using var source = new LocalEventBrokerTests.Fixture();
        await using var broker = source.Create();
        f.ViewModel.BindLocalEvents(broker);
        if (change is "lock")
        {
            PublishVoiceSession(f, Kora.Core.Platform.WindowsSessionState.Locked);
            await f.ViewModel.PrivacyClosureTask;
        }
        if (change is "question")
        {
            using var host = new InteractionFixture();
            await using var replies = f.ViewModel.BindQuestionVoiceReplies(host.Questions, host.Time,
                Microsoft.Extensions.Logging.Abstractions.NullLogger<HostQuestionVoiceReplyService>.Instance)!;
            await host.QuestionAsync();
        }
        if (change is "exit") { await f.ViewModel.ExitAsync(); }
        f.ViewModel.CompletionNoticeStatus.Should().Contain("Off");
        await f.ViewModel.DisposeAsync();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CompletionNoticeConsentRequiresExplicitRunActivationNotRestoredReadiness(bool explicitActivation)
    {
        var f = await Fixture.CreateInitializedAsync();
        using var source = new LocalEventBrokerTests.Fixture();
        await using var broker = source.Create();
        var target = BindSessions(f).Session;
        source.Session = target.Authority.SessionId;
        f.ViewModel.BindLocalEvents(broker);
        f.ViewModel.CompletionNoticeDisclosure.Should().Contain("Off by default").And.Contain("independent");
        f.ViewModel.CompletionNoticeStatus.Should().Contain("Off");
        f.ViewModel.PublishForegroundConversationTarget(new(target, () => true));
        if (explicitActivation)
        {
            if (f.ViewModel.IsVoiceEnabled) { await f.ViewModel.ToggleListeningCommand.ExecuteAsync(); }
            await f.ViewModel.ToggleListeningCommand.ExecuteAsync();
        }
        using var root = HostActivity.BeginRoot(new(new(Guid.NewGuid()), source.Session, new(Guid.NewGuid()), RequestOrigin.LocalUi),
            HostActivityLayer.Desktop, HostOperation.Policy);
        var enable = () => f.ViewModel.ChangeCompletionNoticesNativeAsync(source.Session, target.Authority.Generation, true, () => true, source.Token);
        if (explicitActivation)
        {
            f.ViewModel.CanEnableCompletionNotices.Should().BeTrue();
            await enable();
            f.ViewModel.CompletionNoticeStatus.Should().Contain("On");
            await f.ViewModel.ToggleListeningCommand.ExecuteAsync();
            f.ViewModel.CompletionNoticeStatus.Should().Contain("Off");
        }
        else
        {
            f.ViewModel.CanEnableCompletionNotices.Should().BeFalse();
            await enable.Should().ThrowAsync<InvalidOperationException>();
        }
        await f.ViewModel.DisposeAsync();
    }

    [Theory]
    [InlineData("success")]
    [InlineData("off")]
    [InlineData("no-target")]
    [InlineData("no-native")]
    [InlineData("input-race")]
    [InlineData("owner")]
    [InlineData("call-cycle")]
    public async Task ActualMainHostDeliversOnlyConsentedCurrentGapAndNeverOpensAWindow(string change)
    {
        var f = await Fixture.CreateInitializedAsync();
        using var source = new LocalEventBrokerTests.Fixture();
        await using var broker = source.Create();
        var target = BindSessions(f).Session;
        source.Session = target.Authority.SessionId;
        var item = source.Event(LocalEventType.Completed);
        source.Events = [item with { Source = LocalEventSource.ConversationTask, QueueReason = null,
            Id = LocalEvent.Identity(LocalEventSource.ConversationTask, source.Session.Value, item.SubjectId),
            SourceRevision = 3, RelatedRevision = 3 }];
        f.ViewModel.BindLocalEvents(broker);
        if (f.ViewModel.IsVoiceEnabled) { await f.ViewModel.ToggleListeningCommand.ExecuteAsync(); }
        await f.ViewModel.ToggleListeningCommand.ExecuteAsync();
        f.ViewModel.PublishForegroundConversationTarget(new(target, () => true));
        using (var root = HostActivity.BeginRoot(new(new(Guid.NewGuid()), source.Session, new(Guid.NewGuid()), RequestOrigin.LocalUi),
            HostActivityLayer.Desktop, HostOperation.Policy))
        {
            await f.ViewModel.ChangeCompletionNoticesNativeAsync(source.Session, target.Authority.Generation,
                true, () => true, source.Token);
        }
        var fresh = source.Event(LocalEventType.Completed);
        source.Events = [fresh with { Source = LocalEventSource.ConversationTask, QueueReason = null,
            Id = LocalEvent.Identity(LocalEventSource.ConversationTask, source.Session.Value, fresh.SubjectId),
            SourceRevision = 3, RelatedRevision = 3 }];
        var snapshot = await source.Observe(broker);
        var before = f.TextToSpeech.SpeakCalls;
        f.WindowActions.Clear();
        if (change is "off") { f.ViewModel.RetireCompletionNotices(); }
        if (change is "no-target") { f.ViewModel.PublishForegroundConversationTarget(null); }
        if (change is "owner") { f.ViewModel.BindVoiceOwnershipGate(() => false); }
        if (change is "call-cycle")
        {
            f.CallState.SetState(Kora.Core.Communication.CallState.Active);
            await f.ViewModel.CallClosureTask;
            f.CallState.SetState(Kora.Core.Communication.CallState.Clear);
            await f.ViewModel.CallClosureTask;
            f.ViewModel.CompletionNoticeStatus.Should().Contain("Off");
            f.WindowActions.Clear();
        }
        if (change is "input-race") { f.TextToSpeech.BeforeSpeak = () => f.ViewModel.CommandText = "new unsent input"; }
        await f.ViewModel.DeliverCompletionNoticesAsync(snapshot, () => change is not "no-native", source.Token);
        if (change is "success")
        {
            f.TextToSpeech.SpeakCalls.Should().Be(before + 1);
            f.TextToSpeech.SpokenText.Should().Be(ConversationCompletionVoiceNotices.Statement);
            f.ViewModel.CompletionNoticeStatus.Should().Contain("Completed");
        }
        else if (change is not "input-race") { f.TextToSpeech.SpeakCalls.Should().Be(before); }
        else { f.ViewModel.CompletionNoticeStatus.Should().NotContain("outcome: Completed"); }
        f.WindowActions.Should().BeEmpty();
        await f.ViewModel.DeliverCompletionNoticesAsync(snapshot, () => true, source.Token);
        f.TextToSpeech.SpeakCalls.Should().BeLessThanOrEqualTo(before + 1);
        await f.ViewModel.DisposeAsync();
    }
}