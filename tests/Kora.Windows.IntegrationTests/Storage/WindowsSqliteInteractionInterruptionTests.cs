using System.Diagnostics;

using AwesomeAssertions;

using Kora.Application.Interaction;
using Kora.Core.Authorization;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.Windows.IntegrationTests.Audio;
using Kora.Windows.Storage;

namespace Kora.Windows.IntegrationTests.Storage;

[Collection(nameof(DurableStorageCompositionTestGroup))]
public sealed class WindowsSqliteInteractionInterruptionTests
{
    private const string RootVariable = "KORA_INTERACTION_CHILD_ROOT";
    private const string ModeVariable = "KORA_INTERACTION_CHILD_MODE";

    [Theory]
    [InlineData("uncommitted")]
    [InlineData("committed")]
    [InlineData("consume-uncommitted")]
    [InlineData("consume-committed")]
    [InlineData("done-uncommitted")]
    [InlineData("done-committed")]
    public async Task Owned_child_kill_reopens_atomic_question_grant_audit_and_preserves_active_generation_without_replay(string mode)
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.InitializeAsync();
        var question = await fixture.PresentAsync();
        if (mode.StartsWith("consume-", StringComparison.Ordinal) || mode.StartsWith("done-", StringComparison.Ordinal))
        {
            (await fixture.RunAsync(() => fixture.Authorization.ApproveAsync(question.Key,
                new([mode.StartsWith("done-", StringComparison.Ordinal) ? "session" : "once"]),
                RequestOrigin.LocalUi, fixture.Token))).Outcome.Should().Be(HostInteractionOutcome.Approved);
        }
        HostQuestionRecord? pending = null;
        if (mode.StartsWith("done-", StringComparison.Ordinal))
        {
            pending = (await fixture.RunAsync(() => fixture.Questions.CreateAsync(fixture.Request,
                new("Owned pending question", QuestionKind.Text, [], maximumTextLength: 32),
                fixture.Time.Now.AddMinutes(5), fixture.Token))).Question!;
        }
        var initialAudits = fixture.Count("security_audit_events");
        var committed = mode is "committed" or "consume-committed" or "done-committed";
        var journal = fixture.DatabasePath + "-journal";
        var acl = new FileInfo(journal).GetAccessControl().GetSecurityDescriptorBinaryForm();
        var marker = Path.Combine(fixture.Paths.LocalRoot, "interaction-child-ready");
        var start = OwnedStorageChildProcess.CreateStart(typeof(WindowsSqliteInteractionInterruptionTests),
            nameof(Fixture_owned_child_interaction_only));
        start.Environment[RootVariable] = fixture.Paths.LocalRoot;
        start.Environment[ModeVariable] = mode;
        using var child = Process.Start(start) ?? throw new InvalidOperationException("The owned interaction child did not start.");
        var output = child.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var error = child.StandardError.ReadToEndAsync(CancellationToken.None);
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(fixture.Token);
            deadline.CancelAfter(TimeSpan.FromSeconds(30));
            while (!File.Exists(marker))
            {
                if (child.HasExited)
                {
                    throw new InvalidOperationException($"The scratch child failed before the checkpoint: {await output} {await error}");
                }
                await Task.Delay(TimeSpan.FromMilliseconds(25), deadline.Token);
            }
            if (!committed)
            {
                OwnedStorageChildProcess.AssertHotJournal(journal);
                var taskLease = () =>
                {
                    using var lease = new FileStream(Path.Combine(fixture.Paths.LocalRoot, "HostStorageV1", "operation.lock"),
                        FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                };
                taskLease.Should().Throw<IOException>();
            }
            child.Kill(entireProcessTree: true);
            await child.WaitForExitAsync(deadline.Token);
            await OwnedStorageChildProcess.WaitForReleasedDatabaseAsync(fixture.Paths.LocalRoot, fixture.DatabasePath, deadline.Token);
            fixture.Reopen();
            await fixture.Store.InitializeAsync(fixture.Token);
            new FileInfo(journal).GetAccessControl().GetSecurityDescriptorBinaryForm().Should().Equal(acl);
            var session = (await fixture.Store.ReadSessionAsync(fixture.Request.SessionId, fixture.Token))!;
            session.Generation.Value.Should().Be(string.Equals(mode, "done-committed", StringComparison.Ordinal) ? 2 : 1);
            session.IsActive.Should().Be(!string.Equals(mode, "done-committed", StringComparison.Ordinal));
            var storedQuestions = await fixture.Store.ReadQuestionsAsync(fixture.Request.SessionId, fixture.Token);
            var stored = storedQuestions.Single(q => q.Key.QuestionId == question.Key.QuestionId);
            var grants = await fixture.Store.ReadGrantsAsync(fixture.Token);
            fixture.Count("security_audit_events").Should().Be(initialAudits + 1 + (committed ? 1 : 0));
            if (mode.StartsWith("consume-", StringComparison.Ordinal))
            {
                stored.Status.Should().Be(QuestionStatus.Answered);
                grants.Should().ContainSingle().Which.UseCount.Should().Be(committed ? 1 : 0);
                grants.Single().Status.Should().Be(committed ? OperationGrantStatus.Consumed : OperationGrantStatus.Active);
            }
            else if (mode.StartsWith("done-", StringComparison.Ordinal))
            {
                stored.Status.Should().Be(QuestionStatus.Answered);
                grants.Should().ContainSingle().Which.Status.Should().Be(committed
                    ? OperationGrantStatus.Revoked : OperationGrantStatus.Active);
                var savedPending = storedQuestions.Single(q => q.Key.QuestionId == pending!.Key.QuestionId);
                savedPending.Status.Should().Be(committed ? QuestionStatus.Cancelled : QuestionStatus.Pending);
                savedPending.Key.Revision.Value.Should().Be(committed ? 2 : 1);
                if (committed)
                {
                    var stale = () => fixture.RunAsync(() => fixture.Store.SetSessionLifecycleAsync(fixture.Request,
                        new(1), active: true, remove: false, fixture.Token));
                    await stale.Should().ThrowAsync<InvalidOperationException>();
                    (await fixture.RunAsync(() => fixture.Store.SetSessionLifecycleAsync(fixture.Request,
                        new(2), active: true, remove: false, fixture.Token))).Generation.Value.Should().Be(3);
                    await fixture.PublishAsyncAfterLifecycle();
                    (await fixture.RunAsync(() => fixture.Questions.SubmitAsync(pending!.Key, new([], "stale"),
                        RequestOrigin.LocalUi, fixture.Token))).Outcome.Should().Be(HostInteractionOutcome.Conflict);
                }
            }
            else if (committed)
            {
                stored.Status.Should().Be(QuestionStatus.Answered);
                grants.Should().ContainSingle();
                var grant = grants.Single();
                (await fixture.RunAsync(() => fixture.Authorization.ConsumeAsync(fixture.Request, grant.Id, grant.Revision, fixture.Token)))
                    .Outcome.Should().Be(HostInteractionOutcome.Denied);
                (await fixture.Store.ReadGrantsAsync(fixture.Token)).Single().UseCount.Should().Be(0);
            }
            else
            {
                stored.Key.Should().Be(question.Key);
                stored.Status.Should().Be(QuestionStatus.Pending);
                grants.Should().BeEmpty();
            }
            (await fixture.Tasks.ReadTaskAsync(fixture.Request.TaskId, fixture.Token))!.State.Should().Be(HostTaskState.IntentRecorded);
            var questionsBeforeRecovery = await fixture.Store.ReadQuestionsAsync(fixture.Request.SessionId, fixture.Token);
            var grantsBeforeRecovery = await fixture.Store.ReadGrantsAsync(fixture.Token);
            var auditsBeforeRecovery = fixture.Count("security_audit_events");
            using var recovery = new StorageRecoveryFixture(fixture.Paths, fixture.Tasks);
            (await recovery.Recovery.RecoverAsync(fixture.Token)).Should().ContainSingle()
                .Which.State.Should().Be(HostTaskState.Interrupted);
            (await recovery.Recovery.RecoverAsync(fixture.Token)).Should().BeEmpty();
            recovery.Gaps.Should().BeEmpty();
            fixture.Reopen();
            (await fixture.Store.ReadQuestionsAsync(fixture.Request.SessionId, fixture.Token)).Should()
                .BeEquivalentTo(questionsBeforeRecovery, options => options.WithStrictOrdering());
            (await fixture.Store.ReadGrantsAsync(fixture.Token)).Should()
                .BeEquivalentTo(grantsBeforeRecovery, options => options.WithStrictOrdering());
            var lateDecision = () => fixture.RunAsync(() => fixture.Authorization.PresentAsync(fixture.Request, fixture.Token));
            await lateDecision.Should().ThrowAsync<InvalidDataException>();
            fixture.Count("security_audit_events").Should().Be(auditsBeforeRecovery);
        }
        finally
        {
            if (!child.HasExited)
            {
                child.Kill(entireProcessTree: true);
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await child.WaitForExitAsync(cleanup.Token);
            }
            await Task.WhenAll(output, error);
        }
    }

    [WindowsFact]
    public async Task Fixture_owned_child_interaction_only()
    {
        var root = Environment.GetEnvironmentVariable(RootVariable);
        if (root is null)
        {
            return;
        }
        OwnedStorageChildProcess.RequireOwnedRoot(root);
        var mode = Environment.GetEnvironmentVariable(ModeVariable);
        if (mode is not ("uncommitted" or "committed" or "consume-uncommitted"
            or "consume-committed" or "done-uncommitted" or "done-committed"))
        {
            throw new InvalidOperationException("The scratch child interaction mode is invalid.");
        }
        var paths = new ExistingPaths(root);
        var tasks = new WindowsSqliteHostTaskStore(paths);
        var request = (await tasks.ReadIncompleteAsync(2, TestContext.Current.CancellationToken)).Single().Request;
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
        };
        ActivitySource.AddActivityListener(listener);
        var checkpoint = new InteractionTransactionCheckpoint();
        var time = new InteractionStorageFixture.Clock();
        var store = new WindowsSqliteHostInteractionStore(paths, tasks, time, checkpoint);
        var question = (await store.ReadQuestionsAsync(request.SessionId, TestContext.Current.CancellationToken))
            .Single(q => q.Proposal is not null);
        var proposal = question.Proposal ?? throw new InvalidDataException("The owned child approval proposal is missing.");
        using var host = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request);
        await store.PublishTrustedSnapshotAsync(request, new HostAuthorizationPolicy(true, true, false, true), proposal,
            1, TestContext.Current.CancellationToken);
        if (mode is "uncommitted" or "consume-uncommitted" or "done-uncommitted")
        {
            checkpoint.Audit = (connection, transaction) =>
            {
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = "PRAGMA cache_size=1; PRAGMA cache_spill=ON;";
                command.ExecuteNonQuery();
            };
            checkpoint.Commit = (_, _) =>
            {
                OwnedStorageChildProcess.SignalAndBlock(root, "interaction-child-ready");
            };
        }
        var authorization = new HostAuthorizationService(store, time);
        if (mode.StartsWith("consume-", StringComparison.Ordinal))
        {
            var grant = (await store.ReadGrantsAsync(TestContext.Current.CancellationToken)).Single();
            (await authorization.ConsumeAsync(request, grant.Id, grant.Revision, TestContext.Current.CancellationToken))
                .Outcome.Should().Be(HostInteractionOutcome.Consumed);
        }
        else if (mode.StartsWith("done-", StringComparison.Ordinal))
        {
            (await store.SetSessionLifecycleAsync(request, new(1), active: false, remove: false,
                TestContext.Current.CancellationToken)).Generation.Value.Should().Be(2);
        }
        else
        {
            var approved = await authorization.ApproveAsync(question.Key, new(["once"]), RequestOrigin.LocalUi,
                TestContext.Current.CancellationToken);
            approved.Outcome.Should().Be(HostInteractionOutcome.Approved);
        }
        await File.WriteAllTextAsync(Path.Combine(root, "interaction-child-ready"), "owned committed interaction checkpoint",
            TestContext.Current.CancellationToken);
        await Task.Delay(Timeout.InfiniteTimeSpan, TestContext.Current.CancellationToken);
    }

    private sealed class ExistingPaths(string root) : IApplicationDataPaths
    {
        public string LocalRoot => root;
        public string RoamingRoot => Path.Combine(root, "UnusedRoaming");
    }
}
