using System.Collections.Concurrent;
using System.Diagnostics;

using AwesomeAssertions;

using Kora.Application.Hosting;
using Kora.Application.UnitTests.Configuration;
using Kora.Core.Commands;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Application.UnitTests.Hosting;

public sealed partial class SessionWorkspaceServiceTests
{
    private static readonly SessionQueueReadProfile[] AllDiagnostics =
    [
        SessionQueueReadProfile.Version, SessionQueueReadProfile.Capabilities,
        SessionQueueReadProfile.Readiness, SessionQueueReadProfile.Runtimes,
    ];

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DiagnosticsExactSelectionReviewsWithoutWritesAndConfirmsExistingEnqueuesOnlyInFixedOrder(bool all)
    {
        using var f = new Fixture();
        await using var queue = f.QueueService();
        var service = new SessionWorkspaceService(f, new(f), f, f.Logger, queue);
        SessionQueueReadProfile[] chosen = all ? AllDiagnostics.Reverse().ToArray()
            : [SessionQueueReadProfile.Runtimes, SessionQueueReadProfile.Capabilities];
        var review = await service.ReviewDiagnosticsPlanAsync(await f.ReadQueueAsync(f.Request.SessionId, f.Token),
            chosen, SessionDiagnosticsPlanPrerequisite.None, () => true, f.Token);
        review.Profiles.Should().Equal(chosen.OrderBy(profile => profile));
        review.ExistingPending.Should().Be(0);
        review.ResultingPending.Should().Be(chosen.Length);
        review.Queue.DispatchControl!.RunId.Should().Be(f.QueueRun);
        review.AdmissionRevision.Should().Be(f.ControlRevision);
        f.TaskWrites.Should().BeEmpty();
        f.QueueRows.Should().BeEmpty();
        var result = await service.ConfirmDiagnosticsPlanAsync(review, () => true, f.Token);
        result.StoppedBy.Should().BeNull();
        result.Items.Should().OnlyContain(item => item.State == SessionDiagnosticsPlanItemState.Accepted);
        result.Items.Select(item => item.Profile).Should().Equal(review.Profiles);
        result.Items.Select(item => item.Request!.TaskId).Distinct().Should().HaveCount(chosen.Length);
        result.Items.Select(item => item.Request!.RequestId).Distinct().Should().HaveCount(chosen.Length);
        result.Items.Should().OnlyContain(item => item.CommittedEntry != null && item.CommittedQueueRevision > 0);
        f.QueueRows.Should().OnlyContain(row => row.Dependency == null && row.Target == null && row.IsPending
            && row.Request != f.Request && row.Request.SessionId == f.Request.SessionId);
        f.TaskWrites.Should().HaveCount(chosen.Length * 2);
        f.TaskWrites.Should().OnlyContain(task => task.Request.Origin == RequestOrigin.LocalUi);
        f.QueueActions.Should().Be(0);
        var repeat = () => service.ConfirmDiagnosticsPlanAsync(review, () => true, f.Token);
        await repeat.Should().ThrowAsync<InvalidOperationException>();
        f.QueueRows.Should().HaveCount(chosen.Length);
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("duplicate")]
    [InlineData("targeted")]
    [InlineData("runtime-status")]
    [InlineData("unknown")]
    [InlineData("too-many")]
    [InlineData("prerequisite")]
    public async Task CompleteInvalidDiagnosticsSelectionRejectsBeforeAnyReadOrEffect(string kind)
    {
        using var f = new Fixture();
        await using var queue = f.QueueService();
        var expected = await f.ReadQueueAsync(f.Request.SessionId, f.Token);
        SessionQueueReadProfile[] profiles = kind switch
        {
            "empty" => [],
            "duplicate" => [SessionQueueReadProfile.Version, SessionQueueReadProfile.Version],
            "targeted" => [SessionQueueReadProfile.Version, SessionQueueReadProfile.Capability],
            "runtime-status" => [SessionQueueReadProfile.Version, SessionQueueReadProfile.RuntimeStatus],
            "unknown" => [SessionQueueReadProfile.Version, (SessionQueueReadProfile)99],
            "too-many" => [.. AllDiagnostics, SessionQueueReadProfile.Version],
            _ => AllDiagnostics,
        };
        f.QueueFailure = "read";
        var review = () => queue.ReviewDiagnosticsPlanAsync(expected, profiles,
            kind == "prerequisite" ? (SessionDiagnosticsPlanPrerequisite)99 : SessionDiagnosticsPlanPrerequisite.None,
            RequestOrigin.LocalUi, () => true, f.Token);
        await review.Should().ThrowAsync<InvalidDataException>();
        f.TaskWrites.Should().BeEmpty();
        f.QueueRows.Should().BeEmpty();
    }

