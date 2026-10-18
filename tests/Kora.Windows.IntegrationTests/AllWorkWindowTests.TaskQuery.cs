using Avalonia.Automation.Peers;
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
    public async Task RetainedTaskCorrelationFieldsAreAccessibleReadFreeAndRetirePrivateResultsWithoutAuthority()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteTaskControlTests.WaitAsync(f);
            var held = new GatedWorkStore(f.Store);
            var access = new WindowsSqliteSessionWorkspaceTests.Access();
            var service = new SessionWorkspaceService(held, new(f.Tasks), access, NullLogger<SessionWorkspaceService>.Instance);
            var admitted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Func<bool>? navigationAdmitted = null;
            var model = new AllWorkViewModel(service, access, NullLogger<AllWorkViewModel>.Instance,
                async (_, current, _) =>
                {
                    navigationAdmitted = current;
                    admitted.TrySetResult();
                    await release.Task.WaitAsync(f.Token);
                });
            var window = new AllWorkWindow(model);
            window.Show();
            try
            {
                var taskField = window.FindControl<TextBox>("RetainedTaskId")!;
                var requestField = window.FindControl<TextBox>("RetainedRequestId")!;
                var clear = window.FindControl<Button>("ClearTaskQuery")!;
                var fields = new[] { taskField, requestField };
                foreach (var field in fields)
                {
                    var peer = ControlAutomationPeer.CreatePeerForElement(field)!;
                    peer.GetName().Should().Contain("Exact retained").And.Contain("empty means Any")
                        .And.Contain("typing performs no read").And.Contain("Escape clears both");
                    field.Focus().Should().BeTrue();
                }
                held.Reads.Should().Be(0);
                var audits = f.Count("security_audit_events");
                var tasks = f.Count("host_tasks");
                var retention = await f.Store.ReadRetentionAsync(f.Request.SessionId, f.Token);
                model.QueryKind = SessionListSearchKind.ExactId;
                model.QueryText = f.Request.SessionId.Value.ToString("D");
                model.Availability = SessionWorkOverviewAvailability.Observed;
                model.Content = SessionWorkOverviewContent.Task;
                model.NeedsAnswer = true;
                model.RangeInputs[0].Minimum = "1";
                await model.RefreshAsync();
                var task = model.Records.Single().Work!.Tasks[0];
                model.TaskState = task.State;
                var taskId = task.Request.TaskId.Value.ToString("D");
                var requestId = task.Request.RequestId.Value.ToString("D");
                var reads = held.Reads;
                taskField.Text = taskId;
                requestField.Text = requestId;
                model.TaskIdText.Should().Be(taskId);
                model.RequestIdText.Should().Be(requestId);
                held.Reads.Should().Be(reads);
                model.TaskQueryHint.Should().Contain("same validated retained task").And.Contain("no current task/session selection");
                await model.RefreshAsync();
                model.Select(model.Records.Single());
                var stale = model.SelectedRecord;
                var navigation = model.NavigateAsync();
                await admitted.Task.WaitAsync(f.Token);
                navigationAdmitted!().Should().BeTrue();
                reads = held.Reads;
                taskField.Text = "aaaaaaaa-0000-0000-0000-000000000001";
                navigationAdmitted().Should().BeFalse();
                release.TrySetResult();
                await navigation;
                held.Reads.Should().Be(reads);
                AssertRetired();
                model.Select(stale);
                model.CanNavigate.Should().BeFalse();
                taskField.Text = taskId;
                foreach (var field in fields)
                {
                    await model.RefreshAsync();
                    model.Select(model.Records.Single());
                    reads = held.Reads;
                    field.Focus().Should().BeTrue();
                    field.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
                    held.Reads.Should().Be(reads);
                    model.TaskIdText.Should().BeEmpty();
                    model.RequestIdText.Should().BeEmpty();
                    field.IsFocused.Should().BeTrue();
                    window.IsVisible.Should().BeTrue();
                    AssertRetired();
                    taskField.Text = taskId;
                    requestField.Text = requestId;
                }
                await model.RefreshAsync();
                reads = held.Reads;
                clear.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                held.Reads.Should().Be(reads);
                taskField.IsFocused.Should().BeTrue();
                model.TaskIdText.Should().BeEmpty();
                model.RequestIdText.Should().BeEmpty();
                model.TaskState.Should().Be(task.State);
                model.QueryText.Should().Be(f.Request.SessionId.Value.ToString("D"));
                model.NeedsAnswer.Should().BeTrue();
                model.RangeInputs[0].Minimum.Should().Be("1");
                AssertRetired();
                taskField.Text = taskId;
                requestField.Text = requestId;
                window.Close();
                model.TaskIdText.Should().BeEmpty();
                model.RequestIdText.Should().BeEmpty();
                fields.Should().OnlyContain(field => !field.IsEnabled && field.Text == string.Empty);
                model.TaskIdText = taskId;
                model.RequestIdText = requestId;
                model.ClearTaskQuery();
                model.TaskIdText.Should().BeEmpty();
                model.RequestIdText.Should().BeEmpty();
                held.Writes.Should().Be(0);
                f.Count("security_audit_events").Should().Be(audits);
                f.Count("host_tasks").Should().Be(tasks);
                (await f.Store.ReadRetentionAsync(f.Request.SessionId, f.Token)).Should().Be(retention);

                void AssertRetired()
                {
                    model.Records.Should().BeEmpty();
                    model.SelectedRecord.Should().BeNull();
                    model.SelectedDetails.Should().BeEmpty();
                    model.CanNavigate.Should().BeFalse();
                    model.CanNext.Should().BeFalse();
                }
            }
            finally { release.TrySetResult(); window.Close(); }
        });
    }

    [WindowsFact]
    public async Task InvalidRetainedTaskIDsNeverReadOrFallBackAndUnavailableCannotMatchPositiveCorrelation()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        var held = new GatedWorkStore(f.Store);
        var access = new WindowsSqliteSessionWorkspaceTests.Access();
        var service = new SessionWorkspaceService(held, new(f.Tasks), access, NullLogger<SessionWorkspaceService>.Instance);
        var model = new AllWorkViewModel(service, access, NullLogger<AllWorkViewModel>.Instance,
            (_, _, _) => throw new InvalidOperationException("Correlation grants no navigation."));
        try
        {
            foreach (var text in new[] { " ", " AAAAAAAA-0000-0000-0000-000000000001", "aaaaaaaa-0000-0000-0000-000000000001 ",
                "AAAAAAAA-0000-0000-0000-000000000001", "aaaaaaaa000000000000000000000001",
                "{aaaaaaaa-0000-0000-0000-000000000001}", Guid.Empty.ToString("D"), "invalid" })
            {
                model.TaskIdText = text;
                model.CanSearch.Should().BeFalse();
                await model.RefreshAsync();
                held.Reads.Should().Be(0);
                model.TaskIdText.Should().Be(text);
                model.Status.Should().Contain("Invalid retained task correlation").And.Contain("No read performed");
                model.ClearTaskQuery();
                model.RequestIdText = text;
                await model.RefreshAsync();
                held.Reads.Should().Be(0);
                model.CanSearch.Should().BeFalse();
                model.ClearTaskQuery();
            }
            held.Fail = true;
            model.Availability = SessionWorkOverviewAvailability.ReadUnavailable;
            model.TaskIdText = f.Request.TaskId.Value.ToString("D");
            model.RequestIdText = f.Request.RequestId.Value.ToString("D");
            await model.RefreshAsync();
            model.Records.Should().BeEmpty();
            model.ClearTaskQuery();
            await model.RefreshAsync();
            model.Records.Single().Availability.Should().Be(SessionWorkOverviewAvailability.ReadUnavailable);
        }
        finally { model.Close(); }
    }
}
