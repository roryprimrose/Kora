using AwesomeAssertions;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Theory]
    [InlineData("open sessions")]
    [InlineData("Kora, open sessions")]
    public async Task Exact_session_entry_is_local_passive_and_does_not_invoke_model(string command)
    {
        var fixture = await Fixture.CreateInitializedAsync();
        fixture.ViewModel.ShowSessions();
        var opens = 0;
        fixture.ViewModel.SessionsRequested += (_, _) => opens++;
        fixture.ViewModel.CommandText = command;
        await fixture.ViewModel.RunTypedCommand.ExecuteAsync();
        opens.Should().Be(1);
        fixture.Reasoner.Requests.Should().BeEmpty();
        fixture.Session.LockCalls.Should().Be(0);
        fixture.ViewModel.ShowSessions();
        opens.Should().Be(2);
    }
}
