using System.Diagnostics;
using AwesomeAssertions;
using Kora.Application.Maintenance;
using Kora.Core.Auditing;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Maintenance;

namespace Kora.Application.UnitTests.Maintenance;

public sealed partial class MaintenanceViewModelTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnsavedAndDisabledSettingsNeverScheduleStartupOrPeriodicNetwork(bool saved)
    {
        var preferences = new Preferences { Settings = saved ? MaintenanceSettings.Default : null };
        using var f = new Fixture(preferences);
        f.State.NetworkEnabled.Should().BeFalse();
        f.State.IncludePrereleases.Should().BeFalse();
        f.State.CheckIntervalHours.Should().Be(6);
        f.State.NextCheck.Should().BeNull();
        f.Time.Timers[0].Due.Should().Be(Timeout.InfiniteTimeSpan);
        f.Time.Timers[0].Fire();
        await f.State.CheckAsync();
        f.Client.Calls.Should().Be(0);
        preferences.Writes.Should().Be(0);
        f.Audit.Events.Should().BeEmpty();
        f.State.PreferencesStatus.Should().Contain(saved ? "saved" : "until");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EveryEnabledNetworkStateHasAnEligibleDeadlineBeforeChecking(bool savedOptIn)
    {
        using var f = new Fixture(new Preferences { Settings = savedOptIn ? new(true, false, 6) : null });
        if (!savedOptIn)
        {
            f.State.NextCheck.Should().BeNull();
            f.State.NetworkEnabled = true;
        }
        var established = f.Time.Now;
        f.State.NextCheck.Should().Be(established);
        f.State.NetworkEnabled = false;
        f.Time.Now = f.Time.Now.AddHours(1);
        f.State.NextCheck.Should().Be(established);
        f.State.NetworkEnabled = true;
        f.State.NextCheck.Should().Be(established);
        f.Client.Calls.Should().Be(0);
    }

    [Fact]
    public void LoadingDoesNotRequireNativeGestureAndStartupWaitsForExplicitHostInitialization()
    {
        var preferences = new Preferences { Settings = new(true, true, 12) };
        using var root = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.HostSystem), HostActivityLayer.Application, HostOperation.Runtime);
        using var f = new Fixture(preferences, bindGate: false, initializeConfiguredChecks: false);
        f.State.NetworkEnabled.Should().BeTrue();
        f.State.IncludePrereleases.Should().BeTrue();
        f.State.Channel.Should().Be(ReleaseChannel.Preview);
        f.State.CheckIntervalHours.Should().Be(12);
        f.Time.Timers[0].Due.Should().Be(Timeout.InfiniteTimeSpan);
        f.Time.Timers[0].Fire();
        f.Client.Calls.Should().Be(0);
        f.Admitted = false;
        f.State.BindGate(() => f.Admitted);
        f.Time.Timers[0].Due.Should().Be(Timeout.InfiniteTimeSpan);
        f.Time.Timers[0].Fire();
        f.Client.Calls.Should().Be(0);
        f.Admitted = true;
        f.State.InitializeConfiguredChecks();
        f.Time.Timers[0].Due.Should().Be(TimeSpan.Zero);
        f.Time.Timers[0].Fire();
        f.Client.Calls.Should().Be(1);
        f.Client.Channel.Should().Be(ReleaseChannel.Preview);
        f.Audit.Events.Should().BeEmpty();
        preferences.Writes.Should().Be(0);
    }

    [Fact]
    public async Task ClosedStartupGateDoesNotReplayWhenAdmissionReturns()
    {
        using var f = new Fixture(new Preferences { Settings = new(true, false, 6) }, bindGate: false, initializeConfiguredChecks: false);
        f.Admitted = false;
        f.State.BindGate(() => f.Admitted);
        f.State.InitializeConfiguredChecks();
        f.Time.Timers[0].Due.Should().Be(TimeSpan.FromHours(6));
        f.Admitted = true;
        f.State.BindGate(() => f.Admitted);
        f.State.InitializeConfiguredChecks();
        f.Client.Calls.Should().Be(0);
        f.Time.Timers[0].Due.Should().Be(TimeSpan.FromHours(6));
        await f.State.CheckAsync();
        f.Client.Calls.Should().Be(0);
        f.Time.Now = f.State.NextCheck!.Value;
        f.Time.Timers[0].Fire();
        f.Client.Calls.Should().Be(1);
    }

    [Fact]
    public void ConfiguredStartupLinksItsIndependentCheckToHostInitialization()
    {
        using var f = new Fixture(new Preferences { Settings = new(true, false, 6) },
            initializeConfiguredChecks: false);
        var stopped = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = stopped.Add,
        };
        ActivitySource.AddActivityListener(listener);
        ActivityContext initializedContext;
        using (var root = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.HostSystem),
            HostActivityLayer.Application, HostOperation.Runtime))
        {
            initializedContext = Activity.Current!.Context;
            f.State.InitializeConfiguredChecks();
        }
        f.Time.Timers[0].Fire();
        f.Client.Calls.Should().Be(1);
        stopped.Should().Contain(activity => activity.OperationName.Equals("runtime.request", StringComparison.Ordinal)
            && activity.Links.Any(link => link.Context == initializedContext));
    }

    [Fact]
    public void InitializationWithoutABoundGateIsConsumedWithoutLaterReplay()
    {
        using var f = new Fixture(new Preferences { Settings = new(true, false, 6) },
            bindGate: false, initializeConfiguredChecks: false);
        f.Time.Timers[0].Due.Should().Be(Timeout.InfiniteTimeSpan);
        f.State.InitializeConfiguredChecks();
        f.Time.Timers[0].Due.Should().Be(Timeout.InfiniteTimeSpan);
        f.State.BindGate(() => true);
        f.Time.Timers[0].Due.Should().Be(Timeout.InfiniteTimeSpan);
        f.State.InitializeConfiguredChecks();
        f.Time.Timers[0].Fire();
        f.Client.Calls.Should().Be(0);
        f.Time.Timers[0].Due.Should().Be(Timeout.InfiniteTimeSpan);
    }

    [Fact]
    public void PrivacyClosureBeforeInitializationDoesNotBecomeAStartupReplay()
    {
        using var f = new Fixture(new Preferences { Settings = new(true, false, 6) },
            initializeConfiguredChecks: false);
        f.Time.Timers[0].Due.Should().Be(Timeout.InfiniteTimeSpan);
        f.State.PrivacyClosed();
        f.Time.Timers[0].Due.Should().Be(Timeout.InfiniteTimeSpan);
        f.State.InitializeConfiguredChecks();
        var future = f.Time.Now.AddHours(6);
        f.State.NextCheck.Should().Be(future);
        f.Time.Timers[0].Due.Should().Be(TimeSpan.FromHours(6));
        f.State.InitializeConfiguredChecks();
        f.Time.Timers[0].Fire();
        f.Client.Calls.Should().Be(0);
        f.State.NextCheck.Should().Be(future);
        f.Time.Timers[0].Due.Should().Be(TimeSpan.FromHours(6));
        f.Time.Now = future;
        f.Client.Result = f.Client.Success(f.Time.Now);
        f.Time.Timers[0].Fire();
        f.Client.Calls.Should().Be(1);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(6)]
    [InlineData(168)]
    public async Task CustomIntervalsScheduleChecksWithoutExtendingMetadataFreshness(int hours)
    {
        using var f = new Fixture(new Preferences { Settings = new(true, false, hours) });
        await f.State.CheckAsync();
        f.State.NextCheck.Should().Be(f.Time.Now.AddHours(hours).AddMinutes(15));
        f.Time.Timers[0].Due.Should().Be(hours == 1 ? TimeSpan.FromMinutes(75) : MaintenanceViewModel.Freshness);
        if (hours == 168)
        {
            f.Time.Now += MaintenanceViewModel.Freshness;
            f.Time.Timers[0].Fire();
            f.Client.Calls.Should().Be(1);
            f.State.IsStale.Should().BeTrue();
            f.State.CanReview.Should().BeFalse();
            f.Time.Timers[0].Due.Should().Be(TimeSpan.FromHours(162).Add(TimeSpan.FromMinutes(15)));
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(169)]
    public void InvalidIntervalEditsAreExplicitAndDoNotChangeTheSetting(int hours)
    {
        using var f = new Fixture();
        f.State.Invoking(value => value.CheckIntervalHours = hours).Should().Throw<InvalidDataException>();
        f.State.CheckIntervalHours.Should().Be(6);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExplicitNativeSavePersistsAllSettingsAndRestartResumesOnlyWhenEnabled(bool enabled)
    {
        var preferences = new Preferences();
        using (var f = new Fixture(preferences))
        {
            f.State.NetworkEnabled = enabled;
            f.State.IncludePrereleases = true;
            f.State.IncludePrereleases = true;
            f.State.CheckIntervalHours = 37;
            f.State.CheckIntervalHours = 37;
            preferences.Settings.Should().BeNull();
            await f.State.SavePreferencesCommand.ExecuteAsync();
            preferences.Settings.Should().Be(new MaintenanceSettings(enabled, true, 37));
            preferences.Pending.Should().BeFalse();
            f.State.PreferencesStatus.Should().Contain("saved");
            f.Audit.Events.Where(value => value.ActionId is "maintenance.settings").Select(value => value.Outcome)
                .Should().Equal(SecurityAuditOutcome.Requested, SecurityAuditOutcome.Succeeded);
            f.State.CheckIntervalHours = 38;
            f.State.PreferencesStatus.Should().Contain("until");
        }
        using var restarted = new Fixture(preferences);
        restarted.State.NetworkEnabled.Should().Be(enabled);
        restarted.State.IncludePrereleases.Should().BeTrue();
        restarted.State.CheckIntervalHours.Should().Be(37);
        restarted.Time.Timers[0].Due.Should().Be(enabled ? TimeSpan.Zero : Timeout.InfiniteTimeSpan);
        restarted.Client.Calls.Should().Be(0);
    }

    [Fact]
    public async Task SavedSettingsCannotShortenCadenceOrRateLimitAcrossManualAndToggleChanges()
    {
        using var f = new Fixture(new Preferences());
        f.State.NetworkEnabled = true;
        f.Client.Result = new(ReleaseAvailability.RateLimited, "rate-limited", f.Time.Now, RetryAt: f.Time.Now.AddHours(24));
        await f.State.CheckAsync();
        var retry = f.State.NextCheck;
        f.State.NetworkEnabled = false;
        f.State.NetworkEnabled = true;
        f.State.IncludePrereleases = true;
        f.State.IncludePrereleases = false;
        f.State.Channel.Should().Be(ReleaseChannel.Production);
        f.State.CheckIntervalHours = 1;
        await f.State.SavePreferencesAsync();
        await f.State.CheckAsync();
        f.Time.Timers[0].Fire();
        f.State.NextCheck.Should().Be(retry);
        f.Client.Calls.Should().Be(1);
        f.State.Status.Should().Contain("deferred");
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData("io")]
    [InlineData("access")]
    [InlineData("unavailable")]
    public async Task LoadFailuresAreSurfacedAndCannotEnableNetworkUntilAnExplicitConfirmedRepair(string failure)
    {
        var preferences = new Preferences
        {
            LoadFailure = failure switch
            {
                "invalid" => new InvalidDataException("fixture"),
                "io" => new IOException("fixture"),
                "access" => new UnauthorizedAccessException("fixture"),
                _ => new InvalidOperationException("fixture"),
            },
        };
        using var f = new Fixture(preferences);
        f.State.NetworkEnabled.Should().BeFalse();
        f.State.PreferencesStatus.Should().Contain("could not").And.Contain("remain off");
        f.State.Status.Should().Contain("Unknown");
        f.State.NetworkEnabled = true;
        await f.State.CheckAsync();
        f.Time.Timers[0].Fire();
        f.Client.Calls.Should().Be(0);
        f.Time.Timers[0].Due.Should().Be(Timeout.InfiniteTimeSpan);
        preferences.LoadFailure = null;
        await f.State.SavePreferencesAsync();
        f.State.NetworkEnabled.Should().BeTrue();
        f.State.PreferencesStatus.Should().Contain("saved");
        f.Time.Timers[0].Due.Should().Be(TimeSpan.Zero);
        await f.State.CheckAsync();
        f.Client.Calls.Should().Be(1);
    }

    [Theory]
    [InlineData("begin")]
    [InlineData("save")]
    [InlineData("audit")]
    [InlineData("confirm")]
    public async Task UncertainWriteAndAuditFailuresKeepChecksOffAcrossRestartUntilExplicitRepair(string phase)
    {
        var preferences = new Preferences();
        using (var f = new Fixture(preferences))
        {
            f.State.NetworkEnabled = true;
            if (phase is "begin") { preferences.OnBegin = () => throw new IOException("begin fixture"); }
            if (phase is "save") { preferences.OnSave = () => throw new IOException("save fixture"); }
            if (phase is "confirm") { preferences.OnConfirm = () => throw new IOException("confirm fixture"); }
            if (phase is "audit")
            {
                f.Audit.BeforeWrite = record =>
                {
                    if (record.ActionId is "maintenance.settings" && record.Outcome == SecurityAuditOutcome.Succeeded)
                    { throw new IOException("terminal fixture"); }
                };
            }
            await f.State.SavePreferencesAsync();
            f.State.NetworkEnabled.Should().BeFalse();
            f.State.PreferencesStatus.Should().Contain("could not");
            preferences.Pending.Should().BeTrue();
            f.Time.Timers[0].Fire();
            f.Client.Calls.Should().Be(0);
        }
        using var restarted = new Fixture(preferences);
        restarted.State.NetworkEnabled.Should().BeFalse();
        restarted.Time.Timers[0].Due.Should().Be(Timeout.InfiniteTimeSpan);
        preferences.OnBegin = null;
        preferences.OnSave = null;
        preferences.OnConfirm = null;
        restarted.State.NetworkEnabled = true;
        await restarted.State.SavePreferencesAsync();
        preferences.Pending.Should().BeFalse();
        restarted.State.NetworkEnabled.Should().BeTrue();
        restarted.State.PreferencesStatus.Should().Contain("saved");
    }

    [Theory]
    [InlineData(RequestOrigin.ActivatedVoice)]
    [InlineData(RequestOrigin.HostSystem)]
    public async Task PreferenceSaveCannotRelabelAmbientOriginsAsANativeGesture(RequestOrigin origin)
    {
        var preferences = new Preferences();
        using var f = new Fixture(preferences);
        using var root = HostActivity.BeginRoot(HostRequest.Create(origin), HostActivityLayer.Application, HostOperation.Request);
        f.State.IncludePrereleases = true;
        f.State.CheckIntervalHours = 12;
        await f.State.SavePreferencesAsync();
        preferences.Writes.Should().Be(0);
        preferences.Pending.Should().BeFalse();
        f.State.IncludePrereleases.Should().BeFalse();
        f.State.CheckIntervalHours.Should().Be(6);
        f.Audit.Events.Should().NotContain(value => value.Outcome == SecurityAuditOutcome.Succeeded);
        f.Client.Calls.Should().Be(0);
    }

    [Theory]
    [InlineData("requested")]
    [InlineData("write")]
    [InlineData("terminal")]
    [InlineData("confirm")]
    [InlineData("dispose")]
    public async Task SaveRevalidatesAdmissionDuringPersistenceAndAuditWithoutLateEnablement(string boundary)
    {
        var preferences = new Preferences();
        using var f = new Fixture(preferences);
        f.State.NetworkEnabled = true;
        var notifications = 0;
        var atDisposal = -1;
        f.State.PropertyChanged += (_, _) => notifications++;
        f.Audit.BeforeWrite = record =>
        {
            if (record.ActionId is not "maintenance.settings") { return; }
            if (boundary is "requested" && record.Outcome == SecurityAuditOutcome.Requested) { f.Admitted = false; }
            if (record.Outcome == SecurityAuditOutcome.Succeeded)
            {
                if (boundary is "terminal") { f.Admitted = false; }
                if (boundary is "dispose")
                {
                    f.State.Dispose();
                    atDisposal = notifications;
                }
            }
        };
        if (boundary is "write") { preferences.OnSave = f.State.PrivacyClosed; }
        if (boundary is "confirm") { preferences.OnConfirm = () => f.Admitted = false; }
        await f.State.SavePreferencesAsync();
        preferences.Pending.Should().Be(boundary is not "requested");
        if (boundary is "requested") { preferences.Writes.Should().Be(0); }
        if (boundary is not "dispose") { f.State.NetworkEnabled.Should().BeFalse(); }
        else { notifications.Should().Be(atDisposal); }
        f.Time.Timers[0].Fire();
        f.Client.Calls.Should().Be(0);
    }

    [Fact]
    public async Task InvalidatedInflightChecksRetainCooldownAndSuppressLateMetadata()
    {
        using var f = new Fixture(new Preferences());
        f.State.NetworkEnabled = true;
        var pending = new TaskCompletionSource<ReleaseCheck>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Client.Pending = pending.Task;
        var checking = f.State.CheckAsync();
        f.State.NetworkEnabled = false;
        f.State.NetworkEnabled = true;
        f.State.CheckIntervalHours = 1;
        f.Client.Token.IsCancellationRequested.Should().BeTrue();
        f.State.NextCheck.Should().Be(f.Time.Now.AddMinutes(5));
        pending.SetResult(f.Client.Success(f.Time.Now));
        await checking;
        f.State.LastVerified.Should().BeNull();
        f.State.Availability.Should().Be(ReleaseAvailability.Unknown);
        await f.State.CheckAsync();
        f.Client.Calls.Should().Be(1);
        f.Time.Timers[0].Due.Should().Be(TimeSpan.FromMinutes(5));
    }

    [Fact]
    public async Task ConcurrentSaveCannotStartASecondPersistenceOrAuditTransaction()
    {
        var preferences = new Preferences();
        using var f = new Fixture(preferences);
        Task? duplicate = null;
        string? pendingStatus = null;
        preferences.OnSave = () =>
        {
            pendingStatus = f.State.PreferencesStatus;
            duplicate = f.State.SavePreferencesAsync();
        };
        await f.State.SavePreferencesAsync();
        pendingStatus.Should().Contain("Saving settings").And.Contain("not yet confirmed");
        duplicate.Should().NotBeNull();
        duplicate!.IsCompletedSuccessfully.Should().BeTrue();
        preferences.Writes.Should().Be(1);
        preferences.Pending.Should().BeFalse();
        f.Audit.Events.Where(value => value.ActionId is "maintenance.settings").Select(value => value.Outcome)
            .Should().Equal(SecurityAuditOutcome.Requested, SecurityAuditOutcome.Succeeded);
    }

    [Fact]
    public async Task SavingBeforeGateBindingCannotPersistEnableOrArmAnyChecks()
    {
        var preferences = new Preferences();
        using var f = new Fixture(preferences, bindGate: false, initializeConfiguredChecks: false);
        await f.State.SavePreferencesAsync();
        preferences.Writes.Should().Be(0);
        preferences.Pending.Should().BeFalse();
        f.State.NetworkEnabled.Should().BeFalse();
        f.Time.Timers[0].Due.Should().Be(Timeout.InfiniteTimeSpan);
        f.Client.Calls.Should().Be(0);
        f.Audit.Events.Where(value => value.ActionId is "maintenance.settings").Select(value => value.Outcome)
            .Should().Equal(SecurityAuditOutcome.Requested, SecurityAuditOutcome.Failed);
    }

    [Fact]
    public async Task SettingsEditsAndQueuedTimersDuringSaveCannotRearmOrCheckAnUnconfirmedSnapshot()
    {
        var preferences = new Preferences { Settings = new(true, false, 6) };
        using var f = new Fixture(preferences);
        preferences.OnSave = () =>
        {
            f.State.CheckIntervalHours = 12;
            f.Time.Timers[0].Due.Should().Be(Timeout.InfiniteTimeSpan);
            f.Time.Timers[0].Fire();
            f.Client.Calls.Should().Be(0);
            f.State.PrivacyClosed();
            f.Time.Timers[0].Due.Should().Be(Timeout.InfiniteTimeSpan);
        };
        await f.State.SavePreferencesAsync();
        f.State.CheckIntervalHours.Should().Be(12);
        preferences.Pending.Should().BeTrue();
        f.State.NetworkEnabled.Should().BeFalse();
        f.State.PreferencesStatus.Should().Contain("could not");
        f.Time.Timers[0].Due.Should().Be(Timeout.InfiniteTimeSpan);
        f.Client.Calls.Should().Be(0);
        f.Audit.Events.Where(value => value.ActionId is "maintenance.settings").Select(value => value.Outcome)
            .Should().Equal(SecurityAuditOutcome.Requested, SecurityAuditOutcome.Failed);
    }

    [Fact]
    public async Task UncertainSavedOptInCannotRearmAfterANativeToggleAndPrivacyClosure()
    {
        var preferences = new Preferences { Settings = new(true, false, 6) };
        using var f = new Fixture(preferences);
        preferences.OnSave = () => throw new IOException("unconfirmed fixture");
        await f.State.SavePreferencesAsync();
        preferences.Pending.Should().BeTrue();
        f.State.NetworkEnabled = true;
        f.State.PrivacyClosed();
        f.Time.Timers[0].Fire();
        await f.State.CheckAsync();
        f.Client.Calls.Should().Be(0);
        f.Time.Timers[0].Due.Should().Be(Timeout.InfiniteTimeSpan);
        f.State.PreferencesStatus.Should().Contain("remain off");
        f.Audit.Events.Should().NotContain(value => string.Equals(value.ActionId, "maintenance.settings", StringComparison.Ordinal)
            && value.Outcome == SecurityAuditOutcome.Succeeded);
    }

    [Fact]
    public async Task DisposalDuringFinalAdmissionReadCannotResurrectTheConfiguredTimerOrPublishLateState()
    {
        using var f = new Fixture(new Preferences { Settings = new(true, false, 6) });
        var closeOnRead = false;
        var notifications = 0;
        var atDisposal = -1;
        f.State.PropertyChanged += (_, _) => notifications++;
        f.State.BindGate(() =>
        {
            if (!closeOnRead) { return true; }
            f.State.Dispose();
            atDisposal = notifications;
            return false;
        });
        var pending = new TaskCompletionSource<ReleaseCheck>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Client.Pending = pending.Task;
        var checking = f.State.CheckAsync();
        closeOnRead = true;
        pending.SetCanceled(TestContext.Current.CancellationToken);
        await checking;
        f.Time.Timers[0].Disposed.Should().BeTrue();
        f.Time.Timers[0].Due.Should().Be(Timeout.InfiniteTimeSpan);
        atDisposal.Should().BeGreaterThanOrEqualTo(0);
        notifications.Should().Be(atDisposal);
        f.Time.Timers[0].Fire();
        f.Client.Calls.Should().Be(1);
        f.Opener.Versions.Should().BeEmpty();
    }

    [Fact]
    public async Task RequestedAuditFailureCannotPersistOrScheduleAnUnauditedOptIn()
    {
        var preferences = new Preferences();
        using var f = new Fixture(preferences);
        f.State.NetworkEnabled = true;
        f.Audit.BeforeWrite = record =>
        {
            if (record.ActionId is "maintenance.settings") { throw new IOException("requested fixture"); }
        };
        await f.State.SavePreferencesAsync();
        preferences.Writes.Should().Be(0);
        preferences.Pending.Should().BeFalse();
        f.State.NetworkEnabled.Should().BeFalse();
        f.State.PreferencesStatus.Should().Contain("could not");
        f.Time.Timers[0].Fire();
        f.Client.Calls.Should().Be(0);
    }

    [Fact]
    public async Task PrivacyClosureSuppressesAlreadyQueuedTimerCallbacksAndDoesNotReplayOnRebinding()
    {
        using var f = new Fixture(new Preferences { Settings = new(true, false, 6) });
        f.State.PrivacyClosed();
        f.Time.Timers[0].Fire();
        f.State.BindGate(() => true);
        f.Time.Timers[0].Fire();
        f.Client.Calls.Should().Be(0);
        f.Time.Timers[0].Due.Should().Be(TimeSpan.FromHours(6));
        await f.State.CheckAsync();
        f.Client.Calls.Should().Be(0);
        f.Time.Now = f.State.NextCheck!.Value;
        f.Time.Timers[0].Fire();
        f.Client.Calls.Should().Be(1);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(37)]
    [InlineData(168)]
    public async Task SavedAutomationDeniesClosedTicksAndResumesOnlyAtAFutureIndependentInterval(int hours)
    {
        var preferences = new Preferences { Settings = new(true, false, hours) };
        using var f = new Fixture(preferences);
        await f.State.CheckAsync();
        f.Client.Calls.Should().Be(1);
        f.Admitted = false;
        f.Time.Now = f.State.NextCheck!.Value;
        f.Time.Timers[0].Fire();
        f.Client.Calls.Should().Be(1);
        f.State.Availability.Should().Be(ReleaseAvailability.Unknown);
        var future = f.Time.Now.AddHours(hours);
        f.State.NextCheck.Should().Be(future);
        f.Time.Timers[0].Due.Should().Be(TimeSpan.FromHours(hours));
        f.Admitted = true;
        f.State.BindGate(() => f.Admitted);
        f.State.InitializeConfiguredChecks();
        f.Time.Timers[0].Fire();
        await f.State.CheckAsync();
        f.Client.Calls.Should().Be(1);
        f.State.NextCheck.Should().Be(future);
        f.Time.Now = future;
        f.Client.Result = f.Client.Success(f.Time.Now);
        f.Time.Timers[0].Fire();
        f.Client.Calls.Should().Be(2);
        f.State.NextCheck.Should().Be(f.Time.Now.AddHours(hours).AddMinutes(15));
        preferences.Writes.Should().Be(0);
        f.Opener.Versions.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PrivacyAndDeniedTicksCannotShortenALaterServerBackoff(bool privacyNotification)
    {
        using var f = new Fixture(new Preferences { Settings = new(true, false, 6) });
        var retry = f.Time.Now.AddHours(24);
        f.Client.Result = new(ReleaseAvailability.RateLimited, "rate-limited", f.Time.Now, RetryAt: retry);
        await f.State.CheckAsync();
        f.Admitted = false;
        f.Time.Now = f.Time.Now.AddHours(1);
        if (privacyNotification) { f.State.PrivacyClosed(); }
        else { f.Time.Timers[0].Fire(); }
        f.State.NextCheck.Should().Be(retry);
        f.Time.Timers[0].Due.Should().Be(TimeSpan.FromHours(23));
        f.Admitted = true;
        f.Time.Timers[0].Fire();
        await f.State.CheckAsync();
        f.Client.Calls.Should().Be(1);
        f.State.NextCheck.Should().Be(retry);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnsavedRunOnlyPermissionStillRequiresExplicitRecoveryAfterPrivacyClosure(bool savedDisabled)
    {
        using var f = new Fixture(new Preferences { Settings = savedDisabled ? MaintenanceSettings.Default : null });
        f.State.NetworkEnabled = true;
        f.State.PrivacyClosed();
        f.Time.Timers[0].Fire();
        f.State.BindGate(() => true);
        f.Time.Timers[0].Fire();
        f.Client.Calls.Should().Be(0);
        f.Time.Timers[0].Due.Should().Be(Timeout.InfiniteTimeSpan);
        await f.State.CheckAsync();
        f.Client.Calls.Should().Be(1);
    }

    [Fact]
    public async Task SavedAutomationRearmsWhenAdmissionClosesDuringAnInflightCheck()
    {
        using var f = new Fixture(new Preferences { Settings = new(true, false, 6) });
        var pending = new TaskCompletionSource<ReleaseCheck>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Client.Pending = pending.Task;
        var checking = f.State.CheckAsync();
        f.Admitted = false;
        pending.SetResult(f.Client.Success(f.Time.Now));
        await checking;
        f.State.LastVerified.Should().BeNull();
        f.State.NextCheck.Should().Be(f.Time.Now.AddHours(6));
        f.Time.Timers[0].Due.Should().Be(TimeSpan.FromHours(6));
        f.Admitted = true;
        f.State.BindGate(() => f.Admitted);
        f.Time.Timers[0].Fire();
        f.Client.Calls.Should().Be(1);
        f.Client.Pending = null;
        f.Time.Now = f.State.NextCheck!.Value;
        f.Client.Result = f.Client.Success(f.Time.Now);
        f.Time.Timers[0].Fire();
        f.Client.Calls.Should().Be(2);
    }

    [Fact]
    public async Task AConfiguredTickWhileBusyKeepsAFutureTimerWithoutStartingASecondCheck()
    {
        using var f = new Fixture(new Preferences { Settings = new(true, false, 6) });
        var pending = new TaskCompletionSource<ReleaseCheck>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Client.Pending = pending.Task;
        var checking = f.State.CheckAsync();
        f.Time.Now = f.State.NextCheck!.Value;
        f.Time.Timers[0].Fire();
        f.Client.Calls.Should().Be(1);
        f.State.NextCheck.Should().Be(f.Time.Now.AddHours(6));
        f.Time.Timers[0].Due.Should().Be(TimeSpan.FromHours(6));
        pending.SetResult(f.Client.Success(f.Time.Now));
        await checking;
        f.Client.Calls.Should().Be(1);
    }

    [Fact]
    public async Task SaveWithoutStorageAndSaveAfterDisposalNeverPretendPersistenceSucceeded()
    {
        using var f = new Fixture();
        f.State.PreferencesStatus.Should().Contain("unavailable");
        await f.State.SavePreferencesAsync();
        f.State.PreferencesStatus.Should().Contain("could not");
        f.State.Dispose();
        f.State.InitializeConfiguredChecks();
        await f.State.SavePreferencesAsync();
        f.State.BindGate(() => true);
        f.State.Invoking(value => value.BindGate(null!)).Should().Throw<ArgumentNullException>();
        f.Audit.Events.Should().BeEmpty();
    }

    private sealed class Preferences : IMaintenancePreferences
    {
        internal MaintenanceSettings? Settings;
        internal bool Pending;
        internal int Writes;
        internal Exception? LoadFailure;
        internal Action? OnBegin;
        internal Action? OnSave;
        internal Action? OnConfirm;

        public MaintenanceSettings? Load()
        {
            if (LoadFailure is not null) { throw LoadFailure; }
            if (Pending) { throw new InvalidDataException("Unconfirmed fixture settings."); }
            return Settings;
        }

        public void BeginWrite()
        {
            Pending = true;
            OnBegin?.Invoke();
        }

        public void Save(MaintenanceSettings settings)
        {
            Settings = settings;
            Writes++;
            OnSave?.Invoke();
        }

        public void ConfirmWrite()
        {
            OnConfirm?.Invoke();
            Pending = false;
        }
    }
}
