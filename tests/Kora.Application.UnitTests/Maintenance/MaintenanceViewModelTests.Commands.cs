using AwesomeAssertions;
using System.Diagnostics;
using Kora.Core.Auditing;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Maintenance;

namespace Kora.Application.UnitTests.Maintenance;

public sealed partial class MaintenanceViewModelTests
{
    [Theory]
    [InlineData(RequestOrigin.LocalUi)]
    [InlineData(RequestOrigin.ActivatedVoice)]
    public async Task Original_native_typed_and_activated_cached_workflow_uses_same_record_and_no_network_or_browser(RequestOrigin origin)
    {
        using var f = new Fixture();
        var unknown = await f.Commands.ExecuteAsync(MaintenanceCommand.Status, origin, () => true, TestContext.Current.CancellationToken);
        unknown.Should().Contain("Unknown").And.Contain("No successful metadata verification").And.Contain("Production");
        f.Client.Calls.Should().Be(0);
        f.State.NetworkEnabled = true;
        await f.State.CheckAsync();
        var calls = f.Client.Calls;
        var snapshot = f.State.LastVerified;
        var review = await f.Commands.ExecuteAsync(MaintenanceCommand.Review, origin, () => true, TestContext.Current.CancellationToken);
        review.Should().Contain("Canonical cached record: 1").And.Contain(ReleaseFixture.Source)
            .And.Contain("Expected SHA-256").And.Contain("Unsigned POC").And.Contain("No updater");
        f.State.CanOpen.Should().BeTrue();
        var snooze = await f.Commands.ExecuteAsync(MaintenanceCommand.Snooze, origin, () => true, TestContext.Current.CancellationToken);
        snooze.Should().Contain("24 hours in this run").And.Contain("No approval or security prompt changed");
        f.State.LastVerified.Should().BeSameAs(snapshot);
        f.Client.Calls.Should().Be(calls);
        f.Opener.Versions.Should().BeEmpty();
        f.Store.LastRequest!.Origin.Should().Be(origin);
        f.Store.Authority!.SessionId.Should().Be(f.Store.LastRequest.SessionId);
        f.Audit.Events.Should().Contain(item => item.ActionId == "maintenance.snooze"
            && item.CorrelationId == f.Store.LastRequest.RequestId.Value && item.Outcome == SecurityAuditOutcome.Succeeded);
        f.Store.Tasks.Last().State.Should().Be(HostTaskState.Succeeded);
        await f.Commands.DisposeAsync();
        await f.Commands.DisposeAsync();
        var disposed = () => f.Commands.ExecuteAsync(MaintenanceCommand.Status, origin, () => true, CancellationToken.None);
        await disposed.Should().ThrowAsync<ObjectDisposedException>();
    }

