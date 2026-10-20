using AwesomeAssertions;
using Kora.Core.Commands;
using Kora.Core.Communication;
using Kora.Core.Dependencies;
using Kora.Application.Hosting;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Voice;
using System.Diagnostics;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Fact]
    public async Task UnitConversionTypedAndCurrentNameVoiceUseLocalActionAndOrdinaryOutput()
    {
        var f = await Fixture.CreateInitializedAsync();
        await f.RunAsync("set assistant name to Nova");
        f.TextToSpeech.ClearSpokenResponse();
        var mode = f.ViewModel.DefaultResponseMode;
        await f.RunAsync("convert 1 in to cm");
        f.ViewModel.ResponseTitle.Should().Be("Unit conversion result");
        f.ViewModel.ResponseBody.Should().StartWith("2.54 cm. Exact.");
        await f.RaiseActivatedTranscriptAsync("Nova, convert 1 m to in", 1);
        f.ViewModel.ResponseBody.Should().StartWith("39.370078740157 in. Rounded.");
        f.ViewModel.ResponseBody.Should().Contain("midpoint to even");
        await f.RunAsync("convert help");
        f.ViewModel.ResponseBody.Should().Contain("kelvin").And.Contain("28 digits");
        f.ViewModel.DefaultResponseMode.Should().Be(mode);
        f.Reasoner.Requests.Should().BeEmpty();
        f.HostStore.Records.Should().BeEmpty();
    }

    [Fact]
    public async Task UnitConversionVisualOnlyDoesNotForceSpeechOrChangePreferences()
    {
        var f = new Fixture(enableResponseModeConfiguration: true);
        await using var admission = f.OutputAdmission;
        await f.ViewModel.InitializeAsync();
        await f.RunAsync("set responses.default-mode to VisualOnly");
        f.TextToSpeech.ClearSpokenResponse();
        await f.RunAsync("convert 1 in to cm");
        f.ViewModel.ResponseBody.Should().StartWith("2.54 cm.");
        f.TextToSpeech.SpokenText.Should().BeNull();
        f.ViewModel.DefaultResponseMode.Should().Be(ResponseOutputMode.VisualOnly);
        f.OutputPreferences.SavedMode.Should().Be(ResponseOutputMode.VisualOnly);
        f.Reasoner.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task UnitConversionHasSeparateHostOwnedContentFreeTracesAndRejectsExternalOrigins()
    {
        var f = await Fixture.CreateInitializedAsync();
        var activities = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activities.Add,
        };
        ActivitySource.AddActivityListener(listener);
        using var hostile = new Activity("untrusted incoming").AddBaggage("kora.session.id", "hostile").Start();
        await f.RunAsync("convert 1 m to cm");
        await f.RunAsync("convert 2 m to cm");
        activities.Should().HaveCount(4);
        var roots = activities.Where(activity => activity.ParentId is null).ToArray();
        roots.Should().HaveCount(2);
        roots[0].GetTagItem("kora.session.id").Should().NotBe(roots[1].GetTagItem("kora.session.id"));
        activities.Should().OnlyContain(activity => activity.Status == ActivityStatusCode.Ok);
        activities.SelectMany(activity => activity.Baggage).Should().BeEmpty();
        activities.SelectMany(activity => activity.TagObjects).Should().NotContain(pair => Equals(pair.Value, "hostile"));
        Activity.Current.Should().BeSameAs(hostile);
        await HostRequestRunner.RunAsync(RequestOrigin.HostSystem, () => f.RunAsync("convert 1 m to cm"));
        f.ViewModel.Transcript.Should().Contain("admission is closed");
        f.Reasoner.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData("convert 1 kg to m", "IncompatibleUnits")]
    [InlineData("convert 1 metres to m", "UnknownUnit")]
    [InlineData("convert -1 m to cm", "NegativeQuantity")]
    [InlineData("convert -1 kelvin to celsius", "BelowAbsoluteZero")]
    [InlineData("convert 1e2 m to cm", "InvalidInput")]
    [InlineData("convert 9999999999999999999999999999 mi to mm", "Overflow")]
    public async Task UnitConversionFailuresAreVisibleWithoutReasoning(string input, string outcome)
    {
        var f = await Fixture.CreateInitializedAsync();
        f.TextToSpeech.ClearSpokenResponse();
        await f.RunAsync(input);
        f.ViewModel.ResponseTitle.Should().Be("Unit conversion " + outcome);
        f.ViewModel.ResponseBody.Should().StartWith("No result.");
        f.TextToSpeech.SpokenText.Should().BeNull();
        f.Reasoner.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData("privacy")]
    [InlineData("ownership")]
    [InlineData("call")]
    [InlineData("dispose")]
    public async Task UnitConversionClosedHostCannotProduceAResult(string transition)
    {
        var f = await Fixture.CreateInitializedAsync();
        switch (transition)
        {
            case "privacy": f.Session.IsUnlocked = false; break;
            case "ownership": f.ViewModel.BindCallOwnershipGate(static () => false); break;
            case "call": f.CallState.SetState(CallState.Unknown); await f.Dispatcher.LastInvocation; break;
            case "dispose": f.ViewModel.Dispose(); break;
        }
        await f.RunAsync("convert 1 m to cm");
        f.ViewModel.Transcript.Should().Contain("Unit conversion unavailable");
        f.ViewModel.ResponseTitle.Should().NotBe("Unit conversion result");
        f.Reasoner.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task UnitConversionNeverAnswersPendingQuestionsApprovalsOrSecurityPreviews()
    {
        var f = new Fixture();
        f.Probe.Status = new("local.inference", "Local", DependencyReadiness.Ready, "Ready");
        f.Reasoner.Question = new("Which target?", ["One", "Two"]);
        await f.ViewModel.InitializeAsync();
        await f.RunAsync("choose");
        await f.ViewModel.ActiveReasoningTask!;
        var requests = f.Reasoner.Requests.Count;
        f.ViewModel.CommandText = "unfinished answer";
        var title = f.ViewModel.ResponseTitle;
        var body = f.ViewModel.ResponseBody;
        await f.RunAsync("convert 1 m to cm");
        f.ViewModel.IsModelQuestionPending.Should().BeTrue();
        f.ViewModel.CommandText.Should().Be("convert 1 m to cm");
        f.ViewModel.ResponseTitle.Should().Be(title);
        f.ViewModel.ResponseBody.Should().Be(body);
        await f.RaiseActivatedTranscriptAsync("Kora, convert help", 1);
        f.ViewModel.IsModelQuestionPending.Should().BeTrue();
        await f.RunAsync("convert help extra");
        f.Reasoner.Requests.Should().HaveCount(requests);
        await f.ViewModel.CancelModelQuestionAsync();
        await f.ViewModel.PrepareGrantChangeAsync(new(GrantChangeOperation.Add, BuiltInAction.LockMachine, ModelApprovalScope.Always));
        title = f.ViewModel.ResponseTitle;
        body = f.ViewModel.ResponseBody;
        await f.RunAsync("convert help");
        f.ViewModel.IsGrantChangePending.Should().BeTrue();
        f.ViewModel.ResponseTitle.Should().Be(title);
        f.ViewModel.ResponseBody.Should().Be(body);
        await f.ViewModel.RejectPendingGrantChangeAsync();
        f.Reasoner.Question = null;
        f.Reasoner.Action = BuiltInAction.LockMachine;
        await f.RunAsync("please lock");
        await f.ViewModel.ActiveReasoningTask!;
        title = f.ViewModel.ResponseTitle;
        await f.RunAsync("convert 1 m to cm");
        f.ViewModel.IsModelActionApprovalPending.Should().BeTrue();
        f.ViewModel.ResponseTitle.Should().Be(title);
        f.Session.LockCalls.Should().Be(0);
    }

    [Fact]
    public async Task UnitConversionDoesNotDismissOpenGrantEditor()
    {
        var f = await Fixture.CreateInitializedAsync();
        await f.RunAsync("manage grants");
        f.ViewModel.IsGrantEditorVisible.Should().BeTrue();
        var title = f.ViewModel.ResponseTitle;
        var body = f.ViewModel.ResponseBody;
        await f.RunAsync("convert 1 m to cm");
        f.ViewModel.IsGrantEditorVisible.Should().BeTrue();
        f.ViewModel.ResponseTitle.Should().Be(title);
        f.ViewModel.ResponseBody.Should().Be(body);
        f.Reasoner.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData("generation")]
    [InlineData("owner")]
    [InlineData("privacy")]
    public async Task UnitConversionRetiredQueuedVoiceCannotExecute(string change)
    {
        var f = await Fixture.CreateInitializedAsync();
        long owner = 1;
        f.ViewModel.BindCallOwnershipGate(static () => true, () => owner);
        await f.ViewModel.BeginPushToTalkAsync();
        await f.ViewModel.EndPushToTalkAsync();
        f.Dispatcher.BeforeInvoke = () =>
        {
            switch (change)
            {
                case "generation": f.Voice.AdvanceGeneration(); break;
                case "owner": owner++; break;
                case "privacy": f.Session.IsUnlocked = false; break;
            }
        };
        await f.Voice.RaiseTranscriptAsync("Kora, convert 1 m to cm", 1);
        f.ViewModel.ResponseTitle.Should().NotBe("Unit conversion result");
        f.Reasoner.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task UnitConversionBusyInputIsReservedButNeverExecuted()
    {
        var f = await Fixture.CreateInitializedAsync();
        f.Probe.Gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var busy = f.ViewModel.DetectMicrophonesAsync();
        await f.RunAsync("convert 1 m to cm");
        f.ViewModel.Transcript.Should().Contain("pending question, approval, security preview or operation");
        f.Probe.Gate.SetResult();
        await busy;
        f.Reasoner.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task UnitConversionUnavailableNameAndUnprefixedVoiceRefuseWithoutFallback()
    {
        var f = await Fixture.CreateInitializedAsync();
        await f.RaiseActivatedTranscriptAsync("convert 1 m to cm", 1);
        f.ViewModel.Transcript.Should().Contain("current-name admission is closed");
        await f.RunAsync("set assistant name to Nova");
        await f.RaiseActivatedTranscriptAsync("Kora, convert 1 m to cm", 1);
        f.ViewModel.ResponseTitle.Should().Be("Activate me before asking a question.");
        f.Reasoner.Requests.Should().BeEmpty();
        var unavailable = new Fixture();
        unavailable.NamePreferences.Name = "Supported Commands";
        await unavailable.ViewModel.InitializeAsync();
        await unavailable.RunAsync("Kora, convert 1 m to cm");
        unavailable.ViewModel.Transcript.Should().Contain("current-name admission is closed");
        unavailable.Reasoner.Requests.Should().BeEmpty();
    }
}
