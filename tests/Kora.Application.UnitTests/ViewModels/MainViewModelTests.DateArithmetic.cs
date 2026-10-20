using AwesomeAssertions;
using Kora.Application.ViewModels;
using Kora.Core.Auditing;
using Kora.Core.Commands;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Theory]
    [InlineData("date help", "Calendar dates only")]
    [InlineData("date difference 2000-02-28 2000-03-01", "2 whole calendar days")]
    [InlineData("date difference 2000-03-01 2000-02-28", "-2 whole calendar days")]
    [InlineData("date add 2000-03-01 -1", "2000-02-29")]
    [InlineData("date add 0001-01-01 3652058", "9999-12-31")]
    [InlineData("Kora, date add 9999-12-31 -3652058", "0001-01-01")]
    public async Task DateCommandsUseRegisteredCalendarActionsWithoutModelOrForcedSpeech(string text, string result)
    {
        var f = new Fixture();
        var before = f.Events.ToArray();
        await f.RunAsync(text);
        f.ViewModel.ResponseTitle.Should().Be("Calendar date arithmetic");
        f.ViewModel.ResponseBody.Should().Contain(result);
        f.Reasoner.Requests.Should().BeEmpty();
        f.Events.Where(item => !item.StartsWith("window.", StringComparison.Ordinal)).Should().Equal(before);
        f.TextToSpeech.SpokenText.Should().BeNull();
        f.Audit.Events.Should().BeEmpty();
    }

    [Theory]
    [InlineData("date add 1900-02-29 1", "input invalid")]
    [InlineData("date add 9999-12-31 1", "failed")]
    [InlineData("date add 0001-01-01 -1", "failed")]
    [InlineData("date help and approve once", "input invalid")]
    [InlineData("date add 2000-02-29 +1", "input invalid")]
    public async Task MalformedDatesAndDomainOverflowNeverFallback(string text, string outcome)
    {
        var f = new Fixture();
        await f.RunAsync(text);
        f.ViewModel.Transcript.Should().StartWith("Date arithmetic " + outcome + ".");
        f.Reasoner.Requests.Should().BeEmpty();
        f.TextToSpeech.SpokenText.Should().BeNull();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DateCommandsPreservePendingQuestionAndActionApproval(bool approval)
    {
        var f = new Fixture();
        f.Probe.Status = new("local.inference", "Local inference", DependencyReadiness.Ready, "fixture");
        if (approval) { f.Reasoner.Action = BuiltInAction.LockMachine; }
        else { f.Reasoner.Question = new("Which target?", ["One", "Two"]); }
        await f.ViewModel.InitializeAsync();
        await f.RunAsync("help with this machine");
        await f.ViewModel.ActiveReasoningTask!;
        var title = f.ViewModel.ResponseTitle;
        var body = f.ViewModel.ResponseBody;
        var requests = f.Reasoner.Requests.Count;
        await f.RunAsync("date help");
        await f.RunAsync("date add 2000-02-29 +0");
        f.ViewModel.IsModelActionApprovalPending.Should().Be(approval);
        f.ViewModel.IsModelQuestionPending.Should().Be(!approval);
        f.ViewModel.ResponseTitle.Should().Be(title);
        f.ViewModel.ResponseBody.Should().Be(body);
        f.ViewModel.Transcript.Should().Contain("pending");
        f.Reasoner.Requests.Should().HaveCount(requests);
        f.Session.LockCalls.Should().Be(0);
    }

    [Fact]
    public async Task DateCommandsPreserveGrantReviewAndFailClosedWithoutRegistration()
    {
        var f = new Fixture();
        await f.ViewModel.PrepareGrantChangeAsync(new(GrantChangeOperation.Add, BuiltInAction.LockMachine, ModelApprovalScope.Session));
        var body = f.ViewModel.ResponseBody;
        await f.RunAsync("date help");
        f.ViewModel.IsGrantChangePending.Should().BeTrue();
        f.ViewModel.ResponseBody.Should().Be(body);
        var unavailable = new Fixture(enableDateArithmetic: false);
        await unavailable.RunAsync("date help");
        unavailable.ViewModel.Transcript.Should().Contain("not registered");
        unavailable.Reasoner.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData("owner")]
    [InlineData("private")]
    [InlineData("locked")]
    [InlineData("name")]
    [InlineData("call")]
    [InlineData("origin")]
    public async Task DateCommandsRetainOriginalHostPrivacyCallNameAndOriginGates(string loss)
    {
        var f = new Fixture();
        if (loss.Equals("owner", StringComparison.Ordinal)) { f.ViewModel.BindCallOwnershipGate(static () => false); }
        if (loss.Equals("private", StringComparison.Ordinal)) { f.ViewModel.CloseForObservedPrivacyEvent("fixture", true); }
        if (loss.Equals("locked", StringComparison.Ordinal)) { f.Session.IsUnlocked = false; }
        if (loss.Equals("name", StringComparison.Ordinal))
        {
            f.NamePreferences.Name = "\0";
            await f.ViewModel.InitializeAsync();
        }
        if (loss.Equals("call", StringComparison.Ordinal))
        {
            await f.RunAsync("set call.manual-active to on");
            f.ViewModel.IsProtectedCall.Should().BeTrue();
        }
        using var system = loss.Equals("origin", StringComparison.Ordinal)
            ? HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.HostSystem), HostActivityLayer.Application, HostOperation.Request) : null;
        var body = f.ViewModel.ResponseBody;
        await f.RunAsync("date add 2000-02-29 1");
        f.ViewModel.ResponseBody.Should().Be(body);
        f.Reasoner.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData("Date")]
    [InlineData("Date Help")]
    [InlineData("What Time")]
    public async Task DateCommandsUseCurrentActivationNameWithoutCollisions(string name)
    {
        var f = new Fixture();
        f.NamePreferences.Name = name;
        await f.ViewModel.InitializeAsync();
        await f.RunAsync(name + ", date add 2000-02-29 1");
        f.ViewModel.ResponseBody.Should().Be("2000-03-01");
        await f.RunAsync("date help");
        f.ViewModel.ResponseTitle.Should().Be("Calendar date arithmetic");
        f.Reasoner.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData("What Time, date add 2000-02-29 1", true)]
    [InlineData("date add 2000-02-29 1", false)]
    public async Task DateVoiceCommandsRequireCurrentNameAndOriginalConsent(string text, bool expected)
    {
        var f = new Fixture();
        await f.ViewModel.InitializeAsync();
        f.ViewModel.AssistantNameInput = "What Time";
        await f.ViewModel.ApplyAssistantNameCommand.ExecuteAsync();
        var before = f.ViewModel.ResponseBody;
        await f.RaiseActivatedTranscriptAsync(text, 1);
        f.ViewModel.ResponseBody.Should().Be(expected ? "2000-03-01" : before);
        f.Reasoner.Requests.Should().BeEmpty();
        f.TextToSpeech.SpokenText.Should().NotBe("2000-03-01");
    }

    [Theory]
    [InlineData(SecurityAuditInitiator.ModelSuggestion)]
    [InlineData(SecurityAuditInitiator.LocalUser)]
    [InlineData((SecurityAuditInitiator)999)]
    public async Task DateCommandsCannotBePromotedFromNonCommandInitiators(SecurityAuditInitiator initiator)
    {
        var f = new Fixture();
        var routing = typeof(Kora.Application.ViewModels.MainViewModel).GetMethod(
            "HandleTranscriptAsync", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        await (Task)routing.Invoke(f.ViewModel, ["date help", 1f, initiator, null, null, null])!;
        f.ViewModel.Transcript.Should().Contain("unavailable");
        f.Reasoner.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task DateCommandsPreserveActionPreviewAndRejectRetiredQueuedVoice()
    {
        var f = new Fixture();
        var action = new ResponseAction((ResponseActionKind)int.MaxValue, "Security preview");
        typeof(Kora.Application.ViewModels.MainViewModel).GetProperty(nameof(f.ViewModel.ResponseActions))!
            .SetValue(f.ViewModel, new[] { action });
        var body = f.ViewModel.ResponseBody;
        await f.RunAsync("date help");
        f.ViewModel.ResponseActions.Should().ContainSingle().Which.Should().BeSameAs(action);
        f.ViewModel.ResponseBody.Should().Be(body);
        f.ViewModel.Transcript.Should().Contain("pending");
        var voice = new Fixture();
        await voice.RaiseActivatedTranscriptAsync("Kora, what can you do", 1);
        await voice.ViewModel.BeginPushToTalkAsync();
        await voice.ViewModel.EndPushToTalkAsync();
        body = voice.ViewModel.ResponseBody;
        voice.Dispatcher.BeforeInvoke = voice.Voice.AdvanceGeneration;
        await voice.Voice.RaiseTranscriptAsync("Kora, date add 2000-02-29 1", 1);
        voice.ViewModel.ResponseBody.Should().Be(body);
        voice.Reasoner.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task DateCommandsRejectReplacedHostTraceRatherThanCreatingFreshAuthority()
    {
        var f = new Fixture();
        using var root = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi),
            HostActivityLayer.Application, HostOperation.Request);
        using var replacement = new System.Diagnostics.Activity("untrusted-replacement").Start();
        var body = f.ViewModel.ResponseBody;
        HostActivity.HasScope.Should().BeTrue();
        HostActivity.Current.Should().BeNull();
        var routing = typeof(Kora.Application.ViewModels.MainViewModel).GetMethod(
            "HandleTranscriptAsync", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        await (Task)routing.Invoke(f.ViewModel, ["date help", 1f, SecurityAuditInitiator.TypedCommand, null, null, null])!;
        f.ViewModel.Transcript.Should().Contain("unavailable");
        f.ViewModel.ResponseBody.Should().Be(body);
        f.Reasoner.Requests.Should().BeEmpty();
    }
}
