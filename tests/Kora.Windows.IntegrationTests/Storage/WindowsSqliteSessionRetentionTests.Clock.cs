using AwesomeAssertions;

using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Windows.IntegrationTests.Storage;

public sealed partial class WindowsSqliteSessionRetentionTests
{
    [Fact]
    public async Task Backward_clock_after_selection_holds_before_retiring_sources_without_renewing_deadlines()
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.InitializeAsync();
        await WindowsSqliteSessionWorkspaceTests.FinishAsync(fixture, HostTaskState.Succeeded);
        var target = fixture.Request.SessionId;
        var clock = await fixture.Store.ReadRetentionAsync(target, fixture.Token);
        var metadata = await fixture.Store.ReadMetadataAsync(target, fixture.Token);
        var history = await fixture.Store.ReadHistoryAsync(target, null, 50, fixture.Token);
        var audits = fixture.Count("security_audit_events");
        var tasks = fixture.Count("host_tasks");
        fixture.Time.Now = clock.DeleteDue;
        var admissionChecks = 0;
        var retired = false;

        var retain = () => RetainAsync(fixture, eligible: () =>
        {
            if (++admissionChecks == 2) { fixture.Time.Now = clock.ArchiveDue.AddTicks(-1); }
            return true;
        }, revoke: (_, _) =>
        {
            retired = true;
            return ValueTask.CompletedTask;
        });

        await retain.Should().ThrowAsync<InvalidOperationException>();

        admissionChecks.Should().Be(2);
        retired.Should().BeFalse();
        fixture.Count("security_audit_events").Should().Be(audits);
        fixture.Count("host_tasks").Should().Be(tasks);
        (await fixture.Store.ReadRetentionAsync(target, fixture.Token)).Should().Be(clock);
        (await fixture.Store.ReadMetadataAsync(target, fixture.Token)).Should().Be(metadata);
        (await fixture.Store.ReadHistoryAsync(target, null, 50, fixture.Token)).Should().BeEquivalentTo(history);
        fixture.Time.Now = clock.DeleteDue;
        (await RetainAsync(fixture)).Should().Be(new SessionRetentionBatch(0, 1, 0, false));
        (await fixture.Store.ReadRetentionAsync(target, fixture.Token)).Should().Be(clock with { Purged = true });
        fixture.Count("security_audit_events").Should().Be(audits + 1);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Backward_clock_during_retirement_or_commit_holds_lifecycle_until_a_fresh_due_run(
        bool deleting, bool atCommit)
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.InitializeAsync();
        var perpetual = await fixture.GrantAsync("perpetual");
        await WindowsSqliteSessionWorkspaceTests.FinishAsync(fixture, HostTaskState.Succeeded);
        var target = fixture.Request.SessionId;
        var clock = await fixture.Store.ReadRetentionAsync(target, fixture.Token);
        var cutoff = deleting ? clock.DeleteDue : clock.ArchiveDue;
        fixture.Time.Now = cutoff.AddHours(-1);
        var workspace = WindowsSqliteSessionWorkspaceTests.Service(fixture, new());
        var independent = await workspace.CreateAsync(new("Independent conversation"), RequestOrigin.LocalUi, fixture.Token);
        var independentClock = await fixture.Store.ReadRetentionAsync(independent.Authority.SessionId, fixture.Token);
        var metadata = await fixture.Store.ReadMetadataAsync(target, fixture.Token);
        var history = await fixture.Store.ReadHistoryAsync(target, null, 50, fixture.Token);
        var auditCount = fixture.Count("security_audit_events");
        var taskCount = fixture.Count("host_tasks");
        var checkpoint = new InteractionTransactionCheckpoint();
        fixture.Reopen(checkpoint);
        await fixture.Store.InitializeAsync(fixture.Token);
        fixture.Time.Now = cutoff;
        var retired = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (atCommit) { checkpoint.Commit = (_, _) => fixture.Time.Now = clock.ArchiveDue.AddTicks(-1); }
        var operation = RetainAsync(fixture, revoke: async (id, token) =>
        {
            id.Should().Be(target);
            retired.SetResult();
            await release.Task.WaitAsync(token);
        });
        await retired.Task.WaitAsync(fixture.Token);
        if (!atCommit) { fixture.Time.Now = clock.ArchiveDue.AddTicks(-1); }
        release.SetResult();

        var complete = () => operation;
        await complete.Should().ThrowAsync<InvalidOperationException>();

