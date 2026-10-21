using System.Reflection;
using AwesomeAssertions;
using Kora.Application.Hosting;
using Kora.Application.UnitTests.Clock;
using Kora.Core.Auditing;
using Kora.Core.Commands;
using Kora.Core.Communication;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Voice;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Fact]
    public async Task ClockTypedAnswersAndHelpUseOneHostInstantWithoutModelsSpeechOrDeviceEnumeration()
    {
        var clock = new ClockTestHost();
        var f = new Fixture(localClock: clock.CreateWorkflow());
        await f.ViewModel.InitializeAsync();
        f.ViewModel.DefaultResponseMode = ResponseOutputMode.VoiceOnly;
        await f.ViewModel.PreferenceUpdate;
        f.TextToSpeech.ClearSpokenResponse();
        var events = f.Events.ToArray();
        var audits = f.Audit.Events.Count;
        var outputs = f.TextToSpeech.OutputEnumerationCalls;
        foreach (var input in new[] { "what time is it?", "Kora, what is today's date", "what is the date and time" })
        {
            await f.RunAsync(input);
            f.ViewModel.ResponseTitle.Should().Be("Host-local date and time.");
            f.ViewModel.ResponseBody.Should().Contain("UTC+14:00; fixture/+14");
            f.ViewModel.IsVisualResponseVisible.Should().BeTrue();
        }
        f.ViewModel.ResponseBody.Should().Be("Local date and time: 2026-01-01, 00:00:00 (UTC+14:00; fixture/+14).");
        foreach (var input in new[] { "help local clock", "what time is it in Paris" })
        {
            await f.RunAsync(input);
            f.ViewModel.ResponseBody.Should().Be(LocalClockCommand.Syntax);
        }
        clock.Reads.Should().Be(3);
        f.Reasoner.Requests.Should().BeEmpty();
        f.TextToSpeech.SpokenText.Should().BeNull();
        f.Events.Where(item => !item.StartsWith("window.", StringComparison.Ordinal)).Should().Equal(events);
        f.TextToSpeech.OutputEnumerationCalls.Should().Be(outputs);
        f.Audit.Events.Should().HaveCount(audits);
    }

    [Fact]
    public async Task ClockCurrentNameActivatedVoiceUsesTheSameActionsAndRecognitionGrammar()
    {
        var clock = new ClockTestHost();
        var f = new Fixture(localClock: clock.CreateWorkflow());
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.SetAssistantNameAsync("Nova", SecurityAuditInitiator.LocalUser);
        await f.RaiseActivatedTranscriptAsync("Nova, what is the date and time?", 1);
        f.ViewModel.ResponseBody.Should().Contain("2026-01-01, 00:00:00");
        clock.Reads.Should().Be(1);
        foreach (var phrase in LocalClockCommand.FixedPhrases)
        {
            f.Voice.StartedPhrases.Should().Contain(phrase).And.Contain("Nova " + phrase);
        }
        await f.RaiseActivatedTranscriptAsync("what time is it", 1);
        clock.Reads.Should().Be(1);
        f.ViewModel.Transcript.Should().StartWith("Local clock unavailable:");
    }

    [Theory]
    [InlineData("question")]
    [InlineData("approval")]
    [InlineData("grant")]
    [InlineData("power")]
    [InlineData("native-review")]
    [InlineData("grant-editor")]
    public async Task ClockPreservesTheCompletePendingInteractionAndNeverAnswersIt(string pending)
    {
        var clock = new ClockTestHost();
        var f = new Fixture(localClock: clock.CreateWorkflow());
        f.Probe.Status = new("local.inference", "Local", DependencyReadiness.Ready, "fixture");
        await f.ViewModel.InitializeAsync();
        switch (pending)
        {
            case "question":
            case "approval":
                if (string.Equals(pending, "question", StringComparison.Ordinal)) { f.Reasoner.Question = new("Exact?", ["One", "Two"]); }
                else { f.Reasoner.Action = BuiltInAction.LockMachine; }
                await f.RunAsync("please do this work");
                await f.ViewModel.ActiveReasoningTask!;
                break;
            case "grant":
                await f.ViewModel.PrepareGrantChangeAsync(new(GrantChangeOperation.Add, BuiltInAction.LockMachine, ModelApprovalScope.Session));
                break;
            case "power": await f.RunAsync("shut down the computer"); break;
            case "native-review": f.ViewModel.BindResponseReadDecisionGate(static () => false); break;
            case "grant-editor": await f.RunAsync("manage grants"); break;
        }
        var body = f.ViewModel.ResponseBody;
        var title = f.ViewModel.ResponseTitle;
        var choices = f.ViewModel.ModelQuestionChoices;
        var audits = f.Audit.Events.Count;
        var requests = f.Reasoner.Requests.Count;
        foreach (var phrase in LocalClockCommand.FixedPhrases.Append("what time is it please"))
        {
            await f.RunAsync(phrase);
            f.ViewModel.ResponseBody.Should().Be(body);
            f.ViewModel.ResponseTitle.Should().Be(title);
            f.ViewModel.ModelQuestionChoices.Should().BeSameAs(choices);
        }
        f.Audit.Events.Should().HaveCount(audits);
        f.Reasoner.Requests.Should().HaveCount(requests);
        clock.Reads.Should().Be(0);
        f.Session.LockCalls.Should().Be(0);
    }

    [Theory]
    [InlineData("owner")]
    [InlineData("private")]
    [InlineData("locked")]
    [InlineData("call")]
    [InlineData("name")]
    [InlineData("external")]
    public async Task ClockRejectsUnknownOrUnadmittedOriginalHostState(string loss)
    {
        var clock = new ClockTestHost();
        var f = new Fixture(localClock: clock.CreateWorkflow());
        switch (loss)
        {
            case "owner": f.ViewModel.BindCallOwnershipGate(static () => false); break;
            case "private": f.ViewModel.CloseForObservedPrivacyEvent("fixture", true); break;
            case "locked": f.Session.IsUnlocked = false; break;
            case "call": f.CallState.SetState(CallState.Unknown); break;
            case "name": f.NamePreferences.Name = "\0"; await f.ViewModel.InitializeAsync(); break;
        }
        var body = f.ViewModel.ResponseBody;
        if (string.Equals(loss, "external", StringComparison.Ordinal))
        {
            await HostRequestRunner.RunAsync(RequestOrigin.HostSystem, () => f.RunAsync("what time is it"));
        }
        else { await f.RunAsync("what time is it"); }
        clock.Reads.Should().Be(0);
        f.ViewModel.ResponseBody.Should().Be(body);
        f.ViewModel.Transcript.Should().StartWith("Local clock unavailable:");
    }

    [Theory]
    [InlineData("stale")]
    [InlineData("unavailable")]
    [InlineData("unbound")]
    public async Task ClockRetiresLateAnswersAndReportsUnavailableWithoutReplacingTheOriginal(string state)
    {
        var clock = new ClockTestHost();
        var f = new Fixture(localClock: string.Equals(state, "unbound", StringComparison.Ordinal) ? null : clock.CreateWorkflow());
        if (string.Equals(state, "stale", StringComparison.Ordinal)) { clock.BeforeRead = () => f.ViewModel.BindCallOwnershipGate(static () => false); }
        if (string.Equals(state, "unavailable", StringComparison.Ordinal)) { clock.Failure = new InvalidTimeZoneException("fixture"); }
        var body = f.ViewModel.ResponseBody;
        await f.RunAsync("what time is it");
        f.ViewModel.ResponseBody.Should().Be(body);
        f.ViewModel.Transcript.Should().StartWith("Local clock unavailable");
    }

    [Theory]
    [InlineData(SecurityAuditInitiator.ModelSuggestion)]
    [InlineData(SecurityAuditInitiator.LocalUser)]
    [InlineData((SecurityAuditInitiator)999)]
    public async Task ClockRejectsNonCommandInitiators(SecurityAuditInitiator initiator)
    {
        var clock = new ClockTestHost();
        var f = new Fixture(localClock: clock.CreateWorkflow());
        var routing = typeof(Kora.Application.ViewModels.MainViewModel).GetMethod(
            "HandleTranscriptAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        await (Task)routing.Invoke(f.ViewModel, ["what time is it", 1f, initiator, null, null, null])!;
        clock.Reads.Should().Be(0);
    }

    [Fact]
    public async Task ClockRejectsQueuedVoiceWhoseOwnershipRevisionChanged()
    {
        var clock = new ClockTestHost();
        var f = new Fixture(localClock: clock.CreateWorkflow());
        await f.RaiseActivatedTranscriptAsync("Kora, what can you do", 1);
        long owner = 1;
        f.ViewModel.BindCallOwnershipGate(static () => true, () => owner);
        await f.ViewModel.BeginPushToTalkAsync();
        await f.ViewModel.EndPushToTalkAsync();
        f.Dispatcher.BeforeInvoke = () => owner++;
        await f.Voice.RaiseTranscriptAsync("Kora, what time is it", 1);
        clock.Reads.Should().Be(0);
        f.ViewModel.Transcript.Should().StartWith("Local clock unavailable:");
    }

    [Fact]
    public async Task ClockRejectsARetiredVoiceGenerationBeforeRouting()
    {
        var clock = new ClockTestHost();
        var f = new Fixture(localClock: clock.CreateWorkflow());
        await f.RaiseActivatedTranscriptAsync("Kora, what can you do", 1);
        await f.ViewModel.BeginPushToTalkAsync();
        await f.ViewModel.EndPushToTalkAsync();
        f.Dispatcher.BeforeInvoke = f.Voice.AdvanceGeneration;
        await f.Voice.RaiseTranscriptAsync("Kora, what time is it", 1);
        clock.Reads.Should().Be(0);
        f.ViewModel.Transcript.Should().StartWith("Local clock unavailable:");
    }

    [Fact]
    public async Task ClockRemainsAnOrdinaryVisualAnswerWithoutAWindowSubscriber()
    {
        var clock = new ClockTestHost();
        var f = new Fixture(subscribeToWindowActions: false, localClock: clock.CreateWorkflow());
        await f.RunAsync("what time is it");
        f.ViewModel.ResponseBody.Should().Contain("UTC+14:00; fixture/+14");
    }
}
