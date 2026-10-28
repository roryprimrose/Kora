using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Interactivity;

using AwesomeAssertions;

using Kora.Application.Hosting;
using Kora.Windows.IntegrationTests.Storage;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

public sealed partial class AllWorkWindowTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StopAllClosureKeepsTheOriginalJoinWithoutResurrectingItsPrivatePresentation(bool fails)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            var access = new WindowsSqliteSessionWorkspaceTests.Access { CanControl = false };
            using var model = new AllWorkViewModel(WindowsSqliteSessionWorkspaceTests.Service(f, access),
                access, NullLogger<AllWorkViewModel>.Instance, (_, _, _) => Task.CompletedTask);
            var joined = new TaskCompletionSource<AllWorkStopResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            Func<bool>? original = null;
            model.BindStopAll((current, _) => { original = current; return joined.Task; });
            model.CanStopAll.Should().BeFalse("inspection alone never grants control");
            access.CanControl = true;
            var window = new AllWorkWindow(model);
            window.Show();
            var stopping = model.StopAllAsync();
            original!().Should().BeTrue();
            window.Close();
            original().Should().BeFalse();
            stopping.IsCompleted.Should().BeFalse("closing cannot abandon accepted physical resources");
            if (fails) { joined.SetException(new IOException("Synthetic physical join failure.")); }
            else { joined.SetResult(new(AllWorkStopResult.OutcomeKind.Completed)); }
            await stopping;
            model.StopAllResult.Should().BeNull();
            model.StopAllStatus.Should().BeEmpty();
            model.CanStopAll.Should().BeFalse();
        });
    }

    [Fact]
    public async Task StopAllIsExplicitAccessibleAndSeparateFromEveryPassiveOverviewOperation()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            var access = new WindowsSqliteSessionWorkspaceTests.Access();
            var service = WindowsSqliteSessionWorkspaceTests.Service(f, access);
            using var model = new AllWorkViewModel(service, access, NullLogger<AllWorkViewModel>.Instance,
                (_, _, _) => Task.CompletedTask);
            model.CanStopAll.Should().BeFalse("a missing native binder cannot fake availability");
            var calls = 0;
            var joined = new TaskCompletionSource<AllWorkStopResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            model.BindStopAll((current, _) =>
            {
                calls++;
                current().Should().BeTrue();
                return joined.Task;
            });
            var window = new AllWorkWindow(model);
            window.Show();
            try
            {
                var button = window.FindControl<Button>("StopAllSubmittedWork")!;
                ControlAutomationPeer.CreatePeerForElement(button)!.GetName().Should()
                    .Contain("full host review").And.Contain("keep Kora open").And.Contain("not just this page");
                await model.RefreshAsync();
                model.Select(model.Records.Count == 0 ? null : model.Records[0]);
                model.ClearQuery();
                model.Cancel();
                calls.Should().Be(0);
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                calls.Should().Be(1);
                model.CanStopAll.Should().BeFalse();
                model.StopAllStatus.Should().Contain("resource joining");
                await model.StopAllAsync();
                calls.Should().Be(1);
                model.StopAllOperation.IsCompleted.Should().BeFalse();
                joined.SetResult(new(AllWorkStopResult.OutcomeKind.NothingToStop));
                await model.StopAllOperation;
                model.CanStopAll.Should().BeTrue();
                model.StopAllStatus.Should().Contain("complete host review").And.Contain("not a future all-clear");
                window.IsVisible.Should().BeTrue();
            }
            finally { joined.TrySetResult(new(AllWorkStopResult.OutcomeKind.Cancelled)); window.Close(); }
        });
    }
}
