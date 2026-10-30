using AwesomeAssertions;
using Kora.Core.Hosting;
using Kora.Core.Memory;

namespace Kora.Windows.IntegrationTests.Storage;

public sealed partial class WindowsSqliteMemoryTests
{
    [Theory]
    [InlineData(0, false)]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(1, true)]
    [InlineData(2, false)]
    [InlineData(2, true)]
    public async Task CanonicalProfileTransactionRejectsStaleSessionCopySourceAtProposalReviewAndActualAdmission(int stage, bool forget)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        using var session = Service(f);
        using var profile = ProfileService(f);
        var source = await Admit(f, session);
        MemoryRecord? candidate = null;
        if (stage > 0)
        {
            candidate = (await Run(f, () => profile.ProposeSessionCopyAsync(Candidate,
                MemoryScope.DeviceProfile(source.Lineage.Profile), source, f.Token))).Record!;
        }
        if (stage > 1)
        {
            candidate = (await Run(f, () => profile.ReviewSessionCopyAsync(candidate!, source, true, f.Token))).Record!;
        }
        await Run(f, () => forget ? session.ForgetAsync(source.Id, source.Revision, f.Token)
            : session.DisableAsync(source.Id, source.Revision, f.Token));
        var result = stage == 0
            ? await Run(f, () => profile.ProposeSessionCopyAsync(Candidate, MemoryScope.DeviceProfile(source.Lineage.Profile), source, f.Token))
            : stage == 1
                ? await Run(f, () => profile.ReviewSessionCopyAsync(candidate!, source, true, f.Token))
                : await Run(f, () => profile.AdmitSessionCopyAsync(candidate!, source, f.Token));
        result.Outcome.Should().Be(MemoryOutcome.RevisionConflict);
        result.Record.Should().BeNull();
        f.Count("reviewed_memory").Should().Be(1);
        f.Reopen();
        await f.Store.InitializeAsync(f.Token);
        using var restarted = ProfileService(f);
        await NewIntent(f);
        (await f.RunAsync(() => new ValueTask<System.Collections.Immutable.ImmutableArray<MemoryRecord>>(
            restarted.ObserveSessionAsync(f.Token)))).Should().BeEmpty();
        AssertContentFreeAudit(f);
    }

    [Theory]
    [InlineData("forget")]
    [InlineData("disposition")]
    [InlineData("retention")]
    public async Task IndependentlyAdmittedSessionCopyRetainsExactNewIdentityAndReceiptAfterOriginalDeletion(string deletion)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        using var session = Service(f);
        using var profile = ProfileService(f);
        var source = await Admit(f, session);
        var proposed = (await Run(f, () => profile.ProposeSessionCopyAsync(
            Candidate with { ContentClass = MemoryContentClass.WorkflowPreference },
            MemoryScope.DeviceProfile(source.Lineage.Profile), source, f.Token))).Record!;
        var reviewed = (await Run(f, () => profile.ReviewSessionCopyAsync(proposed, source, true, f.Token))).Record!;
        var copy = (await Run(f, () => profile.AdmitSessionCopyAsync(reviewed, source, f.Token))).Record!;
        copy.Id.Should().NotBe(source.Id);
        copy.Receipt.Should().NotBe(source.Receipt);
        copy.Lineage.Request.Should().NotBe(source.Lineage.Request);
        var originalSession = source.Lineage.Request.SessionId;
        if (deletion is "forget")
        { await Run(f, () => session.ForgetAsync(source.Id, source.Revision, f.Token)); }
        else if (deletion is "disposition")
        {
            var preview = await f.Store.PreviewDispositionAsync(originalSession, new(1), 0, f.Token);
            await NewIntent(f);
            await f.RunAsync(() => f.Store.DisposeSessionAsync(f.Request, preview, () => true, f.Token));
        }
        else
        {
            f.Time.Now = (await f.Store.ReadRetentionAsync(originalSession, f.Token)).DeleteDue;
            f.Request = InteractionStorageFixture.NewRequest(origin: RequestOrigin.HostSystem);
            (await f.RunAsync(() => f.Store.ApplyRetentionAsync(() => true,
                (_, _) => ValueTask.CompletedTask, f.Token))).Deleted.Should().Be(1);
        }
        f.Reopen();
        await f.Store.InitializeAsync(f.Token);
        f.Request = InteractionStorageFixture.NewRequest();
        await f.AdmitAsync(newSession: true);
        using var later = ProfileService(f);
        await NewIntent(f);
        var rows = await f.RunAsync(() => new ValueTask<System.Collections.Immutable.ImmutableArray<MemoryRecord>>(
            later.ObserveSessionAsync(f.Token)));
        rows.Should().Equal(copy);
        var used = await Run(f, () => later.UseAsync(copy.Id, copy.Revision, MemoryDestination.Local, f.Token));
        used.Use!.Receipt.Should().Be(copy.Receipt);
        used.Use.Lineage.Should().Be(copy.Lineage);
        AssertContentFreeAudit(f);
    }

    [Fact]
    public async Task SessionCopyCannotEvictEitherScopeOrReuseTheGlobalFullIdentityNamespace()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        using var session = Service(f);
        var source = await Admit(f, session);
        for (var index = 1; index < MemoryPolicy.MaximumEntries; index++)
        {
            await NewIntent(f);
            var boundary = await f.RunAsync(() => f.Store.ReadMemoryBoundaryAsync(f.Request, 1, f.Token));
            var reviewed = new MemoryRecord(new(Guid.NewGuid()), new(2), MemoryScope.DeviceProfile(boundary.Profile),
                new(f.Request, boundary.Generation, boundary.Profile, MemoryProposalOrigin.User, null, null),
                Candidate, f.Time.Now, MemoryReviewState.Reviewed, MemoryRetentionState.Pending,
                new(f.Request.RequestId, new(2), f.Time.Now, boundary));
            var admitted = reviewed with { Revision = new(3), Review = MemoryReviewState.Admitted, Retention = MemoryRetentionState.Enabled };
            (await f.RunAsync(() => f.Store.TransactDeviceProfileMemoryAsync(f.Request, boundary,
                _ => new(new(MemoryOutcome.Succeeded, MemoryReason.None, admitted), null, admitted, reviewed),
                () => true, f.Token))).Outcome.Should().Be(MemoryOutcome.Succeeded);
        }
        using var profile = ProfileService(f);
        var result = await Run(f, () => profile.ProposeSessionCopyAsync(Candidate,
            MemoryScope.DeviceProfile(source.Lineage.Profile), source, f.Token));
        result.Outcome.Should().Be(MemoryOutcome.CapacityExceeded);
        result.Record.Should().BeNull();
        f.Count("reviewed_memory").Should().Be(MemoryPolicy.MaximumEntries);
        (await Run(f, () => session.UseAsync(source.Id, source.Revision, MemoryDestination.Local, f.Token))).Use!.Candidate
            .Should().Be(source.Candidate);
    }

    [Theory]
    [InlineData("audit")]
    [InlineData("commit")]
    [InlineData("cancel")]
    public async Task SessionCopyCanonicalAdmissionKeepsSourceUnchangedWhenAuditStorageOrCancellationCloses(string fault)
    {
        using var f = new InteractionStorageFixture();
        var checkpoint = new InteractionTransactionCheckpoint();
        f.Reopen(checkpoint);
        await f.InitializeAsync();
        using var session = Service(f);
        using var profile = ProfileService(f);
        var source = await Admit(f, session);
        var proposed = (await Run(f, () => profile.ProposeSessionCopyAsync(Candidate,
            MemoryScope.DeviceProfile(source.Lineage.Profile), source, f.Token))).Record!;
        var reviewed = (await Run(f, () => profile.ReviewSessionCopyAsync(proposed, source, true, f.Token))).Record!;
        await NewIntent(f);
        if (fault is "audit") { checkpoint.Audit = (_, _) => throw new IOException("Required audit unavailable."); }
        if (fault is "commit") { checkpoint.Commit = (_, _) => throw new IOException("Commit unconfirmed."); }
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(f.Token);
        if (fault is "cancel") { cancellation.Cancel(); }
        if (fault is "cancel")
        {
            (await f.RunAsync(() => new ValueTask<MemoryResult>(
                profile.AdmitSessionCopyAsync(reviewed, source, cancellation.Token)))).Outcome.Should().Be(MemoryOutcome.Cancelled);
        }
        else
        {
            await f.Invoking(value => value.RunAsync(() => new ValueTask<MemoryResult>(
                profile.AdmitSessionCopyAsync(reviewed, source, f.Token)))).Should().ThrowAsync<IOException>();
        }
        f.Count("reviewed_memory").Should().Be(1);
        checkpoint.Audit = null;
        checkpoint.Commit = null;
        f.Reopen();
        await f.Store.InitializeAsync(f.Token);
        using var restarted = Service(f);
        (await Run(f, () => restarted.UseAsync(source.Id, source.Revision, MemoryDestination.Local, f.Token))).Use!.Candidate
            .Should().Be(source.Candidate);
        AssertContentFreeAudit(f);
    }
}
