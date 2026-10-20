using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Threading;

using AwesomeAssertions;

using Kora.Application.Hosting;
using Kora.Core.Hosting;
using Kora.Core.Storage;
using Kora.Windows.IntegrationTests.Audio;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

public sealed partial class WorkspaceSurfaceCommandControllerTests
{
    private const string QueryTaskId = "aaaaaaaa-bbbb-4ccc-8ddd-eeeeeeeeeeee";
    private const string PrepareQuery = "prepare all work task " + QueryTaskId;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReentrantPreparationEditsOrClosureCannotReceiveNativeAcknowledgement(bool close)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new NavigationFixture();
            await f.Native.InitializeAsync();
            var store = new RefusingWorkspaceStore();
            var service = new SessionWorkspaceService(store, new(f.Native.Tasks), f.Native.Access,
                NullLogger<SessionWorkspaceService>.Instance);
            var model = new AllWorkViewModel(service, f.Native.Access, NullLogger<AllWorkViewModel>.Instance,
                (_, _, _) => throw new InvalidOperationException("Preparation cannot navigate."));
            var changed = false;
            model.PropertyChanged += (_, args) =>
            {
                if (changed || !string.Equals(args.PropertyName, nameof(AllWorkViewModel.TaskIdText), StringComparison.Ordinal)) { return; }
                changed = true;
                if (close) { model.Close(); }
                else { model.RequestIdText = "bbbbbbbb-bbbb-4ccc-8ddd-eeeeeeeeeeee"; }
            };
            model.PrepareTaskQuery(Guid.Parse(QueryTaskId)).Should().BeFalse();
            model.Records.Should().BeEmpty();
            model.CanNavigate.Should().BeFalse();
            store.Calls.Should().Be(0);
            model.Close();
        });
    }

    [WindowsFact]
    public async Task NativeTaskQueryPreparationIsReadFreeAccessibleAndPreservesEveryOtherDraftInExistingSingleton()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new NavigationFixture();
            await f.Native.InitializeAsync();
            var store = new RefusingWorkspaceStore();
            f.Bind(new(store, new(f.Native.Tasks), f.Native.Access, NullLogger<SessionWorkspaceService>.Instance));
            AllWorkWindow? window = null;
            var opens = 0;
            using var opened = Window.WindowOpenedEvent.AddClassHandler<AllWorkWindow>((view, _) => { window = view; opens++; });
            await f.Run("all work query help");
            opens.Should().Be(0);
            await f.Run(PrepareQuery);
            f.Native.Main.Transcript.Should().Contain("PREPARED / REVIEW NEEDED");
            var model = (AllWorkViewModel)window!.DataContext!;
            model.TaskIdText.Should().Be(QueryTaskId);
            var field = window.FindControl<TextBox>("RetainedTaskId")!;
            field.IsFocused.Should().BeTrue();
            ControlAutomationPeer.CreatePeerForElement(field)!.GetName().Should().Contain("Exact retained");
            model.RequestIdText = "bbbbbbbb-bbbb-4ccc-8ddd-eeeeeeeeeeee";
            model.Filter = SessionListFilter.Active;
            model.QueryKind = SessionListSearchKind.ExactId;
            model.QueryText = "cccccccc-bbbb-4ccc-8ddd-eeeeeeeeeeee";
            model.NeedsAnswer = true;
            model.Availability = SessionWorkOverviewAvailability.Observed;
            model.Content = SessionWorkOverviewContent.Task;
            model.TaskState = HostTaskState.Unknown;
            foreach (var range in model.RangeInputs) { range.Minimum = "1"; range.Maximum = "2"; }
            await f.Run(PrepareQuery);
            opens.Should().Be(1);
            model.TaskIdText.Should().Be(QueryTaskId);
            model.RequestIdText.Should().Be("bbbbbbbb-bbbb-4ccc-8ddd-eeeeeeeeeeee");
            model.Filter.Should().Be(SessionListFilter.Active);
            model.QueryKind.Should().Be(SessionListSearchKind.ExactId);
            model.QueryText.Should().Be("cccccccc-bbbb-4ccc-8ddd-eeeeeeeeeeee");
            model.NeedsAnswer.Should().BeTrue();
            model.Availability.Should().Be(SessionWorkOverviewAvailability.Observed);
            model.Content.Should().Be(SessionWorkOverviewContent.Task);
            model.TaskState.Should().Be(HostTaskState.Unknown);
            model.RangeInputs.Should().OnlyContain(range => range.Minimum == "1" && range.Maximum == "2");
            model.Status.Should().Contain("ALL other filter drafts remain AND-composed")
                .And.Contain("same validated retained task");
            model.Records.Should().BeEmpty();
            model.SelectedRecord.Should().BeNull();
            model.SelectedDetails.Should().BeEmpty();
            model.CanNavigate.Should().BeFalse();
            model.CanNext.Should().BeFalse();
            await f.Run("all work query help");
            model.TaskIdText.Should().Be(QueryTaskId);
            store.Calls.Should().Be(0);
        });
    }

    [Theory]
    [InlineData("owner")]
    [InlineData("input")]
    [InlineData("native-dispose")]
    [InlineData("binding-dispose")]
    [InlineData("close")]
    [InlineData("filter")]
    [InlineData("source")]
    public async Task QueuedTaskPreparationRejectsRetiredOriginalAndNativeSources(string boundary)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new NavigationFixture();
            await f.Native.InitializeAsync();
            f.Bind();
            AllWorkWindow? window = null;
            using var opened = Window.WindowOpenedEvent.AddClassHandler<AllWorkWindow>((view, _) => window = view);
            f.AllWork!.Open();
            var model = (AllWorkViewModel)window!.DataContext!;
            model.TaskIdText = "bbbbbbbb-bbbb-4ccc-8ddd-eeeeeeeeeeee";
            f.Native.Main.CommandText = PrepareQuery;
            var opening = f.Native.Main.RunTypedCommand.ExecuteAsync();
            opening.IsCompleted.Should().BeFalse();
            switch (boundary)
            {
                case "owner": f.Native.Access.SetOpen(false); f.Native.Access.SetOpen(true); break;
                case "input": f.Native.Main.CommandText = "all work query help"; break;
                case "native-dispose": f.AllWork.Dispose(); break;
                case "binding-dispose": f.Commands!.Dispose(); break;
                case "close": window.Close(); break;
                case "filter": model.NeedsAnswer = true; break;
                case "source": model.Revoke(); break;
            }
            await opening;
            f.Native.Main.Transcript.Should().Contain("unavailable");
            model.TaskIdText.Should().NotBe(QueryTaskId);
        });
    }

    [Theory]
    [InlineData("focus")]
    [InlineData("close")]
    [InlineData("owner")]
    [InlineData("dispose")]
    public async Task NativePreparationCannotClaimSuccessAfterOpeningCallbackRevokesSourceOrFocus(string boundary)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new NavigationFixture();
            await f.Native.InitializeAsync();
            f.Bind();
            using var opened = Window.WindowOpenedEvent.AddClassHandler<AllWorkWindow>((window, _) =>
            {
                switch (boundary)
                {
                    case "focus": window.FindControl<TextBox>("RetainedTaskId")!.IsEnabled = false; break;
                    case "close": window.Close(); break;
                    case "owner": f.Native.Access.SetOpen(false); break;
                    case "dispose": f.AllWork!.Dispose(); break;
                }
            });
            await f.Run(PrepareQuery);
            f.Native.Main.Transcript.Should().Contain("unavailable");
        });
    }

    [WindowsFact]
    public async Task RepreparingSameIdRetiresResultsSelectionDetailsNavigationAndContinuationWithoutReading()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new NavigationFixture();
            await f.Native.InitializeAsync();
            var service = f.Native.Sessions;
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Func<bool>? current = null;
            var model = new AllWorkViewModel(service, f.Native.Access, NullLogger<AllWorkViewModel>.Instance,
                async (_, admitted, _) => { current = admitted; entered.SetResult(); await release.Task; });
            try
            {
                await model.RefreshAsync();
                var observed = model.Records.First(record => record.Work?.Tasks.Length > 0);
                var id = observed.Work!.Tasks[0].Request.TaskId.Value;
                model.PrepareTaskQuery(id).Should().BeTrue();
                await model.RefreshAsync();
                model.Select(model.Records[0]);
                var stale = model.SelectedRecord;
                var navigation = model.NavigateAsync();
                await entered.Task;
                current!().Should().BeTrue();
                model.PrepareTaskQuery(id).Should().BeTrue();
                current().Should().BeFalse();
                model.Records.Should().BeEmpty();
                model.SelectedRecord.Should().BeNull();
                model.SelectedDetails.Should().BeEmpty();
                model.CanNext.Should().BeFalse();
                model.CanNavigate.Should().BeFalse();
                model.Select(stale);
                model.CanNavigate.Should().BeFalse();
                release.SetResult();
                await navigation;
                model.Close();
                model.PrepareTaskQuery(id).Should().BeFalse();
            }
            finally { release.TrySetResult(); model.Close(); }
        });
    }
}
