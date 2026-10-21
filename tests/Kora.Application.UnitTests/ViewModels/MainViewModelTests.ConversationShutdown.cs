using AwesomeAssertions;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExitRequiresExplicitConversationShutdownAdmission(bool admitted)
    {
        var f = await Fixture.CreateInitializedAsync();
        var closed = false;
        var calls = 0;
        f.ViewModel.WindowActionRequested += (_, _) => closed = true;
        f.ViewModel.BindConversationShutdown(() => { calls++; return Task.FromResult(admitted); });
        await f.ViewModel.ExitAsync();
        calls.Should().Be(1);
        closed.Should().Be(admitted);
        f.ViewModel.BindConversationShutdown(null);
    }
}
