using AwesomeAssertions;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Theory]
    [InlineData("memory remember \"not delivered\"")]
    [InlineData("memory propose \"not delivered\"")]
    [InlineData("memory list named")]
    [InlineData("memory forget selected")]
    public async Task InvalidMemoryCommandsRemainLocalWithoutModelFallback(string command)
    {
        var fixture = await Fixture.CreateInitializedAsync();
        await fixture.RunAsync(command);
        fixture.ViewModel.ResponseTitle.Should().Be("Memory command not accepted.");
        fixture.HostStore.Records.Should().BeEmpty();
        fixture.Reasoner.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnboundMemoryCommandsReportFailureWithoutInference(bool voice)
    {
        var fixture = await Fixture.CreateInitializedAsync();
        if (voice) { await fixture.RaiseActivatedTranscriptAsync("Kora, memory help", 1); }
        else { await fixture.RunAsync("memory help"); }
        fixture.ViewModel.ResponseTitle.Should().Be("Memory command not confirmed.");
        fixture.ViewModel.ResponseBody.Should().Contain("memory management service is unavailable");
        fixture.HostStore.Records.Should().BeEmpty();
        fixture.Reasoner.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task BusyBootstrapKeepsReservedMemoryCommandsAvailableWithoutCancellingOrReasoning()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        fixture.Probe.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var busy = fixture.ViewModel.DetectMicrophonesAsync();
        try
        {
            fixture.ViewModel.IsBusy.Should().BeTrue();
            fixture.ViewModel.CommandText = "memory help";
            fixture.ViewModel.RunTypedCommand.CanExecute(null).Should().BeTrue();
            await fixture.RunAsync("memory help");
            fixture.ViewModel.ResponseTitle.Should().Be("Memory command not confirmed.");
            fixture.ViewModel.IsBusy.Should().BeTrue();
            fixture.Reasoner.Requests.Should().BeEmpty();
            fixture.HostStore.Records.Should().BeEmpty();
        }
        finally
        {
            fixture.Probe.Gate.SetResult();
            await busy;
        }
    }
}
