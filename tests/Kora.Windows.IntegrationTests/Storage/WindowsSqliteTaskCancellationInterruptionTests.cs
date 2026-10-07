using System.Diagnostics;

using AwesomeAssertions;

using Kora.Application.Hosting;
using Kora.Application.Interaction;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.Windows.Storage;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests.Storage;

[Collection(nameof(DurableStorageCompositionTestGroup))]
public sealed class WindowsSqliteTaskCancellationInterruptionTests
{
    private const string RootVariable = "KORA_TASK_CANCEL_CHILD_ROOT";
    private const string ModeVariable = "KORA_TASK_CANCEL_CHILD_MODE";
    private const string Marker = "task-cancel-child-ready";
    private const string Identity = "task-cancel-child-identity";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Owned_process_interruption_reopens_one_task_question_audit_truth_without_replay(bool committed)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        var start = OwnedStorageChildProcess.CreateStart(typeof(WindowsSqliteTaskCancellationInterruptionTests),
            nameof(Fixture_owned_child_cancel_only));
        start.Environment[RootVariable] = f.Paths.LocalRoot;
        start.Environment[ModeVariable] = committed ? "after" : "before";
        using var child = Process.Start(start) ?? throw new InvalidOperationException("The owned cancellation child did not start.");
        var output = child.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var error = child.StandardError.ReadToEndAsync(CancellationToken.None);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(f.Token);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            while (!File.Exists(Path.Combine(f.Paths.LocalRoot, Marker)))
            {
                if (child.HasExited) { throw new InvalidOperationException($"Owned cancellation child failed: {await output} {await error}"); }
                await Task.Delay(TimeSpan.FromMilliseconds(25), deadline.Token);
            }
            child.Kill(entireProcessTree: true);
            await child.WaitForExitAsync(deadline.Token);
            await OwnedStorageChildProcess.WaitForReleasedDatabaseAsync(f.Paths.LocalRoot, f.DatabasePath, deadline.Token);
            var taskId = new HostId<TaskIdentity>(Guid.ParseExact(await File.ReadAllTextAsync(
                Path.Combine(f.Paths.LocalRoot, Identity), deadline.Token), "D"));
            f.Reopen();
            await f.Store.InitializeAsync(f.Token);
            var observed = (await f.Store.ReadTaskAsync(f.Request.SessionId, taskId, f.Token))!;
            observed.Task.State.Should().Be(committed ? HostTaskState.Cancelled : HostTaskState.IntentRecorded);
            observed.Question!.Status.Should().Be(committed ? QuestionStatus.Cancelled : QuestionStatus.Pending);
            observed.Task.Revision.Value.Should().Be(committed ? 2 : 1);
            observed.Question.Key.Revision.Value.Should().Be(committed ? 2 : 1);
            observed.CurrentSource.Should().BeFalse();
            var audits = f.Count("security_audit_events");
            var recovered = await new HostTaskCoordinator(f.Tasks).RecoverAsync(10, f.Token);
            if (committed) { recovered.Should().NotContain(task => task.Request.TaskId == taskId); }
            else { recovered.Single(task => task.Request.TaskId == taskId).State.Should().Be(HostTaskState.Interrupted); }
            f.Count("security_audit_events").Should().Be(audits);
            (await new HostTaskCoordinator(f.Tasks).RecoverAsync(10, f.Token)).Should().BeEmpty();
        }
        finally
        {
            if (!child.HasExited)
            {
                child.Kill(entireProcessTree: true);
                await child.WaitForExitAsync(CancellationToken.None);
            }
        }
    }

    [Fact]
    public async Task Fixture_owned_child_cancel_only()
    {
        var root = Environment.GetEnvironmentVariable(RootVariable);
        if (root is null) { return; }
        OwnedStorageChildProcess.RequireOwnedRoot(root);
        var mode = Environment.GetEnvironmentVariable(ModeVariable);
        if (mode is not ("before" or "after")) { throw new InvalidOperationException("Unknown cancellation fixture mode."); }
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
        };
        ActivitySource.AddActivityListener(listener);
        var tasks = new WindowsSqliteHostTaskStore(new ExistingPaths(root));
        var checkpoint = new InteractionTransactionCheckpoint();
        var time = new InteractionStorageFixture.Clock();
        var store = new WindowsSqliteHostInteractionStore(new ExistingPaths(root), tasks, time, checkpoint);
        var token = TestContext.Current.CancellationToken;
        await store.InitializeAsync(token);
        var session = (await store.ReadSessionsAsync(null, 1, token)).Records.Single();
        var request = InteractionStorageFixture.NewRequest(session.SessionId);
        HostQuestionRecord question;
        using (var host = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request))
        {
            await new HostTaskCoordinator(tasks).RecordIntentAsync(request, token);
            await store.PublishTrustedSnapshotAsync(request, new(true, false, false, true), null, 0, token);
            question = (await new HostQuestionService(store, time).CreateAsync(request, LocalVersionWait.CreateSpec(),
                time.Now.AddMinutes(5), token)).Question!;
            await store.AdmitVersionWaitAsync(question.Key, token);
        }
        await File.WriteAllTextAsync(Path.Combine(root, Identity), request.TaskId.Value.ToString("D"), token);
        if (mode is "before") { checkpoint.Commit = (_, _) => OwnedStorageChildProcess.SignalAndBlock(root, Marker); }
        var service = new SessionWorkspaceService(store, new(tasks), new WindowsSqliteSessionWorkspaceTests.Access(),
            NullLogger<SessionWorkspaceService>.Instance);
        await service.CancelTaskAsync(new(request.SessionId, request.TaskId, new(1), session.Generation,
            question.Key.QuestionId, question.Key.Revision), RequestOrigin.LocalUi, () => true, token);
        OwnedStorageChildProcess.SignalAndBlock(root, Marker);
    }

    private sealed class ExistingPaths(string root) : IApplicationDataPaths
    {
        public string LocalRoot => root;
        public string RoamingRoot => Path.Combine(root, "UnusedRoaming");
    }
}
