using AwesomeAssertions;
using Kora.Application.Maintenance;
using Kora.Core.Auditing;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Maintenance;

namespace Kora.Application.UnitTests.Maintenance;

public sealed partial class MaintenanceViewModelTests
{
    [Fact]
    public async Task Installer_requires_separate_exact_native_approval_and_rechecks_before_and_after_download()
    {
        using var f = new Fixture();
        using var downloader = new InstallerDownloader();
        var launcher = new InstallerLauncher();
        f.State.BindUpgradeServices(downloader, launcher);
        f.State.UpgradeDisclosure.Should().Contain("unsigned").And.Contain("Windows may request");
        await f.State.UpgradeCommand.ExecuteAsync();
        downloader.Calls.Should().Be(0);
        f.State.NetworkEnabled = true;
        await f.State.CheckAsync();
        f.State.UpgradeApproved = true;
        f.State.UpgradeApproved.Should().BeFalse();
        await f.State.ReviewAsync();
        f.State.CanReviewUpgrade.Should().BeTrue();
        f.State.UpgradeApproved = true;
        f.State.CanUpgrade.Should().BeTrue();
        await f.State.UpgradeCommand.ExecuteAsync();
        downloader.Calls.Should().Be(1);
        downloader.Releases.Single().Version.ToString().Should().Be("1.2.3");
        downloader.Lease.Disposed.Should().BeTrue();
        launcher.Assets.Single().Name.Should().Be("Kora-Setup-1.2.3-win-x64.exe");
        f.Client.Calls.Should().Be(3);
        f.State.UpgradeApproved.Should().BeFalse();
        f.State.Status.Should().Contain("does not claim installation success");
        f.Audit.Events.Should().Contain(item => item.ActionId == "maintenance.start-installer"
            && item.Outcome == SecurityAuditOutcome.Succeeded);
        await f.State.UpgradeAsync();
        launcher.Assets.Should().ContainSingle();
    }

