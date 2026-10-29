using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Principal;

using AwesomeAssertions;

using Kora.Core.Auditing;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Skills;
using Kora.Definitions.Skills;
using Kora.Windows.Skills.RuntimeSetup;
using Kora.Windows.IntegrationTests.Audio;

namespace Kora.Windows.IntegrationTests.Skills;

[Collection("Host tracing")]
public sealed class WindowsBundledRuntimeSetupTests : IDisposable
{
    private readonly ActivityListener listener = new()
    {
        ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
        Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
    };

    public WindowsBundledRuntimeSetupTests() => ActivitySource.AddActivityListener(listener);
    public void Dispose() => listener.Dispose();
    private static HostActivity Begin(RequestOrigin origin = RequestOrigin.LocalUi) =>
        HostActivity.BeginRoot(HostRequest.Create(origin), HostActivityLayer.Desktop, HostOperation.Runtime);

    [Fact]
    public async Task Prepare_reconstruct_verify_and_forget_complete_without_runtime_or_effect_authority()
    {
        var fixture = new BundledRuntimeSetupFixture();
        using var host = Begin();
        var review = await fixture.Review();
        review.Disclosure.Should().Contain("Download: 0 bytes").And.Contain("100 existing bytes")
            .And.Contain("not an upstream signed distribution manifest").And.Contain("Synthetic MIT")
            .And.Contain("No runtime files are copied").And.Contain("W1");
        var result = await fixture.Service.ApplyAsync(review, true, CancellationToken.None);
        result.Preparation.Should().Be(BundledRuntimePreparation.Prepared);
        var restarted = fixture.Reconstruct();
        (await restarted.InspectAsync(CancellationToken.None)).Preparation.Should().Be(BundledRuntimePreparation.Prepared);
        var verify = await restarted.ReviewAsync(BundledRuntimeSetupOperation.Verify, () => true, CancellationToken.None);
        (await restarted.ApplyAsync(verify, true, CancellationToken.None)).Preparation.Should().Be(BundledRuntimePreparation.Prepared);
        var worker = restarted.CreateWorker();
        worker.QualifiedImplementationDigest.Should().BeNull();
        worker.IsQuiescent.Should().BeTrue();
        var script = EmbeddedFixedActionCatalogue.Resolve(EmbeddedSkillCatalogue.Load().Packages.Single(package =>
            string.Equals(package.Manifest.Id, "kora.session.lock", StringComparison.Ordinal)));
        var request = new HostRequest(host.Request.RequestId, host.Request.SessionId, host.Request.TaskId, RequestOrigin.LocalUi, new(Guid.NewGuid()));
        await FluentActions.Awaiting(() => worker.RunAsync(script, request, () => true, CancellationToken.None))
            .Should().ThrowAsync<InvalidOperationException>();
        var forget = await restarted.ReviewAsync(BundledRuntimeSetupOperation.Forget, () => true, CancellationToken.None);
        await restarted.ApplyAsync(forget, true, CancellationToken.None);
        fixture.Saved.Value.Should().BeNull();
        fixture.Native.Value.Should().BeSameAs(BundledRuntimeSetupFixture.Installation);
        fixture.AuditLog.Events.Select(item => item.Outcome).Should().Equal(
            SecurityAuditOutcome.Requested, SecurityAuditOutcome.Succeeded,
            SecurityAuditOutcome.Requested, SecurityAuditOutcome.Succeeded,
            SecurityAuditOutcome.Requested, SecurityAuditOutcome.Succeeded);
        fixture.AuditLog.Events.Should().OnlyContain(item => string.Equals(item.TargetId, "powershell.protected-installed", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("declined")]
    [InlineData("expired")]
    [InlineData("privacy")]
    [InlineData("changed-inventory")]
    [InlineData("changed-device")]
    [InlineData("changed-registration")]
    public async Task Denial_before_commit_preserves_preferences_and_never_opens_worker(string fault)
    {
        var fixture = new BundledRuntimeSetupFixture();
        using var host = Begin();
        var plan = await fixture.Review();
        if (fault is "expired") { fixture.Time.Now += TimeSpan.FromMinutes(6); }
        if (fault is "privacy") { fixture.Current = false; }
        if (fault is "changed-inventory") { fixture.Native.Value = fixture.Native.Value! with { InventoryDigest = new('d', 64) }; }
        if (fault is "changed-device") { fixture.Native.Value = fixture.Native.Value! with { DeviceDigest = new('d', 64) }; }
        if (fault is "changed-registration") { fixture.Saved.Value = new(new('d', 64), new('b', 64), true); }
        await FluentActions.Awaiting(() => fixture.Service.ApplyAsync(plan, fault is not "declined", CancellationToken.None))
            .Should().ThrowAsync<Exception>();
        fixture.Saved.Writes.Should().Be(0);
        fixture.Service.CreateWorker().QualifiedImplementationDigest.Should().BeNull();
        fixture.AuditLog.Events.Should().NotContain(item => item.Outcome == SecurityAuditOutcome.Succeeded);
    }

    [Fact]
    public async Task Foreign_host_request_foreign_owner_replayed_review_and_model_origin_are_rejected()
    {
        var fixture = new BundledRuntimeSetupFixture();
        using var host = Begin();
        var plan = await fixture.Review();
        await FluentActions.Awaiting(() => fixture.Reconstruct().ApplyAsync(plan, true, CancellationToken.None))
            .Should().ThrowAsync<InvalidOperationException>();
        using (Begin())
        {
            var foreign = await fixture.Review();
            using (Begin())
            {
                await FluentActions.Awaiting(() => fixture.Service.ApplyAsync(foreign, true, CancellationToken.None))
                    .Should().ThrowAsync<InvalidOperationException>();
            }
        }
        await fixture.Service.ApplyAsync(plan, true, CancellationToken.None);
        await FluentActions.Awaiting(() => fixture.Service.ApplyAsync(plan, true, CancellationToken.None))
            .Should().ThrowAsync<InvalidOperationException>();
        using (Begin(RequestOrigin.HostSystem))
        {
            await FluentActions.Awaiting(() => fixture.Review()).Should().ThrowAsync<UnauthorizedAccessException>();
        }
    }

    [Fact]
    public async Task Cancellation_retains_exclusive_mutation_until_ignoring_native_inspection_physically_joins()
    {
        var fixture = new BundledRuntimeSetupFixture();
        using var host = Begin();
        var plan = await fixture.Review();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Native.Hold = () => { entered.TrySetResult(); return release.Task; };
        using var cancellation = new CancellationTokenSource();
        var applying = fixture.Service.ApplyAsync(plan, true, cancellation.Token);
        await entered.Task.WaitAsync(TestContext.Current.CancellationToken);
        cancellation.Cancel();
        applying.IsCompleted.Should().BeFalse();
        await FluentActions.Awaiting(() => fixture.Service.InspectAsync(CancellationToken.None))
            .Should().ThrowAsync<InvalidOperationException>();
        fixture.Service.CreateWorker().QualifiedImplementationDigest.Should().BeNull();
        release.SetResult();
        await FluentActions.Awaiting(() => applying).Should().ThrowAsync<OperationCanceledException>();
        fixture.Saved.Writes.Should().Be(0);
        fixture.Native.Hold = null;
        await fixture.Service.InspectAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Revocation_after_pending_write_and_terminal_audit_failure_remain_unconfirmed_and_recover_explicitly()
    {
        foreach (var auditFailure in new[] { false, true })
        {
            var fixture = new BundledRuntimeSetupFixture();
            using var host = Begin();
            var plan = await fixture.Review();
            if (auditFailure)
            {
                fixture.AuditLog.BeforeWrite = item =>
                {
                    if (item.Outcome == SecurityAuditOutcome.Succeeded) { throw new IOException("Synthetic durable audit failure."); }
                };
            }
            else { fixture.Saved.AfterWrite = () => fixture.Current = false; }
            await FluentActions.Awaiting(() => fixture.Service.ApplyAsync(plan, true, CancellationToken.None))
                .Should().ThrowAsync<Exception>();
            fixture.Saved.Pending!.Confirmed.Should().BeFalse();
            var reconstructed = fixture.Reconstruct();
            (await reconstructed.InspectAsync(CancellationToken.None)).Preparation.Should().Be(BundledRuntimePreparation.Unconfirmed);
            fixture.Current = true;
            fixture.Saved.AfterWrite = null;
            fixture.AuditLog.BeforeWrite = null;
            var recovery = await fixture.Service.ReviewAsync(BundledRuntimeSetupOperation.Verify, () => true, CancellationToken.None);
            (await fixture.Service.ApplyAsync(recovery, true, CancellationToken.None)).Preparation.Should().Be(BundledRuntimePreparation.Prepared);
        }
    }

    [Fact]
    public async Task Invalid_metadata_failed_native_observations_and_failed_readback_are_not_missing_or_ready()
    {
        var fixture = new BundledRuntimeSetupFixture();
        using var host = Begin();
        fixture.Saved.ReadFailure = new InvalidDataException("Malformed persisted registration.");
        await FluentActions.Awaiting(() => fixture.Service.InspectAsync(CancellationToken.None)).Should().ThrowAsync<InvalidDataException>();
        fixture.Saved.ReadFailure = null;
        fixture.Native.Failure = new UnauthorizedAccessException("Bad ACL/reparse/source/closure.");
        await FluentActions.Awaiting(() => fixture.Service.InspectAsync(CancellationToken.None)).Should().ThrowAsync<UnauthorizedAccessException>();
        fixture.Native.Failure = null;
        var review = await fixture.Review();
        fixture.Saved.AfterWrite = () => fixture.Saved.ReadFailure = new IOException("Synthetic readback failure.");
        await FluentActions.Awaiting(() => fixture.Service.ApplyAsync(review, true, CancellationToken.None)).Should().ThrowAsync<IOException>();
        fixture.Saved.Pending!.Confirmed.Should().BeFalse();
        fixture.Service.Description.Should().Contain("has not been confirmed");
    }

    [Fact]
    public async Task Changed_or_other_device_inventory_cannot_be_silently_rebaselined_by_verify()
    {
        var fixture = new BundledRuntimeSetupFixture();
        using var host = Begin();
        await fixture.Service.ApplyAsync(await fixture.Review(), true, CancellationToken.None);
        fixture.Native.Value = fixture.Native.Value! with { InventoryDigest = new('d', 64) };
        (await fixture.Service.InspectAsync(CancellationToken.None)).Preparation.Should().Be(BundledRuntimePreparation.Changed);
        await FluentActions.Awaiting(() => fixture.Review(BundledRuntimeSetupOperation.Verify)).Should().ThrowAsync<InvalidDataException>();
        fixture.Native.Value = BundledRuntimeSetupFixture.Installation with { DeviceDigest = new('e', 64) };
        (await fixture.Service.InspectAsync(CancellationToken.None)).Preparation.Should().Be(BundledRuntimePreparation.OtherDevice);
        await FluentActions.Awaiting(() => fixture.Review(BundledRuntimeSetupOperation.Verify)).Should().ThrowAsync<InvalidDataException>();
    }

    [Fact]
    public async Task Forget_reconciles_own_registration_without_inspecting_or_removing_a_broken_shared_runtime()
    {
        var fixture = new BundledRuntimeSetupFixture();
        fixture.Saved.Value = new(new('a', 64), new('b', 64), false);
        fixture.Native.Failure = new IOException("Broken shared runtime preserved.");
        using var host = Begin();
        var review = await fixture.Review(BundledRuntimeSetupOperation.Forget);
        var result = await fixture.Service.ApplyAsync(review, true, CancellationToken.None);
        result.InstallationInspected.Should().BeFalse();
        fixture.Saved.Value.Should().BeNull();
    }

    [Fact]
    public async Task Revocation_after_final_journal_clear_must_not_publish_prepared_state()
    {
        var fixture = new BundledRuntimeSetupFixture();
        using var host = Begin();
        var plan = await fixture.Review();
        fixture.Saved.AfterWrite = () =>
        {
            if (fixture.Saved.Writes == 3) { fixture.Current = false; }
        };
        await FluentActions.Awaiting(() => fixture.Service.ApplyAsync(plan, true, CancellationToken.None))
            .Should().ThrowAsync<UnauthorizedAccessException>();
        fixture.Service.Description.Should().Contain("has not been confirmed");
        fixture.Service.CreateWorker().QualifiedImplementationDigest.Should().BeNull();
    }

    [Fact]
    public async Task Final_registration_readback_failure_keeps_durable_journal_across_reconstruction()
    {
        var fixture = new BundledRuntimeSetupFixture();
        using var host = Begin();
        var plan = await fixture.Review();
        fixture.Saved.AfterWrite = () =>
        {
            if (fixture.Saved.Writes == 2) { fixture.Saved.ReadFailure = new IOException("Final registration readback failed."); }
        };
        await FluentActions.Awaiting(() => fixture.Service.ApplyAsync(plan, true, CancellationToken.None)).Should().ThrowAsync<IOException>();
        fixture.Saved.ReadFailure = null;
        (await fixture.Reconstruct().InspectAsync(CancellationToken.None)).Preparation.Should().Be(BundledRuntimePreparation.Unconfirmed);
    }

    [Fact]
    public async Task Disposal_closes_admission_and_joins_ignoring_inspection_before_releasing_resources()
    {
        var fixture = new BundledRuntimeSetupFixture();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Native.Hold = () => release.Task;
        var inspection = fixture.Service.InspectAsync(CancellationToken.None);
        await fixture.Native.Entered.Task.WaitAsync(TestContext.Current.CancellationToken);
        var disposing = fixture.Service.DisposeAsync().AsTask();
        disposing.IsCompleted.Should().BeFalse();
        await FluentActions.Awaiting(() => fixture.Service.InspectAsync(CancellationToken.None)).Should().ThrowAsync<ObjectDisposedException>();
        release.SetResult();
        await FluentActions.Awaiting(() => inspection).Should().ThrowAsync<ObjectDisposedException>();
        await disposing;
        await fixture.Service.DisposeAsync();
    }

    [Fact]
    public async Task Missing_installation_never_offers_preparation_or_fallback()
    {
        var fixture = new BundledRuntimeSetupFixture();
        fixture.Native.Value = null;
        using var host = Begin();
        (await fixture.Service.InspectAsync(CancellationToken.None)).Preparation.Should().Be(BundledRuntimePreparation.Missing);
        await FluentActions.Awaiting(() => fixture.Review()).Should().ThrowAsync<InvalidOperationException>();
        fixture.Saved.Writes.Should().Be(0);
    }

    [WindowsFact]
    public void Native_protection_policy_rejects_user_owned_and_unprivileged_mutable_runtime_bytes()
    {
        var system = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        var users = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);
        var security = new FileSecurity();
        security.SetOwner(system);
        security.AddAccessRule(new(system, FileSystemRights.FullControl, AccessControlType.Allow));
        WindowsBundledRuntimeInstallationInspector.VerifyProtected(security);
        security.AddAccessRule(new(users, FileSystemRights.ReadAndExecute, AccessControlType.Allow));
        WindowsBundledRuntimeInstallationInspector.VerifyProtected(security);
        security.AddAccessRule(new(users, FileSystemRights.Write, AccessControlType.Allow));
        ((Action)(() => WindowsBundledRuntimeInstallationInspector.VerifyProtected(security))).Should().Throw<UnauthorizedAccessException>();
        security.SetOwner(users);
        ((Action)(() => WindowsBundledRuntimeInstallationInspector.VerifyProtected(security))).Should().Throw<UnauthorizedAccessException>();
    }
}
