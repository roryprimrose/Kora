using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

using AwesomeAssertions;

using Kora.Application.Hosting;
using Kora.Core.Hosting;
using Kora.Core.Storage;
using Kora.Windows.IntegrationTests.Audio;
using Kora.Windows.IntegrationTests.Storage;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

public sealed partial class AllWorkWindowTests
{
    [WindowsFact]
    public async Task ExactTaskStateSelectorIsVisibleAccessibleVolatileAndReadFreeWithNoAuthorityOrAbsenceInference()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteTaskControlTests.WaitAsync(f);
            var held = new GatedWorkStore(f.Store);
            var access = new WindowsSqliteSessionWorkspaceTests.Access();
            var service = new SessionWorkspaceService(held, new(f.Tasks), access, NullLogger<SessionWorkspaceService>.Instance);
            var model = new AllWorkViewModel(service, access, NullLogger<AllWorkViewModel>.Instance,
                (_, _, _) => throw new InvalidOperationException("State filtering cannot navigate."));
            var window = new AllWorkWindow(model);
            window.Show();
            try
            {
                var control = window.FindControl<ComboBox>("ObservedTaskState")!;
                var peer = ControlAutomationPeer.CreatePeerForElement(control)!;
                control.SelectedItem.Should().BeSameAs(model.TaskStateChoices[0]);
                peer.GetProvider<ISelectionProvider>()!.GetSelection().Should()
                    .ContainSingle().Which.GetName().Should().Be("Any");
                peer.GetName().Should().Contain("changing performs no read").And.Contain("omissions prove no absence")
                    .And.Contain("ownership or authority");
                model.TaskStateChoices.Skip(1).Select(choice => choice.Value!.Value).Should().Equal(Enum.GetValues<HostTaskState>());
                held.Reads.Should().Be(0);
                var audits = f.Count("security_audit_events");
                var tasks = f.Count("host_tasks");
                var retention = await f.Store.ReadRetentionAsync(f.Request.SessionId, f.Token);
                model.QueryKind = SessionListSearchKind.ExactId;
                model.QueryText = f.Request.SessionId.Value.ToString("D");
                model.NeedsAnswer = true;
                model.Availability = SessionWorkOverviewAvailability.Observed;
                model.Content = SessionWorkOverviewContent.PendingQuestion;
                model.RangeInputs[0].Minimum = "1";
                await model.RefreshAsync();
                var observed = model.Records.Single().Work!.Tasks.Select(task => task.State).ToArray();
                foreach (var choice in model.TaskStateChoices.Skip(1))
                {
                    model.Select(model.Records.Count == 0 ? null : model.Records[0]);
                    var stale = model.SelectedRecord;
                    var reads = held.Reads;
                    control.Focus().Should().BeTrue();
                    control.SelectedItem = choice;
                    model.TaskState.Should().Be(choice.Value);
                    held.Reads.Should().Be(reads);
                    model.Records.Should().BeEmpty();
                    model.SelectedRecord.Should().BeNull();
                    model.SelectedDetails.Should().BeEmpty();
                    model.CanNavigate.Should().BeFalse();
                    model.CanNext.Should().BeFalse();
                    model.Select(stale);
                    model.CanNavigate.Should().BeFalse();
                    await model.RefreshAsync();
                    model.Records.Should().HaveCount(observed.Contains(choice.Value!.Value) ? 1 : 0);
                    control.IsFocused.Should().BeTrue();
                }
                model.TaskState = observed[0];
                await model.RefreshAsync();
                model.Select(model.Records.Single());
                var beforeClear = held.Reads;
                window.FindControl<Button>("ClearQuery")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                held.Reads.Should().Be(beforeClear);
                model.TaskState.Should().Be(observed[0], "Clear query preserves independent filters");
                model.Records.Should().BeEmpty();
                model.SelectedDetails.Should().BeEmpty();
                model.CanNavigate.Should().BeFalse();
                model.CanNext.Should().BeFalse();
                await model.RefreshAsync();
                model.Select(model.Records.Single());
                beforeClear = held.Reads;
                window.FindControl<TextBox>("MetadataQuery")!.RaiseEvent(
                    new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
                held.Reads.Should().Be(beforeClear);
                model.Records.Should().BeEmpty();
                model.SelectedDetails.Should().BeEmpty();
                model.CanNavigate.Should().BeFalse();
                model.CanNext.Should().BeFalse();
                var invalid = () => model.TaskState = (HostTaskState)99;
                invalid.Should().Throw<ArgumentOutOfRangeException>();
                model.ClearRanges();
                model.Content = SessionWorkOverviewContent.Any;
                model.Availability = SessionWorkOverviewAvailability.ReadUnavailable;
                held.Fail = true;
                await model.RefreshAsync();
                model.Records.Should().BeEmpty();
                control.SelectedIndex = 0;
                await model.RefreshAsync();
                model.Records.Single().Availability.Should().Be(SessionWorkOverviewAvailability.ReadUnavailable);
                control.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Down });
                model.TaskState.Should().Be(Enum.GetValues<HostTaskState>()[0]);
                control.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Up });
                model.TaskState.Should().BeNull();
                window.Close();
                control.IsEnabled.Should().BeFalse();
                model.TaskState.Should().BeNull();
                model.TaskState = HostTaskState.Failed;
                model.TaskState.Should().BeNull();
                model.Records.Should().BeEmpty();
                held.Writes.Should().Be(0);
                f.Count("security_audit_events").Should().Be(audits);
                f.Count("host_tasks").Should().Be(tasks);
                (await f.Store.ReadRetentionAsync(f.Request.SessionId, f.Token)).Should().Be(retention);
            }
            finally { window.Close(); }
        });
    }
}
