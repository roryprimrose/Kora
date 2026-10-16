using AwesomeAssertions;

using Kora.Application.Hosting;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;
using Kora.Windows.IntegrationTests.Audio;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests.Storage;

public sealed partial class WindowsSqliteSessionQueueTests
{
    [WindowsFact]
    public async Task PrivateMultiSessionOverviewPreservesExactSnapshotsReasonsDeadlinesAndAllPassiveBoundaries()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        var question = await WindowsSqliteTaskControlTests.WaitAsync(f);
        var access = new WindowsSqliteSessionWorkspaceTests.Access();
        await using var queue = new SessionQueueService(f.Store, f.Store, new(f.Tasks), access,
            new NeverAction(), new(7, 2), NullLogger<SessionQueueService>.Instance, f.Time);
        var service = new SessionWorkspaceService(f.Store, new(f.Tasks), access,
            NullLogger<SessionWorkspaceService>.Instance, queue);
        var created = new List<SessionWorkspaceEntry>();
        for (var i = 0; i < 12; i++)
        {
            created.Add(await service.CreateAsync(new("Private grouped metadata"), RequestOrigin.LocalUi, f.Token));
        }
        var done = await service.ChangeLifecycleAsync(created[0].Authority.SessionId, new(1), false, RequestOrigin.LocalUi, f.Token);
        var waiting = await EnqueueAsync(f, f.Request.SessionId);
        var first = await EnqueueAsync(f, created[1].Authority.SessionId, minutes: 17);
        var later = await EnqueueAsync(f, created[1].Authority.SessionId);
        var running = await AdmitAsync(f, first, slots: 2, budget: 11);
        var dependency = await EnqueueAsync(f, created[2].Authority.SessionId, dependency: running.Request.TaskId);
        var unknown = await AdmitAsync(f, await EnqueueAsync(f, created[3].Authority.SessionId), slots: 2);
        await CompleteAsync(f, unknown, SessionQueueState.Unknown);
        var quarantined = await EnqueueAsync(f, created[3].Authority.SessionId);
        var ready = await EnqueueAsync(f, created[4].Authority.SessionId);
        for (var i = 0; i < 52; i++)
        {
            var request = InteractionStorageFixture.NewRequest(created[5].Authority.SessionId);
            using var host = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request);
            var task = new HostTaskRecord(request, new(1), HostTaskState.IntentRecorded);
            await f.Tasks.CommitAsync(task, 0, f.Token);
            await f.Tasks.CommitAsync(task.Next(HostTaskState.Succeeded), 1, f.Token);
        }
        var audits = f.Count("security_audit_events");
        var tasks = f.Count("host_tasks");
        var retention = await f.Store.ReadRetentionAsync(f.Request.SessionId, f.Token);
        var questionBefore = await f.Store.ReadQuestionPageAsync(f.Request.SessionId, null, 50, f.Token);
        var fifo = await f.Store.ReadQueueAsync(first.Request.SessionId, f.Token);
        var rows = new List<SessionWorkOverviewRow>();
        SessionWorkOverviewCursor? cursor = null;
        using var root = HostActivity.BeginRoot(f.Request, HostActivityLayer.Application, HostOperation.Request);
        do
        {
            var page = await service.ReadWorkOverviewAsync(SessionListFilter.All, false, cursor, f.Token);
            page.Scanned.Should().BeInRange(1, 10);
            SessionWorkOverviewPage.Serialize(page).Length.Should().BeLessThanOrEqualTo(65536);
            rows.AddRange(page.Records);
            cursor = page.Next;
        } while (cursor is not null);
        rows.Should().HaveCount(13);
        rows.Select(row => row.Session.Authority.SessionId).Distinct().Should().HaveCount(13);
        rows.Single(row => row.Session.Authority.SessionId == done.SessionId).Session.Authority.Should().Be(done);
        foreach (var row in rows)
        {
            row.Availability.Should().Be(SessionWorkOverviewAvailability.Observed);
            var exact = await service.ReadWorkAsync(row.Session.Authority.SessionId, f.Token);
            row.Session.Should().Be(exact.Session);
            row.Work!.AuthorityRevision.Should().Be(exact.AuthorityRevision);
            row.Work.QueueRevision.Should().Be(exact.Queue.Revision);
            row.Work.ObservedAt.Should().Be(exact.ObservedAt);
            row.Work.QueueRecords.Should().Equal(exact.QueueRecords);
            row.Work.PendingQuestions.Should().Equal(exact.PendingQuestions);
            row.Work.PendingCapacity.Should().Be(7);
            row.Work.ExecutionSlots.Should().Be(2);
            row.Work.Tasks.Should().OnlyContain(task => task.Request.SessionId == row.Session.Authority.SessionId);
        }
        var queueRows = rows.SelectMany(row => row.Work!.QueueRecords).ToArray();
        queueRows.Single(row => row.Entry.Request.TaskId == waiting.Request.TaskId).Eligibility.Should().Be(SessionQueueEligibility.UnclassifiedWorkOrWait);
        queueRows.Single(row => row.Entry.Request.TaskId == later.Request.TaskId).Eligibility.Should().Be(SessionQueueEligibility.SessionCurrent);
        queueRows.Single(row => row.Entry.Request.TaskId == dependency.Request.TaskId).Eligibility.Should().Be(SessionQueueEligibility.DependencyNotSucceeded);
        queueRows.Single(row => row.Entry.Request.TaskId == quarantined.Request.TaskId).Eligibility.Should().Be(SessionQueueEligibility.UnknownQuarantine);
        queueRows.Single(row => row.Entry.Request.TaskId == ready.Request.TaskId).Eligibility.Should().Be(SessionQueueEligibility.Ready);
        queueRows.Single(row => row.Entry.Request.TaskId == first.Request.TaskId).Entry.Should().Be(running);
        queueRows.Single(row => row.Entry.Request.TaskId == first.Request.TaskId).ActiveDeadline.Should().Be(running.ActiveDeadlineAt);
        rows.Single(row => row.Session.Authority.SessionId == created[5].Authority.SessionId).Work!.OmittedTasks.Should().BeGreaterThan(0);
        var target = rows.Single(row => row.Session.Authority.SessionId == created[5].Authority.SessionId).Work!;
        var ranges = new SessionWorkOverviewRanges(new(target.AuthorityRevision, target.AuthorityRevision),
            new(target.QueueRevision, target.QueueRevision), new(target.PendingCount, target.PendingCount),
            new(target.PendingCapacity, target.PendingCapacity), new(target.ExecutionSlots, target.ExecutionSlots),
            new(target.ObservedCurrentFixedReads, target.ObservedCurrentFixedReads),
            new(target.OmittedQueueRecords, target.OmittedQueueRecords), new(target.OmittedTasks, target.OmittedTasks),
            new(target.OmittedQuestions, target.OmittedQuestions));
        var ranged = new List<SessionWorkOverviewRow>();
        do
        {
            var page = await service.ReadWorkOverviewAsync(SessionListFilter.All, false, cursor, f.Token, ranges: ranges);
            SessionWorkOverviewPage.Serialize(page).Length.Should().BeLessThanOrEqualTo(65536);
            ranged.AddRange(page.Records);
            cursor = page.Next;
        } while (cursor is not null);
        ranged.Select(row => row.Session).Should().Equal(rows.Where(ranges.Matches).Select(row => row.Session));
        ranged.Should().NotBeEmpty().And.OnlyContain(row => row.Work!.OmittedTasks == target.OmittedTasks);
        f.Count("security_audit_events").Should().Be(audits);
        f.Count("host_tasks").Should().Be(tasks);
        (await f.Store.ReadRetentionAsync(f.Request.SessionId, f.Token)).Should().Be(retention);
        (await f.Store.ReadQuestionPageAsync(f.Request.SessionId, null, 50, f.Token)).Records.Should()
            .BeEquivalentTo(questionBefore.Records, options => options.WithStrictOrdering());
        (await f.Store.ReadQueueAsync(first.Request.SessionId, f.Token)).Should().BeEquivalentTo(fifo);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("delete")]
    [InlineData("corrupt")]
    [InlineData("owner")]
    [InlineData("cancel")]
    public async Task OverviewCannotReinitializeOrInferAnEmptyInventoryFromUnavailablePrivateStorage(string mode)
    {
        using var f = new InteractionStorageFixture();
        var access = new WindowsSqliteSessionWorkspaceTests.Access();
        if (mode is not "missing") { await f.InitializeAsync(); }
        if (mode is "delete") { File.Delete(f.DatabasePath); }
        if (mode is "corrupt") { await File.WriteAllTextAsync(f.DatabasePath, "corrupt private test database", f.Token); }
        if (mode is "owner") { access.CanInspect = false; }
        var service = WindowsSqliteSessionWorkspaceTests.Service(f, access);
        using var root = HostActivity.BeginRoot(f.Request, HostActivityLayer.Application, HostOperation.Request);
        using var cancel = new CancellationTokenSource();
        if (mode is "cancel") { cancel.Cancel(); }
        var read = () => service.ReadWorkOverviewAsync(SessionListFilter.All, false, null, cancel.Token);
        await read.Should().ThrowAsync<Exception>();
        if (mode is "missing" or "delete") { File.Exists(f.DatabasePath).Should().BeFalse(); }
    }
}