    [Theory]
    [InlineData(6, true)]
    [InlineData(7, false)]
    public async Task DiagnosticsEntirePlanFitsExactCapacityOrRejectsWithoutTrimming(int pending, bool fits)
    {
        using var f = new Fixture();
        await using var queue = f.QueueService();
        for (var index = 0; index < pending; index++)
        {
            await queue.ExecuteCommandAsync(f.QueueCommand(SessionCommandOperation.QueueEnqueue) with { QueueRevision = f.QueueRevision },
                RequestOrigin.LocalUi, () => true, f.Token);
        }
        var before = f.QueueRows.ToArray();
        var writes = f.TaskWrites.Count;
        var review = async () => await queue.ReviewDiagnosticsPlanAsync(await f.ReadQueueAsync(f.Request.SessionId, f.Token),
            AllDiagnostics, SessionDiagnosticsPlanPrerequisite.None, RequestOrigin.LocalUi, () => true, f.Token);
        if (fits)
        {
            var preview = await review();
            preview.ResultingPending.Should().Be(10);
            (await queue.ConfirmDiagnosticsPlanAsync(preview, RequestOrigin.LocalUi, () => true, f.Token))
                .Items.Should().OnlyContain(item => item.State == SessionDiagnosticsPlanItemState.Accepted);
            f.QueueRows.Should().HaveCount(10);
        }
        else
        {
            await review.Should().ThrowAsync<InvalidOperationException>();
            f.QueueRows.Should().Equal(before);
            f.TaskWrites.Should().HaveCount(writes);
        }
        f.QueueRows.Take(pending).Should().Equal(before);
        f.QueueActions.Should().Be(0);
    }

    [Theory]
    [InlineData("queue")]
    [InlineData("control")]
    [InlineData("generation")]
    [InlineData("done")]
    [InlineData("foreign")]
    [InlineData("later-malformed")]
    [InlineData("later-foreign")]
    [InlineData("held")]
    public async Task DiagnosticsFullConfirmationPreflightRejectsChangedOrInvalidLaterStateBeforeFirstEnqueue(string change)
    {
        using var f = new Fixture();
        await using var queue = f.QueueService();
        for (var index = 0; index < 2; index++)
        {
            await queue.ExecuteCommandAsync(f.QueueCommand(SessionCommandOperation.QueueEnqueue), RequestOrigin.LocalUi, () => true, f.Token);
        }
        var review = await DiagnosticsReview(f, queue);
        var before = f.QueueRows.ToArray();
        var writes = f.TaskWrites.Count;
        f.QueueReadProjection = snapshot => change switch
        {
            "queue" => snapshot with { Revision = snapshot.Revision + 1 },
            "control" => snapshot with { DispatchControl = snapshot.DispatchControl! with { Revision = 1 } },
            "generation" => snapshot with { Generation = new(2) },
            "held" => snapshot with { DispatchControl = snapshot.DispatchControl! with { Held = true } },
            "later-malformed" => snapshot with { Entries = [snapshot.Entries[0], snapshot.Entries[1] with { ExpiresAt = DateTimeOffset.MinValue }] },
            "later-foreign" => snapshot with { Entries = [snapshot.Entries[0], snapshot.Entries[1] with
            { Request = snapshot.Entries[1].Request with { SessionId = new(Guid.NewGuid()) } }] },
            _ => snapshot,
        };
        if (change is "done" or "foreign")
        {
            f.ExactListReader = _ => new(change == "done" ? f.Session with { IsActive = false }
                : f.Session with { SessionId = new(Guid.NewGuid()) }, null);
        }
        var result = await queue.ConfirmDiagnosticsPlanAsync(review, RequestOrigin.LocalUi, () => true, f.Token);
        result.Items[0].State.Should().BeOneOf(SessionDiagnosticsPlanItemState.Conflict, SessionDiagnosticsPlanItemState.Unavailable);
        result.Items.Skip(1).Should().OnlyContain(item => item.State == SessionDiagnosticsPlanItemState.NotAttempted);
        f.QueueRows.Should().Equal(before);
        f.TaskWrites.Should().HaveCount(writes);
    }

