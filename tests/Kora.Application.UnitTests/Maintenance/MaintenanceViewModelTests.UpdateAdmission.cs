using AwesomeAssertions;
using Kora.Core.Hosting;
using Kora.Core.Maintenance;

namespace Kora.Application.UnitTests.Maintenance;

public sealed partial class MaintenanceViewModelTests
{
    [Theory]
    [InlineData("before")]
    [InlineData("after")]
    [InlineData("generation")]
    [InlineData("cancel")]
    [InlineData("receipt")]
    [InlineData("late-receipt")]
    public async Task Update_actions_revalidate_the_durable_control_session_and_never_return_late_success(string failure)
    {
        using var f = new Fixture();
        f.State.NetworkEnabled = true;
        var eligible = true;
        var operations = 0;
        using var cancellation = new CancellationTokenSource();
        f.Store.BeforeOperation = () =>
        {
            operations++;
            if (operations == 2 && failure is "before") { eligible = false; }
            if (operations == 2 && failure is "cancel") { cancellation.Cancel(); }
            if (operations == 3 && failure is "after") { eligible = false; }
            if (operations == 3 && failure is "generation") { f.Store.Authority = f.Store.Authority! with { Generation = new(99) }; }
        };
        f.Store.BeforeCommit = record =>
        {
            if (f.Client.Calls == 0 || record.State != HostTaskState.Succeeded) { return; }
            if (failure is "receipt") { f.Store.FailTerminal = true; }
            if (failure is "late-receipt") { eligible = false; }
        };
        var action = () => f.Commands.CheckForUpdatesAsync(null, RequestOrigin.LocalUi, () => eligible, cancellation.Token);
        if (failure is "cancel") { await action.Should().ThrowAsync<OperationCanceledException>(); }
        else if (failure is "receipt") { await action.Should().ThrowAsync<IOException>(); }
        else { await action.Should().ThrowAsync<InvalidOperationException>(); }
        f.Store.FailTerminal = false;
        if (failure is "receipt")
        {
            var retry = () => f.Commands.CheckForUpdatesAsync(null, RequestOrigin.LocalUi, () => true, CancellationToken.None);
            await retry.Should().ThrowAsync<InvalidOperationException>();
        }
        if (failure is "before" or "cancel") { f.Client.Calls.Should().Be(0); }
    }

    [Fact]
    public async Task Revocation_between_cached_admission_and_update_admission_prevents_network_work()
    {
        using var f = new Fixture();
        await f.Commands.ExecuteAsync(MaintenanceCommand.Status, RequestOrigin.LocalUi, () => true, CancellationToken.None);
        var receiptReturned = false;
        var checksAfterReceipt = 0;
        f.Store.BeforeCommit = record =>
        {
            if (record.State == HostTaskState.Succeeded) { receiptReturned = true; }
        };
        bool Eligible() => !receiptReturned || ++checksAfterReceipt == 1;
        var action = () => f.Commands.CheckForUpdatesAsync(null, RequestOrigin.LocalUi, Eligible, CancellationToken.None);
        await action.Should().ThrowAsync<InvalidOperationException>();
        f.Client.Calls.Should().Be(0);
    }
}
