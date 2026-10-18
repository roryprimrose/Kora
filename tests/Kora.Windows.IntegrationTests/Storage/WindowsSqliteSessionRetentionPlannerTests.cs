using System.Text.Json;

using AwesomeAssertions;

using Kora.Core.Hosting;
using Kora.Core.Storage;
using Kora.Windows.IntegrationTests.Audio;
using Kora.Windows.Storage;

namespace Kora.Windows.IntegrationTests.Storage;

public sealed class WindowsSqliteSessionRetentionPlannerTests
{
    [WindowsFact]
    public async Task ZeroAndKnownWorkHeldAndNewlyDueAreReadOnlyWithoutPrivateContentOrIdentity()
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.Tasks.InitializeAsync(fixture.Token);
        await fixture.Store.InitializeAsync(fixture.Token);
        var zero = fixture.Store.ReadRetentionPlan(new(1, 3), fixture.Time.Now, fixture.Token);
        zero.Status.Should().Be(SessionRetentionPlanStatus.Complete);
        zero.Aggregate.Should().Be(new SessionRetentionPlanAggregate(0, 0, 0, 0, 0, 0, 0, 0, 0));
        await fixture.AdmitAsync(newSession: true);
        var bytes = File.ReadAllBytes(fixture.DatabasePath);
        var journal = File.ReadAllBytes(fixture.DatabasePath + "-journal");
        var clock = await fixture.Store.ReadRetentionAsync(fixture.Request.SessionId, fixture.Token);
        var held = fixture.Store.ReadRetentionPlan(new(1, 3), fixture.Time.Now.AddDays(5), fixture.Token);
        held.Aggregate!.WorkHeld.Should().Be(1);
        held.Aggregate.NewlyDeleteDue.Should().Be(0);
        File.ReadAllBytes(fixture.DatabasePath).Should().Equal(bytes);
        File.ReadAllBytes(fixture.DatabasePath + "-journal").Should().Equal(journal);
        await WindowsSqliteSessionWorkspaceTests.FinishAsync(fixture, HostTaskState.Succeeded);
        bytes = File.ReadAllBytes(fixture.DatabasePath);
        journal = File.ReadAllBytes(fixture.DatabasePath + "-journal");
        var due = fixture.Store.ReadRetentionPlan(new(1, 3), fixture.Time.Now.AddDays(5), fixture.Token);
        due.Status.Should().Be(SessionRetentionPlanStatus.Complete);
        due.Aggregate!.NewlyDeleteDue.Should().Be(1);
        due.Aggregate.RecordedArchiveDue.Should().Be(1);
        due.Aggregate.RecordedDeleteDue.Should().Be(0);
        var json = JsonSerializer.Serialize(due.Aggregate);
        json.Should().NotContain(fixture.Request.SessionId.Value.ToString("D"))
            .And.NotContain(fixture.Paths.LocalRoot).And.NotContain("payload").And.NotContain("name");
        File.ReadAllBytes(fixture.DatabasePath).Should().Equal(bytes);
        File.ReadAllBytes(fixture.DatabasePath + "-journal").Should().Equal(journal);
        (await fixture.Store.ReadRetentionAsync(fixture.Request.SessionId, fixture.Token)).Should().Be(clock);
        fixture.Store.RetentionPlannerStamp(fixture.Token).Should().NotBe(zero.Snapshot);
    }

    [WindowsFact]
    public async Task KeptAndUnknownQuestionExclusionsValidateBeforeFiltering()
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.InitializeAsync();
        var question = await fixture.PresentAsync();
        var service = WindowsSqliteSessionWorkspaceTests.Service(fixture, new());
        var id = fixture.Request.SessionId;
        using var root = HostActivity.BeginRoot(
            new(new(Guid.NewGuid()), id, new(Guid.NewGuid()), RequestOrigin.LocalUi),
            HostActivityLayer.Application, HostOperation.Request);
        var held = await service.ReadRetentionAsync(id, fixture.Token);
        await service.SetRetentionHoldAsync(held, true, RequestOrigin.LocalUi, static () => true, fixture.Token);
        var kept = fixture.Store.ReadRetentionPlan(new(1, 3), fixture.Time.Now.AddDays(5), fixture.Token);
        kept.Aggregate!.Kept.Should().Be(1);
        kept.Aggregate.NewlyDeleteDue.Should().Be(0);
        var fresh = await service.ReadRetentionAsync(id, fixture.Token);
        await service.SetRetentionHoldAsync(fresh, false, RequestOrigin.LocalUi, static () => true, fixture.Token);
        // Terminal task metadata alone cannot establish whether the retained question is still pending.
        await WindowsSqliteSessionWorkspaceTests.FinishAsync(fixture, HostTaskState.Succeeded);
        var unknown = fixture.Store.ReadRetentionPlan(new(1, 3), fixture.Time.Now.AddDays(5), fixture.Token);
        unknown.Aggregate!.UnknownHold.Should().Be(1);
        unknown.Aggregate.NewlyDeleteDue.Should().Be(0);
        question.Key.QuestionId.Value.Should().NotBe(Guid.Empty);
        fixture.Mutate("UPDATE session_retention SET last_activity='invalid';");
        var corrupt = () => fixture.Store.ReadRetentionPlan(new(1, 3), fixture.Time.Now.AddDays(5), fixture.Token);
        corrupt.Should().Throw<InvalidDataException>();
    }

    [WindowsFact]
    public async Task RemovedSessionIsExcludedWithoutInterpretingItsClockAsNewCleanupEligibility()
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.InitializeAsync();
        await WindowsSqliteSessionWorkspaceTests.FinishAsync(fixture, HostTaskState.Succeeded);
        var id = fixture.Request.SessionId;
        using var root = HostActivity.BeginRoot(
            new(new(Guid.NewGuid()), id, new(Guid.NewGuid()), RequestOrigin.LocalUi),
            HostActivityLayer.Application, HostOperation.Request);
        var service = WindowsSqliteSessionWorkspaceTests.Service(fixture, new());
        var preview = await service.PreviewDispositionAsync(id, new(1), 0, fixture.Token);
        await service.ConfirmDispositionAsync(preview, RequestOrigin.LocalUi, static () => true, fixture.Token);
        var bytes = File.ReadAllBytes(fixture.DatabasePath);
        var result = fixture.Store.ReadRetentionPlan(new(1, 3), fixture.Time.Now.AddDays(5), fixture.Token);
        result.Status.Should().Be(SessionRetentionPlanStatus.Complete);
        result.Aggregate!.Removed.Should().Be(1);
        result.Aggregate.NewlyArchiveDue.Should().Be(0);
        result.Aggregate.NewlyDeleteDue.Should().Be(0);
        File.ReadAllBytes(fixture.DatabasePath).Should().Equal(bytes);
    }

    [Theory]
    [InlineData("date")]
    [InlineData("future")]
    [InlineData("missing-clock")]
    [InlineData("missing-session")]
    [InlineData("missing-clock-and-session")]
    [InlineData("head")]
    [InlineData("suffix")]
    [InlineData("task-head")]
    [InlineData("task-state")]
    [InlineData("unknown-change")]
    [InlineData("exemption")]
    public async Task CorruptIrrelevantMetadataNeverReturnsCompleteOrFalseZero(string reason)
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.InitializeAsync();
        fixture.Mutate(reason switch
        {
            "date" => "UPDATE session_retention SET last_activity='not-a-date';",
            "future" => "UPDATE session_retention SET last_activity='2099-01-01T00:00:00.0000000+00:00';",
            "missing-clock" => "DELETE FROM session_retention;",
            "missing-session" => "DELETE FROM work_sessions;",
            "missing-clock-and-session" => "DELETE FROM host_task_events; DELETE FROM host_tasks; DELETE FROM session_retention; DELETE FROM work_sessions;",
            "head" => "UPDATE authority_head SET hash='" + new string('0', 64) + "';",
            "suffix" => "UPDATE authority_head SET sequence=sequence+1;",
            "task-head" => "DELETE FROM host_task_events;",
            "task-state" => "UPDATE host_tasks SET state=2;",
            "unknown-change" => """
                UPDATE security_audit_events SET envelope=json_set(envelope,'$.Changes[0].Kind','unknown-metadata')
                WHERE json_array_length(envelope,'$.Changes')>0;
                """,
            _ => "UPDATE session_retention SET perpetual=1;",
        });
        using var reader = new WindowsSqliteSessionRetentionPlannerReader(fixture.Store, fixture.Paths);
        var result = await reader.ReadAsync(new(1, 3), fixture.Time.Now, HostRequest.Create(RequestOrigin.LocalUi), fixture.Token);
        result.Status.Should().Be(SessionRetentionPlanStatus.Unavailable);
        result.Aggregate.Should().BeNull();
        result.Snapshot.Should().BeNull();
    }

    [WindowsFact]
    public async Task MissingUninitializedDisposedAndNonNativeSourcesCannotAcquirePlannerAuthority()
    {
        using var fixture = new InteractionStorageFixture();
        using var reader = new WindowsSqliteSessionRetentionPlannerReader(fixture.Store, fixture.Paths);
        var result = await reader.ReadAsync(new(1, 3), fixture.Time.Now, HostRequest.Create(RequestOrigin.LocalUi), fixture.Token);
        result.Status.Should().Be(SessionRetentionPlanStatus.Unavailable);
        Directory.Exists(Path.Combine(fixture.Paths.LocalRoot, HostInteractionSchema.Partition)).Should().BeFalse();
        foreach (var origin in new[] { RequestOrigin.ActivatedVoice, RequestOrigin.HostSystem })
        {
            var action = () => reader.ReadAsync(new(1, 3), fixture.Time.Now, HostRequest.Create(origin), fixture.Token);
            action.Should().Throw<InvalidOperationException>();
        }
        var original = HostRequest.Create(RequestOrigin.LocalUi);
        var invoked = new HostRequest(original.RequestId, original.SessionId, original.TaskId, original.Origin, new(Guid.NewGuid()));
        var invocation = () => reader.ReadAsync(new(1, 3), fixture.Time.Now, invoked, fixture.Token);
        invocation.Should().Throw<InvalidOperationException>();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var cancelled = () => reader.ReadAsync(new(1, 3), fixture.Time.Now, HostRequest.Create(RequestOrigin.LocalUi), cancellation.Token).AsTask();
        await cancelled.Should().ThrowAsync<OperationCanceledException>();
        reader.Dispose();
        reader.IsCurrent(new(SessionRetentionPlanStatus.Complete, null, "stale")).Should().BeFalse();
        reader.IsCurrent(new(SessionRetentionPlanStatus.Unavailable, null, null)).Should().BeFalse();
    }

    [WindowsFact]
    public async Task ExactStoreAndPreferenceStampsAndNotificationsRetireWithoutWritingOrReplaying()
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.InitializeAsync();
        using var reader = new WindowsSqliteSessionRetentionPlannerReader(fixture.Store, fixture.Paths);
        var result = await reader.ReadAsync(new(1, 3), fixture.Time.Now, HostRequest.Create(RequestOrigin.LocalUi), fixture.Token);
        result.Status.Should().Be(SessionRetentionPlanStatus.Complete);
        reader.IsCurrent(result).Should().BeTrue();
        var changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        reader.Changed += (_, _) => changed.TrySetResult();
        var preferences = new Kora.Application.Configuration.LocalSessionRetentionPreferences(fixture.Paths);
        preferences.Save(new(2, 60));
        await changed.Task.WaitAsync(TimeSpan.FromSeconds(5), fixture.Token);
        reader.IsCurrent(result).Should().BeFalse();
        preferences.Reset();
        reader.IsCurrent(result).Should().BeFalse();
        using var secondReader = new WindowsSqliteSessionRetentionPlannerReader(fixture.Store, fixture.Paths);
        var second = await secondReader.ReadAsync(new(1, 3), fixture.Time.Now, HostRequest.Create(RequestOrigin.LocalUi), fixture.Token);
        second.Status.Should().Be(SessionRetentionPlanStatus.Complete);
        fixture.Mutate("UPDATE session_retention SET last_activity='invalid';");
        secondReader.IsCurrent(second).Should().BeFalse();
        reader.Dispose();
        reader.IsCurrent(result).Should().BeFalse();
    }

    [WindowsFact]
    public async Task BoundedSourceExhaustionHasNoPartialSessionAggregateOrUnboundedCount()
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.InitializeAsync();
        using (var connection = fixture.OpenRaw())
        {
            using var transaction = connection.BeginTransaction();
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                WITH RECURSIVE n(value) AS (SELECT 1 UNION ALL SELECT value+1 FROM n WHERE value<$limit)
                INSERT INTO host_tasks(task_id,request_id,session_id,origin,invocation_id,revision,state)
                SELECT printf('%08x-0000-0000-0000-000000000001',value),
                    printf('%08x-0000-0000-0000-000000000001',value),$session,0,NULL,1,0 FROM n;
                INSERT INTO host_task_events SELECT task_id,revision,state FROM host_tasks
                    WHERE task_id<>$original;
                """;
            command.Parameters.AddWithValue("$limit", SessionRetentionPlanner.MaximumCandidates);
            command.Parameters.AddWithValue("$session", fixture.Request.SessionId.Value.ToString("D"));
            command.Parameters.AddWithValue("$original", fixture.Request.TaskId.Value.ToString("D"));
            command.ExecuteNonQuery();
            transaction.Commit();
        }
        var before = File.ReadAllBytes(fixture.DatabasePath);
        var result = fixture.Store.ReadRetentionPlan(new(1, 3), fixture.Time.Now.AddDays(5), fixture.Token);
        result.Status.Should().Be(SessionRetentionPlanStatus.Incomplete);
        result.Aggregate.Should().BeNull();
        result.Snapshot.Should().NotBeNullOrWhiteSpace();
        File.ReadAllBytes(fixture.DatabasePath).Should().Equal(before);
    }

    [Theory]
    [InlineData(9, true)]
    [InlineData(10, false)]
    public async Task ExactElapsedTimeLimitDoesNotPublishAnExhaustedObservation(int elapsedSeconds, bool completes)
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.InitializeAsync();
        var clock = new StepTime(fixture.Time.Now, elapsedSeconds);
        var store = new WindowsSqliteHostInteractionStore(fixture.Paths, fixture.Tasks, clock);
        await store.InitializeAsync(fixture.Token);
        clock.Armed = true;
        if (completes)
        {
            store.ReadRetentionPlan(new(1, 3), clock.GetUtcNow(), fixture.Token).Status.Should().Be(SessionRetentionPlanStatus.Complete);
        }
        else
        {
            var action = () => store.ReadRetentionPlan(new(1, 3), clock.GetUtcNow(), fixture.Token);
            action.Should().Throw<TimeoutException>();
        }
    }

    private sealed class StepTime(DateTimeOffset now, int elapsedSeconds) : TimeProvider
    {
        private bool started;
        internal bool Armed { get; set; }
        public override DateTimeOffset GetUtcNow() => now;
        public override long TimestampFrequency => 1;
        public override long GetTimestamp()
        {
            if (!Armed) { return 0; }
            if (!started) { started = true; return 0; }
            return elapsedSeconds;
        }
    }
}
