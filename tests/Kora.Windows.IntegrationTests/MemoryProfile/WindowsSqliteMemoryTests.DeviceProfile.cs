using AwesomeAssertions;
using Kora.Application.Memory;
using Kora.Core.Hosting;
using Kora.Core.Memory;
using Kora.Windows.IntegrationTests.Audio;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests.Storage;

public sealed partial class WindowsSqliteMemoryTests
{
    [Theory]
    [InlineData("done")]
    [InlineData("disposition")]
    [InlineData("retention")]
    public async Task DeviceProfileLifetimeAndOriginalLineageAreIndependentOfCreatingSession(string end)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        using var service = ProfileService(f);
        var admitted = await AdmitProfile(f, service);
        var original = f.Request.SessionId;
        if (end is "done")
        {
            await NewIntent(f);
            await f.RunAsync(() => f.Store.SetSessionLifecycleAsync(f.Request, new(1), false, false, f.Token));
        }
        else if (end is "disposition")
        {
            var preview = await f.Store.PreviewDispositionAsync(original, new(1), 0, f.Token);
            await NewIntent(f);
            await f.RunAsync(() => f.Store.DisposeSessionAsync(f.Request, preview, static () => true, f.Token));
        }
        else
        {
            f.Time.Now = (await f.Store.ReadRetentionAsync(original, f.Token)).DeleteDue;
            f.Request = InteractionStorageFixture.NewRequest(origin: RequestOrigin.HostSystem);
            (await f.RunAsync(() => f.Store.ApplyRetentionAsync(static () => true,
                (_, _) => ValueTask.CompletedTask, f.Token))).Deleted.Should().Be(1);
        }
        f.Reopen();
        await f.Store.InitializeAsync(f.Token);
        f.Request = InteractionStorageFixture.NewRequest();
        await f.AdmitAsync(newSession: true);
        using var restarted = ProfileService(f);
        var used = await Run(f, () => restarted.UseAsync(admitted.Id, admitted.Revision, MemoryDestination.Local, f.Token));
        used.Use!.Candidate.Should().Be(Candidate);
        used.Use.Lineage.Should().Be(admitted.Lineage);
        used.Use.UsedBy.SessionId.Should().NotBe(original);
        await NewIntent(f);
        var boundary = await f.RunAsync(() => f.Store.ReadMemoryBoundaryAsync(f.Request, 1, f.Token));
        await f.RunAsync(() => f.Store.TransactMemoryAsync(f.Request, boundary, rows =>
        {
            rows.Should().BeEmpty("ordinary session consumers must not acquire profile authority");
            return new(new(MemoryOutcome.Succeeded, MemoryReason.None), null, null);
        }, static () => true, f.Token));
        AssertContentFreeAudit(f);
    }

    [WindowsFact]
    public async Task DeviceProfileEditDisableForgetAndRestartPreserveIdentityAndInvalidateOldReview()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        using var service = ProfileService(f);
        var admitted = await AdmitProfile(f, service);
        f.Request = InteractionStorageFixture.NewRequest();
        await f.AdmitAsync(newSession: true);
        using var later = ProfileService(f);
        var disabled = (await Run(f, () => later.DisableAsync(admitted.Id, admitted.Revision, f.Token))).Record!;
        (await Run(f, () => later.UseAsync(disabled.Id, disabled.Revision, MemoryDestination.Local, f.Token))).Use.Should().BeNull();
        var replacement = new MemoryCandidate(MemoryContentClass.WorkflowPreference, "Prefer explicit examples");
        var edit = (await Run(f, () => later.EditAsync(disabled.Id, disabled.Revision, replacement, f.Token))).Record!;
        edit.Id.Should().Be(admitted.Id);
        edit.CreatedAt.Should().Be(admitted.CreatedAt);
        Payload(f).Should().NotContain("Prefer explicit examples").And.NotContain("exact reviewed memory");
        f.Reopen();
        using var restarted = ProfileService(f);
        (await Run(f, () => restarted.ReviewAsync(edit.Id, edit.Revision, true, f.Token))).Outcome
            .Should().Be(MemoryOutcome.InvalidTransition);
        edit = (await Run(f, () => restarted.EditAsync(edit.Id, edit.Revision, replacement, f.Token))).Record!;
        var review = (await Run(f, () => restarted.ReviewAsync(edit.Id, edit.Revision, true, f.Token))).Record!;
        var readmitted = (await Run(f, () => restarted.AdmitAsync(review.Id, review.Revision, f.Token))).Record!;
        var forgotten = (await Run(f, () => restarted.ForgetAsync(readmitted.Id, readmitted.Revision, f.Token))).Record!;
        forgotten.Id.Should().Be(admitted.Id);
        f.Reopen();
        await f.Store.InitializeAsync(f.Token);
        Payload(f).Should().Contain("\"Candidate\":null").And.Contain("\"Receipt\":null");
        f.Count("reviewed_memory").Should().Be(1);
    }

    [WindowsFact]
    public async Task ProfileStoreRejectsForeignProfileAndSessionOnlyProposalAndHostilePersistedScope()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        using var service = ProfileService(f);
        var admitted = await AdmitProfile(f, service);
        await NewIntent(f);
        var boundary = await f.RunAsync(() => f.Store.ReadMemoryBoundaryAsync(f.Request, 1, f.Token));
        var foreign = () => f.RunAsync(() => f.Store.TransactDeviceProfileMemoryAsync(f.Request,
            boundary with { Profile = new(Guid.NewGuid()) },
            _ => throw new InvalidOperationException("foreign profile must not observe content"), static () => true, f.Token));
        await foreign.Should().ThrowAsync<InvalidOperationException>();
        using var sessionOnly = Service(f);
        (await Run(f, () => sessionOnly.ProposeAsync(Candidate, admitted.Scope, MemoryProposalOrigin.Model, f.Token)))
            .Reason.Should().Be(MemoryReason.ScopeMismatch);
        f.Mutate("UPDATE reviewed_memory SET payload=json_set(payload,'$.ScopeKind',3);");
        f.Reopen();
        await f.Store.Invoking(store => store.InitializeAsync(f.Token).AsTask()).Should().ThrowAsync<InvalidDataException>();
    }

    [Theory]
    [InlineData("audit")]
    [InlineData("commit")]
    [InlineData("cancel")]
    public async Task DeviceProfileRequiredAuditCommitAndCancellationNeverPublishChangedState(string fault)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        using var original = ProfileService(f);
        var admitted = await AdmitProfile(f, original);
        await NewIntent(f);
        var payload = Payload(f);
        var audits = f.Count("security_audit_events");
        var checkpoint = new InteractionTransactionCheckpoint();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(f.Token);
        if (fault is "audit") { checkpoint.Audit = (_, _) => throw new IOException("Required profile audit unavailable."); }
        if (fault is "commit") { checkpoint.Commit = (_, _) => throw new IOException("Profile commit unconfirmed."); }
        f.Reopen(checkpoint);
        using var service = ProfileService(f);
        if (fault is "cancel")
        {
            cancellation.Cancel();
            var cancelled = await f.RunAsync(() => new ValueTask<MemoryResult>(
                service.DisableAsync(admitted.Id, admitted.Revision, cancellation.Token)));
            cancelled.Outcome.Should().Be(MemoryOutcome.Cancelled);
        }
        else
        {
            await f.Invoking(value => value.RunAsync(() => new ValueTask<MemoryResult>(
                service.DisableAsync(admitted.Id, admitted.Revision, f.Token)))).Should().ThrowAsync<IOException>();
        }
        f.Reopen();
        Payload(f).Should().Be(payload);
        f.Count("security_audit_events").Should().Be(audits);
        AssertContentFreeAudit(f);
    }

    [WindowsFact]
    public async Task DeviceProfilePendingShellCanBeReplacedOrForgottenInLaterSessionButNeverAdmittedFromOldReview()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        using var service = ProfileService(f);
        var admitted = await AdmitProfile(f, service);
        var edited = (await Run(f, () => service.EditAsync(admitted.Id, admitted.Revision, Candidate, f.Token))).Record!;
        f.Request = InteractionStorageFixture.NewRequest();
        await f.AdmitAsync(newSession: true);
        using var later = ProfileService(f);
        (await Run(f, () => later.AdmitAsync(edited.Id, edited.Revision, f.Token))).Reason.Should().Be(MemoryReason.ScopeMismatch);
        var replacement = (await Run(f, () => later.EditAsync(edited.Id, edited.Revision, Candidate, f.Token))).Record!;
        replacement.Lineage.Request.SessionId.Should().Be(f.Request.SessionId);
        var reviewed = (await Run(f, () => later.ReviewAsync(replacement.Id, replacement.Revision, true, f.Token))).Record!;
        var readmitted = (await Run(f, () => later.AdmitAsync(reviewed.Id, reviewed.Revision, f.Token))).Record!;
        edited = (await Run(f, () => later.EditAsync(readmitted.Id, readmitted.Revision, Candidate, f.Token))).Record!;
        f.Request = InteractionStorageFixture.NewRequest();
        await f.AdmitAsync(newSession: true);
        using var last = ProfileService(f);
        (await Run(f, () => last.ForgetAsync(edited.Id, edited.Revision, f.Token))).Record!.Retention
            .Should().Be(MemoryRetentionState.Forgotten);
        f.Reopen();
        await f.Store.InitializeAsync(f.Token);
    }

    private static MemoryAdmissionService ProfileService(InteractionStorageFixture f)
    {
        var access = new Access();
        return new(f.Store, access, access, new SessionMemoryScopeAccess(f.Store, access, access),
            new Audit(), NullLogger<MemoryAdmissionService>.Instance, f.Time, f.Store, MemoryScopeKind.DeviceProfile);
    }

    private static async Task<MemoryRecord> AdmitProfile(InteractionStorageFixture f, MemoryAdmissionService service)
    {
        await NewIntent(f);
        var boundary = await f.RunAsync(() => f.Store.ReadMemoryBoundaryAsync(f.Request, 1, f.Token));
        var proposal = (await Run(f, () => service.ProposeAsync(Candidate, MemoryScope.DeviceProfile(boundary.Profile),
            MemoryProposalOrigin.User, f.Token))).Record!;
        var reviewed = (await Run(f, () => service.ReviewAsync(proposal.Id, proposal.Revision, true, f.Token))).Record!;
        return (await Run(f, () => service.AdmitAsync(reviewed.Id, reviewed.Revision, f.Token))).Record!;
    }
}
