using AwesomeAssertions;

using Kora.Application.Agents;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AgentCommandsPreserveOriginalChannelAndNeverUseLegacyPromptReasoner(bool voice)
    {
        var f = await Fixture.CreateInitializedAsync();
        await f.ViewModel.SetAssistantNameAsync("Nova", Kora.Core.Auditing.SecurityAuditInitiator.LocalUser);
        var received = new List<AgentRunCommand>();
        f.ViewModel.AgentRunRequested += (command, origin, eligible) =>
        {
            eligible().Should().BeTrue();
            origin.Should().Be(voice ? RequestOrigin.ActivatedVoice : RequestOrigin.LocalUi);
            HostActivity.RequireCurrent().Request.Origin.Should().Be(origin);
            received.Add(command);
            return Task.CompletedTask;
        };
        foreach (var text in new[] { "review agent", "agent source C:\\role.agent.md", "agent request task",
            "agent targets", "agent next targets", "agent target exact-id", "agent review invocation",
            "agent confirm invocation", "agent stop" })
        {
            if (voice) { await f.RaiseActivatedTranscriptAsync("Nova, " + text, 1); }
            else { await f.RunAsync(text); }
        }
        received.Select(item => item.Action).Should().Equal("Open", "Source", "Request", "Targets",
            "NextTargets", "Target", "Review", "Invoke", "Stop");
        f.Reasoner.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task AgentUnboundOrNonoriginalInputCannotDispatch()
    {
        var f = await Fixture.CreateInitializedAsync();
        await f.RunAsync("review agent");
        f.ViewModel.Transcript.Should().Contain("unavailable");
        var calls = 0;
        f.ViewModel.AgentRunRequested += (_, _, _) => { calls++; return Task.CompletedTask; };
        await f.RaiseActivatedTranscriptAsync("agent confirm invocation", 1);
        using (HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.HostSystem), HostActivityLayer.Application, HostOperation.Request))
        { await f.RunAsync("agent confirm invocation"); }
        calls.Should().Be(0);
    }

    [Fact]
    public async Task AgentCannotReplacePendingQuestion()
    {
        var f = new Fixture();
        f.Probe.Status = new("local.inference", "Local", Kora.Core.Dependencies.DependencyReadiness.Ready, "ready");
        f.Reasoner.Question = new("Original question?", ["one", "two"]);
        await f.ViewModel.InitializeAsync();
        await f.RunAsync("do the work");
        await f.ViewModel.ActiveReasoningTask!;
        var calls = 0;
        f.ViewModel.AgentRunRequested += (_, _, _) => { calls++; return Task.CompletedTask; };
        await f.RunAsync("agent confirm invocation");
        calls.Should().Be(0);
        f.ViewModel.IsResponseInteractionPending.Should().BeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AgentShutdownJoinsBoundOwnershipOnly(bool bound)
    {
        var f = await Fixture.CreateInitializedAsync();
        var calls = 0;
        if (bound) { f.ViewModel.BindAgentShutdown(() => { calls++; return Task.CompletedTask; }); }
        await f.ViewModel.ExitAsync();
        calls.Should().Be(bound ? 1 : 0);
    }

    [Fact]
    public async Task AgentUnavailableActivationNameCannotCreateAuthority()
    {
        var f = new Fixture();
        f.NamePreferences.Name = "Supported Commands";
        await f.ViewModel.InitializeAsync();
        var calls = 0;
        f.ViewModel.AgentRunRequested += (_, _, _) => { calls++; return Task.CompletedTask; };
        await f.RunAsync("Kora, review agent");
        calls.Should().Be(0);
        f.Reasoner.Requests.Should().BeEmpty();
    }
}
