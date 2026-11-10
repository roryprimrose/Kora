using AwesomeAssertions;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Maintenance;
using Kora.Core.Tools;

namespace Kora.Application.UnitTests.Maintenance;

public sealed partial class MaintenanceViewModelTests
{
    [Theory]
    [InlineData(InstallerStartOutcome.Started)]
    [InlineData(InstallerStartOutcome.Denied)]
    public async Task NativeUpgradeUsesTheSameGovernedLauncherAndTruthfulStartupMessage(InstallerStartOutcome outcome)
    {
        using var f = new Fixture();
        using var downloader = new InstallerDownloader();
        var launcher = new InstallerLauncher();
        f.State.NetworkEnabled = true;
        f.State.BindUpgradeServices(downloader, launcher);
        f.State.BindInstallerReview((_, _, _) => Task.FromResult(outcome));

        var message = await f.Commands.RequestUpgradeAsync(null, RequestOrigin.LocalUi,
            static () => true, TestContext.Current.CancellationToken);

        message.Should().NotBeNullOrWhiteSpace();
        if (outcome == InstallerStartOutcome.Denied) { message.Should().Contain("Installer was not started"); }
        downloader.Calls.Should().Be(0);
        launcher.Assets.Should().BeEmpty();
    }

    [Fact]
    public async Task MaintenanceObservationsDistinguishUncheckedAndCurrentWithoutARelease()
    {
        using var f = new Fixture();
        f.State.ReadMcpObservation().Availability.Should().Be(ReleaseAvailability.Unknown);
        f.State.ReadMcpObservation().ReleaseVersion.Should().BeNull();
        f.State.NetworkEnabled = true;
        var now = f.Time.GetUtcNow();
        f.Client.Result = new(ReleaseAvailability.UpToDate, "current", now, now, null);
        using var root = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Application, HostOperation.Request);

        var reply = await f.Commands.InvokeAsync(MaintenanceTools.Check, "{}", static () => true, TestContext.Current.CancellationToken);

