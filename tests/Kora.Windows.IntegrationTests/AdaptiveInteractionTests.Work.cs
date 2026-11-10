using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;

using AwesomeAssertions;

using Kora.Application.Hosting;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

public sealed partial class AdaptiveInteractionTests
{
    [Fact]
    public async Task WorkOverviewUsesTheSharedShellAndRetiresPrivateRecordsWhenReturningToChat()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new Fixture();
            await f.Initialize();
            await f.Run("show sessions");
            await f.SelectConversation();
            f.Model.ComposerBody = "preserve while inspecting work";
            var window = f.Window!;
            using var work = CreateHostedWork(f);
            var extraWindows = 0;
            using var opened = Window.WindowOpenedEvent.AddClassHandler<AllWorkWindow>((_, _) => extraWindows++);
            window.FindControl<Button>("WorkActivity")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            window.ActiveActivity.Should().Be(WorkbenchActivity.Work);
            var view = (AllWorkView)window.FindControl<ContentControl>("HostedViewContent")!.Content!;
            var model = work.CurrentModel!;
            await model.RefreshAsync();
            model.Records.Should().NotBeEmpty();
            model.Select(model.Records[0]);
            model.SelectedDetails.Should().NotBeEmpty();
            window.FindControl<Button>("SessionsActivity")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await f.Sessions.InteractionOperation;
            Dispatcher.UIThread.RunJobs();
            model.Records.Should().BeEmpty();
            model.SelectedDetails.Should().BeEmpty();
            model.CanStopAll.Should().BeFalse();
            f.Model.ComposerBody.Should().Be("preserve while inspecting work");
            window.FindControl<Button>("WorkActivity")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            work.CurrentModel.Should().BeSameAs(model);
            model.Records.Should().BeEmpty("reopening cannot restore private records or navigation authority");
            await model.RefreshAsync();
            model.Records.Should().NotBeEmpty();
            view.FindControl<Button>("CloseView")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            window.IsVisible.Should().BeTrue();
            window.ActiveActivity.Should().Be(WorkbenchActivity.Sessions);
            extraWindows.Should().Be(0);
            f.Admission.Calls.Should().Be(0);
        });
    }

    [Fact]
    public async Task NarrowHostedWorkStacksItsReadingPanesWithoutAnotherWindow()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new Fixture();
            await f.Initialize();
            await f.Run("show sessions");
            using var work = CreateHostedWork(f);
            work.Open();
            var window = f.Window!;
            var view = (AllWorkView)window.FindControl<ContentControl>("HostedViewContent")!.Content!;
            window.Width = 640;
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            view.FindControl<Grid>("WorkPanes")!.ColumnDefinitions.Should().HaveCount(1);
            Grid.GetRow(view.FindControl<Grid>("WorkMetadataPane")!).Should().Be(1);
            window.Width = 1120;
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            view.FindControl<Grid>("WorkPanes")!.ColumnDefinitions.Should().HaveCount(2);
            Grid.GetColumn(view.FindControl<Grid>("WorkMetadataPane")!).Should().Be(1);
            f.Admission.Calls.Should().Be(0);
        });
    }

    [Fact]
    public async Task ClosingHostedWorkOwnerDoesNotResolveOrCreateAnotherShell()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new Fixture();
            await f.Initialize();
            await f.Run("show sessions");
            var window = f.Window!;
            var resolutions = 0;
            using var work = new AllWorkWindowController(f.Native.Main, f.Native.Sessions, f.Native.Access,
                NullLogger<AllWorkViewModel>.Instance, f.Sessions);
            work.BindWorkbench(() => { resolutions++; return window; },
                view => window.ShowHostedView(WorkbenchActivity.Work, view));
            work.Open();
            var model = work.CurrentModel!;
            await model.RefreshAsync();
            window.Close();
            work.CurrentModel.Should().BeNull();
            model.Records.Should().BeEmpty();
            model.CanEdit.Should().BeFalse();
            resolutions.Should().Be(1);
        });
    }

    private static AllWorkWindowController CreateHostedWork(Fixture f)
    {
        var work = new AllWorkWindowController(f.Native.Main, f.Native.Sessions, f.Native.Access,
            NullLogger<AllWorkViewModel>.Instance, f.Sessions);
        work.Bind();
        work.BindWorkbench(() => f.Window!, view => f.Window!.ShowHostedView(WorkbenchActivity.Work, view));
        f.Window!.BindWorkNavigation(work.Open);
        return work;
    }

    [Theory]
    [InlineData("hide-return")]
    [InlineData("minimize-restore")]
    [InlineData("chat-return")]
    public async Task HostedWorkRetirementExpiresStopAllAdmissionWithoutReleasingItsOwnedOperation(string boundary)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new Fixture();
            await f.Initialize();
            await f.Run("show sessions");
            using var work = CreateHostedWork(f);
            work.Open();
            var model = work.CurrentModel!;
            var pending = new TaskCompletionSource<AllWorkStopResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            Func<bool>? admitted = null;
            model.BindStopAll((current, _) => { admitted = current; return pending.Task; });
            var operation = model.StopAllAsync();
            admitted.Should().NotBeNull();
            admitted!().Should().BeTrue();
            var window = f.Window!;
            try
            {
                switch (boundary)
                {
                    case "hide-return": window.Hide(); work.Open(); break;
                    case "minimize-restore":
                        window.WindowState = WindowState.Minimized;
                        window.WindowState = WindowState.Normal;
                        break;
                    case "chat-return": await f.Run("show sessions"); work.Open(); break;
                }
                admitted().Should().BeFalse("reopening cannot renew original-channel review admission");
                operation.IsCompleted.Should().BeFalse("presentation loss does not physically complete an owned stop operation");
                model.StopAllResult.Should().BeNull();
            }
            finally { pending.TrySetResult(new(AllWorkStopResult.OutcomeKind.PartialUnconfirmed)); }
            await operation;
            model.StopAllResult.Should().BeNull("a late terminal result cannot repopulate a retired presentation");
            model.StopAllStatus.Should().Contain("retired");
            model.CanStopAll.Should().BeTrue("the next explicit operation must perform a fresh review");
            f.Admission.Calls.Should().Be(0);
        });
    }
}
