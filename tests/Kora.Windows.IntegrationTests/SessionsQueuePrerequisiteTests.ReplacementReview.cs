using AwesomeAssertions;

using Kora.Application.Hosting;
using Kora.Core.Commands;
using Kora.Core.Hosting;
using Kora.Core.Storage;
using Kora.Windows.IntegrationTests.Storage;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionsQueuePrerequisiteTests
{
    [Fact]
    public async Task ReplacementCommandUsesTheExistingNativeSessionsOwnerAndCloseRevokesReviewEvenWithCompactOpen()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var parent = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".net-test-artifacts"));
            Directory.CreateDirectory(parent);
            using var host = new Kora.NativeUxFixture.NativeUxFixtureSession(parent);
            await host.InitializeAsync();
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteSessionWorkspaceTests.FinishAsync(f, HostTaskState.Succeeded);
            var access = new WindowsSqliteSessionWorkspaceTests.Access();
            var action = new NeverAction();
            await using var queue = Queue(f, access, action);
            var service = new SessionWorkspaceService(f.Store, new(f.Tasks), access, NullLogger<SessionWorkspaceService>.Instance, queue);
            var sink = new Kora.Windows.Storage.WindowsSqliteEvidenceSink(f.Paths);
            sink.Initialize();
            var evidence = new Kora.Application.Diagnostics.DurableEvidenceQuery(
                new Kora.Windows.Storage.WindowsSqliteEvidenceReader(sink), access, f.Time,
                NullLogger<Kora.Application.Diagnostics.DurableEvidenceQuery>.Instance);
            var snapshot = await f.Store.ReadQueueAsync(f.Request.SessionId, f.Token);
            var task = Guid.NewGuid();
            await queue.ExecuteCommandAsync(new(SessionCommandOperation.QueueEnqueue, f.Request.SessionId.Value, 1)
            {
                QueueRevision = snapshot.Revision, QueueRunId = snapshot.DispatchControl!.RunId,
                QueueControlRevision = snapshot.DispatchControl.Revision, WorkRequestId = Guid.NewGuid(), TaskId = task,
            }, RequestOrigin.LocalUi, () => true, f.Token);
            var entry = (await f.Store.ReadQueueAsync(f.Request.SessionId, f.Token)).Entries.Single();
            f.Time.Now = entry.ExpiresAt;
            using var controller = new SessionsWindowController(host.Main, service, evidence, access, NullLogger<SessionsViewModel>.Instance);
            controller.Bind();
            SessionsWindow? window = null;
            var opens = 0;
            using var opened = Avalonia.Controls.Window.WindowOpenedEvent.AddClassHandler<SessionsWindow>((view, _) =>
            { window = view; opens++; });
            host.Main.CommandText = "session queue replacement review " + f.Request.SessionId.Value.ToString("D") + " " + task.ToString("D");
            await host.Main.RunTypedCommand.ExecuteAsync();
            opens.Should().Be(1);
            window.Should().NotBeNull();
            var model = (SessionsViewModel)window!.DataContext!;
            model.CanConfirmExpiredRequeue.Should().BeTrue();
            model.SelectedSessionRecord!.Authority.SessionId.Should().Be(f.Request.SessionId);
            host.Main.ResponseTitle.Should().Be("Queue replacement review opened.");
            controller.OpenCompact();
            window.Close();
            model.CanConfirmExpiredRequeue.Should().BeFalse();
            f.Count("host_tasks").Should().Be(3);
            action.Reads.Should().Be(0);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReplacementPreparationDisclosesFullNativeReviewWithoutAnyDurableMutation(bool paused)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteSessionWorkspaceTests.FinishAsync(f, HostTaskState.Succeeded);
        var access = new WindowsSqliteSessionWorkspaceTests.Access();
        var action = new NeverAction();
        await using var queue = Queue(f, access, action);
        var model = Model(f, access, queue);
        await model.RefreshAsync();
        await model.SelectAsync(model.Sessions.Single());
        model.ChooseNoQueuePrerequisite();
        await model.EnqueueReadinessAsync();
        var entry = model.QueueRecords.Single();
        if (paused) { await model.PauseQueueAsync(); }
        f.Time.Now = entry.ExpiresAt;
        var before = await f.Store.ReadQueueAsync(f.Request.SessionId, f.Token);
        var audits = f.Count("security_audit_events");
        var tasks = f.Count("host_tasks");
        (await model.PrepareExactReplacementReviewAsync(new(f.Request.SessionId.Value, entry.Request.TaskId.Value),
            RequestOrigin.ActivatedVoice, () => true, () => true, f.Token)).Should().BeTrue();
        model.SelectedSessionRecord!.Authority.SessionId.Should().Be(f.Request.SessionId);
        model.CanConfirmExpiredRequeue.Should().BeTrue();
        model.ExpiredRequeueStatus.Should().Contain("Separate explicit confirmation")
            .And.Contain("REMOVE (history retained)").And.Contain("Cancelled").And.Contain("Removed")
            .And.Contain(entry.Request.RequestId.Value.ToString("D")).And.Contain(entry.Request.TaskId.Value.ToString("D"))
            .And.Contain(entry.ExpiresAt.ToString("O")).And.Contain("NEW same-profile tail request")
            .And.Contain("NEW profile").And.Contain("readiness").And.Contain("configuration revision")
            .And.Contain("run ").And.Contain("pause revision").And.Contain("resulting pending capacity 1/10")
            .And.Contain("Exact prerequisite none").And.Contain("existing dependents remain blocked");
        (await f.Store.ReadQueueAsync(f.Request.SessionId, f.Token)).Should().BeEquivalentTo(before);
        f.Count("security_audit_events").Should().Be(audits);
        f.Count("host_tasks").Should().Be(tasks);
        action.Reads.Should().Be(0);
        await model.DiscardExpiredRequeueAsync();
        model.CanConfirmExpiredRequeue.Should().BeFalse();
        model.CanDiscardExpiredRequeue.Should().BeFalse();
        (await f.Store.ReadQueueAsync(f.Request.SessionId, f.Token)).Should().BeEquivalentTo(before);
        f.Count("host_tasks").Should().Be(tasks);
        f.Count("security_audit_events").Should().Be(audits);
        model.Close();
    }

    [Theory]
    [InlineData("pending")]
    [InlineData("missing-task")]
    [InlineData("missing-session")]
    [InlineData("admission")]
    [InlineData("native")]
    [InlineData("cancel")]
    [InlineData("control")]
    [InlineData("held")]
    [InlineData("oldrun")]
    [InlineData("task-mismatch")]
    [InlineData("dependency")]
    public async Task ReplacementPreparationRefusesIneligibleSourcesWithoutLatestOrSelectedFallback(string failure)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteSessionWorkspaceTests.FinishAsync(f, HostTaskState.Succeeded);
        var access = new WindowsSqliteSessionWorkspaceTests.Access();
        var action = new NeverAction();
        await using var queue = Queue(f, access, action);
        var model = Model(f, access, queue);
        await model.RefreshAsync();
        await model.SelectAsync(model.Sessions.Single());
        model.ChooseNoQueuePrerequisite();
        await model.EnqueueVersionAsync();
        var entry = model.QueueRecords.Single();
        if (failure is not "pending") { f.Time.Now = entry.ExpiresAt; }
        if (failure is "held")
        {
            await model.PauseQueueAsync();
            var held = (await f.Store.ReadWorkAsync(f.Request.SessionId, access.ControlRevision, new(), f.Token)).Queue;
            await f.Store.HoldDispatchControlAsync(held, f.Token);
        }
        if (failure is "oldrun" or "task-mismatch" or "dependency")
        {
            using var connection = f.OpenRaw();
            using var command = connection.CreateCommand();
            command.CommandText = failure switch
            {
                "oldrun" => "UPDATE session_queue SET payload=json_set(payload,'$.RunId',$value);",
                "task-mismatch" => "UPDATE host_tasks SET state=2 WHERE task_id=$value;",
                _ => "UPDATE session_queue SET payload=json_set(payload,'$.Dependency',json_object('Value',$value));",
            };
            command.Parameters.AddWithValue("$value", failure is "oldrun" or "dependency"
                ? Guid.NewGuid().ToString("D") : entry.Request.TaskId.Value.ToString("D"));
            command.ExecuteNonQuery();
        }
        var before = f.Count("host_tasks");
        var audits = f.Count("security_audit_events");
        var originalSelection = model.SelectedSessionRecord;
        if (failure is "control") { access.CanControl = false; }
        using var cancellation = new CancellationTokenSource();
        if (failure is "cancel") { cancellation.Cancel(); }
        var act = () => model.PrepareExactReplacementReviewAsync(new(failure is "missing-session" ? Guid.NewGuid() : f.Request.SessionId.Value,
            failure is "missing-task" ? Guid.NewGuid() : entry.Request.TaskId.Value), RequestOrigin.LocalUi,
            () => failure is not "admission", () => failure is not "native", cancellation.Token);
        try { (await act()).Should().BeFalse(); }
        catch (Exception exception) when (exception is InvalidOperationException or OperationCanceledException or System.IO.InvalidDataException) { }
        model.CanConfirmExpiredRequeue.Should().BeFalse();
        f.Count("host_tasks").Should().Be(before);
        f.Count("security_audit_events").Should().Be(audits);
        action.Reads.Should().Be(0);
        if (failure is not ("dependency" or "task-mismatch")) { model.SelectedSessionRecord.Should().Be(originalSelection); }
        model.Close();
    }

    [Theory]
    [InlineData("control")]
    [InlineData("buffer")]
    [InlineData("source")]
    [InlineData("selection")]
    [InlineData("native")]
    [InlineData("input")]
    public async Task PreparedReplacementCannotConfirmAfterItsOriginalFenceChanges(string change)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteSessionWorkspaceTests.FinishAsync(f, HostTaskState.Succeeded);
        var access = new WindowsSqliteSessionWorkspaceTests.Access();
        var action = new NeverAction();
        await using var queue = Queue(f, access, action);
        var model = Model(f, access, queue);
        await model.RefreshAsync();
        await model.SelectAsync(model.Sessions.Single());
        model.ChooseNoQueuePrerequisite();
        await model.EnqueueVersionAsync();
        var entry = model.QueueRecords.Single();
        f.Time.Now = entry.ExpiresAt;
        var native = true;
        var input = true;
        (await model.PrepareExactReplacementReviewAsync(new(f.Request.SessionId.Value, entry.Request.TaskId.Value),
            RequestOrigin.LocalUi, () => input, () => native, f.Token)).Should().BeTrue();
        switch (change)
        {
            case "control": access.ControlRevision++; break;
            case "buffer": model.ComposerBody = "new private draft"; break;
            case "source": await model.RefreshWorkAsync(); break;
            case "selection": model.SelectQueueEntry(null); break;
            case "native": native = false; break;
            default: input = false; break;
        }
        var tasks = f.Count("host_tasks");
        var audits = f.Count("security_audit_events");
        model.CanConfirmExpiredRequeue.Should().BeFalse();
        await model.ConfirmExpiredRequeueAsync();
        f.Count("host_tasks").Should().Be(tasks);
        f.Count("security_audit_events").Should().Be(audits);
        action.Reads.Should().Be(0);
        model.Close();
    }
}
