using AwesomeAssertions;
using Kora.Application.Skills;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TwoSourceSkillCommandsShareCurrentNameActivatedAndTypedRouteWithoutModelRetargeting(bool voice)
    {
        var f = await Fixture.CreateInitializedAsync();
        await f.ViewModel.SetAssistantNameAsync("Nova", Kora.Core.Auditing.SecurityAuditInitiator.LocalUser);
        var commands = new List<SharedSkillInvocationCommand>();
        f.ViewModel.SharedSkillInvocationRequested += (command, origin, eligible) =>
        {
            command.ToolCapable.Should().BeTrue();
            origin.Should().Be(voice ? RequestOrigin.ActivatedVoice : RequestOrigin.LocalUi);
            eligible().Should().BeTrue();
            commands.Add(command);
            return Task.CompletedTask;
        };
        foreach (var input in new[]
        {
            "review tool skill two-source-comparison", "tool skill targets", "tool skill target exact-id",
            "tool skill address https://example.com/", "tool skill second address https://second.example/",
            "tool skill clear second address", "tool skill second address https://second.example/",
            "tool skill request   exact comparison  ", "tool skill review tool revision", "tool skill enable",
            "tool skill review invocation", "tool skill confirm invocation", "tool skill close",
        })
        {
            if (voice) { await f.RaiseActivatedTranscriptAsync("Nova, " + input, 1); }
            else { await f.RunAsync(input); }
        }
        commands.Select(command => command.Action).Should().Equal("Review", "Targets", "Target", "Address",
            "SecondAddress", "SecondAddress", "SecondAddress", "Request", "ReviewToolRevision", "Enable", "Prepare", "Invoke", "Close");
        commands.Single(command => command.Action is "Request").Argument.Should().Be("  exact comparison  ");
        f.Reasoner.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SharedSkillCommandsPreserveOriginalTypedOrActivatedVoiceChannelAndExactRequest(bool voice)
    {
        var f = await Fixture.CreateInitializedAsync();
        await f.ViewModel.SetAssistantNameAsync("Nova", Kora.Core.Auditing.SecurityAuditInitiator.LocalUser);
        var received = new List<SharedSkillInvocationCommand>();
        var origins = new List<RequestOrigin>();
        f.ViewModel.SharedSkillInvocationRequested += (command, origin, eligible) =>
        {
            eligible().Should().BeTrue();
            HostActivity.RequireCurrent().Request.Origin.Should().Be(origin);
            received.Add(command);
            origins.Add(origin);
            return Task.CompletedTask;
        };
        foreach (var text in new[]
        {
            "review shared skill concise-review", "shared skill enable", "shared skill request   original prose  ",
            "shared skill target exact-id", "shared skill review invocation", "shared skill confirm invocation", "shared skill close",
        })
        {
            if (voice) { await f.RaiseActivatedTranscriptAsync("Nova, " + text, 1); }
            else { await f.RunAsync(text); }
        }
        received.Select(item => item.Action).Should().Equal("Review", "Enable", "Request", "Target", "Prepare", "Invoke", "Close");
        received.Single(item => string.Equals(item.Action, "Request", StringComparison.Ordinal)).Argument.Should().Be("  original prose  ");
        origins.Should().OnlyContain(origin => origin == (voice ? RequestOrigin.ActivatedVoice : RequestOrigin.LocalUi));
        f.Reasoner.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task SharedSkillCommandsCannotReplacePendingQuestionOrApproval()
    {
        var f = new Fixture();
        f.Probe.Status = new("local.inference", "Local", Kora.Core.Dependencies.DependencyReadiness.Ready, "ready");
        f.Reasoner.Question = new("Keep this exact question?", ["one", "two"]);
        await f.ViewModel.InitializeAsync();
        await f.RunAsync("please do this work");
        await f.ViewModel.ActiveReasoningTask!;
        var called = false;
        f.ViewModel.SharedSkillInvocationRequested += (_, _, _) => { called = true; return Task.CompletedTask; };
        var body = f.ViewModel.ResponseBody;
        await f.RunAsync("shared skill confirm invocation");
        called.Should().BeFalse();
        f.ViewModel.ResponseBody.Should().Be(body);
        f.ViewModel.IsResponseInteractionPending.Should().BeTrue();
    }

    [Fact]
    public async Task SharedSkillUnboundOrRetiredOriginalInputDoesNotDispatch()
    {
        var f = await Fixture.CreateInitializedAsync();
        await f.RunAsync("review shared skill concise-review");
        f.ViewModel.Transcript.Should().Contain("unavailable");
        var called = false;
        f.ViewModel.SharedSkillInvocationRequested += (_, _, _) => { called = true; return Task.CompletedTask; };
        await f.RaiseActivatedTranscriptAsync("shared skill enable", 1);
        called.Should().BeFalse();
        using (HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.HostSystem), HostActivityLayer.Application, HostOperation.Request))
        { await f.RunAsync("shared skill enable"); }
        called.Should().BeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SharedSkillShutdownJoinsOnlyItsBoundOwnedOperation(bool bound)
    {
        var f = await Fixture.CreateInitializedAsync();
        var calls = 0;
        if (bound) { f.ViewModel.BindSharedSkillUseShutdown(() => { calls++; return Task.CompletedTask; }); }
        await f.ViewModel.ExitAsync();
        calls.Should().Be(bound ? 1 : 0);
    }

    [Fact]
    public async Task SharedSkillUnavailableAssistantPrefixDoesNotCreateSelectionAuthority()
    {
        var f = new Fixture();
        f.NamePreferences.Name = "Supported Commands";
        await f.ViewModel.InitializeAsync();
        var calls = 0;
        f.ViewModel.SharedSkillInvocationRequested += (_, _, _) => { calls++; return Task.CompletedTask; };
        await f.RunAsync("Kora, shared skill enable");
        calls.Should().Be(0);
        f.Reasoner.Requests.Should().BeEmpty();
    }
}
