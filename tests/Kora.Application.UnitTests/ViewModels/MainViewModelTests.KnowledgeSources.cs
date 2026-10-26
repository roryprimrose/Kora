using AwesomeAssertions;
using Kora.Application.ViewModels;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExitJoinsNamedSourceResourcesBeforeAnyShutdownDecision(bool fail)
    {
        var fixture = await Fixture.CreateInitializedAsync();
        var joined = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = false;
        fixture.ViewModel.BindKnowledgeSourcesShutdown(async () =>
        {
            started = true;
#pragma warning disable VSTHRD003 // Deterministic completion barrier, not dispatcher-affine foreign work.
            await joined.Task;
#pragma warning restore VSTHRD003
            if (fail) { throw new IOException("Synthetic cleanup uncertainty."); }
        });
        var exit = fixture.ViewModel.ExitAsync();
        started.Should().BeTrue();
        exit.IsCompleted.Should().BeFalse();
        fixture.WindowActions.Should().NotContain(WindowAction.Close);
        joined.SetResult();
        await exit;
        fixture.WindowActions.Contains(WindowAction.Close).Should().Be(!fail);
        fixture.ViewModel.BindKnowledgeSourcesShutdown(null);
        await fixture.ViewModel.DisposeAsync();
    }
}