    [Theory]
    [InlineData("revision")]
    [InlineData("private")]
    [InlineData("readback")]
    [InlineData("cancel")]
    public async Task DiagnosticsStopsAfterFirstAcceptedItemAndNeverRetriesOrRollsBack(string change)
    {
        using var f = new Fixture();
        using var cancellation = new CancellationTokenSource();
        await using var queue = f.QueueService();
        var review = await DiagnosticsReview(f, queue);
        var calls = 0;
        f.AfterQueueRead = () =>
        {
            if (f.QueueRows.Count != 1 || calls++ != 0) { return; }
            switch (change)
            {
                case "revision": f.AdvanceQueueRevision(); break;
                case "private": f.AdvanceRevision(); break;
                case "readback": f.QueueFailure = "read"; throw new IOException("readback unavailable");
                case "cancel": cancellation.Cancel(); break;
            }
        };
        var result = await queue.ConfirmDiagnosticsPlanAsync(review, RequestOrigin.LocalUi, () => true, cancellation.Token);
        result.Items[0].State.Should().Be(SessionDiagnosticsPlanItemState.Accepted);
        result.Items[1].State.Should().Be(change == "cancel" ? SessionDiagnosticsPlanItemState.Cancelled
            : change == "readback" ? SessionDiagnosticsPlanItemState.Unavailable : SessionDiagnosticsPlanItemState.Conflict);
        result.Items.Skip(2).Should().OnlyContain(item => item.State == SessionDiagnosticsPlanItemState.NotAttempted);
        f.QueueRows.Should().ContainSingle().Which.Should().Be(result.Items[0].CommittedEntry);
        f.QueueActions.Should().Be(0);
        var repeat = () => queue.ConfirmDiagnosticsPlanAsync(review, RequestOrigin.LocalUi, () => true, f.Token);
        await repeat.Should().ThrowAsync<InvalidOperationException>();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DiagnosticsFirstAttemptAuditOrTerminalReceiptUncertaintyPreservesFreshIdsAndStops(bool afterCommit)
    {
        using var f = new Fixture { Failure = afterCommit ? "receipt" : null };
        await using var queue = f.QueueService();
        var review = await DiagnosticsReview(f, queue);
        if (!afterCommit) { f.QueueFailure = "enqueue"; }
        var result = await queue.ConfirmDiagnosticsPlanAsync(review, RequestOrigin.LocalUi, () => true, f.Token);
        result.StoppedBy.Should().Be(SessionDiagnosticsPlanItemState.Unavailable);
        result.Items[0].State.Should().Be(SessionDiagnosticsPlanItemState.Unavailable);
        result.Items[0].Request.Should().NotBeNull();
        result.Items.Skip(1).Should().OnlyContain(item => item.State == SessionDiagnosticsPlanItemState.NotAttempted && item.Request == null);
        if (afterCommit)
        {
            result.Items[0].CommittedEntry.Should().Be(f.QueueRows.Single());
            result.Items[0].CommittedQueueRevision.Should().Be(f.QueueRevision);
        }
        else { f.QueueRows.Should().BeEmpty(); }
    }

    [Theory]
    [InlineData("retire")]
    [InlineData("replace")]
    [InlineData("private")]
    [InlineData("original-cancel")]
    [InlineData("confirm-cancel")]
    [InlineData("admission")]
    [InlineData("dispose")]
    public async Task DiagnosticsOneShotReviewRetiresOnLifetimeAndPrivateAdmissionChanges(string kind)
    {
        using var f = new Fixture();
        using var original = new CancellationTokenSource();
        using var confirming = new CancellationTokenSource();
        await using var queue = f.QueueService();
        var admitted = true;
        var review = await queue.ReviewDiagnosticsPlanAsync(await f.ReadQueueAsync(f.Request.SessionId, f.Token),
            AllDiagnostics, SessionDiagnosticsPlanPrerequisite.None, RequestOrigin.LocalUi, () => admitted, original.Token);
        switch (kind)
        {
            case "retire": queue.RetireDiagnosticsPlan(review); break;
            case "replace": _ = await DiagnosticsReview(f, queue); break;
            case "private": f.AdvanceRevision(); break;
            case "original-cancel": original.Cancel(); break;
            case "confirm-cancel": confirming.Cancel(); break;
            case "admission": admitted = false; break;
            case "dispose": await queue.DisposeAsync(); break;
        }
        var confirm = () => queue.ConfirmDiagnosticsPlanAsync(review, RequestOrigin.LocalUi, () => true, confirming.Token);
        if (kind is "retire" or "replace" or "dispose") { await confirm.Should().ThrowAsync<InvalidOperationException>(); }
        else { (await confirm()).Items.Should().NotContain(item => item.State == SessionDiagnosticsPlanItemState.Accepted); }
        f.TaskWrites.Should().BeEmpty();
        f.QueueRows.Should().BeEmpty();
    }

    [Fact]
    public async Task DiagnosticsChangingLimitsAfterReviewRejectsBeforeAnyIntentAndDoesNotResizeSelection()
    {
        using var f = new Fixture();
        await using var settings = new SessionQueueConfigurationTestFixture();
        await settings.Refresh();
        await using var queue = f.QueueService(configuration: settings.Service);
        var review = await DiagnosticsReview(f, queue);
        await settings.Set("1", Kora.Core.Configuration.SessionQueueOption.PendingPerSession);
        var result = await queue.ConfirmDiagnosticsPlanAsync(review, RequestOrigin.LocalUi, () => true, f.Token);
        result.StoppedBy.Should().Be(SessionDiagnosticsPlanItemState.Conflict);
        result.Items.Should().HaveCount(4);
        f.TaskWrites.Should().BeEmpty();
        f.QueueRows.Should().BeEmpty();
    }

    [Fact]
    public async Task DiagnosticsControlRaceAtSingleEnqueueFenceIsUnavailableWithoutNewQueueItemOrDispatch()
    {
        using var f = new Fixture();
        await using var queue = f.QueueService();
        var review = await DiagnosticsReview(f, queue);
        f.BeforeEnqueue = _ =>
        {
            f.SetDiagnosticsControl(review.Queue.DispatchControl! with { Revision = 1, Paused = true });
            return Task.CompletedTask;
        };
        var result = await queue.ConfirmDiagnosticsPlanAsync(review, RequestOrigin.LocalUi, () => true, f.Token);
        result.Items[0].State.Should().Be(SessionDiagnosticsPlanItemState.Unavailable);
        f.QueueRows.Should().BeEmpty();
        f.QueueActions.Should().Be(0);
    }

    [Fact]
    public async Task DiagnosticsCannotRelabelSystemProvenanceAsNativeReview()
    {
        using var f = new Fixture();
        await using var queue = f.QueueService();
        using var activity = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.HostSystem),
            HostActivityLayer.Application, HostOperation.Request);
        var review = () => DiagnosticsReview(f, queue);
        await review.Should().ThrowAsync<InvalidOperationException>();
        f.TaskWrites.Should().BeEmpty();
    }

    [Theory]
    [InlineData("lost-readback")]
    [InlineData("database-failure")]
    [InlineData("late-cancel")]
    [InlineData("late-private")]
    [InlineData("missing-entry")]
    [InlineData("wrong-profile")]
    [InlineData("wrong-dependency")]
    [InlineData("malformed-entry")]
    [InlineData("foreign-generation")]
    [InlineData("foreign-run")]
    [InlineData("foreign-admission")]
    [InlineData("wrong-lifetime")]
    [InlineData("wrong-control-receipt")]
    public async Task DiagnosticsUncertainFirstCommitNeverClaimsAcceptedOrRetriesAndRetainsAvailableReceipt(string failure)
    {
        using var f = new Fixture();
        using var cancellation = new CancellationTokenSource();
        await using var queue = f.QueueService();
        var review = await DiagnosticsReview(f, queue);
        f.AfterDiagnosticsEnqueueCommit = () =>
        {
            if (failure == "lost-readback") { throw new IOException("The commit reply was lost."); }
            if (failure == "database-failure") { throw new System.Data.DataException("The database reply was unavailable."); }
            if (failure == "late-cancel") { cancellation.Cancel(); }
            if (failure == "late-private") { f.AdvanceRevision(); }
        };
        f.DiagnosticsEnqueueProjection = snapshot => failure switch
        {
            "missing-entry" => snapshot with { Entries = [] },
            "wrong-profile" => snapshot with { Entries = [snapshot.Entries[0] with
            { Profile = SessionQueueReadProfile.Readiness, RecordVersion = SessionQueueEntry.FixedReadRecordVersion }] },
            "wrong-dependency" => snapshot with { Entries = [snapshot.Entries[0] with { Dependency = new(Guid.NewGuid()) }] },
            "malformed-entry" => snapshot with { Entries = [snapshot.Entries[0] with { ExpiresAt = DateTimeOffset.MinValue }] },
            "foreign-generation" => snapshot with { Entries = [snapshot.Entries[0] with { Generation = new(9) }] },
            "foreign-run" => snapshot with { Entries = [snapshot.Entries[0] with { RunId = Guid.NewGuid() }] },
            "foreign-admission" => snapshot with { Entries = [snapshot.Entries[0] with { AdmissionRevision = 9 }] },
            "wrong-lifetime" => snapshot with { Entries = [snapshot.Entries[0] with { PendingLifetimeMinutes = 9 }] },
            "wrong-control-receipt" => snapshot with { DispatchControl = snapshot.DispatchControl! with { Paused = true } },
            _ => snapshot,
        };
        var result = await queue.ConfirmDiagnosticsPlanAsync(review, RequestOrigin.LocalUi, () => true, cancellation.Token);
        result.StoppedBy.Should().Be(SessionDiagnosticsPlanItemState.Unavailable);
        result.Items[0].State.Should().Be(SessionDiagnosticsPlanItemState.Unavailable);
        result.Items[0].Request.Should().NotBeNull();
        result.Items.Skip(1).Should().OnlyContain(item => item.State == SessionDiagnosticsPlanItemState.NotAttempted);
        f.QueueRows.Should().ContainSingle().Which.IsPending.Should().BeTrue();
        if (failure is "late-cancel" or "late-private" or "wrong-control-receipt")
        {
            result.Items[0].CommittedEntry.Should().Be(f.QueueRows.Single());
        }
        else { result.Items[0].CommittedEntry.Should().BeNull(); }
        f.QueueActions.Should().Be(0);
    }

    [Theory]
    [InlineData("before")]
    [InlineData("during")]
    [InlineData("unavailable")]
    [InlineData("stale")]
    [InlineData("origin")]
    [InlineData("held")]
    [InlineData("missing-control")]
    [InlineData("foreign-control")]
    [InlineData("duplicate")]
    public async Task DiagnosticsReviewFailureOrCancellationPublishesNoReviewAndNeverWrites(string change)
    {
        using var f = new Fixture();
        using var cancellation = new CancellationTokenSource();
        await using var queue = f.QueueService();
        var snapshot = await f.ReadQueueAsync(f.Request.SessionId, f.Token);
        if (change == "before") { cancellation.Cancel(); }
        if (change == "during") { f.AfterQueueRead = cancellation.Cancel; }
        if (change == "unavailable") { f.QueueFailure = "read"; }
        if (change == "stale") { snapshot = snapshot with { Revision = 99 }; }
        f.QueueReadProjection = observed => change switch
        {
            "held" => observed with { DispatchControl = observed.DispatchControl! with { Held = true } },
            "missing-control" => observed with { DispatchControl = null },
            "foreign-control" => observed with { DispatchControl = observed.DispatchControl! with { Generation = new(9) } },
            "duplicate" => observed with { Entries = [DiagnosticsRow(f), DiagnosticsRow(f)] },
            _ => observed,
        };
        var review = () => queue.ReviewDiagnosticsPlanAsync(snapshot, AllDiagnostics, SessionDiagnosticsPlanPrerequisite.None,
            change == "origin" ? RequestOrigin.ActivatedVoice : RequestOrigin.LocalUi, () => true, cancellation.Token);
        await review.Should().ThrowAsync<Exception>();
        f.TaskWrites.Should().BeEmpty();
        f.QueueRows.Should().BeEmpty();
    }

    [Fact]
    public async Task DiagnosticsConcurrentReviewAndConfirmationCannotReplaceOrDoubleConsumeApplyingPlan()
    {
        using var f = new Fixture();
        await using var queue = f.QueueService();
        var review = await DiagnosticsReview(f, queue);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.BeforeEnqueue = async _ => { entered.TrySetResult(); await release.Task; };
        var applying = queue.ConfirmDiagnosticsPlanAsync(review, RequestOrigin.LocalUi, () => true, f.Token);
        await entered.Task;
        try
        {
            var replace = () => DiagnosticsReview(f, queue);
            await replace.Should().ThrowAsync<InvalidOperationException>();
            var twice = () => queue.ConfirmDiagnosticsPlanAsync(review, RequestOrigin.LocalUi, () => true, f.Token);
            await twice.Should().ThrowAsync<InvalidOperationException>();
        }
        finally { release.TrySetResult(); }
        (await applying).Items.Should().OnlyContain(item => item.State == SessionDiagnosticsPlanItemState.Accepted);
        f.QueueRows.Should().HaveCount(4);
    }

    [Fact]
    public async Task DiagnosticsSelectedInputIsFrozenAndOldReviewCannotRetireNewReview()
    {
        using var f = new Fixture();
        await using var queue = f.QueueService();
        var chosen = new[] { SessionQueueReadProfile.Version };
        var review = await queue.ReviewDiagnosticsPlanAsync(await f.ReadQueueAsync(f.Request.SessionId, f.Token),
            chosen, SessionDiagnosticsPlanPrerequisite.None, RequestOrigin.LocalUi, () => true, f.Token);
        chosen[0] = SessionQueueReadProfile.RuntimeStatus;
        review.Profiles.Should().Equal(SessionQueueReadProfile.Version);
        var newer = await DiagnosticsReview(f, queue);
        queue.RetireDiagnosticsPlan(review);
        (await queue.ConfirmDiagnosticsPlanAsync(newer, RequestOrigin.LocalUi, () => true, f.Token))
            .Items.Should().OnlyContain(item => item.State == SessionDiagnosticsPlanItemState.Accepted);
    }

    [Fact]
    public async Task DiagnosticsLimitSourceChangesAfterFirstAcceptedReceiptStopRemainingItemsWithoutExpiryRewrite()
    {
        using var f = new Fixture();
        await using var settings = new SessionQueueConfigurationTestFixture();
        await settings.Refresh();
        await using var queue = f.QueueService(configuration: settings.Service);
        var review = await DiagnosticsReview(f, queue);
        f.AfterQueueRead = () =>
        {
            if (f.QueueRows.Count == 1)
            {
                settings.PreferencesStore.Value = new(pendingPerSession: 1);
            }
        };
        var result = await queue.ConfirmDiagnosticsPlanAsync(review, RequestOrigin.LocalUi, () => true, f.Token);
        result.Items[0].State.Should().Be(SessionDiagnosticsPlanItemState.Accepted);
        result.Items[1].State.Should().Be(SessionDiagnosticsPlanItemState.Conflict);
        result.Items.Skip(2).Should().OnlyContain(item => item.State == SessionDiagnosticsPlanItemState.NotAttempted);
        f.QueueRows.Should().ContainSingle().Which.Should().Be(result.Items[0].CommittedEntry);
        f.QueueRows[0].ExpiresAt.Should().Be(f.QueueRows[0].EnqueuedAt.AddMinutes(30));
        f.QueueActions.Should().Be(0);
    }

    [Fact]
    public async Task DiagnosticsEveryAdmittedSingleEnqueueHasFreshCorrelatedHostContextAndTerminatesItsActivity()
    {
        using var f = new Fixture();
        var stopped = new ConcurrentBag<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = stopped.Add,
        };
        ActivitySource.AddActivityListener(listener);
        await using var queue = f.QueueService();
        var contexts = new List<HostRequest>();
        f.BeforeEnqueue = _ =>
        {
            contexts.Add(HostActivity.RequireCurrent().Request);
            Activity.Current.Should().NotBeNull();
            return Task.CompletedTask;
        };
        var review = await DiagnosticsReview(f, queue);
        var result = await queue.ConfirmDiagnosticsPlanAsync(review, RequestOrigin.LocalUi, () => true, f.Token);
        contexts.Should().HaveCount(4).And.OnlyContain(request => request.SessionId == f.Request.SessionId
            && request.Origin == RequestOrigin.LocalUi);
        contexts.Select(request => request.TaskId).Distinct().Should().HaveCount(4);
        contexts.Should().OnlyContain(control => !result.Items.Any(item => item.Request == control));
        stopped.Should().NotBeEmpty();
        stopped.Should().OnlyContain(activity => activity.IdFormat == ActivityIdFormat.W3C);
        stopped.Should().Contain(activity => activity.Status == ActivityStatusCode.Ok);
        f.QueueActions.Should().Be(0);
    }

    private static SessionQueueEntry DiagnosticsRow(Fixture f) =>
        new(f.Request, new(1), new(1), 1, SessionQueueState.Pending, f.QueueRun, f.ControlRevision,
            f.QueueTime.GetUtcNow(), f.QueueTime.GetUtcNow().AddMinutes(30));

    private static async Task<SessionDiagnosticsPlanReview> DiagnosticsReview(Fixture f, SessionQueueService queue) =>
        await queue.ReviewDiagnosticsPlanAsync(await f.ReadQueueAsync(f.Request.SessionId, f.Token), AllDiagnostics,
            SessionDiagnosticsPlanPrerequisite.None, RequestOrigin.LocalUi, () => true, f.Token);

    private partial class Fixture
    {
        internal Action? AfterDiagnosticsEnqueueCommit { get; set; }
        internal Func<SessionQueueSnapshot, SessionQueueSnapshot>? DiagnosticsEnqueueProjection { get; set; }
        internal void SetDiagnosticsControl(SessionQueueDispatchControl control) => dispatchControl = control;
    }
}