        reply.Maintenance!.Availability.Should().Be(ReleaseAvailability.UpToDate);
        reply.Maintenance.ReleaseVersion.Should().BeNull();
        reply.Maintenance.Installer.Should().BeNull();
    }

    [Fact]
    public async Task VerifiedMetadataCannotEscapeWhenHostAdmissionClosesBeforeReturning()
    {
        using var f = new Fixture();
        f.State.NetworkEnabled = true;
        using var root = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Application, HostOperation.Request);
        bool Current()
        {
            if (f.Client.Calls > 0) { f.Admitted = false; }
            return true;
        }

        await f.State.Invoking(value => value.ExecuteUpdateAsync(null, Current,
            TestContext.Current.CancellationToken)).Should().ThrowAsync<InvalidOperationException>();

        f.Client.Calls.Should().Be(1);
        f.State.CanOpen.Should().BeFalse();
    }

    [Theory]
    [InlineData("none")]
    [InlineData("system")]
    [InlineData("unknown")]
    public async Task McpMaintenanceRequiresOriginalInputAndAnExactKnownTool(string boundary)
    {
        using var f = new Fixture();
        if (boundary is "none")
        {
            (await f.Commands.InvokeAsync(MaintenanceTools.Check, "{}", () => true, TestContext.Current.CancellationToken))
                .Outcome.Should().Be(CapabilityOutcome.Denied);
        }
        else
        {
            using var root = HostActivity.BeginRoot(HostRequest.Create(boundary is "system" ? RequestOrigin.HostSystem : RequestOrigin.LocalUi),
                HostActivityLayer.Application, HostOperation.Request);
            (await f.Commands.InvokeAsync(boundary is "unknown" ? "unknown" : MaintenanceTools.Check, "{}",
                () => true, TestContext.Current.CancellationToken)).Outcome.Should().Be(CapabilityOutcome.Denied);
        }
        f.Client.Calls.Should().Be(0);
    }

    [Theory]
    [InlineData("metadata")]
    [InlineData("receipt")]
    [InlineData("late")]
    [InlineData("cancel")]
    public async Task McpMaintenanceCannotReturnLateSuccessOrHideFailedReceipts(string boundary)
    {
        using var f = new Fixture();
        f.State.NetworkEnabled = true;
        using var cancellation = new CancellationTokenSource();
        using var root = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Application, HostOperation.Request);
        var current = true;
        f.Store.BeforeCommit = record =>
        {
            if (f.Client.Calls == 0 || record.State != HostTaskState.Succeeded) { return; }
            if (boundary is "receipt") { f.Store.FailTerminal = true; }
            if (boundary is "late") { current = false; }
            if (boundary is "cancel") { cancellation.Cancel(); }
        };
        if (boundary is "metadata") { f.Client.Pending = Task.FromException<ReleaseCheck>(new IOException("Metadata unavailable.")); }
        var invoke = () => f.Commands.InvokeAsync(MaintenanceTools.Check, "{}", () => current, cancellation.Token).AsTask();

        await invoke.Should().ThrowAsync<Exception>();

        f.Store.FailTerminal = false;
        if (boundary is "receipt")
        {
            (await f.Commands.InvokeAsync(MaintenanceTools.Check, "{}", () => true, TestContext.Current.CancellationToken))
                .Outcome.Should().Be(CapabilityOutcome.Denied);
        }
    }

    [Fact]
    public async Task McpCheckReturnsFreshMetadataWithoutOpeningInstallerReview()
    {
        using var f = new Fixture();
        f.State.NetworkEnabled = true;
        using var root = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Application, HostOperation.Request);

        var result = await f.Commands.InvokeAsync(MaintenanceTools.Check, "{}", () => true, TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(CapabilityOutcome.Succeeded);
        result.Maintenance!.ReleaseVersion.Should().Be("1.2.3");
        result.Maintenance.Installer.Should().BeNull();
    }

    [Fact]
    public async Task RetiringTheCallerDuringNativeReviewCannotClaimInstallerStartup()
    {
        using var f = new Fixture();
        f.State.NetworkEnabled = true;
        f.State.BindUpgradeServices(new InstallerDownloader(), new InstallerLauncher());
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var review = new TaskCompletionSource<InstallerStartOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.State.BindInstallerReview((_, _, _) => { entered.SetResult(); return review.Task; });
        var current = true;
        using var root = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Application, HostOperation.Request);
        var running = f.Commands.InvokeAsync(MaintenanceTools.Upgrade, "{}", () => current, TestContext.Current.CancellationToken).AsTask();
        await entered.Task;
        current = false;
        review.SetResult(InstallerStartOutcome.Started);

        await this.Invoking(_ => running).Should().ThrowAsync<InvalidOperationException>();
    }

    [Theory]
    [InlineData(RequestOrigin.HostSystem)]
    [InlineData(RequestOrigin.LocalUi)]
    public async Task NativeUpgradeEntryCannotRelabelAmbientAuthority(RequestOrigin origin)
    {
        using var f = new Fixture();
        using var root = HostActivity.BeginRoot(HostRequest.Create(origin), HostActivityLayer.Application, HostOperation.Request);
        await f.Commands.Invoking(value => value.RequestUpgradeAsync(null, origin, () => true,
            TestContext.Current.CancellationToken)).Should().ThrowAsync<InvalidOperationException>();
        f.Client.Calls.Should().Be(0);
    }

    [Theory]
    [InlineData(InstallerStartOutcome.Started)]
    [InlineData(InstallerStartOutcome.Denied)]
    [InlineData(InstallerStartOutcome.Cancelled)]
    [InlineData(InstallerStartOutcome.Failed)]
    [InlineData(InstallerStartOutcome.Unknown)]
    public async Task McpUpgradeWaitsForNativeReviewAndReportsOnlyInstallerStartup(InstallerStartOutcome outcome)
    {
        using var f = new Fixture();
        using var downloader = new InstallerDownloader();
        var launcher = new InstallerLauncher();
        f.State.BindUpgradeServices(downloader, launcher);
        f.State.NetworkEnabled = true;
        var reviews = 0;
        f.State.BindInstallerReview((target, current, token) =>
        {
            token.ThrowIfCancellationRequested();
            target.Release!.Version.ToString().Should().Be("1.2.3");
            current().Should().BeTrue();
            reviews++;
            return Task.FromResult(outcome);
        });
        using var root = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Application, HostOperation.Request);

        var reply = await f.Commands.InvokeAsync(MaintenanceTools.Upgrade, "{}", () => true, TestContext.Current.CancellationToken);

        reviews.Should().Be(1);
        reply.Outcome.Should().Be(outcome == InstallerStartOutcome.Started ? CapabilityOutcome.Succeeded : CapabilityOutcome.Denied);
        if (outcome == InstallerStartOutcome.Started) { reply.Maintenance!.Installer.Should().Be(InstallerStartOutcome.Started); }
        f.State.UpgradeApproved.Should().BeFalse();
        downloader.Calls.Should().Be(0, "the tool cannot replace a native approval with a model grant");
        launcher.Assets.Should().BeEmpty();
    }

    [Fact]
    public async Task McpWaitingReviewDoesNotHoldTheNativeMaintenanceControlLock()
    {
        using var f = new Fixture();
        f.State.BindUpgradeServices(new InstallerDownloader(), new InstallerLauncher());
        f.State.NetworkEnabled = true;
        var review = new TaskCompletionSource<InstallerStartOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.State.BindInstallerReview((_, _, _) => { entered.SetResult(); return review.Task; });
        var invocation = Task.Run(async () =>
        {
            using var root = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Application, HostOperation.Request);
            return await f.Commands.InvokeAsync(MaintenanceTools.Upgrade, "{}", () => true, TestContext.Current.CancellationToken);
        }, TestContext.Current.CancellationToken);
        await entered.Task;

        await f.Commands.ExecuteAsync(MaintenanceCommand.Review, RequestOrigin.LocalUi, () => true, TestContext.Current.CancellationToken);
        f.State.CanOpen.Should().BeTrue();
        invocation.IsCompleted.Should().BeFalse();
        review.SetResult(InstallerStartOutcome.Denied);
        (await invocation).Outcome.Should().Be(CapabilityOutcome.Denied);
    }

    [Theory]
    [InlineData("network")]
    [InlineData("host")]
    [InlineData("version")]
    [InlineData("input")]
    public async Task McpMaintenanceCannotGrantNetworkAccessOrSubstituteAnotherVersion(string denial)
    {
        using var f = new Fixture();
        if (denial is not "network") { f.State.NetworkEnabled = true; }
        using var root = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Application, HostOperation.Request);
        var input = denial is "version" ? """{"version":"1.2.2"}""" : denial is "input" ? """{"approved":true}""" : "{}";

        var reply = await f.Commands.InvokeAsync(MaintenanceTools.Check, input, () => denial is not "host",
            TestContext.Current.CancellationToken);

        reply.Outcome.Should().Be(CapabilityOutcome.Denied);
        if (denial is "network" or "host" or "input") { f.Client.Calls.Should().Be(0); }
    }
}
