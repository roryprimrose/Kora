using AwesomeAssertions;
using Kora.Application.Memory;
using Kora.Core.Auditing;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Memory;
using Kora.Core.Storage;
using Kora.Core.Tools;
using Kora.Windows.IntegrationTests.Audio;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests.Storage;

[Collection(nameof(DurableStorageCompositionTestGroup))]
public sealed partial class WindowsSqliteMemoryTests
{
    private static MemoryCandidate Candidate => new(MemoryContentClass.ResponsePreference, "exact reviewed memory café");

    [WindowsFact]
    public async Task Actual_store_enforces_exact_global_identity_capacity_without_eviction_or_tombstone_reuse()
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.InitializeAsync();
        for (var index = 0; index <= MemoryPolicy.MaximumEntries; index++)
        {
            await NewIntent(fixture);
            var boundary = await fixture.RunAsync(() => fixture.Store.ReadMemoryBoundaryAsync(fixture.Request, 1, fixture.Token));
            var reviewed = new MemoryRecord(new(Guid.NewGuid()), new(2), MemoryScope.Session(fixture.Request.SessionId),
                new(fixture.Request, boundary.Generation, boundary.Profile, MemoryProposalOrigin.User, null, null),
                Candidate, fixture.Time.Now, MemoryReviewState.Reviewed, MemoryRetentionState.Pending,
                new(fixture.Request.RequestId, new(2), fixture.Time.Now, boundary));
            var admitted = reviewed with { Revision = new(3), Review = MemoryReviewState.Admitted, Retention = MemoryRetentionState.Enabled };
            var result = await fixture.RunAsync(() => fixture.Store.TransactMemoryAsync(fixture.Request, boundary,
                _ => new(new(MemoryOutcome.Succeeded, MemoryReason.None, admitted), null, admitted, reviewed),
                () => true, fixture.Token));
            result.Outcome.Should().Be(index < MemoryPolicy.MaximumEntries ? MemoryOutcome.Succeeded : MemoryOutcome.CapacityExceeded);
        }
        fixture.Count("reviewed_memory").Should().Be(MemoryPolicy.MaximumEntries);
        await FinishIntent(fixture);
        var access = new Access();
        var sessions = new Kora.Application.Hosting.SessionWorkspaceService(fixture.Store, new(fixture.Tasks), access,
            NullLogger<Kora.Application.Hosting.SessionWorkspaceService>.Instance);
        var other = await sessions.CreateAsync(new("Other exact session"), RequestOrigin.LocalUi, fixture.Token);
        await using (var management = new MemoryManagementService(sessions, fixture.Store, access, access,
            fixture.Store, new Audit(), NullLogger<MemoryAdmissionService>.Instance, fixture.Time))
        {
            var result = await management.ExecuteAsync(new(Kora.Core.Commands.MemoryCommandOperation.Propose,
                other.Authority.SessionId.Value, Candidate: Candidate), RequestOrigin.LocalUi, () => true, fixture.Token);
            result.Outcome.Should().Be("CapacityExceeded");
            result.Memories.Should().BeEmpty();
        }
        fixture.Reopen();
        await fixture.Store.InitializeAsync(fixture.Token);
        fixture.Count("reviewed_memory").Should().Be(MemoryPolicy.MaximumEntries);
    }

    [WindowsFact]
    public async Task Actual_store_admission_reopens_exact_bytes_and_disabled_state_without_hosted_authority()
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.InitializeAsync();
        using var service = Service(fixture);
        var proposal = (await Run(fixture, () => service.ProposeAsync(Candidate,
            MemoryScope.Session(fixture.Request.SessionId), MemoryProposalOrigin.User, fixture.Token))).Record!;
        fixture.Count("reviewed_memory").Should().Be(0);
        var reviewed = (await Run(fixture, () => service.ReviewAsync(proposal.Id, proposal.Revision, true, fixture.Token))).Record!;
        fixture.Count("reviewed_memory").Should().Be(0);
        var admitted = (await Run(fixture, () => service.AdmitAsync(reviewed.Id, reviewed.Revision, fixture.Token))).Record!;
        fixture.Count("reviewed_memory").Should().Be(1);
        fixture.Reopen();
        using var restarted = Service(fixture);
        var used = await Run(fixture, () => restarted.UseAsync(admitted.Id, admitted.Revision, MemoryDestination.Local, fixture.Token));
        used.Use!.Candidate.Should().Be(Candidate);
        used.Use.Lineage.Should().Be(admitted.Lineage);
        used.Use.Receipt.Should().Be(admitted.Receipt);
        (await Run(fixture, () => restarted.UseAsync(admitted.Id, admitted.Revision, MemoryDestination.Hosted, fixture.Token)))
            .Reason.Should().Be(MemoryReason.DisclosureNotAdmitted);
        var disabled = (await Run(fixture, () => restarted.DisableAsync(admitted.Id, admitted.Revision, fixture.Token))).Record!;
        fixture.Reopen();
        using var disabledRestart = Service(fixture);
        (await Run(fixture, () => disabledRestart.UseAsync(disabled.Id, disabled.Revision, MemoryDestination.Local, fixture.Token)))
            .Reason.Should().Be(MemoryReason.NotEnabled);
        (await Run(fixture, () => disabledRestart.AdmitAsync(disabled.Id, disabled.Revision, fixture.Token)))
            .Outcome.Should().Be(MemoryOutcome.InvalidTransition);
        AssertContentFreeAudit(fixture);
    }

    [WindowsFact]
    public async Task Edit_redacts_old_body_before_restart_and_requires_new_exact_review()
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.InitializeAsync();
        using var service = Service(fixture);
        var admitted = await Admit(fixture, service);
        var replacement = new MemoryCandidate(MemoryContentClass.Decision, "new reviewed decision");
        var edited = (await Run(fixture, () => service.EditAsync(admitted.Id, admitted.Revision, replacement, fixture.Token))).Record!;
        var payload = Payload(fixture);
        payload.Should().NotContain(Candidate.Value).And.NotContain(replacement.Value);
        payload.Should().Contain("\"Candidate\":null").And.Contain("\"Receipt\":null");
        var reviewed = (await Run(fixture, () => service.ReviewAsync(edited.Id, edited.Revision, true, fixture.Token))).Record!;
        var readmitted = (await Run(fixture, () => service.AdmitAsync(reviewed.Id, reviewed.Revision, fixture.Token))).Record!;
        fixture.Reopen();
        using var restarted = Service(fixture);
        (await Run(fixture, () => restarted.UseAsync(readmitted.Id, readmitted.Revision, MemoryDestination.Local, fixture.Token)))
            .Use!.Candidate.Should().Be(replacement);
        var forgotten = (await Run(fixture, () => restarted.ForgetAsync(readmitted.Id, readmitted.Revision, fixture.Token))).Record!;
        new FileInfo(fixture.DatabasePath + "-journal").Length.Should().Be(0);
        System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(fixture.DatabasePath)).Should().NotContain(replacement.Value);
        Payload(fixture).Should().NotContain(replacement.Value).And.Contain("\"Candidate\":null");
        fixture.Reopen();
        using var tombstoneRestart = Service(fixture);
        (await Run(fixture, () => tombstoneRestart.EditAsync(forgotten.Id, forgotten.Revision, Candidate, fixture.Token)))
            .Outcome.Should().Be(MemoryOutcome.InvalidTransition);
        fixture.Count("reviewed_memory").Should().Be(1);
        foreach (var path in new[] { fixture.DatabasePath, fixture.DatabasePath + "-journal" })
        {
            var bytes = System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(path));
            bytes.Should().NotContain(replacement.Value).And.NotContain("exact reviewed memory");
        }
    }

    [WindowsFact]
    public async Task Restart_of_pending_edit_has_no_candidate_or_receipt_and_cannot_admit()
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.InitializeAsync();
        using var service = Service(fixture);
        var admitted = await Admit(fixture, service);
        var edited = (await Run(fixture, () => service.EditAsync(admitted.Id, admitted.Revision, Candidate, fixture.Token))).Record!;
        fixture.Reopen();
        using var restarted = Service(fixture);
        (await Run(fixture, () => restarted.ReviewAsync(edited.Id, edited.Revision, true, fixture.Token)))
            .Outcome.Should().Be(MemoryOutcome.InvalidTransition);
        (await Run(fixture, () => restarted.AdmitAsync(edited.Id, edited.Revision, fixture.Token)))
            .Outcome.Should().Be(MemoryOutcome.InvalidTransition);
        Payload(fixture).Should().Contain("\"Candidate\":null");
    }

    [Theory]
    [InlineData("DELETE FROM reviewed_memory;")]
    [InlineData("UPDATE reviewed_memory SET revision=revision+1;")]
    [InlineData("UPDATE reviewed_memory SET payload=json_set(payload,'$.Candidate.Value','hostile');")]
    [InlineData("UPDATE reviewed_memory SET payload=json_set(payload,'$.Candidate.ContentClass',7);")]
    [InlineData("UPDATE reviewed_memory SET payload=json_set(payload,'$.Lineage.SessionGeneration.Value',99);")]
    [InlineData("UPDATE reviewed_memory SET payload=json_set(payload,'$.Receipt.Revision.Value',999);")]
    [InlineData("UPDATE reviewed_memory SET payload=json_set(payload,'$.Retention',999);")]
    [InlineData("UPDATE memory_profile SET profile_id='11111111-1111-1111-1111-111111111111';")]
    [InlineData("DELETE FROM memory_profile;")]
    [InlineData("DROP TABLE reviewed_memory;")]
    public async Task Hostile_or_missing_saved_memory_refuses_without_replacement(string sql)
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.InitializeAsync();
        using var service = Service(fixture);
        await Admit(fixture, service);
        fixture.Mutate(sql);
        fixture.Reopen();
        var action = () => fixture.Store.InitializeAsync(fixture.Token).AsTask();
        await action.Should().ThrowAsync<InvalidDataException>();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Required_audit_and_commit_failures_roll_back_memory_revision_and_typed_audit_atomically(bool atCommit)
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.InitializeAsync();
        using var original = Service(fixture);
        var admitted = await Admit(fixture, original);
        await NewIntent(fixture);
        var beforePayload = Payload(fixture);
        var beforeAudit = fixture.Count("security_audit_events");
        var checkpoint = new InteractionTransactionCheckpoint();
        if (atCommit) { checkpoint.Commit = (_, _) => throw new IOException("injected commit refusal"); }
        else { checkpoint.Audit = (_, _) => throw new IOException("required audit unavailable"); }
        fixture.Reopen(checkpoint);
        using var service = Service(fixture);
        var action = () => fixture.RunAsync(() => new ValueTask<MemoryResult>(
            service.DisableAsync(admitted.Id, admitted.Revision, fixture.Token)));
        await action.Should().ThrowAsync<IOException>();
        fixture.Reopen();
        Payload(fixture).Should().Be(beforePayload);
        fixture.Count("security_audit_events").Should().Be(beforeAudit);
        using var reopened = Service(fixture);
        (await Run(fixture, () => reopened.UseAsync(admitted.Id, admitted.Revision, MemoryDestination.Local, fixture.Token)))
            .Outcome.Should().Be(MemoryOutcome.Succeeded);
    }

    [WindowsFact]
    public async Task V6_migration_preserves_exact_existing_authority_and_rejects_downgraded_committed_memory()
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.InitializeAsync();
        var grants = await fixture.GrantAsync("perpetual");
        var audits = fixture.Count("security_audit_events");
        fixture.Mutate("DROP TABLE session_file; DROP TABLE reviewed_memory; DROP TABLE memory_profile; PRAGMA user_version=6;");
        fixture.Reopen();
        await fixture.Store.InitializeAsync(fixture.Token);
        fixture.Count("security_audit_events").Should().Be(audits);
        (await fixture.Store.ReadGrantsAsync(fixture.Token)).Should().ContainSingle().Which.Should().Be(grants);
        using var service = Service(fixture);
        await Admit(fixture, service);
        fixture.Mutate("DROP TABLE session_file; DROP TABLE reviewed_memory; DROP TABLE memory_profile; PRAGMA user_version=6;");
        fixture.Reopen();
        var action = () => fixture.Store.InitializeAsync(fixture.Token).AsTask();
        await action.Should().ThrowAsync<InvalidDataException>();
    }

    [WindowsFact]
    public async Task Lifecycle_retirement_atomically_tombstones_memory_and_refuses_late_writes_or_restart_use()
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.InitializeAsync();
        using var service = Service(fixture);
        var admitted = await Admit(fixture, service);
        var originalBoundary = await fixture.RunAsync(() => fixture.Store.ReadMemoryBoundaryAsync(fixture.Request, 1, fixture.Token));
        await NewIntent(fixture);
        await fixture.RunAsync(() => fixture.Store.SetSessionLifecycleAsync(fixture.Request, new(1), active: false,
            remove: false, fixture.Token));
        Payload(fixture).Should().Contain("\"Candidate\":null").And.Contain("\"Retention\":3");
        fixture.Reopen();
        var late = () => fixture.RunAsync(() => fixture.Store.TransactMemoryAsync(fixture.Request, originalBoundary,
            _ => throw new InvalidOperationException("A retired boundary must never reach transition."), () => true, fixture.Token));
        await late.Should().ThrowAsync<InvalidOperationException>().WithMessage("*boundary is closed*");
        using var restarted = Service(fixture);
        var action = () => Run(fixture, () => restarted.EditAsync(admitted.Id, admitted.Revision, Candidate, fixture.Token));
        await action.Should().ThrowAsync<InvalidOperationException>();
        Payload(fixture).Should().NotContain(Candidate.Value);
    }

    [WindowsFact]
    public async Task Session_retention_tombstones_owned_memory_without_deleting_unrelated_memory_or_audit()
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.InitializeAsync();
        using var first = Service(fixture);
        await Admit(fixture, first);
        var owner = fixture.Request.SessionId;
        var clock = await fixture.Store.ReadRetentionAsync(owner, fixture.Token);
        fixture.Time.Now = clock.DeleteDue;
        fixture.Request = InteractionStorageFixture.NewRequest();
        await fixture.AdmitAsync(newSession: true);
        using var second = Service(fixture);
        var independent = await Admit(fixture, second);
        var other = fixture.Request.SessionId;
        fixture.Request = InteractionStorageFixture.NewRequest(origin: RequestOrigin.HostSystem);
        var deleted = await fixture.RunAsync(() => fixture.Store.ApplyRetentionAsync(() => true,
            (_, _) => ValueTask.CompletedTask, fixture.Token));
        deleted.Deleted.Should().Be(1);
        fixture.Count("reviewed_memory").Should().Be(2);
        (await fixture.Store.ReadRetentionAsync(owner, fixture.Token)).Purged.Should().BeTrue();
        fixture.Reopen();
        await fixture.Store.InitializeAsync(fixture.Token);
        fixture.Request = InteractionStorageFixture.NewRequest(other);
        using var restarted = Service(fixture);
        (await Run(fixture, () => restarted.UseAsync(independent.Id, independent.Revision, MemoryDestination.Local, fixture.Token)))
            .Use!.Candidate.Should().Be(Candidate);
        using var connection = fixture.OpenRaw();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT payload FROM reviewed_memory WHERE session_id=$id;";
        command.Parameters.AddWithValue("$id", owner.Value.ToString("D"));
        ((string)command.ExecuteScalar()!).Should().Contain("\"Candidate\":null").And.Contain("\"Receipt\":null");
        AssertContentFreeAudit(fixture);
    }

    [WindowsFact]
    public async Task Exact_disposition_hash_includes_memory_and_atomic_disposition_forgets_its_body()
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.InitializeAsync();
        using var service = Service(fixture);
        var memory = await Admit(fixture, service);
        var session = fixture.Request.SessionId;
        var preview = await fixture.Store.PreviewDispositionAsync(session, new(1), 0, fixture.Token);
        await Run(fixture, () => service.DisableAsync(memory.Id, memory.Revision, fixture.Token));
        var changed = await fixture.Store.PreviewDispositionAsync(session, new(1), 0, fixture.Token);
        changed.StoreRevision.Should().NotBe(preview.StoreRevision);
        await NewIntent(fixture);
        var stale = () => fixture.RunAsync(() => fixture.Store.DisposeSessionAsync(fixture.Request, preview, () => true, fixture.Token));
        await stale.Should().ThrowAsync<InvalidOperationException>();
        await FinishIntent(fixture);
        var exact = await fixture.Store.PreviewDispositionAsync(session, new(1), 0, fixture.Token);
        await NewIntent(fixture);
        await fixture.RunAsync(() => fixture.Store.DisposeSessionAsync(fixture.Request, exact, () => true, fixture.Token));
        Payload(fixture).Should().Contain("\"Candidate\":null").And.Contain("\"Receipt\":null");
        fixture.Reopen();
        await fixture.Store.InitializeAsync(fixture.Token);
    }

    [WindowsFact]
    public async Task Uninventoried_memory_copy_holds_deletion_acceptance_and_is_never_silently_discarded()
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.InitializeAsync();
        using var service = Service(fixture);
        var memory = await Admit(fixture, service);
        var owner = fixture.Request.SessionId;
        var clock = await fixture.Store.ReadRetentionAsync(owner, fixture.Token);
        var directory = new Kora.Windows.Storage.RestrictedStorageDirectory(
            fixture.Paths, includeKeys: false, partitionName: Kora.Windows.Storage.HostInteractionSchema.Partition);
        var copy = Path.Combine(directory.Artifacts, "uninventoried-memory.backup");
        await Kora.Windows.Storage.StorageFilePublication.WriteStagingAsync(directory, copy,
            File.ReadAllBytes(fixture.DatabasePath), Kora.Windows.Storage.StoragePublicationKind.Artifact, null, fixture.Token);
        var forget = () => Run(fixture, () => service.ForgetAsync(memory.Id, memory.Revision, fixture.Token));
        await forget.Should().ThrowAsync<InvalidDataException>();
        await FinishIntent(fixture);
        fixture.Time.Now = clock.DeleteDue;
        fixture.Request = InteractionStorageFixture.NewRequest(origin: RequestOrigin.HostSystem);
        var action = () => fixture.RunAsync(() => fixture.Store.ApplyRetentionAsync(() => true,
            (_, _) => ValueTask.CompletedTask, fixture.Token));
        await action.Should().ThrowAsync<InvalidDataException>();
        File.Exists(copy).Should().BeTrue();
        (await fixture.Store.ReadRetentionAsync(owner, fixture.Token)).Purged.Should().BeFalse();
        Payload(fixture).Should().NotContain("\"Candidate\":null");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Late_control_revocation_or_cancellation_before_commit_cannot_publish_memory(bool cancel)
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.InitializeAsync();
        using var original = Service(fixture);
        var admitted = await Admit(fixture, original);
        await NewIntent(fixture);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(fixture.Token);
        var access = new Access();
        fixture.Reopen(new InteractionTransactionCheckpoint
        {
            Commit = (_, _) =>
            {
                if (cancel) { cancellation.Cancel(); } else { access.CanControl = false; }
            },
        });
        using var service = new MemoryAdmissionService(fixture.Store, access, access,
            new SessionMemoryScopeAccess(fixture.Store, access, access), new Audit(),
            NullLogger<MemoryAdmissionService>.Instance, fixture.Time, fixture.Store);
        var action = () => fixture.RunAsync(() => new ValueTask<MemoryResult>(
            service.ForgetAsync(admitted.Id, admitted.Revision, cancellation.Token)));
        if (cancel) { (await action()).Outcome.Should().Be(MemoryOutcome.Cancelled); }
        else { await action.Should().ThrowAsync<InvalidOperationException>(); }
        fixture.Reopen();
        Payload(fixture).Should().NotContain("\"Candidate\":null");
        using var reopened = Service(fixture);
        (await Run(fixture, () => reopened.UseAsync(admitted.Id, admitted.Revision, MemoryDestination.Local, fixture.Token)))
            .Outcome.Should().Be(MemoryOutcome.Succeeded);
    }

    [WindowsFact]
    public async Task Durable_scope_and_original_input_are_closed_without_production_authority()
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.InitializeAsync();
        using var service = Service(fixture);
        var result = await Run(fixture, () => service.ProposeAsync(Candidate,
            MemoryScope.DeviceProfile(new(Guid.NewGuid())), MemoryProposalOrigin.User, fixture.Token));
        result.Reason.Should().Be(MemoryReason.ScopeMismatch);
        var admitted = await Admit(fixture, service);
        await NewIntent(fixture, RequestOrigin.HostSystem);
        (await fixture.RunAsync(() => new ValueTask<MemoryResult>(
            service.ForgetAsync(admitted.Id, admitted.Revision, fixture.Token)))).Reason.Should().Be(MemoryReason.UserIntentRequired);
        Payload(fixture).Should().Contain(Candidate.Value!.Replace("é", "\\u00E9", StringComparison.Ordinal));
    }

    private static MemoryAdmissionService Service(InteractionStorageFixture fixture)
    {
        var access = new Access();
        return new(fixture.Store, access, access, new SessionMemoryScopeAccess(fixture.Store, access, access),
            new Audit(), NullLogger<MemoryAdmissionService>.Instance, fixture.Time, fixture.Store);
    }

    private static async Task<MemoryResult> Run(InteractionStorageFixture fixture, Func<Task<MemoryResult>> operation)
    {
        await NewIntent(fixture);
        var result = await fixture.RunAsync(() => new ValueTask<MemoryResult>(operation()));
        await FinishIntent(fixture);
        return result;
    }

    private static async Task NewIntent(InteractionStorageFixture fixture, RequestOrigin origin = RequestOrigin.LocalUi)
    {
        await FinishIntent(fixture);
        fixture.Request = InteractionStorageFixture.NewRequest(fixture.Request.SessionId, origin);
        await fixture.RunAsync(async () => await fixture.Tasks.CommitAsync(
            new(fixture.Request, new(1), HostTaskState.IntentRecorded), 0, fixture.Token));
    }

    private static async Task FinishIntent(InteractionStorageFixture fixture)
    {
        var current = await fixture.Tasks.ReadTaskAsync(fixture.Request.TaskId, fixture.Token);
        if (current?.State == HostTaskState.IntentRecorded)
        {
            await fixture.RunAsync(async () => await fixture.Tasks.CommitAsync(
                current.Next(HostTaskState.Succeeded), current.Revision.Value, fixture.Token));
        }
    }

    private static async Task<MemoryRecord> Admit(InteractionStorageFixture fixture, MemoryAdmissionService service)
    {
        var proposal = (await Run(fixture, () => service.ProposeAsync(Candidate,
            MemoryScope.Session(fixture.Request.SessionId), MemoryProposalOrigin.User, fixture.Token))).Record!;
        var reviewed = (await Run(fixture, () => service.ReviewAsync(proposal.Id, proposal.Revision, true, fixture.Token))).Record!;
        return (await Run(fixture, () => service.AdmitAsync(reviewed.Id, reviewed.Revision, fixture.Token))).Record!;
    }

    private static string Payload(InteractionStorageFixture fixture)
    {
        using var connection = fixture.OpenRaw();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT payload FROM reviewed_memory;";
        return (string)(command.ExecuteScalar() ?? throw new InvalidDataException("Memory payload missing."));
    }

    private static void AssertContentFreeAudit(InteractionStorageFixture fixture)
    {
        using var connection = fixture.OpenRaw();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT envelope FROM security_audit_events;";
        using var reader = command.ExecuteReader();
        while (reader.Read()) { reader.GetString(0).Should().NotContain("exact reviewed memory"); }
    }

    private sealed class Access : ISessionWorkspaceAccess, ICapabilityHostAccess, IEvidenceQueryAccess
    {
        public bool CanInspect => true;
        public bool CanControl { get; set; } = true;
        public long ControlRevision => 1;
        public bool IsCurrentHost => true;
    }

    private sealed class Audit : ISecurityAuditLog
    {
        public void Write(SecurityAuditEvent auditEvent) => HostActivity.RequireCurrent();
    }
}