    [Fact]
    public async Task Ambient_model_context_cannot_set_installer_approval_or_execute_a_native_upgrade()
    {
        using var f = new Fixture();
        using var downloader = new InstallerDownloader();
        var launcher = new InstallerLauncher();
        f.State.BindUpgradeServices(downloader, launcher);
        f.State.NetworkEnabled = true;
        await f.State.CheckAsync();
        await f.State.ReviewAsync();
        using var activity = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi),
            HostActivityLayer.Application, HostOperation.Request);
        f.State.UpgradeApproved = true;
        f.State.UpgradeApproved.Should().BeFalse();
        await f.State.UpgradeAsync();
        downloader.Calls.Should().Be(0);
        launcher.Assets.Should().BeEmpty();
        activity.Complete(HostOperationOutcome.Completed);
    }

    [Theory]
    [InlineData("privacy")]
    [InlineData("revocation")]
    [InlineData("version-edit")]
    [InlineData("ownership")]
    public async Task Late_download_cannot_start_after_approval_or_host_changes(string change)
    {
        using var f = new Fixture();
        var pending = new TaskCompletionSource<IReleaseInstallerDownload>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var downloader = new InstallerDownloader { Pending = pending.Task };
        var launcher = new InstallerLauncher();
        f.State.BindUpgradeServices(downloader, launcher);
        f.State.NetworkEnabled = true;
        await f.State.CheckAsync();
        await f.State.ReviewAsync();
        f.State.UpgradeApproved = true;
        var upgrade = f.State.UpgradeAsync();
        downloader.Calls.Should().Be(1);
        f.State.CanUpgrade.Should().BeFalse();
        if (change is "privacy") { f.State.PrivacyClosed(); }
        if (change is "revocation") { f.State.UpgradeApproved = false; }
        if (change is "version-edit") { f.State.RequestedVersionText = "1.2.4"; }
        if (change is "ownership") { f.Admitted = false; }
        pending.SetResult(downloader.Lease);
        await upgrade;
        downloader.Lease.Disposed.Should().BeTrue();
        launcher.Assets.Should().BeEmpty();
        f.State.UpgradeApproved.Should().BeFalse();
        f.Audit.Events.Should().NotContain(item => item.ActionId == "maintenance.start-installer"
            && item.Outcome == SecurityAuditOutcome.Succeeded);
    }

    [Theory]
    [InlineData("before")]
    [InlineData("after")]
    [InlineData("download")]
    [InlineData("launch")]
    [InlineData("protocol")]
    public async Task Release_changes_and_io_failures_never_claim_a_completed_upgrade(string failure)
    {
        using var f = new Fixture();
        using var downloader = new InstallerDownloader();
        var launcher = new InstallerLauncher();
        f.State.BindUpgradeServices(downloader, launcher);
        f.State.NetworkEnabled = true;
        await f.State.CheckAsync();
        await f.State.ReviewAsync();
        f.State.UpgradeApproved = true;
        if (failure is "before") { f.Client.Result = f.Client.Success(f.Time.Now) with { Status = ReleaseAvailability.Unknown }; }
        if (failure is "after")
        { downloader.AfterDownload = () => f.Client.Result = f.Client.Success(f.Time.Now) with { Release = f.State.LastVerified!.Release! with { SourceRevision = new string('c', 40) } }; }
        if (failure is "protocol")
        { downloader.AfterDownload = () => f.Client.Result = f.Client.Success(f.Time.Now) with { Release = f.State.LastVerified!.Release! with { InstallerProtocol = 3 } }; }
        if (failure is "download") { downloader.Failure = new IOException("fixture download"); }
        if (failure is "launch") { launcher.Failure = new IOException("fixture launch"); }
        await f.State.UpgradeAsync();
        launcher.Assets.Should().BeEmpty();
        f.State.Status.Should().Contain(failure is "launch" ? "unconfirmed" : "before installer startup");
        var expectedOutcome = failure is "launch" ? SecurityAuditOutcome.Unknown : SecurityAuditOutcome.Failed;
        f.Audit.Events.Should().Contain(item => item.ActionId == "maintenance.start-installer"
            && item.Outcome == expectedOutcome);
        if (failure is "before") { downloader.Calls.Should().Be(0); }
    }

    [Fact]
    public async Task Expiry_refresh_and_edited_target_retire_installer_approval()
    {
        using var f = new Fixture();
        f.State.BindUpgradeServices(new InstallerDownloader(), new InstallerLauncher());
        f.State.NetworkEnabled = true;
        await f.State.CheckAsync();
        await f.State.ReviewAsync();
        f.State.UpgradeApproved = true;
        f.Time.Now = f.Time.Now.AddHours(6);
        f.State.CanUpgrade.Should().BeFalse();
        f.Time.Now = f.State.NextCheck!.Value;
        f.Client.Result = f.Client.Success(f.Time.Now);
        await f.State.CheckAsync();
        f.State.UpgradeApproved.Should().BeFalse();
        await f.State.ReviewAsync();
        f.State.UpgradeApproved = true;
        f.State.RequestedVersionText = "1.2.4";
        f.State.CanReviewUpgrade.Should().BeFalse();
        f.State.UpgradeApproved.Should().BeFalse();
        f.State.RequestedVersionText = string.Empty;
        f.State.CanReviewUpgrade.Should().BeTrue();
    }

    [Theory]
    [InlineData(RequestOrigin.LocalUi)]
    [InlineData(RequestOrigin.ActivatedVoice)]
    public async Task Governed_update_tools_check_using_saved_consent_but_never_approve_or_start_an_installer(RequestOrigin origin)
    {
        using var f = new Fixture();
        using var downloader = new InstallerDownloader();
        var launcher = new InstallerLauncher();
        f.State.BindUpgradeServices(downloader, launcher);
        var check = new Kora.Tools.Application.ApplicationCheckForUpdates(f.Commands);
        var upgrade = new Kora.Tools.Application.ApplicationUpgrade(f.Commands);
        var disabled = await check.ExecuteAsync(null, origin, () => true, TestContext.Current.CancellationToken);
        disabled.Should().Contain("cannot grant network consent");
        f.Client.Calls.Should().Be(0);
        f.State.NetworkEnabled = true;
        var output = await upgrade.ExecuteAsync(ReleaseVersion.Parse("1.2.3"), origin, () => true, TestContext.Current.CancellationToken);
        output.Should().Contain("Available").And.Contain("separately approve");
        f.Client.Calls.Should().Be(1);
        f.State.UpgradeApproved.Should().BeFalse();
        downloader.Calls.Should().Be(0);
        launcher.Assets.Should().BeEmpty();
    }

    [Fact]
    public async Task Native_exact_and_latest_checks_share_verification_and_target_edits_revoke_approval()
    {
        using var f = new Fixture();
        f.State.BindUpgradeServices(new InstallerDownloader(), new InstallerLauncher());
        f.State.RequestedVersionText.Should().BeEmpty();
        f.State.NetworkEnabled = true;
        f.State.RequestedVersionText = "1.2.3";
        f.State.RequestedVersionText = "1.2.3";
        await f.State.CheckSelectedVersionCommand.ExecuteAsync();
        await f.State.ReviewAsync();
        f.State.UpgradeApproved = true;
        f.State.UpgradeApproved = false;
        f.State.UpgradeApproved = false;
        f.State.CanUpgrade.Should().BeFalse();
        f.Time.Now = f.State.NextCheck!.Value;
        f.State.RequestedVersionText = string.Empty;
        await f.State.CheckSelectedVersionCommand.ExecuteAsync();
        f.Client.Calls.Should().Be(2);
        f.State.RequestedVersionText = "not-a-version";
        var invalid = () => f.State.CheckSelectedVersionCommand.ExecuteAsync();
        await invalid.Should().ThrowAsync<InvalidDataException>();
    }

    [Fact]
    public async Task Missing_exact_version_service_fails_explicitly_without_download()
    {
        using var f = new Fixture();
        using var state = new MaintenanceViewModel(new LatestOnlyClient(),
            new AssemblyApplicationInfo(() => "1.0.0"), ReleaseArchitecture.X64, f.Opener, f.Time,
            new Dispatcher(), f.Audit, Microsoft.Extensions.Logging.Abstractions.NullLogger<MaintenanceViewModel>.Instance);
        state.BindGate(() => true);
        state.NetworkEnabled = true;
        state.RequestedVersionText = "1.2.3";
        var unavailable = () => state.CheckSelectedVersionCommand.ExecuteAsync();
        await unavailable.Should().ThrowAsync<InvalidOperationException>().WithMessage("*verification is unavailable*");
        f.Time.Now = state.NextCheck!.Value;
        state.CheckSelectedVersionCommand.Execute(null);
        state.Status.Should().Contain("operation failed");
        state.CanUpgrade.Should().BeFalse();
    }

    [Fact]
    public async Task A_dropped_dispatch_is_an_explicit_failure_not_an_empty_success()
    {
        using var f = new Fixture();
        using var state = new MaintenanceViewModel(f.Client, new AssemblyApplicationInfo(() => "1.0.0"),
            ReleaseArchitecture.X64, f.Opener, f.Time, new DroppedDispatcher(), f.Audit,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<MaintenanceViewModel>.Instance);
        var action = () => state.ExecuteUpdateAsync(null, false, () => true, CancellationToken.None);
        await action.Should().ThrowAsync<InvalidOperationException>().WithMessage("*dispatch did not produce*");
        f.Client.Calls.Should().Be(0);
    }

    [Theory]
    [InlineData(ReleaseArchitecture.X86)]
    [InlineData(ReleaseArchitecture.Unsupported)]
    public async Task Non_x64_releases_never_offer_an_installer(ReleaseArchitecture architecture)
    {
        using var f = new Fixture();
        using var state = new MaintenanceViewModel(f.Client, new AssemblyApplicationInfo(() => "1.0.0"),
            architecture, f.Opener, f.Time, new Dispatcher(), f.Audit,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<MaintenanceViewModel>.Instance);
        var commands = new MaintenanceCommands(state, f.Store, f.Store, new Kora.Application.Hosting.HostTaskCoordinator(f.Store));
        await using var lifetime = commands;
        state.BindCachedCommands(commands);
        state.BindGate(() => true);
        state.BindUpgradeServices(new InstallerDownloader(), new InstallerLauncher());
        state.NetworkEnabled = true;
        await state.CheckAsync();
        await state.ReviewAsync();
        state.CanReviewUpgrade.Should().BeFalse();
    }

    [Fact]
    public async Task Installer_services_are_required_for_native_approval()
    {
        using var f = new Fixture();
        f.State.NetworkEnabled = true;
        await f.State.CheckAsync();
        await f.State.ReviewAsync();
        f.State.CanReviewUpgrade.Should().BeFalse();
        f.State.UpgradeApproved = true;
        f.State.CanUpgrade.Should().BeFalse();
    }

    [Fact]
    public async Task Legacy_release_metadata_cannot_approve_an_installer_without_a_graceful_stop_contract()
    {
        using var f = new Fixture();
        f.State.BindUpgradeServices(new InstallerDownloader(), new InstallerLauncher());
        f.State.NetworkEnabled = true;
        f.Client.Result = f.Client.Success(f.Time.Now) with
        { Release = f.Client.Success(f.Time.Now).Release! with { InstallerProtocol = 0 } };
        await f.State.CheckAsync();
        await f.State.ReviewAsync();
        f.State.CanReviewUpgrade.Should().BeFalse();
        f.State.UpgradeApproved = true;
        f.State.CanUpgrade.Should().BeFalse();
    }

    [Fact]
    public async Task Unknown_check_results_are_returned_without_release_details_or_install_approval()
    {
        using var f = new Fixture();
        f.State.NetworkEnabled = true;
        f.Client.Result = new(ReleaseAvailability.Unknown, "fixture unavailable", f.Time.Now);
        var result = await f.Commands.CheckForUpdatesAsync(null, RequestOrigin.LocalUi, () => true, CancellationToken.None);
        result.Should().Contain("Unknown").And.NotContain("Expected SHA-256");
        f.State.UpgradeApproved.Should().BeFalse();
    }

    [Theory]
    [InlineData(false, 24)]
    [InlineData(true, 24)]
    [InlineData(false, 1)]
    public async Task Installer_revalidation_preserves_server_retry_deadlines_without_shortening_existing_cadence(bool afterDownload, int retryHours)
    {
        using var f = new Fixture();
        using var downloader = new InstallerDownloader();
        var launcher = new InstallerLauncher();
        f.State.BindUpgradeServices(downloader, launcher);
        f.State.NetworkEnabled = true;
        await f.State.CheckAsync();
        await f.State.ReviewAsync();
        f.State.UpgradeApproved = true;
        var priorDeadline = f.State.NextCheck!.Value;
        var serverDeadline = f.Time.Now.AddHours(retryHours);
        var limited = new ReleaseCheck(ReleaseAvailability.RateLimited, "fixture rate limit",
            f.Time.Now, RetryAt: serverDeadline);
        if (afterDownload) { downloader.AfterDownload = () => f.Client.Result = limited; }
        else { f.Client.Result = limited; }

        await f.State.UpgradeAsync();

        var expectedDeadline = serverDeadline > priorDeadline ? serverDeadline : priorDeadline;
        f.State.NextCheck.Should().Be(expectedDeadline);
        launcher.Assets.Should().BeEmpty();
        downloader.Calls.Should().Be(afterDownload ? 1 : 0);
        if (afterDownload) { downloader.Lease.Disposed.Should().BeTrue(); }
        var calls = f.Client.Calls;
        f.State.UpgradeApproved = true;
        f.State.CanUpgrade.Should().BeFalse();
        await f.State.UpgradeAsync();
        f.Client.Calls.Should().Be(calls);
        if (serverDeadline > priorDeadline)
        {
            f.Time.Now = priorDeadline;
            f.State.NetworkEnabled = false;
            f.State.NetworkEnabled = true;
            f.State.Channel = ReleaseChannel.Preview;
            f.State.CheckIntervalHours = 1;
            await f.State.CheckAsync();
            f.Time.Timers[0].Fire();
            f.Client.Calls.Should().Be(calls);
            f.State.NextCheck.Should().Be(serverDeadline);
        }
    }

    [Fact]
    public async Task A_late_rate_limit_after_disposal_cannot_rearm_or_change_the_dead_host()
    {
        using var f = new Fixture();
        using var downloader = new InstallerDownloader();
        var launcher = new InstallerLauncher();
        f.State.BindUpgradeServices(downloader, launcher);
        f.State.NetworkEnabled = true;
        await f.State.CheckAsync();
        await f.State.ReviewAsync();
        f.State.UpgradeApproved = true;
        var priorDeadline = f.State.NextCheck;
        var response = new TaskCompletionSource<ReleaseCheck>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Client.Pending = response.Task;
        var upgrade = f.State.UpgradeAsync();
        f.State.Dispose();
        response.SetResult(new(ReleaseAvailability.RateLimited, "fixture late response",
            f.Time.Now, RetryAt: f.Time.Now.AddHours(24)));
        await upgrade;
        f.State.NextCheck.Should().Be(priorDeadline);
        f.Time.Timers[0].Disposed.Should().BeTrue();
        downloader.Calls.Should().Be(0);
        launcher.Assets.Should().BeEmpty();
    }

    [Fact]
    public async Task Revocation_after_final_metadata_verification_still_prevents_process_start()
    {
        using var f = new Fixture();
        using var downloader = new InstallerDownloader();
        var launcher = new InstallerLauncher();
        f.State.BindUpgradeServices(downloader, launcher);
        f.State.NetworkEnabled = true;
        await f.State.CheckAsync();
        await f.State.ReviewAsync();
        f.State.UpgradeApproved = true;
        var finalAdmissionReads = 0;
        f.State.BindCachedCommands(f.Commands, () => f.Client.Calls < 3 || ++finalAdmissionReads == 1);
        await f.State.UpgradeAsync();
        launcher.Assets.Should().BeEmpty();
        downloader.Lease.Disposed.Should().BeTrue();
        f.State.Status.Should().Contain("before installer startup");
    }

    [Theory]
    [InlineData("after-launch")]
    [InlineData("dispose")]
    [InlineData("cancel")]
    [InlineData("prelaunch-revocation")]
    public async Task Upgrade_terminal_outcome_preserves_late_effect_uncertainty_and_cancellation(string change)
    {
        using var f = new Fixture();
        using var downloader = new InstallerDownloader();
        var launcher = new InstallerLauncher();
        f.State.BindUpgradeServices(downloader, launcher);
        f.State.NetworkEnabled = true;
        await f.State.CheckAsync();
        await f.State.ReviewAsync();
        f.State.UpgradeApproved = true;
        if (change is "after-launch") { launcher.AfterLaunch = () => f.Admitted = false; }
        if (change is "dispose") { downloader.AfterDownload = f.State.Dispose; }
        if (change is "cancel") { downloader.Failure = new OperationCanceledException("fixture"); }
        if (change is "prelaunch-revocation") { f.State.BindCachedCommands(f.Commands, () => false); }
        await f.State.UpgradeAsync();
        f.State.UpgradeApproved.Should().BeFalse();
        f.Audit.Events.Should().NotContain(item => item.ActionId == "maintenance.start-installer" && item.Outcome == SecurityAuditOutcome.Succeeded);
        if (change is "after-launch")
        {
            launcher.Assets.Should().ContainSingle();
            f.State.Status.Should().Contain("unconfirmed");
        }
        else { launcher.Assets.Should().BeEmpty(); }
    }

    [Theory]
    [InlineData("origin")]
    [InlineData("eligibility")]
    [InlineData("host")]
    [InlineData("cancel")]
    [InlineData("busy")]
    [InlineData("late-eligibility")]
    [InlineData("late-host")]
    public async Task Direct_admitted_action_requires_fresh_original_authority_and_reports_busy_without_a_second_check(string failure)
    {
        using var f = new Fixture();
        f.State.NetworkEnabled = true;
        var eligible = true;
        using var cancellation = new CancellationTokenSource();
        using var root = HostActivity.BeginRoot(HostRequest.Create(failure is "origin" ? RequestOrigin.HostSystem : RequestOrigin.LocalUi),
            HostActivityLayer.Application, HostOperation.Request);
        if (failure is "eligibility") { eligible = false; }
        if (failure is "host") { f.Admitted = false; }
        if (failure is "cancel") { cancellation.Cancel(); }
        var pending = new TaskCompletionSource<ReleaseCheck>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (failure is "busy" or "late-eligibility" or "late-host") { f.Client.Pending = pending.Task; }
        if (failure is "busy")
        {
            var check = f.State.CheckAsync();
            var busy = await f.State.ExecuteUpdateAsync(null, false, () => true, cancellation.Token);
            busy.Should().Contain("busy");
            pending.SetResult(f.Client.Success(f.Time.Now));
            await check;
        }
        else
        {
            var task = f.State.ExecuteUpdateAsync(null, false, () => eligible, cancellation.Token);
            if (failure is "late-eligibility" or "late-host")
            {
                if (failure is "late-eligibility") { eligible = false; }
                else { f.Admitted = false; }
                pending.SetResult(f.Client.Success(f.Time.Now));
            }
            var action = () => task;
            if (failure is "cancel") { await action.Should().ThrowAsync<OperationCanceledException>(); }
            else { await action.Should().ThrowAsync<InvalidOperationException>(); }
        }
        root.Complete(HostOperationOutcome.Completed);
    }

    private sealed class LatestOnlyClient : IReleaseMetadataClient
    {
        public Task<ReleaseCheck> CheckAsync(ReleaseChannel channel, string currentVersion,
            ReleaseArchitecture architecture, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class DroppedDispatcher : IUiDispatcher
    {
        public Task InvokeAsync(Func<Task> action) => Task.CompletedTask;
        public void Post(Action action) { }
    }

    private sealed class InstallerDownloader : IReleaseInstallerDownloader, IDisposable
    {
        internal int Calls;
        internal readonly List<ReleaseMetadata> Releases = [];
        internal readonly InstallerLease Lease = new();
        internal Task<IReleaseInstallerDownload>? Pending;
        internal Exception? Failure;
        internal Action? AfterDownload;
        public void Dispose() => Lease.Disposed = true;
        public Task<IReleaseInstallerDownload> DownloadAsync(ReleaseMetadata release, CancellationToken cancellationToken)
        {
            Calls++;
            Releases.Add(release);
            if (Failure is not null) { throw Failure; }
            AfterDownload?.Invoke();
            return Pending ?? Task.FromResult<IReleaseInstallerDownload>(Lease);
        }
    }

    private sealed class InstallerLease : IReleaseInstallerDownload
    {
        internal bool Disposed;
        public string Path => @"Q:\verified-test-fixture\Kora-Setup-1.2.3-win-x64.exe";
        public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
    }

    private sealed class InstallerLauncher : IReleaseInstallerLauncher
    {
        internal readonly List<ReleaseAsset> Assets = [];
        internal IOException? Failure;
        internal Action? AfterLaunch;
        public Task LaunchAsync(string path, ReleaseAsset installer, Func<bool> admitted, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            admitted().Should().BeTrue();
            if (Failure is not null) { throw Failure; }
            Assets.Add(installer);
            AfterLaunch?.Invoke();
            return Task.CompletedTask;
        }
    }
}
