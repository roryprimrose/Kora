using AwesomeAssertions;

using Kora.Application.Configuration;
using Kora.Application.Interaction;
using Kora.Application.UnitTests.Configuration;
using Kora.Application.UnitTests.Interaction;
using Kora.Core.Auditing;
using Kora.Core.Communication;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Voice;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Theory]
    [InlineData(CallState.Unknown)]
    [InlineData(CallState.Suspected)]
    public async Task Communications_observations_retire_conversational_capture_without_disabling_the_shared_preference_or_replaying(CallState state)
    {
        await using var activity = new CommunicationActivityFixture();
        var fixture = new Fixture(enableConversationalReplies: true, enableQuestionReplies: true,
            communicationActivity: activity.Observations, communicationActivityConfiguration: activity.Configuration);
        fixture.VoiceConsent.Consent = true;
        fixture.Voice.Microphones = [new("mic", "Synthetic")];
        fixture.Voice.DefaultMicrophoneId = "mic";
        fixture.ViewModel.BindConversationalReplyNativeLifetime(static () => true);
        await fixture.ViewModel.InitializeAsync();
        await activity.Observations.JoinAsync();
        await fixture.ViewModel.EnableConversationalRepliesCommand.ExecuteAsync();
        await fixture.ViewModel.EnableCommunicationActivityCommand.ExecuteAsync();
        fixture.ViewModel.AutomaticCallState.Should().Be(CallState.Unknown);
        fixture.Voice.IsListening.Should().BeFalse();
        activity.Source.Signal(CallState.Clear, activity.Time.GetUtcNow());
        await fixture.ViewModel.CallClosureTask;

        using var host = new InteractionFixture();
        var question = await host.QuestionAsync();
        await using var replies = fixture.ViewModel.BindQuestionVoiceReplies(host.Questions, host.Time,
            NullLogger<HostQuestionVoiceReplyService>.Instance)!;
        await replies.OpenConversationalAsync(question, static () => true, _ => { }, activity.Token);
        fixture.Voice.IsListening.Should().BeTrue();
        fixture.TextToSpeech.SpokenText.Should().Be(question.Spec.Text);
        var captures = fixture.Voice.StartCalls;
        var spoken = fixture.TextToSpeech.SpokenText;

        activity.Source.Signal(state, activity.Time.GetUtcNow());
        await fixture.ViewModel.CallClosureTask;
        await replies.Quiescence;
        fixture.ViewModel.AutomaticCallState.Should().Be(state);
        fixture.ViewModel.IsProtectedCall.Should().BeTrue();
        fixture.Voice.IsListening.Should().BeFalse();
        fixture.ViewModel.IsSpeechResponseEnabled.Should().BeFalse();
        fixture.ViewModel.AreConversationalRepliesEnabled.Should().BeTrue();
        fixture.ConversationalPreferences.Value.Should().BeTrue();
        activity.Preferences.Load().Should().Be(CommunicationActivitySelection.WindowsCommunicationsAudio);
        host.Store.Snapshot.Questions.Single().Status.Should().Be(Kora.Core.Interaction.QuestionStatus.Pending);

        activity.Source.Signal(CallState.Clear, activity.Time.GetUtcNow());
        await fixture.ViewModel.CallClosureTask;
        fixture.Voice.StartCalls.Should().Be(captures);
        fixture.Voice.IsListening.Should().BeFalse();
        fixture.TextToSpeech.SpokenText.Should().Be(spoken);
        await fixture.ViewModel.DisposeAsync();
    }

    [Fact]
    public async Task Selected_source_reaches_native_consumer_and_invalidates_active_speech_before_dispatch_without_capture_or_replay()
    {
        await using var activity = new CommunicationActivityFixture();
        var fixture = await Fixture.CreateInitializedAsync(communicationActivity: activity.Observations,
            communicationActivityConfiguration: activity.Configuration);
        await activity.Observations.JoinAsync();
        fixture.ViewModel.CanEnableCommunicationActivity.Should().BeTrue();
        fixture.ViewModel.CanChangeCommunicationActivity.Should().BeTrue();
        fixture.ViewModel.CallProtectionLimitations.Should().Contain("supporting observations only");
        var captures = fixture.Voice.StartCalls;
        await fixture.ViewModel.EnableCommunicationActivityCommand.ExecuteAsync();
        activity.Preferences.Load().Should().Be(CommunicationActivitySelection.WindowsCommunicationsAudio);
        fixture.ViewModel.IsProtectedCall.Should().BeTrue();
        activity.Source.Signal(CallState.Clear, activity.Time.GetUtcNow());
        await fixture.ViewModel.CallClosureTask;
        fixture.TextToSpeech.SpeakGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var response = fixture.RunAsync("what power action is pending");
        await fixture.TextToSpeech.SpeakStarted.Task.WaitAsync(TimeSpan.FromSeconds(10), activity.Token);
        fixture.Dispatcher.BeforeInvoke = () => fixture.Events.Should().Contain("speech.invalidate");
        activity.Source.Signal(CallState.Suspected, activity.Time.GetUtcNow());
        await fixture.ViewModel.CallClosureTask;
        await response;
        fixture.TextToSpeech.IsSpeaking.Should().BeFalse();
        fixture.ViewModel.IsSpeechResponseEnabled.Should().BeFalse();
        fixture.Voice.StartCalls.Should().Be(captures);
        var spoken = fixture.TextToSpeech.SpokenText;
        activity.Source.Signal(CallState.Clear, activity.Time.GetUtcNow());
        await fixture.ViewModel.CallClosureTask;
        fixture.TextToSpeech.SpokenText.Should().Be(spoken);
        fixture.ViewModel.CommunicationActivityStatus.Should().Contain("at most Suspected").And.Contain("maximum age: 5 seconds");
        await fixture.ViewModel.DisableCommunicationActivityCommand.ExecuteAsync();
        fixture.ViewModel.AutomaticCallState.Should().Be(CallState.Unavailable);
        await fixture.ViewModel.DisposeAsync();
    }

    [Fact]
    public async Task Status_native_typed_and_original_voice_share_one_source_setting_without_authority_relabelling()
    {
        await using var activity = new CommunicationActivityFixture();
        var fixture = await Fixture.CreateInitializedAsync(communicationActivity: activity.Observations,
            communicationActivityConfiguration: activity.Configuration);
        await activity.Observations.JoinAsync();
        await fixture.RunAsync("set calls.activity-source to WindowsCommunicationsAudio");
        fixture.ViewModel.IsProtectedCall.Should().BeTrue();
        using (var voice = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.ActivatedVoice),
            HostActivityLayer.Application, HostOperation.Request))
        {
            await fixture.ViewModel.DisableCommunicationActivityCommand.ExecuteAsync();
        }
        activity.Preferences.Load().Should().Be(CommunicationActivitySelection.WindowsCommunicationsAudio);
        await fixture.ViewModel.DeniedMutationAudit;
        fixture.ViewModel.ResponseBody.Should().Contain("Voice-originated");
        await fixture.RunAsync("status calls.activity-source");
        fixture.ViewModel.ResponseBody.Should().Contain("Selected source");
        await fixture.ViewModel.InspectCommunicationActivityCommand.ExecuteAsync();
        fixture.ViewModel.ResponseBody.Should().Contain("Communications audio is at most Suspected");
        await fixture.RunAsync("set calls.activity-source to invalid");
        fixture.ViewModel.ResponseTitle.Should().Contain("Clarify");
        fixture.ViewModel.BindManualCallNativeLifetime(false);
        await fixture.ViewModel.DisableCommunicationActivityCommand.ExecuteAsync();
        fixture.ViewModel.Transcript.Should().Contain("nothing was saved");
        fixture.ViewModel.BindManualCallNativeLifetime(true);
        await fixture.RunAsync("reset calls.activity-source");
        activity.Preferences.Load().Should().Be(CommunicationActivitySelection.None);
        await fixture.ViewModel.DisposeAsync();
    }

    [Fact]
    public async Task Unqualified_source_stays_unavailable_and_error_or_host_loss_never_enables_or_discloses_content()
    {
        await using var activity = new CommunicationActivityFixture();
        activity.Source.IsQualified = false;
        var fixture = await Fixture.CreateInitializedAsync(communicationActivity: activity.Observations,
            communicationActivityConfiguration: activity.Configuration);
        await activity.Observations.JoinAsync();
        fixture.ViewModel.CanEnableCommunicationActivity.Should().BeFalse();
        fixture.ViewModel.CommunicationActivityStatus.Should().Contain("unavailable - no qualified Windows profile");
        fixture.ViewModel.CallProtectionLimitations.Should().Contain("not available");
        await fixture.RunAsync("set calls.activity-source to WindowsCommunicationsAudio");
        activity.Source.Opens.Should().Be(0);
        activity.Preferences.Load().Should().Be(CommunicationActivitySelection.None);
        fixture.ViewModel.BindCallOwnershipGate(static () => false);
        await fixture.ViewModel.EnableCommunicationActivityCommand.ExecuteAsync();
        fixture.ViewModel.Transcript.Should().Contain("nothing was saved");
        fixture.ViewModel.BindCallOwnershipGate(static () => true);
        activity.Store.BeforeRead = _ => throw new IOException("deterministic read failure");
        await fixture.ViewModel.InitializeAsync();
        fixture.ViewModel.CommunicationActivityStatus.Should().Contain("unconfirmed");
        fixture.ViewModel.IsSpeechResponseEnabled.Should().BeFalse();
        await fixture.ViewModel.DisposeAsync();
    }

    [Fact]
    public async Task Missing_optional_source_has_truthful_passive_status_and_no_mutation_route()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        fixture.ViewModel.CommunicationActivityStatus.Should().Contain("unavailable");
        fixture.ViewModel.CanChangeCommunicationActivity.Should().BeFalse();
        await fixture.ViewModel.InspectCommunicationActivityCommand.ExecuteAsync();
        await fixture.ViewModel.EnableCommunicationActivityCommand.ExecuteAsync();
        fixture.ViewModel.Transcript.Should().Contain("nothing was saved");
        await fixture.ViewModel.DisposeAsync();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Source_write_failure_and_disposal_during_write_are_explicit_and_never_restore_output(bool dispose)
    {
        await using var activity = new CommunicationActivityFixture();
        var fixture = await Fixture.CreateInitializedAsync(communicationActivity: activity.Observations,
            communicationActivityConfiguration: activity.Configuration);
        await activity.Observations.JoinAsync();
        activity.Store.BeforeWrite = name =>
        {
            if (name is not "communication-activity.txt") { return; }
            if (dispose) { fixture.ViewModel.Dispose(); }
            else { throw new IOException("deterministic write failure"); }
        };
        await fixture.ViewModel.EnableCommunicationActivityCommand.ExecuteAsync();
        activity.Observations.CurrentState.Should().Be(CallState.Unknown);
        activity.Source.Opens.Should().Be(0);
        if (!dispose) { fixture.ViewModel.Transcript.Should().Contain("not confirmed"); }
        activity.Store.BeforeWrite = null;
        await fixture.ViewModel.DisposeAsync();
    }

    [Fact]
    public async Task Unavailable_prefix_and_pending_question_preserve_original_routing_and_complete_visual_content()
    {
        await using var activity = new CommunicationActivityFixture();
        var fixture = new Fixture(communicationActivity: activity.Observations,
            communicationActivityConfiguration: activity.Configuration);
        fixture.NamePreferences.Name = "Supported Commands";
        await fixture.ViewModel.InitializeAsync();
        await activity.Observations.JoinAsync();
        await fixture.RunAsync("Kora, get calls.activity-source");
        fixture.ViewModel.Transcript.Should().Contain("prefix routing is unavailable");
        await fixture.RunAsync("get calls.activity-source");
        fixture.ViewModel.ResponseBody.Should().Contain("Selected source");
        await fixture.ViewModel.DisposeAsync();

        await using var pendingActivity = new CommunicationActivityFixture();
        var pending = new Fixture(communicationActivity: pendingActivity.Observations,
            communicationActivityConfiguration: pendingActivity.Configuration);
        pending.Probe.Status = new("local.inference", "Local model inference (Ollama)",
            Kora.Core.Dependencies.DependencyReadiness.Ready, "ready");
        pending.Reasoner.Action = Kora.Core.Commands.BuiltInAction.EnableHostedModels;
        pending.Reasoner.Question = new("Exact choice?", ["one", "two"]);
        await pending.ViewModel.InitializeAsync();
        await pendingActivity.Observations.JoinAsync();
        await pending.RunAsync("please do this work");
        await pending.ViewModel.ActiveReasoningTask!;
        var body = pending.ViewModel.ResponseBody;
        await pending.ViewModel.InspectCommunicationActivityCommand.ExecuteAsync();
        pending.ViewModel.Transcript.Should().Contain("Selected source");
        pending.ViewModel.ResponseBody.Should().Be(body);
        await pending.ViewModel.EnableCommunicationActivityCommand.ExecuteAsync();
        pendingActivity.Source.Opens.Should().Be(0);
        pending.Session.LockCalls.Should().Be(0);
        await pending.ViewModel.DisposeAsync();
    }

    [Fact]
    public async Task Private_epoch_change_retires_the_source_and_queued_UI_callbacks_cannot_revive_a_disposed_surface()
    {
        await using var activity = new CommunicationActivityFixture();
        var fixture = await Fixture.CreateInitializedAsync(communicationActivity: activity.Observations,
            communicationActivityConfiguration: activity.Configuration);
        await activity.Observations.JoinAsync();
        await fixture.ViewModel.EnableCommunicationActivityCommand.ExecuteAsync();
        var previous = fixture.PrivacyObservation.Current;
        var changed = previous with
        {
            TopologyRevision = previous.TopologyRevision + 1,
            SessionState = Kora.Core.Platform.WindowsSessionState.Locked
        };
        fixture.PrivacyObservation.Current = changed;
        fixture.PrivacyObservation.Publish(new(previous, changed,
            Kora.Core.Platform.WindowsPrivacyChangeReason.Session, Kora.Core.Platform.PrivacyObservation.Create()));
        await activity.Source.Cancelled.Task.WaitAsync(activity.Token);
        activity.Observations.CurrentState.Should().Be(CallState.Unknown);
        fixture.Dispatcher.DeferPosts = true;
        activity.Observations.Refresh();
        fixture.ViewModel.Dispose();
        fixture.Dispatcher.FlushPosts();
        fixture.ViewModel.IsSpeechResponseEnabled.Should().BeFalse();
        await fixture.ViewModel.DisposeAsync();
    }

    [Fact]
    public async Task Quarantined_source_status_is_not_mistaken_for_successful_release()
    {
        await using var activity = new CommunicationActivityFixture();
        activity.Source.OpenFailure = new IOException("deterministic failed source");
        activity.Source.HasUnreleasedResources = true;
        var fixture = await Fixture.CreateInitializedAsync(communicationActivity: activity.Observations,
            communicationActivityConfiguration: activity.Configuration);
        await activity.Observations.JoinAsync();
        await fixture.ViewModel.EnableCommunicationActivityCommand.ExecuteAsync();
        await FluentActions.Awaiting(activity.Observations.JoinAsync).Should().ThrowAsync<InvalidOperationException>();
        fixture.ViewModel.CommunicationActivityStatus.Should().Contain("quarantined");
        fixture.ViewModel.Dispose();
        await activity.Observations.DisposeAsync();
        await fixture.ViewModel.DisposeAsync();
    }
}