        fixture.Count("security_audit_events").Should().Be(auditCount);
        fixture.Count("host_tasks").Should().Be(taskCount);
        (await fixture.Store.ReadRetentionAsync(target, fixture.Token)).Should().Be(clock);
        (await fixture.Store.ReadMetadataAsync(target, fixture.Token)).Should().Be(metadata);
        (await fixture.Store.ReadHistoryAsync(target, null, 50, fixture.Token)).Should().BeEquivalentTo(history);
        (await fixture.Store.ReadRetentionAsync(independent.Authority.SessionId, fixture.Token)).Should().Be(independentClock);
        (await fixture.Store.ReadMetadataAsync(independent.Authority.SessionId, fixture.Token)).Should().Be(independent);
        (await fixture.Store.ReadGrantsAsync(fixture.Token)).Should().ContainSingle().Which.Should().Be(perpetual);

        checkpoint.Commit = null;
        fixture.Reopen();
        await fixture.Store.InitializeAsync(fixture.Token);
        (await RetainAsync(fixture)).Should().Be(new SessionRetentionBatch(0, 0, 0, false));
        fixture.Time.Now = cutoff;
        (await RetainAsync(fixture)).Should().Be(new SessionRetentionBatch(deleting ? 0 : 1, deleting ? 1 : 0, 0, false));
        fixture.Count("security_audit_events").Should().Be(auditCount + 1);
        (await fixture.Store.ReadRetentionAsync(target, fixture.Token)).Should().Be(clock with { Purged = deleting });
        (await fixture.Store.ReadMetadataAsync(independent.Authority.SessionId, fixture.Token)).Should().Be(independent);
        (await fixture.Store.ReadGrantsAsync(fixture.Token)).Should().ContainSingle().Which.Should().Be(perpetual);
        (await fixture.Tasks.ReadIncompleteAsync(10, fixture.Token)).Should().BeEmpty();
    }

    [Fact]
    public async Task Forward_clock_after_selection_cannot_promote_selected_archive_to_deletion_in_the_same_batch()
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.InitializeAsync();
        await WindowsSqliteSessionWorkspaceTests.FinishAsync(fixture, HostTaskState.Succeeded);
        var target = fixture.Request.SessionId;
        var clock = await fixture.Store.ReadRetentionAsync(target, fixture.Token);
        var history = await fixture.Store.ReadHistoryAsync(target, null, 50, fixture.Token);
        fixture.Time.Now = clock.ArchiveDue;
        var admissionChecks = 0;

        var result = await RetainAsync(fixture, eligible: () =>
        {
            if (++admissionChecks == 2) { fixture.Time.Now = clock.DeleteDue; }
            return true;
        });

        result.Should().Be(new SessionRetentionBatch(1, 0, 0, false));
        (await fixture.Store.ReadMetadataAsync(target, fixture.Token))!.Authority.IsActive.Should().BeFalse();
        (await fixture.Store.ReadHistoryAsync(target, null, 50, fixture.Token)).Records.Should().Equal(history.Records);
        (await fixture.Store.ReadRetentionAsync(target, fixture.Token)).Should().Be(clock);
        (await RetainAsync(fixture)).Should().Be(new SessionRetentionBatch(0, 1, 0, false));
        (await fixture.Store.ReadRetentionAsync(target, fixture.Token)).Purged.Should().BeTrue();
    }

    [Fact]
    public async Task Backward_clock_after_deletion_commit_does_not_interrupt_owned_copy_acceptance()
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.InitializeAsync();
        await WindowsSqliteSessionWorkspaceTests.FinishAsync(fixture, HostTaskState.Succeeded);
        var target = fixture.Request.SessionId;
        var clock = await fixture.Store.ReadRetentionAsync(target, fixture.Token);
        var checkpoint = new InteractionTransactionCheckpoint();
        fixture.Reopen(checkpoint);
        await fixture.Store.InitializeAsync(fixture.Token);
        fixture.Time.Now = clock.DeleteDue;
        var commits = 0;
        checkpoint.Commit = (_, _) =>
        {
            if (++commits == 2) { fixture.Time.Now = clock.ArchiveDue.AddTicks(-1); }
        };

        (await RetainAsync(fixture)).Should().Be(new SessionRetentionBatch(0, 1, 0, false));

        commits.Should().Be(2);
        (await fixture.Store.ReadRetentionAsync(target, fixture.Token)).Should().Be(clock with { Purged = true });
        (await fixture.Store.ReadHistoryAsync(target, null, 50, fixture.Token)).Disposed.Should().BeTrue();
        new FileInfo(fixture.DatabasePath + "-journal").Length.Should().Be(0);
    }
}
