using System.Diagnostics;
using System.Reflection;
using System.Text;
using AwesomeAssertions;
using Kora.Application.Numbers;
using Kora.Core.Commands;
using Kora.Core.Auditing;
using Kora.Core.Communication;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Theory]
    [InlineData("number convert -8000000000000000 from hex to decimal", "-9223372036854775808")]
    [InlineData("Kora, number convert 0 from binary to hex", "0")]
    [InlineData("number convert -AB from hex to binary", "-10101011")]
    [InlineData("number convert 0x1 from hex to decimal", "Invalid integer")]
    [InlineData("number convert -0 from decimal to hex", "Invalid integer")]
    [InlineData("number convert 9223372036854775808 from decimal to hex", "Integer overflow")]
    [InlineData("number help extra", NumberCommand.Syntax)]
    [InlineData("number help", NumberCommand.Help)]
    public async Task NumberCommandsAreDeterministicAndNeverRequestInferenceOrResources(string command, string expected)
    {
        var f = new Fixture();
        var events = f.Events.ToArray();
        await f.RunAsync(command);
        f.ViewModel.ResponseBody.Should().Contain(expected);
        Encoding.UTF8.GetByteCount(f.ViewModel.ResponseTitle + f.ViewModel.ResponseBody).Should()
            .BeLessThanOrEqualTo(NumberCommand.MaximumOutputBytes);
        f.Reasoner.Requests.Should().BeEmpty();
        f.HostStore.Records.Should().BeEmpty();
        f.ClipboardReader.Calls.Should().Be(0);
        f.Audit.Events.Should().BeEmpty();
        f.Events.Where(item => !item.StartsWith("window.", StringComparison.Ordinal)).Should().Equal(events);
        f.ViewModel.Transcript.Should().NotContain("8000000000000000");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NumberReservedRoutePreservesPendingQuestionAndApproval(bool approval)
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
        var count = f.Reasoner.Requests.Count;
        await f.RunAsync("number convert 42 from decimal to binary");
        await f.RunAsync("number help and approve once");
        f.ViewModel.IsModelActionApprovalPending.Should().Be(approval);
        f.ViewModel.IsModelQuestionPending.Should().Be(!approval);
        f.ViewModel.ResponseTitle.Should().Be(title);
        f.ViewModel.ResponseBody.Should().Be(body);
        f.Reasoner.Requests.Should().HaveCount(count);
        f.Session.LockCalls.Should().Be(0);
        f.ViewModel.Transcript.Should().StartWith("Number conversion unavailable:");
    }

    [Fact]
    public async Task NumberCommandsPreserveGrantAndSecurityPreview()
    {
        var f = new Fixture(enableAuditRetention: true, enableAuditImpact: true);
        await using var admission = f.AuditAdmission;
        await f.ViewModel.InitializeAsync();
        f.ViewModel.BindAuditRetentionNativeLifetime(static () => true);
        f.ViewModel.ProposedAuditImpactDays = 30;
        await f.ViewModel.PreviewAuditImpactCommand.ExecuteAsync();
        var preview = f.ViewModel.AuditImpactPreviewStatus;
        await f.RunAsync("number help");
        f.ViewModel.AuditImpactPreviewStatus.Should().Be(preview);
        await f.ViewModel.PrepareGrantChangeAsync(new GrantChange(
            GrantChangeOperation.Add, BuiltInAction.LockMachine, ModelApprovalScope.Session));
        var body = f.ViewModel.ResponseBody;
        await f.RunAsync("number help");
        f.ViewModel.IsGrantChangePending.Should().BeTrue();
        f.ViewModel.ResponseBody.Should().Be(body);
        f.ViewModel.SessionAllowedModelActions.Should().BeEmpty();
    }

    [Fact]
    public async Task NumberCommandsPreserveOpenGrantEditorWithoutAProposal()
    {
        var f = await Fixture.CreateInitializedAsync();
        await f.RunAsync("manage grants");
        f.ViewModel.IsGrantEditorVisible.Should().BeTrue();
        f.ViewModel.IsResponseInteractionPending.Should().BeFalse();
        var title = f.ViewModel.ResponseTitle;
        var body = f.ViewModel.ResponseBody;
        await f.RunAsync("number help");
        f.ViewModel.IsGrantEditorVisible.Should().BeTrue();
        f.ViewModel.ResponseTitle.Should().Be(title);
        f.ViewModel.ResponseBody.Should().Be(body);
        f.ViewModel.Transcript.Should().StartWith("Number conversion unavailable:");
    }

    [Theory]
    [InlineData("owner")]
    [InlineData("lock")]
    [InlineData("privacy")]
    [InlineData("call")]
    [InlineData("system")]
    [InlineData("name")]
    public async Task NumberCommandsFailClosedWithoutOriginalPrivateHost(string state)
    {
        var f = await Fixture.CreateInitializedAsync();
        if (state is "owner") { f.ViewModel.BindCallOwnershipGate(static () => false); }
        if (state is "lock") { f.Session.IsUnlocked = false; }
        if (state is "privacy") { f.ViewModel.CloseForObservedPrivacyEvent("fixture", true); }
        if (state is "call") { f.CallState.SetState(CallState.Active); await f.Dispatcher.LastInvocation; }
        if (state is "name") { f.NamePreferences.Name = "\0"; await f.ViewModel.InitializeAsync(); }
        using var root = state is "system" ? HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.HostSystem),
            HostActivityLayer.Application, HostOperation.Request) : null;
        await f.RunAsync("number convert 42 from decimal to binary");
        f.ViewModel.ResponseBody.Should().NotBe("101010");
        f.ViewModel.Transcript.Should().StartWith("Number conversion unavailable:");
        f.Reasoner.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData("Nova")]
    [InlineData("What Time")]
    public async Task NumberVoiceUsesCurrentNameAndPreservesOutputPolicy(string assistantName)
    {
        var f = await Fixture.CreateInitializedAsync();
        var outputMode = f.ViewModel.DefaultResponseMode;
        await f.RunAsync("set assistant name to " + assistantName);
        await f.RaiseActivatedTranscriptAsync(assistantName + ", number convert -FF from hex to decimal", 1);
        f.ViewModel.ResponseBody.Should().Be("-255");
        f.ViewModel.DefaultResponseMode.Should().Be(outputMode);
        await f.RaiseActivatedTranscriptAsync("number help", 1);
        f.ViewModel.Transcript.Should().StartWith("Number conversion unavailable:");
        await f.RaiseActivatedTranscriptAsync(assistantName + ":number help", 1);
        f.ViewModel.ResponseTitle.Should().Be("Invalid number command.");
        f.ViewModel.BindVoiceOwnershipGate(static () => false);
        await f.RaiseActivatedTranscriptAsync(assistantName + ", number help", 1);
        f.ViewModel.Transcript.Should().StartWith("Number conversion unavailable:");
        f.Reasoner.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task NumberActivityRetainsHostSessionAndNeverLogsLiteral()
    {
        var stopped = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activity => stopped.Add(activity),
        };
        ActivitySource.AddActivityListener(listener);
        var logger = new MemoryPrivacyLogger();
        var f = new Fixture(logger: logger);
        using var hostile = new Activity("untrusted.incoming").SetIdFormat(ActivityIdFormat.W3C).Start();
        await f.RunAsync("number convert 123456789 from decimal to hex");
        await f.RunAsync("number convert -0 from decimal to hex");
        var tools = stopped.Where(activity => activity.OperationName.Contains("tool", StringComparison.Ordinal)).ToArray();
        tools.Should().HaveCount(2);
        tools[0].TraceId.Should().NotBe(tools[1].TraceId);
        tools[0].GetTagItem("kora.session.id").Should().NotBe(tools[1].GetTagItem("kora.session.id"));
        tools.Should().OnlyContain(activity => activity.TraceId != hostile.TraceId);
        tools.Should().OnlyContain(activity => activity.ParentId != null && activity.Duration >= TimeSpan.Zero);
        tools.Select(activity => activity.Status).Should().Equal(ActivityStatusCode.Ok, ActivityStatusCode.Error);
        foreach (var tool in tools)
        {
            var root = stopped.Single(activity => activity.TraceId == tool.TraceId && activity.ParentId is null);
            tool.GetTagItem("kora.session.id").Should().Be(root.GetTagItem("kora.session.id"));
            tool.GetTagItem("kora.request.id").Should().Be(root.GetTagItem("kora.request.id"));
        }
        logger.Messages.Should().NotContain(message => message.Contains("123456789", StringComparison.Ordinal));
        stopped.SelectMany(activity => activity.Tags).Should().NotContain(tag =>
            tag.Value != null && tag.Value.Contains("123456789", StringComparison.Ordinal));
        f.Reasoner.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData("origin")]
    [InlineData("call-revision")]
    [InlineData("owner-revision")]
    [InlineData("busy")]
    [InlineData("initiator")]
    public async Task NumberBoundaryRefusesStaleCapturedInputAndNonCommandAuthority(string change)
    {
        var f = new Fixture();
        long owner = 1;
        f.ViewModel.BindCallOwnershipGate(static () => true, () => owner);
        var type = typeof(Kora.Application.ViewModels.MainViewModel);
        var capture = type.GetMethod("CaptureManualCallInput", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var captured = capture.Invoke(f.ViewModel,
            [RequestOrigin.LocalUi, change is "call-revision" ? -1L : f.ViewModel.CallPolicyRevision, null, false]);
        if (change is "owner-revision") { owner++; }
        if (change is "busy") { type.GetProperty("IsBusy")!.SetValue(f.ViewModel, true); }
        using var root = HostActivity.BeginRoot(HostRequest.Create(
            change is "origin" ? RequestOrigin.ActivatedVoice : RequestOrigin.LocalUi),
            HostActivityLayer.Application, HostOperation.Request);
        var execute = type.GetMethod("ExecuteNumberCommand", BindingFlags.Instance | BindingFlags.NonPublic)!;
        execute.Invoke(f.ViewModel, [NumberCommand.Parse("number help", "Kora"),
            change is "initiator" ? SecurityAuditInitiator.ModelSuggestion : SecurityAuditInitiator.TypedCommand, captured]);
        f.ViewModel.Transcript.Should().StartWith("Number conversion unavailable:");
        f.Reasoner.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task NumberVoiceRejectsCallActivationEvenWithCurrentOwnerAndGeneration()
    {
        var f = await Fixture.CreateInitializedAsync();
        await f.RaiseActivatedTranscriptAsync("Kora, number help", 1);
        await f.ViewModel.BeginPushToTalkAsync();
        await f.ViewModel.EndPushToTalkAsync();
        f.Dispatcher.BeforeInvoke = () =>
        {
            f.CallState.SetState(CallState.Active);
            f.Dispatcher.BeforeInvoke = null;
        };
        await f.Voice.RaiseTranscriptAsync("Kora, number convert 1 from decimal to hex", 1);
        f.ViewModel.ResponseBody.Should().NotBe("1");
        f.Reasoner.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task NumberVoiceRejectsGenerationRetiredBeforeDispatch()
    {
        var f = await Fixture.CreateInitializedAsync();
        await f.RaiseActivatedTranscriptAsync("Kora, number help", 1);
        await f.ViewModel.BeginPushToTalkAsync();
        await f.ViewModel.EndPushToTalkAsync();
        f.Dispatcher.BeforeInvoke = f.Voice.AdvanceGeneration;
        await f.Voice.RaiseTranscriptAsync("Kora, number help", 1);
        f.ViewModel.Transcript.Should().StartWith("Number conversion unavailable:");
        f.Reasoner.Requests.Should().BeEmpty();
    }
}