    [Theory]
    [InlineData("origin")]
    [InlineData("ambient")]
    [InlineData("invalid")]
    [InlineData("unknown-command")]
    [InlineData("unavailable")]
    [InlineData("cancel")]
    public async Task No_synthetic_ambient_model_or_unadmitted_authority(string denial)
    {
        using var f = new Fixture();
        using var token = new CancellationTokenSource();
        if (denial is "cancel") { token.Cancel(); }
        using var activity = denial is "ambient"
            ? HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Application, HostOperation.Request) : null;
        var action = () => f.Commands.ExecuteAsync(denial is "invalid" ? MaintenanceCommand.Invalid
            : denial is "unknown-command" ? (MaintenanceCommand)99 : MaintenanceCommand.Status,
            denial is "origin" ? RequestOrigin.HostSystem : RequestOrigin.LocalUi, () => denial is not "unavailable", token.Token);
        if (denial is "cancel") { await action.Should().ThrowAsync<OperationCanceledException>(); }
        else { await action.Should().ThrowAsync<InvalidOperationException>(); }
        f.Client.Calls.Should().Be(0);
        f.Opener.Versions.Should().BeEmpty();
        f.Store.LastRequest.Should().BeNull();
        await f.Commands.DisposeAsync();
    }

    [Theory]
    [InlineData("create")]
    [InlineData("create-receipt")]
    [InlineData("receipt")]
    [InlineData("generation")]
    [InlineData("ended")]
    [InlineData("foreign-session")]
    [InlineData("channel")]
    [InlineData("privacy")]
    [InlineData("eligibility")]
    [InlineData("cancel")]
    public async Task Durable_failure_generation_target_and_late_admission_never_return_success(string failure)
    {
        using var f = new Fixture();
        using var cancellation = new CancellationTokenSource();
        var eligible = true;
        if (failure is "create") { f.Store.CreateFailure = new IOException("fixture"); }
        if (failure is "create-receipt") { f.Store.FailTerminal = true; }
        if (failure is not ("create" or "create-receipt"))
        {
            await f.Commands.ExecuteAsync(MaintenanceCommand.Status, RequestOrigin.LocalUi, () => true, CancellationToken.None);
        }
        if (failure is "receipt") { f.Store.FailTerminal = true; }
        f.Store.BeforeOperation = () =>
        {
            switch (failure)
            {
                case "generation": f.Store.Authority = f.Store.Authority! with { Generation = new(2) }; break;
                case "ended": f.Store.Authority = f.Store.Authority! with { IsActive = false }; break;
                case "foreign-session": f.Store.Authority = f.Store.Authority! with { SessionId = new(Guid.NewGuid()) }; break;
                case "channel": f.State.Channel = ReleaseChannel.Preview; break;
                case "privacy": f.State.PrivacyClosed(); break;
                case "eligibility": eligible = false; break;
                case "cancel": cancellation.Cancel(); break;
            }
        };
        var action = () => f.Commands.ExecuteAsync(MaintenanceCommand.Status, RequestOrigin.LocalUi, () => eligible, cancellation.Token);
        if (failure is "cancel") { await action.Should().ThrowAsync<OperationCanceledException>(); }
        else if (failure is "create" or "create-receipt" or "receipt") { await action.Should().ThrowAsync<IOException>(); }
        else { await action.Should().ThrowAsync<InvalidOperationException>(); }
        if (failure is "create" or "create-receipt" or "receipt")
        {
            f.Store.FailTerminal = false;
            var retry = () => f.Commands.ExecuteAsync(MaintenanceCommand.Status, RequestOrigin.LocalUi, () => true, CancellationToken.None);
            await retry.Should().ThrowAsync<InvalidOperationException>();
        }
        f.Opener.Versions.Should().BeEmpty();
        await f.Commands.DisposeAsync();
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("stale")]
    [InlineData("not-reviewed")]
    [InlineData("not-available")]
    [InlineData("error")]
    public async Task Snooze_targets_only_fresh_reviewed_available_notice_and_cached_status_is_truthful(string state)
    {
        using var f = new Fixture();
        if (state is not "unknown")
        {
            f.State.NetworkEnabled = true;
            if (state is "not-available")
            {
                f.Client.Result = f.Client.Success(f.Time.Now) with { Status = ReleaseAvailability.UpToDate };
            }
            await f.State.CheckAsync();
            if (state is not "not-reviewed") { await f.State.ReviewAsync(); }
            if (state is "stale") { f.Time.Now = f.Time.Now.AddHours(6); }
            if (state is "error")
            {
                f.Time.Now = f.State.NextCheck!.Value;
                f.Client.Result = new(ReleaseAvailability.Unavailable, "fixture metadata failure", f.Time.Now);
                await f.State.CheckAsync();
            }
        }
        var calls = f.Client.Calls;
        var action = () => f.Commands.ExecuteAsync(MaintenanceCommand.Snooze, RequestOrigin.LocalUi, () => true, CancellationToken.None);
        await action.Should().ThrowAsync<InvalidOperationException>();
        var output = await f.Commands.ExecuteAsync(MaintenanceCommand.Status, RequestOrigin.LocalUi, () => true, CancellationToken.None);
        if (state is "unknown") { output.Should().Contain("Unknown"); }
        if (state is "stale" or "error") { output.Should().Contain("stale"); }
        if (state is "error") { output.Should().Contain("Unavailable").And.Contain("failure"); }
        f.Audit.Events.Should().Contain(item => item.ActionId == "maintenance.snooze" && item.Outcome == SecurityAuditOutcome.Denied);
        f.Client.Calls.Should().Be(calls);
        f.Opener.Versions.Should().BeEmpty();
        await f.Commands.DisposeAsync();
    }

    [Fact]
    public async Task Exact_cached_record_identity_is_not_version_title_or_trace_identity()
    {
        using var f = new Fixture();
        f.State.NetworkEnabled = true;
        await f.State.CheckAsync();
        var original = f.State.LastVerified!;
        f.Store.BeforeOperation = () =>
        {
            f.Time.Now = f.State.NextCheck!.Value;
            f.Client.Result = original with { AttemptedAt = f.Time.Now, VerifiedAt = f.Time.Now };
            f.State.CheckAsync().GetAwaiter().GetResult();
        };
        var action = () => f.Commands.ExecuteAsync(MaintenanceCommand.Review, RequestOrigin.LocalUi, () => true, CancellationToken.None);
        await action.Should().ThrowAsync<InvalidOperationException>();
        f.State.CanOpen.Should().BeFalse();
        await f.Commands.DisposeAsync();
    }

    [Theory]
    [InlineData(SecurityAuditOutcome.Requested)]
    [InlineData(SecurityAuditOutcome.Succeeded)]
    public async Task Required_audit_failure_cannot_mutate_review_or_snooze_or_claim_success(SecurityAuditOutcome failing)
    {
        using var f = new Fixture();
        f.State.NetworkEnabled = true;
        await f.State.CheckAsync();
        f.Audit.BeforeWrite = record =>
        {
            if (record.ActionId is "maintenance.review" && record.Outcome == failing) { throw new IOException("required audit fixture"); }
        };
        var action = () => f.Commands.ExecuteAsync(MaintenanceCommand.Review, RequestOrigin.LocalUi, () => true, CancellationToken.None);
        await action.Should().ThrowAsync<IOException>();
        f.State.CanOpen.Should().BeFalse();
        f.Audit.Events.Should().NotContain(item => item.ActionId == "maintenance.review" && item.Outcome == SecurityAuditOutcome.Succeeded);
        f.Audit.BeforeWrite = null;
        await f.State.ReviewAsync();
        f.Audit.BeforeWrite = record =>
        {
            if (record.ActionId is "maintenance.snooze" && record.Outcome == failing) { throw new IOException("required audit fixture"); }
        };
        var snooze = () => f.Commands.ExecuteAsync(MaintenanceCommand.Snooze, RequestOrigin.LocalUi, () => true, CancellationToken.None);
        await snooze.Should().ThrowAsync<IOException>();
        f.State.Status.Should().NotContain("snoozed");
        await f.Commands.DisposeAsync();
    }

    [Theory]
    [InlineData("owner")]
    [InlineData("review")]
    [InlineData("age")]
    [InlineData("request")]
    [InlineData("invalid")]
    [InlineData("unknown")]
    public async Task Internal_cached_transition_rejects_foreign_lookalike_or_unregistered_target(string boundary)
    {
        using var f = new Fixture();
        using var other = new Fixture();
        var request = HostRequest.Create(RequestOrigin.LocalUi);
        using var activity = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request);
        var target = f.State.CaptureCachedTarget();
        if (boundary is "owner") { target = other.State.CaptureCachedTarget(); }
        if (boundary is "review") { target = target with { Reviewed = new(ReleaseAvailability.Unknown, "lookalike", f.Time.Now) }; }
        if (boundary is "age") { target = target with { Stale = !target.Stale }; }
        var apply = () => f.State.ApplyCached(boundary is "invalid" ? MaintenanceCommand.Invalid
                : boundary is "unknown" ? (MaintenanceCommand)99 : MaintenanceCommand.Status,
            target, boundary is "request" ? HostRequest.Create(RequestOrigin.LocalUi) : request, () => true, CancellationToken.None);
        apply.Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData("admission")]
    [InlineData("cancel")]
    public async Task Admission_changes_after_cache_observation_are_not_terminal_receipts(string boundary)
    {
        using var f = new Fixture();
        using var cancellation = new CancellationTokenSource();
        var eligible = true;
        f.Store.AfterOperation = () =>
        {
            if (boundary is "cancel") { cancellation.Cancel(); }
            else { eligible = false; }
        };
        var action = () => f.Commands.ExecuteAsync(MaintenanceCommand.Status, RequestOrigin.LocalUi, () => eligible, cancellation.Token);
        if (boundary is "cancel") { await action.Should().ThrowAsync<OperationCanceledException>(); }
        else { await action.Should().ThrowAsync<InvalidOperationException>(); }
        f.Store.Tasks.Last().State.Should().Be(HostTaskState.Failed);
        await f.Commands.DisposeAsync();
    }

    [Theory]
    [InlineData("privacy")]
    [InlineData("cancel")]
    public async Task Requested_audit_cannot_hold_a_stale_commit_or_cancelled_target(string boundary)
    {
        using var f = new Fixture();
        using var cancellation = new CancellationTokenSource();
        f.State.NetworkEnabled = true;
        await f.State.CheckAsync();
        f.Audit.BeforeWrite = record =>
        {
            if (record.ActionId is "maintenance.review" && record.Outcome == SecurityAuditOutcome.Requested)
            {
                if (boundary is "cancel") { cancellation.Cancel(); }
                else { f.State.PrivacyClosed(); }
            }
        };
        var action = () => f.Commands.ExecuteAsync(MaintenanceCommand.Review, RequestOrigin.LocalUi, () => true, cancellation.Token);
        if (boundary is "cancel") { await action.Should().ThrowAsync<OperationCanceledException>(); }
        else { await action.Should().ThrowAsync<InvalidOperationException>(); }
        f.State.CanOpen.Should().BeFalse();
        await f.Commands.DisposeAsync();
    }

    [Fact]
    public async Task Requested_audit_change_after_readiness_but_before_commit_is_revalidated()
    {
        using var f = new Fixture();
        f.State.NetworkEnabled = true;
        await f.State.CheckAsync();
        var eligible = true;
        f.Audit.BeforeWrite = record =>
        {
            if (record.ActionId is "maintenance.review" && record.Outcome == SecurityAuditOutcome.Requested) { eligible = false; }
        };
        var action = () => f.Commands.ExecuteAsync(MaintenanceCommand.Review, RequestOrigin.LocalUi,
            () => eligible, CancellationToken.None);
        await action.Should().ThrowAsync<InvalidOperationException>();
        f.State.CanOpen.Should().BeFalse();
        await f.Commands.DisposeAsync();
    }

    [Fact]
    public async Task Unbound_native_review_and_invalid_native_state_provide_explicit_recovery()
    {
        using var f = new Fixture();
        using var unbound = new Kora.Application.Maintenance.MaintenanceViewModel(f.Client,
            new AssemblyApplicationInfo(() => "1.0.0"), ReleaseArchitecture.X64, f.Opener, f.Time,
            new Dispatcher(), f.Audit, Microsoft.Extensions.Logging.Abstractions.NullLogger<Kora.Application.Maintenance.MaintenanceViewModel>.Instance);
        await unbound.ReviewAsync();
        unbound.Status.Should().Contain("Unknown").And.Contain("failed");
        f.Audit.BeforeWrite = record =>
        {
            if (record.ActionId is "maintenance.network" && record.Outcome == SecurityAuditOutcome.Succeeded)
            {
                throw new InvalidDataException("fixture invalid native state");
            }
        };
        f.State.NetworkEnabled = true;
        f.State.Status.Should().Contain("failed");
        f.Audit.Events.Should().Contain(record => record.ActionId == "maintenance.network" && record.Outcome == SecurityAuditOutcome.Failed);
        f.Audit.BeforeWrite = null;
        await f.Commands.DisposeAsync();
    }

    [Fact]
    public async Task Disposal_cancels_active_and_waiting_admission_without_late_success_or_replay()
    {
        using var f = new Fixture();
        var pending = new TaskCompletionSource<Kora.Core.Authorization.WorkSessionAuthorization>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Store.PendingCreation = pending.Task;
        var active = f.Commands.ExecuteAsync(MaintenanceCommand.Status, RequestOrigin.LocalUi, () => true, CancellationToken.None);
        var waiting = f.Commands.ExecuteAsync(MaintenanceCommand.Status, RequestOrigin.LocalUi, () => true, CancellationToken.None);
        var disposing = f.Commands.DisposeAsync().AsTask();
        Func<Task> observeActive = () => active;
        Func<Task> observeWaiting = () => waiting;
        await observeActive.Should().ThrowAsync<OperationCanceledException>();
        await observeWaiting.Should().ThrowAsync<OperationCanceledException>();
        await disposing;
        pending.SetResult(f.Store.Authority!);
        f.Store.LastRequest.Should().BeNull();
        f.Audit.Events.Should().BeEmpty();
        f.Client.Calls.Should().Be(0);
    }

    [Theory]
    [InlineData("disposed")]
    [InlineData("busy")]
    [InlineData("admission")]
    [InlineData("owner")]
    [InlineData("revision")]
    [InlineData("channel")]
    [InlineData("result")]
    [InlineData("review")]
    [InlineData("age")]
    public void Receipt_requires_each_current_host_and_exact_cache_binding(string boundary)
    {
        using var f = new Fixture();
        using var other = new Fixture();
        var target = f.State.CaptureCachedTarget();
        f.State.IsCachedTargetCurrent(target, MaintenanceCommand.Status).Should().BeTrue();
        switch (boundary)
        {
            case "disposed": f.State.Dispose(); break;
            case "busy": typeof(Kora.Application.Maintenance.MaintenanceViewModel)
                .GetField("busy", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .SetValue(f.State, true); break;
            case "admission": f.Admitted = false; break;
            case "owner": target = other.State.CaptureCachedTarget(); break;
            case "revision": target = target with { Revision = target.Revision + 1 }; break;
            case "channel": target = target with { Channel = ReleaseChannel.Preview }; break;
            case "result": target = target with { Result = new(ReleaseAvailability.Unknown, "foreign", f.Time.Now) }; break;
            case "review": target = target with { Reviewed = new(ReleaseAvailability.Unknown, "lookalike", f.Time.Now) }; break;
            case "age": target = target with { Stale = !target.Stale }; break;
        }
        f.State.IsCachedTargetCurrent(target, MaintenanceCommand.Status).Should().BeFalse();
    }

    [Fact]
    public async Task Cached_asset_collection_is_immutable_and_does_not_borrow_provider_collection_authority()
    {
        using var f = new Fixture();
        var raw = f.Client.Success(f.Time.Now);
        var assets = raw.Release!.Assets.ToArray();
        f.Client.Result = raw with { Release = raw.Release with { Assets = assets } };
        f.State.NetworkEnabled = true;
        await f.State.CheckAsync();
        assets[0] = assets[0] with { Sha256 = new string('c', 64) };
        var output = await f.Commands.ExecuteAsync(MaintenanceCommand.Review, RequestOrigin.LocalUi, () => true, CancellationToken.None);
        output.Should().Contain(new string('b', 64)).And.NotContain(new string('c', 64));
        var edit = () => ((IList<ReleaseAsset>)f.State.LastVerified!.Release!.Assets)[0] = assets[0];
        edit.Should().Throw<NotSupportedException>();
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Native_cached_admission_is_separate_from_network_consent_and_requires_both_current_gates(bool host, bool native)
    {
        using var f = new Fixture();
        f.State.NetworkEnabled = true;
        await f.State.CheckAsync();
        var calls = f.Client.Calls;
        f.State.BindCachedCommands(f.Commands, () => native);
        f.Admitted = host;
        await f.State.ReviewAsync();
        f.State.CanOpen.Should().Be(host && native);
        f.State.NetworkEnabled.Should().BeTrue();
        f.Client.Calls.Should().Be(calls);
        f.Opener.Versions.Should().BeEmpty();
    }

    [Theory]
    [InlineData(MaintenanceCommand.Review)]
    [InlineData(MaintenanceCommand.Snooze)]
    public async Task Closure_during_terminal_audit_does_not_publish_stale_cached_success(MaintenanceCommand command)
    {
        using var f = new Fixture();
        f.State.NetworkEnabled = true;
        await f.State.CheckAsync();
        if (command == MaintenanceCommand.Snooze) { await f.State.ReviewAsync(); }
        f.Audit.BeforeWrite = record =>
        {
            if (record.ActionId.StartsWith("maintenance.", StringComparison.Ordinal)
                && record.Outcome == SecurityAuditOutcome.Succeeded) { f.State.PrivacyClosed(); }
        };
        var action = () => f.Commands.ExecuteAsync(command, RequestOrigin.LocalUi, () => true, CancellationToken.None);
        await action.Should().ThrowAsync<InvalidOperationException>();
        f.State.CanOpen.Should().BeFalse();
        f.State.Status.Should().Contain("Unknown");
    }

    [Theory]
    [InlineData("admission")]
    [InlineData("target")]
    [InlineData("cancel")]
    public async Task Terminal_receipt_io_revalidates_channel_target_and_cancellation_without_rewriting_success(string boundary)
    {
        using var f = new Fixture();
        using var cancellation = new CancellationTokenSource();
        var eligible = true;
        await f.Commands.ExecuteAsync(MaintenanceCommand.Status, RequestOrigin.LocalUi, () => true, CancellationToken.None);
        f.Store.BeforeCommit = record =>
        {
            if (record.State != HostTaskState.Succeeded) { return; }
            if (boundary is "cancel") { cancellation.Cancel(); }
            else if (boundary is "target") { f.State.PrivacyClosed(); }
            else { eligible = false; }
        };
        var action = () => f.Commands.ExecuteAsync(MaintenanceCommand.Status, RequestOrigin.LocalUi, () => eligible, cancellation.Token);
        if (boundary is "cancel") { await action.Should().ThrowAsync<OperationCanceledException>(); }
        else { await action.Should().ThrowAsync<InvalidOperationException>(); }
        f.Store.Tasks.Last().State.Should().Be(HostTaskState.Succeeded);
    }

    [Theory]
    [InlineData(MaintenanceCommand.Status)]
    [InlineData(MaintenanceCommand.Review)]
    [InlineData(MaintenanceCommand.Snooze)]
    public async Task Oversized_complete_cached_output_is_rejected_before_any_successful_mutation(MaintenanceCommand command)
    {
        using var f = new Fixture();
        f.Client.Result = f.Client.Success(f.Time.Now);
        if (command == MaintenanceCommand.Status)
        {
            f.Client.Result = f.Client.Result with { Reason = new string('x', MaintenanceCommandParser.MaximumOutputLength) };
        }
        else
        {
            f.Client.Result = f.Client.Result with
            {
                Release = f.Client.Result.Release! with { SourceRevision = new string('a', MaintenanceCommandParser.MaximumOutputLength) },
            };
        }
        f.State.NetworkEnabled = true;
        await f.State.CheckAsync();
        if (command == MaintenanceCommand.Snooze)
        {
            var request = HostRequest.Create(RequestOrigin.LocalUi);
            using var activity = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request);
            typeof(Kora.Application.Maintenance.MaintenanceViewModel)
                .GetField("reviewed", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .SetValue(f.State, f.State.LastVerified);
        }
        var action = () => f.Commands.ExecuteAsync(command, RequestOrigin.LocalUi, () => true, CancellationToken.None);
        await action.Should().ThrowAsync<InvalidDataException>();
        f.Audit.Events.Should().NotContain(record => (string.Equals(record.ActionId, "maintenance.review", StringComparison.Ordinal)
                || string.Equals(record.ActionId, "maintenance.snooze", StringComparison.Ordinal))
            && record.Outcome == SecurityAuditOutcome.Succeeded);
        if (command == MaintenanceCommand.Snooze)
        {
            f.Audit.Events.Should().Contain(record => record.ActionId == "maintenance.snooze" && record.Outcome == SecurityAuditOutcome.Failed);
            f.State.Status.Should().NotContain("snoozed");
        }
    }

    [Fact]
    public async Task Failed_terminal_audit_does_not_restore_private_cache_after_a_concurrent_closure()
    {
        using var f = new Fixture();
        f.State.NetworkEnabled = true;
        await f.State.CheckAsync();
        f.Audit.BeforeWrite = record =>
        {
            if (record.ActionId is "maintenance.review" && record.Outcome == SecurityAuditOutcome.Succeeded)
            {
                f.State.CanReview.Should().BeFalse();
                f.State.CanOpen.Should().BeFalse();
                f.State.Status.Should().Contain("commit is pending");
                f.State.PrivacyClosed();
                throw new IOException("fixture");
            }
        };
        var action = () => f.Commands.ExecuteAsync(MaintenanceCommand.Review, RequestOrigin.LocalUi, () => true, CancellationToken.None);
        await action.Should().ThrowAsync<IOException>();
        f.State.Status.Should().Contain("Unknown");
        f.State.CanOpen.Should().BeFalse();
    }

    [Fact]
    public async Task Durable_maintenance_activities_use_host_session_and_never_incoming_trace_identity_or_baggage()
    {
        using var f = new Fixture();
        var stopped = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = stopped.Add,
        };
        ActivitySource.AddActivityListener(listener);
        using var incoming = new Activity("untrusted").SetParentId("00-11111111111111111111111111111111-1111111111111111-01").Start();
        incoming.AddBaggage("user.text", "foreign");
        await f.Commands.ExecuteAsync(MaintenanceCommand.Status, RequestOrigin.LocalUi, () => true, CancellationToken.None);
        await f.Commands.ExecuteAsync(MaintenanceCommand.Status, RequestOrigin.ActivatedVoice, () => true, CancellationToken.None);
        var roots = stopped.Where(activity => string.Equals(activity.OperationName, "session.request", StringComparison.Ordinal)).ToArray();
        roots.Should().HaveCount(2);
        roots.Should().OnlyContain(activity => activity.ParentSpanId == default
            && activity.TraceId != incoming.TraceId && !activity.Baggage.Any());
        roots.Select(activity => activity.GetTagItem("kora.session.id")).Should().OnlyContain(value => Equals(value, f.Store.Authority!.SessionId.Value));
        roots.Should().OnlyContain(activity => activity.Status == ActivityStatusCode.Ok);
        f.Store.Tasks.Last().Request.SessionId.Should().Be(f.Store.Authority!.SessionId);
    }

    [Theory]
    [InlineData(MaintenanceCommand.Status)]
    [InlineData(MaintenanceCommand.Review)]
    public async Task Expiry_during_terminal_receipt_cannot_return_a_fresh_cached_claim(MaintenanceCommand command)
    {
        using var f = new Fixture();
        f.State.NetworkEnabled = true;
        await f.State.CheckAsync();
        await f.Commands.ExecuteAsync(MaintenanceCommand.Status, RequestOrigin.LocalUi, () => true, CancellationToken.None);
        f.Store.BeforeCommit = record =>
        {
            if (record.State == HostTaskState.Succeeded) { f.Time.Now = f.Time.Now.AddHours(6); }
        };
        var action = () => f.Commands.ExecuteAsync(command, RequestOrigin.LocalUi, () => true, CancellationToken.None);
        await action.Should().ThrowAsync<InvalidOperationException>();
        f.State.IsStale.Should().BeTrue();
        f.State.CanOpen.Should().BeFalse();
        f.Store.BeforeCommit = null;
        var freshRequest = await f.Commands.ExecuteAsync(MaintenanceCommand.Status, RequestOrigin.LocalUi, () => true, CancellationToken.None);
        freshRequest.Should().Contain("stale");
    }
}
