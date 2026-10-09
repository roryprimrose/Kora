using System.Globalization;

using AwesomeAssertions;

using Kora.Application.UnitTests.Interaction;
using Kora.Core.Auditing;
using Kora.Core.Commands;
using Kora.Core.Communication;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.Core.Voice;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Fact]
    public async Task Event_response_revalidates_a_call_change_after_the_brokers_final_original_channel_callback()
    {
        var f = await Fixture.CreateInitializedAsync();
        using var events = new LocalEventBrokerTests.Fixture();
        await using var broker = events.Create();
        var target = (await events.Observe(broker)).Events[0].Event;
        f.ViewModel.BindLocalEvents(broker);
        var reads = 0;
        events.OnAdmissionRead = () => { if (++reads == 3) { f.CallState.SetState(CallState.Active); } };
        await f.RunAsync("event review " + target.Id.ToString("D") + " 1");
        reads.Should().Be(3);
        f.ViewModel.ResponseBody.Should().NotContain(target.Id.ToString("D"));
        events.Saves.Should().Be(1);
        f.TextToSpeech.SpokenText.Should().BeNull();
        f.Reasoner.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData("status")]
    [InlineData("review")]
    [InlineData("dismiss")]
    [InlineData("defer")]
    public async Task Exact_event_commands_are_visual_native_even_with_voice_only_output_and_never_reason_execute_or_retarget(string verb)
    {
        var f = await Fixture.CreateInitializedAsync();
        using var events = new LocalEventBrokerTests.Fixture();
        await using var broker = events.Create();
        var target = (await events.Observe(broker)).Events[0].Event;
        f.ViewModel.BindLocalEvents(broker);
        f.ViewModel.DefaultResponseMode = ResponseOutputMode.VoiceOnly;
        f.TextToSpeech.ClearSpokenResponse();
        var windows = 0;
        f.ViewModel.WindowActionRequested += (_, _) => windows++;
        await f.RunAsync("event " + verb + " " + target.Id.ToString("D") + " " + target.Revision.ToString(CultureInfo.InvariantCulture));
        f.ViewModel.ResponseBody.Should().Contain(target.Id.ToString("D"));
        f.ViewModel.ResponseBody.Should().Contain(LocalEventSnapshot.Scope);
        f.ViewModel.IsVisualResponseVisible.Should().BeTrue();
        f.TextToSpeech.SpokenText.Should().BeNull();
        f.Reasoner.Requests.Should().BeEmpty();
        f.Session.LockCalls.Should().Be(0);
        windows.Should().Be(0);
        f.ViewModel.RevokeSessionPresentation(target.SessionId);
        f.ViewModel.ResponseBody.Should().Contain("revoked");
    }

    [Fact]
    public async Task Current_name_activated_exact_event_input_keeps_voice_origin_but_still_does_not_speak()
    {
        var f = await Fixture.CreateInitializedAsync();
        using var events = new LocalEventBrokerTests.Fixture();
        await using var broker = events.Create();
        var target = (await events.Observe(broker)).Events[0].Event;
        f.ViewModel.BindLocalEvents(broker);
        f.TextToSpeech.ClearSpokenResponse();
        await f.RaiseActivatedTranscriptAsync("Kora, event dismiss " + target.Id.ToString("D") + " 1", 1);
        events.Audits.Should().Contain(item => item.Event.ActionId == "local-event.dismiss"
            && item.Event.Initiator == SecurityAuditInitiator.VoiceCommand);
        f.TextToSpeech.SpokenText.Should().BeNull();
        f.Reasoner.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData("event status")]
    [InlineData("event check https://example.com")]
    [InlineData("event defer friendly-name 1")]
    [InlineData("event dismiss 00000000-0000-0000-0000-000000000000 1")]
    public async Task Reserved_invalid_event_text_is_not_a_model_request_or_effect(string text)
    {
        var f = await Fixture.CreateInitializedAsync();
        f.TextToSpeech.ClearSpokenResponse();
        await f.RunAsync(text);
        f.ViewModel.ResponseBody.Should().Contain(LocalEventCommand.Syntax);
        f.Reasoner.Requests.Should().BeEmpty();
        f.TextToSpeech.SpokenText.Should().BeNull();
    }

    [Theory]
    [InlineData("question")]
    [InlineData("approval")]
    [InlineData("grant")]
    public async Task Reserved_event_input_does_not_replace_or_answer_a_pending_foreground_question_or_approval(string pending)
    {
        var f = new Fixture();
        f.Probe.Status = new("local.inference", "Local model", DependencyReadiness.Ready, "ready");
        if (pending is "question") { f.Reasoner.Question = new("Which exact option?", ["One", "Two"]); }
        else { f.Reasoner.Action = BuiltInAction.LockMachine; }
        await f.ViewModel.InitializeAsync();
        if (pending is "grant") { await f.RunAsync("allow models to lock the machine this session"); }
        else { await f.RunAsync("please help with this workstation"); await f.ViewModel.ActiveReasoningTask!; }
        var body = f.ViewModel.ResponseBody;
        var title = f.ViewModel.ResponseTitle;
        await f.RunAsync("event defer");
        f.ViewModel.ResponseBody.Should().Be(body);
        f.ViewModel.ResponseTitle.Should().Be(title);
        f.ViewModel.Transcript.Should().Contain("remains unchanged");
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("system")]
    [InlineData("model")]
    [InlineData("protected-call")]
    [InlineData("unknown-call")]
    [InlineData("privacy")]
    public async Task Ineligible_original_channels_never_gain_cached_event_authority_or_hidden_replay(string boundary)
    {
        var f = await Fixture.CreateInitializedAsync();
        using var events = new LocalEventBrokerTests.Fixture();
        await using var broker = events.Create();
        var target = (await events.Observe(broker)).Events[0].Event;
        if (boundary is not "missing") { f.ViewModel.BindLocalEvents(broker); }
        if (boundary is "protected-call") { f.CallState.SetState(CallState.Active); }
        if (boundary is "unknown-call") { f.CallState.SetState(CallState.Unknown); }
        if (boundary is "privacy") { f.Session.IsUnlocked = false; }
        var initiator = boundary is "system" ? SecurityAuditInitiator.System
            : boundary is "model" ? SecurityAuditInitiator.ModelSuggestion : SecurityAuditInitiator.LocalUser;
        await f.ViewModel.ExecuteLocalEventCommandAsync(new(LocalEventOperation.Status, target.Id, target.Revision), initiator);
        f.ViewModel.ResponseBody.Should().Contain("unavailable");
        f.ViewModel.ResponseBody.Should().NotContain(target.Id.ToString("D"));
        events.Saves.Should().Be(1);
        f.Reasoner.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData("failure")]
    [InlineData("corrupt")]
    [InlineData("cancellation")]
    [InlineData("call")]
    [InlineData("lock")]
    [InlineData("dispose")]
    [InlineData("voice")]
    [InlineData("topology")]
    public async Task Late_channel_privacy_and_source_failures_cannot_present_a_current_notice(string boundary)
    {
        var f = await Fixture.CreateInitializedAsync();
        using var events = new LocalEventBrokerTests.Fixture();
        await using var broker = events.Create();
        var target = (await events.Observe(broker)).Events[0].Event;
        f.ViewModel.BindLocalEvents(broker);
        events.BeforeRead = _ =>
        {
            switch (boundary)
            {
                case "failure": throw new IOException("Private source text must not be logged.");
                case "corrupt": throw new InvalidDataException("Private source text must not be logged.");
                case "cancellation": throw new OperationCanceledException("Private source text must not be logged.");
                case "call": f.CallState.SetState(CallState.Active); break;
                case "lock": f.Session.IsUnlocked = false; break;
                case "dispose": f.ViewModel.Dispose(); break;
                case "voice": typeof(Kora.Application.ViewModels.MainViewModel)
                    .GetField("voiceEnabled", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                    .SetValue(f.ViewModel, 0); break;
                case "topology": f.PrivacyObservation.Current = f.PrivacyObservation.Current with
                    { TopologyRevision = f.PrivacyObservation.Current.TopologyRevision + 1 }; break;
            }
            return Task.CompletedTask;
        };
        var text = "event review " + target.Id.ToString("D") + " 1";
        if (boundary is "voice") { await f.RaiseActivatedTranscriptAsync("Kora, " + text, 1); }
        else { await f.RunAsync(text); }
        f.ViewModel.ResponseBody.Should().NotContain(target.Id.ToString("D"));
        f.Reasoner.Requests.Should().BeEmpty();
        f.ViewModel.Dispose();
        await f.ViewModel.ExecuteLocalEventCommandAsync(new(LocalEventOperation.Invalid), SecurityAuditInitiator.LocalUser);
    }
}
