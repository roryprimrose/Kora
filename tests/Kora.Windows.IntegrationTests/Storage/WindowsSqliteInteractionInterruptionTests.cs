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
    public async Task Owned_child_kill_reopens_atomic_question_grant_audit_and_preserves_active_generation_without_replay(string mode)
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.InitializeAsync();
        var question = await fixture.PresentAsync();
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
            if (string.Equals(mode, "uncommitted", StringComparison.Ordinal))
            {
                new FileInfo(journal).Length.Should().BeGreaterThan(512, "actual staged writes must have a hot rollback journal");
            }
            child.Kill(entireProcessTree: true);
            await child.WaitForExitAsync(deadline.Token);
            fixture.Reopen();
            await fixture.Store.InitializeAsync(fixture.Token);
            new FileInfo(journal).GetAccessControl().GetSecurityDescriptorBinaryForm().Should().Equal(acl);
            (await fixture.Store.ReadSessionAsync(fixture.Request.SessionId, fixture.Token))!.Generation.Value.Should().Be(1);
            var stored = (await fixture.Store.ReadQuestionsAsync(fixture.Request.SessionId, fixture.Token)).Single();
            var grants = await fixture.Store.ReadGrantsAsync(fixture.Token);
            fixture.Count("security_audit_events").Should().Be(string.Equals(mode, "committed", StringComparison.Ordinal) ? 5 : 4);
            if (string.Equals(mode, "committed", StringComparison.Ordinal))
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
        if (mode is not ("uncommitted" or "committed"))
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
        var question = (await store.ReadQuestionsAsync(request.SessionId, TestContext.Current.CancellationToken)).Single();
        var proposal = question.Proposal ?? throw new InvalidDataException("The owned child approval proposal is missing.");
        using var host = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request);
        await store.PublishTrustedSnapshotAsync(request, new HostAuthorizationPolicy(true, true, false, true), proposal,
            1, TestContext.Current.CancellationToken);
        if (string.Equals(mode, "uncommitted", StringComparison.Ordinal))
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
                File.WriteAllText(Path.Combine(root, "interaction-child-ready"), "owned uncommitted interaction checkpoint");
                using var wait = new ManualResetEventSlim();
                if (!wait.Wait(TimeSpan.FromMinutes(2), TestContext.Current.CancellationToken))
                {
                    throw new TimeoutException("The parent failed to terminate its owned child.");
                }
            };
        }
        var authorization = new HostAuthorizationService(store, time);
        var approved = await authorization.ApproveAsync(question.Key, new(["once"]), RequestOrigin.LocalUi,
            TestContext.Current.CancellationToken);
        approved.Outcome.Should().Be(HostInteractionOutcome.Approved);
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
