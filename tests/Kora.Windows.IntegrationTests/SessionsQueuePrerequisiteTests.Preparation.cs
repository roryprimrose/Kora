using System.Diagnostics;

using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;

using AwesomeAssertions;

using Kora.Application.Hosting;
using Kora.Core.Commands;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;
using Kora.Windows.IntegrationTests.Storage;
using Kora.Windows.IntegrationTests.Audio;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionsQueuePrerequisiteTests
{
    [WindowsFact]
    public async Task PreparationPaneRequiresSeparateOneShotNativeApplyAndPreservesAllExistingSelectionsUntilThen()
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
                var original = model.QueuePrerequisites.Single();
                model.SelectQueuePrerequisite(original);
                model.SelectWorkRecord(model.WorkRecords.Single());
                model.DiagnosticsVersion = true;
                var selectedWork = model.SelectedWorkRecord;
                var tasks = f.Count("host_tasks");
                var audit = f.Count("security_audit_events");
                (await Prepare(model, f, null)).Should().BeTrue();
                model.SelectedQueuePrerequisite.Should().BeSameAs(original);
                model.SelectedWorkRecord.Should().BeSameAs(selectedWork);
                model.DiagnosticsVersion.Should().BeTrue();
                model.QueuePrerequisitePreparationStatus.Should().Contain("PREPARATION ONLY").And.Contain("None (explicit")
                    .And.Contain(f.Request.SessionId.Value.ToString("D")).And.Contain("generation").And.Contain("Only exact Succeeded");
                var apply = window.FindControl<Button>("ApplyPreparedQueuePrerequisite")!;
                var cancel = window.FindControl<Button>("CancelPreparedQueuePrerequisite")!;
                AutomationProperties.GetName(apply).Should().Contain("exact prepared prerequisite once");
                AutomationProperties.GetName(cancel).Should().Contain("preserve existing");
                apply.IsEnabled.Should().BeTrue();
                cancel.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                apply.IsEnabled.Should().BeFalse();
                model.SelectedQueuePrerequisite.Should().BeSameAs(original);
                (await Prepare(model, f, null)).Should().BeTrue();
                apply.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await WaitUntil(() => model.CanRead);
                model.SelectedQueuePrerequisite.Should().BeNull();
                model.DiagnosticsVersion.Should().BeTrue();
                model.SelectedWorkRecord.Should().BeSameAs(selectedWork);
                (await Prepare(model, f, f.Request.TaskId.Value)).Should().BeTrue();
                model.QueuePrerequisitePreparationStatus.Should().Contain(original.Label);
                model.SelectedQueuePrerequisite.Should().BeNull();
                void Reenter(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
                {
                    if (!string.Equals(args.PropertyName, nameof(model.SelectedQueuePrerequisite), StringComparison.Ordinal)) { return; }
                    model.ComposerBody = "reentrant edit";
                    model.SelectWorkRecord(null);
                    model.ChooseNoQueuePrerequisite();
                }
                model.PropertyChanged += Reenter;
                await model.ApplyPreparedQueuePrerequisiteAsync(model.PreparedQueuePrerequisiteChoice);
                model.PropertyChanged -= Reenter;
                model.SelectedQueuePrerequisite.Should().BeSameAs(original);
                model.SelectedWorkRecord.Should().BeSameAs(selectedWork);
                model.ComposerBody.Should().BeEmpty();
                model.CanApplyPreparedQueuePrerequisite.Should().BeFalse();
                await model.ApplyPreparedQueuePrerequisiteAsync(model.PreparedQueuePrerequisiteChoice);
                model.SelectedQueuePrerequisite.Should().BeSameAs(original);
                (await f.Store.ReadQueueAsync(f.Request.SessionId, f.Token)).Entries.Should().BeEmpty();
                f.Count("host_tasks").Should().Be(tasks);
                f.Count("security_audit_events").Should().Be(audit);
                action.Reads.Should().Be(0);
                await model.EnqueueVersionAsync();
                model.QueueRecords.Should().ContainSingle().Which.Dependency.Should().Be(original.TaskId);
                model.SelectedQueuePrerequisite.Should().BeNull();
                model.CanEnqueueVersion.Should().BeFalse();
                action.Reads.Should().Be(0);
            }
            finally { window.Close(); model.Close(); }
        });
    }

    [Theory]
    [InlineData(HostTaskState.IntentRecorded)]
    [InlineData(HostTaskState.DispatchRecorded)]
    [InlineData(HostTaskState.Succeeded)]
    [InlineData(HostTaskState.Failed)]
    [InlineData(HostTaskState.Cancelled)]
    [InlineData(HostTaskState.Unknown)]
    public async Task ExactObservedStateCanBePreparedWithoutUpgradingItOrDispatching(HostTaskState state)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        var access = new WindowsSqliteSessionWorkspaceTests.Access();
        var projected = new MetadataQueueStore(f.Store, snapshot => snapshot with
        {
            Tasks = [snapshot.Tasks.Single() with
            { Task = new(snapshot.Tasks.Single().Task.Request, snapshot.Tasks.Single().Task.Revision, state) }],
        });
        var action = new NeverAction();
        await using var queue = new SessionQueueService(projected, f.Store, new(f.Tasks), access, action,
            new(), NullLogger<SessionQueueService>.Instance, f.Time);
        var model = Model(f, access, queue);
        (await Prepare(model, f, f.Request.TaskId.Value)).Should().BeTrue();
        model.SelectedSessionRecord.Should().BeNull();
        model.QueuePrerequisitePreparationStatus.Should().Contain(state.ToString()).And.Contain("Only exact Succeeded");
        await model.ApplyPreparedQueuePrerequisiteAsync(model.PreparedQueuePrerequisiteChoice);
        model.SelectedQueuePrerequisite!.State.Should().Be(state);
        model.SelectedQueuePrerequisite.Revision.Should().Be(new HostRevision(1));
        model.SelectedSessionRecord!.Authority.SessionId.Should().Be(f.Request.SessionId);
        projected.WorkReads.Should().Be(2, "one bounded preparation and one explicit Apply revalidation; no history or fallback scans");
        action.Reads.Should().Be(0);
        f.Count("host_tasks").Should().Be(1);
        model.Close();
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("oversized")]
    [InlineData("default")]
    [InlineData("conflict")]
    [InlineData("other-session")]
    [InlineData("generation")]
    [InlineData("invalid-state")]
    [InlineData("source")]
    public async Task UnavailableBoundedMetadataFailsClosedEvenForExplicitNoneWithoutAnotherScan(string fault)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        var access = new WindowsSqliteSessionWorkspaceTests.Access();
        var projected = new MetadataQueueStore(f.Store, snapshot => fault switch
        {
            "missing" => snapshot with { QueueRecords = [new(null!, default, null)] },
            "oversized" => snapshot with { Tasks = [.. Enumerable.Repeat(snapshot.Tasks.Single(), 51)] },
            "default" => snapshot with { Tasks = default },
            "conflict" => snapshot with { Tasks = [snapshot.Tasks.Single(), snapshot.Tasks.Single()] },
            "other-session" => snapshot with { Tasks = [snapshot.Tasks.Single() with
            { Task = new(HostRequest.Create(RequestOrigin.LocalUi), new(1), HostTaskState.IntentRecorded) }] },
            "generation" => snapshot with { Tasks = [snapshot.Tasks.Single() with { Generation = new(2) }] },
            "invalid-state" => snapshot with { Tasks = [snapshot.Tasks.Single() with
            { Task = new(snapshot.Tasks.Single().Task.Request, new(1), (HostTaskState)99) }] },
            _ => snapshot with { Tasks = [snapshot.Tasks.Single() with { Source = "" }] },
        });
        await using var queue = new SessionQueueService(projected, f.Store, new(f.Tasks), access, new NeverAction(),
            new(), NullLogger<SessionQueueService>.Instance, f.Time);
        var model = Model(f, access, queue);
        (await Prepare(model, f, null)).Should().BeFalse();
        model.CanApplyPreparedQueuePrerequisite.Should().BeFalse();
        model.SelectedQueuePrerequisite.Should().BeNull();
        projected.WorkReads.Should().Be(1);
        model.QueuePrerequisitePreparationStatus.Should().Contain("unavailable");
        model.Close();
    }

    [Theory]
    [InlineData("original")]
    [InlineData("native")]
    [InlineData("control")]
    [InlineData("private")]
    [InlineData("revision")]
    [InlineData("selection")]
    [InlineData("buffer")]
    [InlineData("cancel")]
    [InlineData("close")]
    [InlineData("reentrant")]
    public async Task DeferredPreparationCannotPublishAfterAnyOriginalFenceChanges(string change)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        var access = new WindowsSqliteSessionWorkspaceTests.Access();
        var projected = new MetadataQueueStore(f.Store, snapshot => snapshot);
        await using var queue = new SessionQueueService(projected, f.Store, new(f.Tasks), access, new NeverAction(),
            new(), NullLogger<SessionQueueService>.Instance, f.Time);
        var model = Model(f, access, queue);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        projected.BeforeReturn = () => { entered.SetResult(); return release.Task; };
        var original = true;
        var native = true;
        using var cancellation = new CancellationTokenSource();
        var read = model.PrepareQueuePrerequisiteAsync(new(f.Request.SessionId.Value, f.Request.TaskId.Value),
            RequestOrigin.LocalUi, () => original, () => native, cancellation.Token);
        await entered.Task;
        switch (change)
        {
            case "original": original = false; break;
            case "native": native = false; break;
            case "control": access.CanControl = false; break;
            case "private": access.CanInspect = false; break;
            case "revision": access.ControlRevision++; break;
            case "selection": model.SelectWorkRecord(null); break;
            case "buffer": model.ComposerBody = "new private buffer"; break;
            case "cancel": cancellation.Cancel(); break;
            case "close": model.Close(); break;
            default:
                var duplicate = () => Prepare(model, f, null);
                await duplicate.Should().ThrowAsync<InvalidOperationException>();
                break;
        }
        release.SetResult();
        (await read).Should().BeFalse();
        model.CanApplyPreparedQueuePrerequisite.Should().BeFalse();
        model.SelectedSessionRecord.Should().BeNull();
        projected.WorkReads.Should().Be(1);
        model.Close();
    }

    [Theory]
    [InlineData("choice")]
    [InlineData("work")]
    [InlineData("buffer")]
    [InlineData("diagnostics")]
    [InlineData("control")]
    [InlineData("owner")]
    [InlineData("queue")]
    [InlineData("state")]
    [InlineData("task-revision")]
    [InlineData("source")]
    [InlineData("current-source")]
    [InlineData("cancel")]
    public async Task StaleExplicitApplyNeverSelectsANewerOptionOrClearsTheExistingChoice(string change)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteSessionWorkspaceTests.FinishAsync(f, HostTaskState.Succeeded);
        var access = new WindowsSqliteSessionWorkspaceTests.Access();
        var mutate = false;
        var projected = new MetadataQueueStore(f.Store, snapshot => !mutate ? snapshot : change switch
        {
            "queue" => snapshot with { Queue = snapshot.Queue with { Revision = snapshot.Queue.Revision + 1 } },
            "state" => snapshot with { Tasks = [snapshot.Tasks.Single() with
            { Task = new(snapshot.Tasks.Single().Task.Request, snapshot.Tasks.Single().Task.Revision, HostTaskState.Failed) }] },
            "task-revision" => snapshot with { Tasks = [snapshot.Tasks.Single() with
            { Task = new(snapshot.Tasks.Single().Task.Request, new(3), snapshot.Tasks.Single().Task.State) }] },
            "current-source" => snapshot with { Tasks = [snapshot.Tasks.Single() with
            { CurrentSource = !snapshot.Tasks.Single().CurrentSource }] },
            _ => snapshot with { Tasks = [snapshot.Tasks.Single() with { Source = "different source" }] },
        });
        await using var queue = new SessionQueueService(projected, f.Store, new(f.Tasks), access, new NeverAction(),
            new(), NullLogger<SessionQueueService>.Instance, f.Time);
        var model = Model(f, access, queue);
        await model.RefreshAsync();
        await model.SelectAsync(model.Sessions.Single());
        var original = model.QueuePrerequisites.Single();
        model.SelectQueuePrerequisite(original);
        var owner = true;
        (await model.PrepareQueuePrerequisiteAsync(new(f.Request.SessionId.Value, null),
            RequestOrigin.LocalUi, () => true, () => owner, f.Token)).Should().BeTrue();
        switch (change)
        {
            case "choice": model.SelectQueuePrerequisite(original); break;
            case "work": model.SelectWorkRecord(model.WorkRecords.Single()); break;
            case "buffer": model.ComposerBody = "edited"; break;
            case "diagnostics": model.DiagnosticsVersion = true; model.DiagnosticsVersion = false; break;
            case "control": access.ControlRevision++; break;
            case "owner": owner = false; break;
            case "cancel": model.CancelPreparedQueuePrerequisite(); break;
            default: mutate = true; break;
        }
        await model.ApplyPreparedQueuePrerequisiteAsync(model.PreparedQueuePrerequisiteChoice);
        model.CanApplyPreparedQueuePrerequisite.Should().BeFalse();
        if (change is not "control") { model.SelectedQueuePrerequisite.Should().BeSameAs(original); }
        (await f.Store.ReadQueueAsync(f.Request.SessionId, f.Token)).Entries.Should().BeEmpty();
        model.Close();
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("close")]
    [InlineData("private")]
    [InlineData("control")]
    [InlineData("selection")]
    [InlineData("buffer")]
    public async Task MissingExactTaskDoesNotBecomeNoneAndExplicitApplyRevalidationIsOneShot(string retirement)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        var access = new WindowsSqliteSessionWorkspaceTests.Access();
        var projected = new MetadataQueueStore(f.Store, snapshot => snapshot);
        await using var queue = new SessionQueueService(projected, f.Store, new(f.Tasks), access, new NeverAction(),
            new(), NullLogger<SessionQueueService>.Instance, f.Time);
        var model = Model(f, access, queue);
        (await Prepare(model, f, Guid.NewGuid())).Should().BeFalse();
        projected.WorkReads.Should().Be(1);
        (await Prepare(model, f, f.Request.TaskId.Value)).Should().BeTrue();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        projected.BeforeReturn = () => { entered.SetResult(); return release.Task; };
        var proposal = model.PreparedQueuePrerequisiteChoice;
        var apply = model.ApplyPreparedQueuePrerequisiteAsync(proposal);
        await entered.Task;
        await model.ApplyPreparedQueuePrerequisiteAsync(proposal);
        switch (retirement)
        {
            case "close": model.Close(); break;
            case "private": access.CanInspect = false; break;
            case "control": access.ControlRevision++; break;
            case "selection": model.SelectWorkRecord(null); break;
            case "buffer": model.ComposerBody = "changed while applying"; break;
            default: model.CancelPreparedQueuePrerequisite(); break;
        }
        release.SetResult();
        await apply;
        model.SelectedSessionRecord.Should().BeNull();
        model.SelectedQueuePrerequisite.Should().BeNull();
        projected.WorkReads.Should().Be(3);
        model.Close();
    }

    [Fact]
    public async Task OldApplyTokenCannotSelectANewProposalAndFiftyMetadataRowsRemainBounded()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        var access = new WindowsSqliteSessionWorkspaceTests.Access();
        var projected = new MetadataQueueStore(f.Store, snapshot => snapshot with
        {
            Tasks = [snapshot.Tasks.Single(), .. Enumerable.Range(1, 49).Select(_ => snapshot.Tasks.Single() with
            {
                Task = new(new(new(Guid.NewGuid()), f.Request.SessionId, new(Guid.NewGuid()), RequestOrigin.LocalUi),
                    new(1), HostTaskState.IntentRecorded),
            })],
        });
        await using var queue = new SessionQueueService(projected, f.Store, new(f.Tasks), access, new NeverAction(),
            new(), NullLogger<SessionQueueService>.Instance, f.Time);
        var model = Model(f, access, queue);
        (await Prepare(model, f, f.Request.TaskId.Value)).Should().BeTrue();
        var old = model.PreparedQueuePrerequisiteChoice;
        (await Prepare(model, f, null)).Should().BeTrue();
        var current = model.PreparedQueuePrerequisiteChoice;
        await model.ApplyPreparedQueuePrerequisiteAsync(old);
        model.PreparedQueuePrerequisiteChoice.Should().BeSameAs(current);
        model.SelectedQueuePrerequisite.Should().BeNull();
        model.SelectedSessionRecord.Should().BeNull();
        projected.WorkReads.Should().Be(2);
        model.Close();
    }

    [Fact]
    public async Task PreparationAndApplyEndCorrelatedPresentationActivitiesWithoutEnqueueOrAuthority()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        var access = new WindowsSqliteSessionWorkspaceTests.Access();
        await using var queue = Queue(f, access, new NeverAction());
        var model = Model(f, access, queue);
        var activities = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => string.Equals(source.Name, "Kora.Desktop", StringComparison.Ordinal),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activities.Add,
        };
        ActivitySource.AddActivityListener(listener);
        using var original = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi),
            HostActivityLayer.Application, HostOperation.Request);
        (await Prepare(model, f, f.Request.TaskId.Value)).Should().BeTrue();
        await model.ApplyPreparedQueuePrerequisiteAsync(model.PreparedQueuePrerequisiteChoice);
        activities.Should().HaveCount(2);
        activities[0].Links.Should().Contain(link => link.Context == original.Activity!.Context);
        activities[1].Links.Should().Contain(link => link.Context == activities[0].Context);
        activities.Should().OnlyContain(activity => activity.Duration > TimeSpan.Zero);
        activities.SelectMany(activity => activity.Tags).Should().NotContain(tag => string.Equals(tag.Value, "private buffer", StringComparison.Ordinal));
        f.Count("host_tasks").Should().Be(1);
        model.Close();
    }

    private static Task<bool> Prepare(SessionsViewModel model, InteractionStorageFixture f, Guid? task) =>
        model.PrepareQueuePrerequisiteAsync(new(f.Request.SessionId.Value, task), RequestOrigin.LocalUi,
            () => true, () => true, f.Token);

    private static async Task WaitUntil(Func<bool> completed)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!completed()) { await Task.Delay(1, timeout.Token); }
    }
}
