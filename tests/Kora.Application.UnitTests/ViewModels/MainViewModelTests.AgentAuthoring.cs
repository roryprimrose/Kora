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
    public async Task AgentAuthoringDispatchPreservesOriginalChannelWithoutRunOrInference(bool voice)
    {
        var f = await Fixture.CreateInitializedAsync();
        await f.ViewModel.SetAssistantNameAsync("Nova", Kora.Core.Auditing.SecurityAuditInitiator.LocalUser);
        var received = new List<AgentProfileAuthoringAction>();
        f.ViewModel.AgentProfileAuthoringRequested += (command, origin, eligible) =>
        {
            eligible().Should().BeTrue();
            origin.Should().Be(voice ? RequestOrigin.ActivatedVoice : RequestOrigin.LocalUi);
            HostActivity.RequireCurrent().Request.Origin.Should().Be(origin);
            received.Add(command.Action);
            return Task.CompletedTask;
        };
        var runs = 0;
        f.ViewModel.AgentRunRequested += (_, _, _) => { runs++; return Task.CompletedTask; };
        foreach (var text in new[] { "agent author", "agent author create example", "agent author source complete-source",
            "agent author validate", "agent author save as new", "agent author list",
            "agent author inspect example", "agent author cancel review", "agent author describe Original request",
            "agent author ask", "agent author draft example: Original description" })
        {
            if (voice) { await f.RaiseActivatedTranscriptAsync("Nova, " + text, 1); }
            else { await f.RunAsync(text); }
        }
        received.Should().Equal(AgentProfileAuthoringAction.Open, AgentProfileAuthoringAction.Create,
            AgentProfileAuthoringAction.Stage, AgentProfileAuthoringAction.Validate, AgentProfileAuthoringAction.ReviewSave,
            AgentProfileAuthoringAction.List, AgentProfileAuthoringAction.Inspect, AgentProfileAuthoringAction.CancelReview,
            AgentProfileAuthoringAction.Describe, AgentProfileAuthoringAction.Ask, AgentProfileAuthoringAction.DraftFromDescription);
        runs.Should().Be(0);
        f.Reasoner.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task UnboundOrNonOriginalAuthoringNeverDispatches()
    {
        var f = await Fixture.CreateInitializedAsync();
        await f.RunAsync("agent author");
        f.ViewModel.Transcript.Should().Contain("unavailable");
        var received = 0;
        f.ViewModel.AgentProfileAuthoringRequested += (_, _, _) => { received++; return Task.CompletedTask; };
        await f.RaiseActivatedTranscriptAsync("agent author", 1);
        using (HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.HostSystem), HostActivityLayer.Application, HostOperation.Request))
        { await f.RunAsync("agent author"); }
        received.Should().Be(0);
        f.Reasoner.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task CancelledAuthoringCloseAbortsExitBeforeRunShutdown()
    {
        var f = await Fixture.CreateInitializedAsync();
        var runStops = 0;
        var runSetStops = 0;
        f.ViewModel.BindAgentProfileAuthoringShutdown(() => Task.FromResult(false));
        f.ViewModel.BindAgentShutdown(() => { runStops++; return Task.CompletedTask; });
        f.ViewModel.BindAgentRunSetShutdown(() => { runSetStops++; return Task.CompletedTask; });
        var exit = () => f.ViewModel.ExitAsync();
        await exit.Should().ThrowAsync<OperationCanceledException>();
        runStops.Should().Be(0);
        runSetStops.Should().Be(0);
        f.ViewModel.BindAgentProfileAuthoringShutdown(() => Task.FromResult(true));
        await f.ViewModel.ExitAsync();
        runStops.Should().Be(1);
        runSetStops.Should().Be(1);
    }

    [Fact]
    public async Task ApprovedAuthoringCloseJoinsBothRunOwnersEvenWhenOneFails()
    {
        var f = await Fixture.CreateInitializedAsync();
        var legacy = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var independent = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var approved = false;
        f.ViewModel.BindAgentProfileAuthoringShutdown(() => { approved = true; return Task.FromResult(true); });
        f.ViewModel.BindAgentShutdown(() => { approved.Should().BeTrue(); return legacy.Task; });
        f.ViewModel.BindAgentRunSetShutdown(() =>
        {
            approved.Should().BeTrue();
            started.SetResult();
            return independent.Task;
        });
        var exit = f.ViewModel.ExitAsync();
        await started.Task.WaitAsync(TestContext.Current.CancellationToken);
        legacy.SetException(new IOException("Legacy resource closure failed."));
        exit.IsCompleted.Should().BeFalse();
        independent.SetResult();
        await exit.Invoking(value => value).Should().ThrowAsync<IOException>();
    }
}
