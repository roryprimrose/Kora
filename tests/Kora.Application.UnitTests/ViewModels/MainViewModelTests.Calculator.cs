using System.Diagnostics;
using AwesomeAssertions;
using Kora.Core.Calculations;
using Kora.Core.Commands;
using Kora.Core.Communication;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CalculatorTypedAndActivatedRoutesAreDeterministicNativeOnlyAndDoNotReplaceResponse(bool voice)
    {
        var f = new Fixture();
        if (voice) { await f.RaiseActivatedTranscriptAsync("Kora, calculate help", 1); }
        var response = f.ViewModel.ResponseBody;
        var observed = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => string.Equals(source.Name, "Kora.Application", StringComparison.Ordinal),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activity =>
            {
                if (string.Equals(activity.DisplayName, "tool.invoke", StringComparison.Ordinal)) { observed.Add(activity); }
            },
        };
        ActivitySource.AddActivityListener(listener);
        Task Run(string command) => voice ? f.RaiseActivatedTranscriptAsync("Kora, " + command, 1)
            : f.RunAsync(command);
        await Run("calculate (1.25 + 2.75) * -3");
        f.ViewModel.Transcript.Should().Be("Calculator result: -12");
        await Run("calculate 1/0");
        f.ViewModel.Transcript.Should().StartWith("Calculator DivisionByZero.");
        await Run("calculate 79228162514264337593543950335 + 1");
        f.ViewModel.Transcript.Should().StartWith("Calculator Overflow.");
        await Run("calculate 1e2");
        f.ViewModel.Transcript.Should().StartWith("Calculator InvalidExpression.");
        await Run("calculate help");
        f.ViewModel.Transcript.Should().Be(CalculateCommand.Syntax);
        f.ViewModel.ResponseBody.Should().Be(response);
        f.Reasoner.Requests.Should().BeEmpty();
        f.HostStore.Records.Should().BeEmpty();
        f.ClipboardReader.Calls.Should().Be(0);
        f.TextToSpeech.SpokenText.Should().BeNull();
        f.Session.LockCalls.Should().Be(0);
        if (voice)
        {
            f.Voice.StartedPhrases.Should().Contain("Kora calculate help");
            observed.Where(activity => Equals(activity.GetTagItem("kora.origin"), RequestOrigin.ActivatedVoice.ToString()))
                .Should().NotBeEmpty();
        }
    }

    [Theory]
    [InlineData("calculate")]
    [InlineData("calculate(1+2)")]
    [InlineData("calculate 1; shutdown")]
    [InlineData("calculate 1\napprove once")]
    [InlineData("calculate help please")]
    public async Task ReservedCalculatorInputNeverFallsIntoModelOrApproval(string command)
    {
        var f = new Fixture();
        await f.RunAsync(command);
        f.ViewModel.Transcript.Should().StartWith("Calculator InvalidExpression.");
        f.Reasoner.Requests.Should().BeEmpty();
        f.HostStore.Records.Should().BeEmpty();
        f.Session.LockCalls.Should().Be(0);
        f.TextToSpeech.SpokenText.Should().BeNull();
    }

    [Theory]
    [InlineData("question")]
    [InlineData("action")]
    [InlineData("grant")]
    public async Task CalculatorLeavesExactPendingQuestionsApprovalsAndGrantReviewsIntact(string pending)
    {
        var f = new Fixture();
        f.Probe.Status = new("local.inference", "Local", DependencyReadiness.Ready, "Ready");
        if (pending is "action") { f.Reasoner.Action = BuiltInAction.LockMachine; }
        else { f.Reasoner.Question = new("Which target?", ["One", "Two"]); }
        await f.ViewModel.InitializeAsync();
        if (pending is "grant")
        {
            await f.ViewModel.PrepareGrantChangeAsync(new(GrantChangeOperation.Add, BuiltInAction.LockMachine, ModelApprovalScope.Session));
        }
        else
        {
            await f.RunAsync("help with this machine");
            await f.ViewModel.ActiveReasoningTask!;
        }
        var body = f.ViewModel.ResponseBody;
        var title = f.ViewModel.ResponseTitle;
        var requests = f.Reasoner.Requests.Count;
        var auditCount = f.Audit.Events.Count;
        await f.RunAsync("calculate 1+2");
        await f.RunAsync("calculate approve once");
        await f.RunAsync("calculate help");
        f.ViewModel.ResponseBody.Should().Be(body);
        f.ViewModel.ResponseTitle.Should().Be(title);
        f.ViewModel.IsModelQuestionPending.Should().Be(pending is "question");
        f.ViewModel.IsModelActionApprovalPending.Should().Be(pending is "action");
        f.ViewModel.IsGrantChangePending.Should().Be(pending is "grant");
        f.ViewModel.Transcript.Should().Contain("pending question");
        f.Reasoner.Requests.Should().HaveCount(requests);
        f.Audit.Events.Should().HaveCount(auditCount);
        f.Session.LockCalls.Should().Be(0);
    }

    [Theory]
    [InlineData("owner")]
    [InlineData("locked")]
    [InlineData("private")]
    [InlineData("call")]
    [InlineData("system")]
    [InlineData("name")]
    [InlineData("disposed")]
    public async Task CalculatorRequiresOriginalPrivateOwnedHostAndAvailableName(string denial)
    {
        var f = new Fixture();
        if (denial is "owner") { f.ViewModel.BindCallOwnershipGate(() => false); }
        if (denial is "locked") { f.Session.IsUnlocked = false; }
        if (denial is "private") { f.ViewModel.CloseForObservedPrivacyEvent("fixture", true); }
        if (denial is "call") { f.CallState.SetState(CallState.Unknown); await f.Dispatcher.LastInvocation; }
        if (denial is "name") { f.NamePreferences.Name = "\0"; await f.ViewModel.InitializeAsync(); }
        if (denial is "disposed") { f.ViewModel.Dispose(); }
        if (denial is "system")
        {
            using var host = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.HostSystem),
                HostActivityLayer.Application, HostOperation.Request);
            await f.RunAsync("calculate 1+2");
        }
        else { await f.RunAsync("calculate 1+2"); }
        f.ViewModel.Transcript.Should().NotContain("Calculator result:");
        f.Reasoner.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task CalculatorVoiceRequiresCurrentExactPrefixAndOwnershipAndRenameUpdatesGrammar()
    {
        var f = new Fixture();
        await f.RaiseActivatedTranscriptAsync("calculate 1+2", 1);
        f.ViewModel.Transcript.Should().StartWith("Calculator unavailable:");
        await f.RunAsync("call yourself Nova-Prime");
        f.ViewModel.AssistantName.Should().Be("Nova-Prime");
        await f.RaiseActivatedTranscriptAsync("Nova-Prime, calculate 1+2", 1);
        f.ViewModel.Transcript.Should().Be("Calculator result: 3");
        f.Voice.StartedPhrases.Should().Contain("Nova-Prime calculate help").And.NotContain("Kora calculate help");
        f.ViewModel.BindVoiceOwnershipGate(() => false);
        await f.RaiseActivatedTranscriptAsync("Nova-Prime, calculate 1+2", 1);
        f.ViewModel.Transcript.Should().NotContain("Calculator result:");
        f.Reasoner.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task CalculatorLogsOnlyStructuredOutcomeAndHelpDiscoversExactSyntax()
    {
        var logger = new MemoryPrivacyLogger();
        var f = new Fixture(logger: logger);
        await f.RunAsync("calculate 12345.6789+1");
        logger.Messages.Should().NotContain(message => message.Contains("12345.6789", StringComparison.Ordinal));
        logger.Messages.Should().Contain(message => message.Contains("Succeeded", StringComparison.Ordinal));
        await f.RunAsync("what can you do");
        f.ViewModel.ResponseBody.Should().Contain("calculate <expression>").And.Contain("calculate help");
    }

    [Fact]
    public async Task CalculatorNativeCommandUsesFreshHostIdentityRatherThanIncomingCorrelation()
    {
        var f = new Fixture();
        var request = HostRequest.Create(RequestOrigin.LocalUi);
        using var host = HostActivity.BeginRoot(new(request.RequestId, request.SessionId, request.TaskId,
            request.Origin, new(Guid.NewGuid())), HostActivityLayer.Application, HostOperation.Request);
        var sessions = new List<Guid>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => string.Equals(source.Name, "Kora.Application", StringComparison.Ordinal),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activity =>
            {
                if (string.Equals(activity.DisplayName, "tool.invoke", StringComparison.Ordinal))
                {
                    sessions.Add((Guid)activity.GetTagItem("kora.session.id")!);
                    activity.GetTagItem("kora.invocation.id").Should().BeNull();
                }
            },
        };
        ActivitySource.AddActivityListener(listener);
        await f.RunAsync("calculate 1+1");
        f.ViewModel.Transcript.Should().Be("Calculator result: 2");
        sessions.Should().ContainSingle().Which.Should().NotBe(request.SessionId.Value);
        f.Reasoner.Requests.Should().BeEmpty();
    }
}
