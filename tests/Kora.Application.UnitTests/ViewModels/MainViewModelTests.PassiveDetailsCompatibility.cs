using AwesomeAssertions;
using Kora.Application.ViewModels;
using Kora.Core.Commands;
using Kora.Core.Dependencies;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Theory]
    [InlineData(BuiltInAction.ShowPowerStatus)]
    [InlineData(BuiltInAction.LockMachine)]
    public async Task Host_exit_waits_for_inflight_response_or_approval_prompt_not_viewer_closure(
        BuiltInAction action)
    {
        var fixture = new Fixture();
        fixture.Probe.Status = new DependencyStatus(
            "local.inference", "Local model inference (Ollama)", DependencyReadiness.Ready, "Ready.");
        fixture.Reasoner.Action = action;
        await fixture.ViewModel.InitializeAsync();
        fixture.TextToSpeech.SpeakGate = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.TextToSpeech.HoldSpeechCompletionOnStop = true;
        await fixture.RunAsync("tell me about this workstation");
        await fixture.TextToSpeech.SpeakStarted.Task.WaitAsync(
            TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        var reasoning = fixture.ViewModel.ActiveReasoningTask!;
        var closes = 0;
        fixture.ViewModel.WindowActionRequested += (_, requested) =>
        {
            if (requested == WindowAction.Close)
            {
                closes++;
            }
        };

        var exit = fixture.ViewModel.ExitAsync();

        exit.IsCompleted.Should().BeFalse();
        closes.Should().Be(0);
        fixture.TextToSpeech.SpeakGate.SetResult();
        await exit.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await reasoning;
        closes.Should().Be(1);
        fixture.Session.LockCalls.Should().Be(0);
    }
}
