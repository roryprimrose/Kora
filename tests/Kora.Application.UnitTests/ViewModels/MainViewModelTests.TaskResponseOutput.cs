using AwesomeAssertions;

using Kora.Application.Hosting;
using Kora.Core.Communication;
using Kora.Core.Voice;
using Kora.Core.Hosting;
using Kora.Core.Conversation;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Theory]
    [InlineData(TaskResponseMode.Inherit, ResponseOutputMode.Hybrid, true, true)]
    [InlineData(TaskResponseMode.Inherit, ResponseOutputMode.VisualOnly, false, true)]
    [InlineData(TaskResponseMode.VisualOnly, ResponseOutputMode.Hybrid, false, true)]
    [InlineData(TaskResponseMode.Hybrid, ResponseOutputMode.VisualOnly, true, true)]
    [InlineData(TaskResponseMode.VoiceOnly, ResponseOutputMode.VisualOnly, true, false)]
    public async Task OneTaskOutputReviewRetainsGenuineAnswerUsesExactChoiceThenRestoresOrdinaryPolicy(
        TaskResponseMode declared, ResponseOutputMode ordinary, bool speak, bool visual)
    {
        var f = await Fixture.CreateInitializedAsync();
        var (store, model) = BindSpeechSuggestions(f, bindSpeech: false);
        model.Answer = "One genuine answer.";
        f.ViewModel.DefaultResponseMode = ordinary;
        var saved = f.OutputPreferences.SavedMode;
        var calls = f.TextToSpeech.SpeakCalls;
        await f.RunAsync("session request output " + store.Session.Authority.SessionId.Value.ToString("D")
            + " 1 0 " + declared + " --  Original unchanged input ");
        f.ViewModel.HasForegroundRequestReview.Should().BeTrue(f.ViewModel.ResponseBody);
        f.ViewModel.ForegroundRequestReviewText.Should().Contain("declared output: " + declared)
            .And.Contain(" Original unchanged input ");
        store.RequestRetentions.Should().Be(0);
        f.TextToSpeech.SpeakCalls.Should().Be(calls);
        await f.ViewModel.ConfirmForegroundRequestAsync();
        store.RequestMessage!.AssistantResponse!.Body.Should().Be(model.Answer);
        f.ViewModel.ResponseBody.Should().Be(model.Answer);
        f.TextToSpeech.SpeakCalls.Should().Be(calls + (speak ? 1 : 0));
        f.ViewModel.IsVisualResponseVisible.Should().Be(visual);
        f.ViewModel.EffectiveResponseMode.Should().Be(ordinary, "the transient execution override is retired after output joins");
        f.ViewModel.TaskResponseMode.Should().BeNull();
        f.OutputPreferences.SavedMode.Should().Be(saved);
        f.ViewModel.RevokeSessionPresentation(store.Session.Authority.SessionId);
        f.ViewModel.IsVisualResponseVisible.Should().BeTrue("revoked task content cannot suppress a later mandatory visual notice");
        await f.RunAsync("what power action is pending");
        f.ViewModel.IsVisualResponseVisible.Should().BeTrue();
        f.TextToSpeech.SpeakCalls.Should().Be(calls + (speak ? 1 : 0) + (ordinary == ResponseOutputMode.VisualOnly ? 0 : 1));
        model.Calls.Should().Be(1);
        await f.ViewModel.DisposeAsync();
    }

    [Theory]
    [InlineData("muted")]
    [InlineData("missing")]
    [InlineData("over-cap")]
    [InlineData("playback")]
    public async Task OneTaskVoiceOnlyNeverHidesFullVisualFallback(string loss)
    {
        var f = await Fixture.CreateInitializedAsync();
        var (store, model) = BindSpeechSuggestions(f, bindSpeech: false);
        model.Answer = loss is "over-cap" ? string.Join(' ', Enumerable.Repeat("word", 90)) : "Complete genuine answer.";
        await f.RunAsync("session request output " + store.Session.Authority.SessionId.Value.ToString("D")
            + " 1 0 VoiceOnly -- Original");
        if (loss is "muted") { f.TextToSpeech.OutputDevices = [f.TextToSpeech.OutputDevices[0] with { IsMuted = true }]; }
        if (loss is "missing") { f.TextToSpeech.OutputDevices = []; }
        if (loss is "playback") { f.TextToSpeech.SpeakException = new InvalidOperationException("synthetic playback failure"); }
        await f.ViewModel.ConfirmForegroundRequestAsync();
        f.ViewModel.ResponseBody.Should().Be(model.Answer);
        f.ViewModel.IsVisualResponseVisible.Should().BeTrue();
        f.ViewModel.TaskResponseMode.Should().BeNull();
        await f.ViewModel.DisposeAsync();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OneTaskOutputCancelAndVoiceOriginRevocationNeverSubmitOrSpeak(bool voice)
    {
        var f = await Fixture.CreateInitializedAsync();
        var (store, model) = BindSpeechSuggestions(f, bindSpeech: false);
        var review = "session request output " + store.Session.Authority.SessionId.Value.ToString("D")
            + " 1 0 VoiceOnly -- Original";
        if (voice) { await f.RaiseActivatedTranscriptAsync("Kora, " + review, 1); }
        else { await f.RunAsync(review); }
        if (voice) { await f.ViewModel.SetVoiceConsentAsync(false); }
        else { await f.ViewModel.CancelForegroundRequestAsync(); }
        await f.ViewModel.ConfirmForegroundRequestAsync();
        store.RequestRetentions.Should().Be(0);
        model.Calls.Should().Be(0);
        f.TextToSpeech.SpeakCalls.Should().Be(0);
        await f.ViewModel.DisposeAsync();
    }

    [Theory]
    [InlineData("locked")]
    [InlineData("protected-voice")]
    [InlineData("unavailable-preference")]
    [InlineData("known-preference")]
    public async Task OneTaskOutputHostRejectsUnknownPreferenceOrOriginalChannelWithoutDefaultReinterpretation(string state)
    {
        var f = new Fixture(enableResponseModeConfiguration: true);
        await f.ViewModel.InitializeAsync();
        if (state is "unavailable-preference")
        {
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            await f.ViewModel.ExecuteResponseModeCommandAsync(new(Kora.Application.Configuration.AppearanceCommandOperation.Reset),
                Kora.Core.Auditing.SecurityAuditInitiator.LocalUser, cancellationToken: cancelled.Token);
        }
        var store = new SessionCommandStore(f);
        var workspace = new SessionWorkspaceService(store, new(f.HostStore), store, NullLogger<SessionWorkspaceService>.Instance);
        await using var conversation = new SessionConversationService(store, store, workspace, store, new SpeechSuggestionModel(),
            TimeProvider.System, NullLogger<SessionConversationService>.Instance);
        f.ViewModel.BindSessionCommands(workspace, new(workspace, conversation, store, TimeProvider.System));
        if (state is "locked") { f.Session.IsUnlocked = false; }
        if (state is "protected-voice") { f.CallState.SetState(CallState.Active); await f.Dispatcher.LastInvocation; }
        var capture = () => conversation.ReviewTaskResponseOutput(TaskResponseMode.Inherit,
            state is "protected-voice" ? RequestOrigin.ActivatedVoice : RequestOrigin.LocalUi);
        if (state is "known-preference") { capture().Configured.Should().Be(ResponseOutputMode.Hybrid); }
        else { capture.Should().Throw<InvalidOperationException>(); }
        store.RequestRetentions.Should().Be(0);
        f.TextToSpeech.SpeakCalls.Should().Be(0);
        await f.ViewModel.DisposeAsync();
    }

    [Theory]
    [InlineData("replace")]
    [InlineData("privacy")]
    [InlineData("call")]
    public async Task OneTaskOutputLatePlaybackNeverRetargetsOrReopensRetiredResponse(string retirement)
    {
        var f = await Fixture.CreateInitializedAsync();
        var (store, model) = BindSpeechSuggestions(f, bindSpeech: false);
        model.Answer = "Exact original answer.";
        await f.RunAsync("session request output " + store.Session.Authority.SessionId.Value.ToString("D")
            + " 1 0 VoiceOnly -- Original");
        f.TextToSpeech.SpeakGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var confirmation = f.ViewModel.ConfirmForegroundRequestAsync();
        await f.TextToSpeech.SpeakStarted.Task;
        if (retirement is "replace") { await f.RunAsync("session help"); }
        if (retirement is "privacy") { f.Session.IsUnlocked = false; }
        if (retirement is "call") { f.CallState.SetState(CallState.Active); await f.Dispatcher.LastInvocation; }
        var title = f.ViewModel.ResponseTitle;
        var body = f.ViewModel.ResponseBody;
        f.TextToSpeech.SpeakGate.TrySetResult();
        await confirmation;
        f.ViewModel.ResponseTitle.Should().Be(title);
        f.ViewModel.ResponseBody.Should().Be(body);
        f.ViewModel.TaskResponseMode.Should().BeNull();
        f.TextToSpeech.SpeakCalls.Should().Be(1);
        if (retirement is "replace") { f.ViewModel.IsVisualResponseVisible.Should().BeTrue(); }
        await f.ViewModel.DisposeAsync();
    }
}
