using Avalonia.Controls;
using Avalonia.Interactivity;

using AwesomeAssertions;

using Kora.Application.Hosting;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;
using Kora.Core.Tools;
using Kora.Windows.IntegrationTests.Audio;
using Kora.Windows.IntegrationTests.Storage;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionsQueuePrerequisiteTests
{
    [Theory]
    [InlineData("EnqueueVersion", SessionQueueReadProfile.Version)]
    [InlineData("EnqueueCapabilities", SessionQueueReadProfile.Capabilities)]
    [InlineData("EnqueueReadiness", SessionQueueReadProfile.Readiness)]
    [InlineData("EnqueueRuntimes", SessionQueueReadProfile.Runtimes)]
    [InlineData("EnqueueCapability", SessionQueueReadProfile.Capability)]
    [InlineData("EnqueueRuntimeStatus", SessionQueueReadProfile.RuntimeStatus)]
    public async Task ActualNativeControlsChooseAlreadyObservedQueuedTaskAndKeepItsExactProfileTargetAndFifoDependency(
        string button, SessionQueueReadProfile profile)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteSessionWorkspaceTests.FinishAsync(f, HostTaskState.Succeeded);
            var access = new WindowsSqliteSessionWorkspaceTests.Access();
            var action = new NeverAction();
            await using var queue = Queue(f, access, action);
            var model = Model(f, access, queue);
            var window = new SessionsWindow(model);
            window.Show();
            try
            {
                await model.RefreshAsync();
                await model.SelectAsync(model.Sessions.Single());
                model.SelectedQueuePrerequisite.Should().BeNull();
                await model.EnqueueVersionAsync();
                var source = model.QueueRecords.Single();
                var picker = window.FindControl<ListBox>("QueuePrerequisites")!;
                var option = model.QueuePrerequisites.Single(row => row.TaskId == source.Request.TaskId);
                option.State.Should().Be(HostTaskState.IntentRecorded);
                option.Revision.Value.Should().Be(1);
                model.QueuePrerequisites.Select(row => row.TaskId).Should().OnlyHaveUniqueItems();
                model.QueuePrerequisites.Should().Contain(row => row.TaskId == f.Request.TaskId);
                model.SelectWorkRecord(model.WorkRecords.Single(row => row.TaskId == source.Request.TaskId.Value));
                model.SelectedQueuePrerequisite.Should().BeNull("work selection never chooses a prerequisite");
                picker.SelectedItem = option;
                if (profile == SessionQueueReadProfile.Capability)
                {
                    window.FindControl<ComboBox>("QueueCapabilities")!.SelectedItem = model.QueueCapabilities.Single(
                        row => string.Equals(row.Descriptor.Id, ReadOnlyCapabilityCatalog.Version, StringComparison.Ordinal));
                }
                var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                model.PropertyChanged += (_, _) =>
                {
                    if (model.CanRead && model.QueueRecords.Count == 2) { completed.TrySetResult(); }
                };
                window.FindControl<Button>(button)!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await completed.Task.WaitAsync(f.Token);
                var dependent = model.QueueRecords[^1];
                dependent.Dependency.Should().Be(source.Request.TaskId);
                dependent.ReadProfile.Should().Be(profile);
                dependent.Target.Should().Be(profile == SessionQueueReadProfile.Capability
                    ? new SessionQueueCapabilityTarget(ReadOnlyCapabilityCatalog.Version) : null);
                model.SelectedQueuePrerequisite.Should().BeNull();
                model.CanEnqueueVersion.Should().BeFalse();
                using var root = HostActivity.BeginRoot(source.Request, HostActivityLayer.Application, HostOperation.Request);
                var running = (await f.Store.AdmitAsync(source, access.ControlRevision, new(), () => true, f.Token)).Entry;
                (await f.Store.FindReadyAsync(access.ControlRevision, new(), f.Token)).Should().BeNull();
                await f.Store.CompleteAsync(running, SessionQueueState.Succeeded, () => true, f.Token);
                (await f.Store.FindReadyAsync(access.ControlRevision, new(), f.Token))!.Request.TaskId.Should().Be(dependent.Request.TaskId);
                await model.RefreshWorkAsync();
                var succeeded = model.QueuePrerequisites.Single(row => row.TaskId == source.Request.TaskId);
                succeeded.State.Should().Be(HostTaskState.Succeeded);
                succeeded.Revision.Value.Should().Be(3);
                model.SelectQueuePrerequisite(option);
                model.SelectedQueuePrerequisite.Should().BeNull("old native objects cannot renew a refreshed observation");
                model.SelectQueuePrerequisite(succeeded);
                model.SelectedQueuePrerequisite.Should().BeSameAs(succeeded);
                action.Reads.Should().Be(0);
                (await f.Store.ReadGrantsAsync(f.Token)).Should().BeEmpty();
            }
            finally { window.Close(); model.Close(); }
        });
    }

    [Theory]
    [InlineData("running", HostTaskState.DispatchRecorded, 2)]
    [InlineData("succeeded", HostTaskState.Succeeded, 3)]
    [InlineData("failed", HostTaskState.Failed, 3)]
    [InlineData("cancelled", HostTaskState.Cancelled, 2)]
    [InlineData("restart-pending", HostTaskState.Interrupted, 2)]
    [InlineData("restart-running", HostTaskState.Unknown, 3)]
    public async Task NativeQueuedMetadataUsesActualHostTaskStateAndRevisionIncludingRestartDivergence(
        string transition, HostTaskState state, long revision)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteSessionWorkspaceTests.FinishAsync(f, HostTaskState.Succeeded);
        var access = new WindowsSqliteSessionWorkspaceTests.Access();
        var action = new NeverAction();
        SessionQueueEntry source;
        await using (var initialQueue = Queue(f, access, action))
        {
            var initial = Model(f, access, initialQueue);
            await initial.RefreshAsync();
            await initial.SelectAsync(initial.Sessions.Single());
            await initial.EnqueueVersionAsync();
            source = initial.QueueRecords.Single();
            if (transition is "cancelled")
            {
                initial.SelectQueueEntry(source);
                await initial.CancelQueueEntryAsync();
            }
            initial.Close();
        }
        if (transition is "running" or "succeeded" or "failed" or "restart-running")
        {
            using var root = HostActivity.BeginRoot(source.Request, HostActivityLayer.Application, HostOperation.Request);
            source = (await f.Store.AdmitAsync(source, access.ControlRevision, new(), () => true, f.Token)).Entry;
            if (transition is "succeeded" or "failed")
            {
                source = await f.Store.CompleteAsync(source,
                    transition is "succeeded" ? SessionQueueState.Succeeded : SessionQueueState.Failed, () => true, f.Token);
            }
        }
        if (transition.StartsWith("restart", StringComparison.Ordinal))
        {
            f.Reopen();
            await f.Store.InitializeAsync(f.Token);
            await new HostTaskCoordinator(f.Tasks).RecoverAsync(100, f.Token);
        }
        await using var queue = Queue(f, access, action);
        var model = Model(f, access, queue);
        var audit = f.Count("security_audit_events");
        var retention = await f.Store.ReadRetentionAsync(f.Request.SessionId, f.Token);
        await model.RefreshAsync();
        await model.SelectAsync(model.Sessions.Single());
        var choice = model.QueuePrerequisites.Single(row => row.TaskId == source.Request.TaskId);
        choice.State.Should().Be(state);
        choice.Revision.Value.Should().Be(revision);
        choice.Label.Should().Contain(state.ToString()).And.Contain("revision " + revision)
            .And.Contain("observed UTC").And.Contain("host metadata source unclassified");
        var work = await f.Store.ReadWorkAsync(f.Request.SessionId, access.ControlRevision, new(), f.Token);
        var row = work.QueueRecords.Single(record => record.Entry.Request.TaskId == source.Request.TaskId);
        row.TaskMetadata!.State.Should().Be(state);
        row.TaskMetadata.Revision.Should().Be(choice.Revision);
        if (transition.StartsWith("restart", StringComparison.Ordinal))
        {
            row.Entry.Revision.Should().NotBe(row.TaskMetadata.Revision);
        }
        f.Count("security_audit_events").Should().Be(audit);
        (await f.Store.ReadRetentionAsync(f.Request.SessionId, f.Token)).Should().Be(retention);
        model.SelectQueuePrerequisite(choice);
        await model.EnqueueVersionAsync();
        var dependent = (await f.Store.ReadQueueAsync(f.Request.SessionId, f.Token)).Entries.Single(entry => entry.Dependency == choice.TaskId);
        dependent.Request.TaskId.Should().NotBe(choice.TaskId);
        if (state is not HostTaskState.Succeeded)
        {
            (await f.Store.FindReadyAsync(access.ControlRevision, new(), f.Token)).Should().BeNull();
        }
        action.Reads.Should().Be(0);
        model.Close();
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("task-id")]
    [InlineData("session")]
    [InlineData("generation")]
    [InlineData("entry-generation")]
    [InlineData("revision")]
    [InlineData("state")]
    [InlineData("source")]
    [InlineData("duplicate-queue")]
    [InlineData("duplicate-task")]
    [InlineData("over-bound")]
    public async Task InvalidOptionalQueueTaskProjectionRevokesExactChoiceWithoutImplicitNoneOrInventedTask(string boundary)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteSessionWorkspaceTests.FinishAsync(f, HostTaskState.Succeeded);
        var access = new WindowsSqliteSessionWorkspaceTests.Access();
        var corrupt = false;
        var projected = new MetadataQueueStore(f.Store, snapshot =>
        {
            if (!corrupt) { return snapshot; }
            var row = snapshot.QueueRecords.Single();
            var task = row.TaskMetadata!;
            return boundary switch
            {
                "duplicate-task" => snapshot with { Tasks = [.. snapshot.Tasks, snapshot.Tasks[0]] },
                "duplicate-queue" => snapshot with { QueueRecords = [row, row] },
                "over-bound" => snapshot with { QueueRecords = [.. Enumerable.Repeat(row, 51)] },
                _ => snapshot with { QueueRecords = [row with { TaskMetadata = boundary switch
                {
                    "missing" => null,
                    "task-id" => task with { TaskId = new(Guid.NewGuid()) },
                    "session" => task with { SessionId = new(Guid.NewGuid()) },
                    "generation" => task with { Generation = new(2) },
                    "revision" => task with { Revision = default },
                    "state" => task with { State = (HostTaskState)99 },
                    "source" => task with { Source = "" },
                    _ => task,
                }, Entry = boundary is "entry-generation" ? row.Entry with { Generation = new(2) } : row.Entry }] },
            };
        });
        var action = new NeverAction();
        await using var queue = new SessionQueueService(projected, f.Store, new(f.Tasks), access, action,
            new(), NullLogger<SessionQueueService>.Instance, f.Time);
        var model = Model(f, access, queue);
        await model.RefreshAsync();
        await model.SelectAsync(model.Sessions.Single());
        await model.EnqueueVersionAsync();
        var source = model.QueueRecords.Single();
        model.SelectQueuePrerequisite(model.QueuePrerequisites.Single(row => row.TaskId == source.Request.TaskId));
        corrupt = true;
        await model.RefreshWorkAsync();
        model.QueuePrerequisites.Should().BeEmpty();
        model.SelectedQueuePrerequisite.Should().BeNull();
        model.QueuePrerequisiteStatus.Should().Contain("unavailable").And.Contain("explicitly choose None");
        model.CanEnqueueVersion.Should().BeFalse();
        await model.EnqueueVersionAsync();
        (await f.Store.ReadQueueAsync(f.Request.SessionId, f.Token)).Entries.Should().ContainSingle();
        await model.RefreshAsync();
        await model.SelectAsync(model.Sessions.Single());
        model.ChooseNoQueuePrerequisite();
        model.CanEnqueueVersion.Should().BeTrue("explicit None still uses the existing route without claiming absent metadata");
        await model.EnqueueVersionAsync();
        (await f.Store.ReadQueueAsync(f.Request.SessionId, f.Token)).Entries.Should().HaveCount(2)
            .And.OnlyContain(entry => entry.Dependency == null);
        action.Reads.Should().Be(0);
        model.Close();
    }
}
