using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using AwesomeAssertions;

using Kora.Core.Configuration;
using Kora.Application.ViewModels;
using Kora.Core.Auditing;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Fact]
    public async Task Renderer_phase_timing_separates_selection_worker_audit_and_confirmed_notification()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        var phases = new ConcurrentQueue<PresenceSwitchPhase>();
        using var timing = new MeterListener();
        var instrument = PresenceSwitchTiming.Instrument;
        timing.InstrumentPublished = (published, observer) =>
        {
            if (ReferenceEquals(published, instrument)) { observer.EnableMeasurementEvents(published); }
        };
        timing.SetMeasurementEventCallback<long>((_, _, tags, _) =>
        {
            if (tags[0].Value is PresenceSwitchPhase phase) { phases.Enqueue(phase); }
        });
        timing.Start();
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Audit.BeforeWrite = audit =>
        {
            if (!string.Equals(audit.ActionId, "configuration.native-presence-renderer", StringComparison.Ordinal)
                || audit.Outcome != SecurityAuditOutcome.Requested) { return; }
            entered.TrySetResult();
            release.Wait(TestContext.Current.CancellationToken);
        };
        fixture.ViewModel.SelectedPresenceRenderer = fixture.ViewModel.PresenceRenderers[1];
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            phases.Take(6).Should().Equal(PresenceSwitchPhase.Selection, PresenceSwitchPhase.Queued,
                PresenceSwitchPhase.WorkerStarted, PresenceSwitchPhase.ConfigurationReadStarted,
                PresenceSwitchPhase.ConfigurationReadReturned, PresenceSwitchPhase.ApplyStarted);
            phases.Should().Contain(PresenceSwitchPhase.RequestedAuditStarted)
                .And.NotContain(PresenceSwitchPhase.RequestedAuditReturned)
                .And.NotContain(PresenceSwitchPhase.RendererNotification);
        }
        finally
        {
            release.Set();
            await fixture.ViewModel.AppearanceUpdate;
        }
        var completed = phases.ToArray();
        completed.Should().ContainInOrder(PresenceSwitchPhase.RequestedAuditReturned,
            PresenceSwitchPhase.TerminalAuditReturned, PresenceSwitchPhase.PublicationReturned,
            PresenceSwitchPhase.ApplyReturning, PresenceSwitchPhase.ApplyReturned,
            PresenceSwitchPhase.UiContinuation, PresenceSwitchPhase.RendererNotification);
        fixture.ViewModel.IsNativePresenceRenderer.Should().BeTrue();
        await fixture.ViewModel.DisposeAsync();
    }

    [Fact]
    public async Task AggregatedAuditOrStorageFailureIsPresentedInsteadOfBecomingAnUnobservedBindingTask()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        fixture.AppearancePreferences.SaveException = new AggregateException(new IOException("Fixture audit/storage failure."));
        fixture.ViewModel.ThemeMode = ApplicationThemeMode.Dark;
        await fixture.ViewModel.AppearanceUpdate;
        fixture.ViewModel.State.Should().Be(Kora.Core.AssistantState.Failure);
        fixture.ViewModel.AppearanceSettingStatus.Should().Contain("not confirmed");
        fixture.AppearancePreferences.SavedMode.Should().BeNull();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AuditCommitYieldsAndBlocksHandoffWhileRecheckingPrivacyBeforeTheEffect(bool losePrivacy)
    {
        var fixture = await Fixture.CreateInitializedAsync();
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var token = TestContext.Current.CancellationToken;
        fixture.Audit.BeforeWrite = audit =>
        {
            if (audit.Outcome != SecurityAuditOutcome.Requested) { return; }
            entered.TrySetResult();
            release.Wait(token);
        };
        var command = fixture.RunAsync("lock the machine");
        try
        {
            await entered.Task.WaitAsync(token);
            command.IsCompleted.Should().BeFalse();
            fixture.Session.LockCalls.Should().Be(0);
            (await fixture.ViewModel.TryPrepareHandoffAsync()).Should().BeFalse();
            if (losePrivacy) { fixture.Session.IsUnlocked = false; }
        }
        finally { release.Set(); }
        await command;
        fixture.Session.LockCalls.Should().Be(losePrivacy ? 0 : 1);
    }

    [Fact]
    public async Task ReaderNotificationsPublishOnlyAfterConfirmedPersistenceIsAvailable()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        var notified = false;
        fixture.ViewModel.PropertyChanged += (_, args) =>
        {
            if (!string.Equals(args.PropertyName, nameof(MainViewModel.ReaderTextScalePercent), StringComparison.Ordinal)) { return; }
            fixture.ViewModel.EnsureReaderTypographyAvailable();
            notified = true;
        };
        fixture.ViewModel.ReaderTextScalePercent = 75;
        await fixture.ViewModel.AppearanceUpdate;
        notified.Should().BeTrue();
        fixture.ViewModel.ReaderTextScalePercent.Should().Be(75);
    }

    [Fact]
    public async Task StalledAppearanceStorageReturnsControlAndCoalescesSliderValues()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var token = TestContext.Current.CancellationToken;
        fixture.AppearancePreferences.BeforeThemeSave = () =>
        {
            entered.SetResult();
            release.Wait(token);
        };
        fixture.ViewModel.ThemeMode = ApplicationThemeMode.Dark;
        try
        {
            await entered.Task.WaitAsync(token);
            fixture.ViewModel.AppearanceUpdate.IsCompleted.Should().BeFalse();
            fixture.ViewModel.ThemeMode.Should().Be(ApplicationThemeMode.System);
            fixture.ViewModel.PresenceSizePixels = 300;
            fixture.ViewModel.PresenceSizePixels = 400;
            fixture.ViewModel.PresenceSizePixels = 500;
            fixture.ViewModel.ShowSettings();
            fixture.AppearancePreferences.SavedPresenceSizePixels.Should().BeNull();
            (await fixture.ViewModel.TryPrepareHandoffAsync()).Should().BeFalse();
        }
        finally { release.Set(); }
        await fixture.ViewModel.AppearanceUpdate;
        fixture.ViewModel.ThemeMode.Should().Be(ApplicationThemeMode.Dark);
        fixture.ViewModel.PresenceSizePixels.Should().Be(500);
        fixture.AppearancePreferences.SavedPresenceSizePixels.Should().Be(500);
        fixture.Audit.Events.Count(item => string.Equals(item.ActionId, "configuration.presence-size", StringComparison.Ordinal)).Should().Be(2);
    }

    [Fact]
    public async Task QueuedAppearanceChangeCannotSurvivePrivateAdmissionLoss()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var token = TestContext.Current.CancellationToken;
        fixture.AppearancePreferences.BeforeThemeSave = () =>
        {
            entered.SetResult();
            release.Wait(token);
        };
        fixture.ViewModel.ThemeMode = ApplicationThemeMode.Dark;
        try
        {
            await entered.Task.WaitAsync(token);
            fixture.ViewModel.PresenceSizePixels = 500;
            fixture.Session.IsUnlocked = false;
        }
        finally { release.Set(); }
        await fixture.ViewModel.AppearanceUpdate;
        fixture.AppearancePreferences.SavedPresenceSizePixels.Should().BeNull();
        fixture.ViewModel.PresenceSizePixels.Should().Be(PresenceSettings.DefaultSizePixels);
        fixture.ViewModel.AppearanceSettingStatus.Should().Contain("not confirmed");
    }

    [Theory]
    [InlineData(SecurityAuditOutcome.Requested)]
    [InlineData(SecurityAuditOutcome.Succeeded)]
    public async Task RendererSelectionAndReadbackYieldDuringAuditAndCoalesceTheLatestChoice(SecurityAuditOutcome blockedOutcome)
    {
        var fixture = await Fixture.CreateInitializedAsync();
        using var request = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi),
            HostActivityLayer.Application, HostOperation.Presentation);
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var token = TestContext.Current.CancellationToken;
        var blocked = 0;
        fixture.Audit.BeforeWrite = audit =>
        {
            if (!string.Equals(audit.ActionId, "configuration.native-presence-renderer", StringComparison.Ordinal)) { return; }
            HostActivity.Current.Should().NotBeNull();
            HostActivity.Current!.Request.Should().Be(request.Request);
            if (audit.Outcome != blockedOutcome || Interlocked.Exchange(ref blocked, 1) != 0) { return; }
            entered.TrySetResult();
            release.Wait(token);
        };
        var selection = Task.Run(() =>
        {
            fixture.ViewModel.SelectedPresenceRenderer = fixture.ViewModel.PresenceRenderers[1];
            return fixture.ViewModel.SelectedPresenceRenderer.IsNative;
        }, token);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), token);
            (await selection.WaitAsync(TimeSpan.FromSeconds(1), token)).Should().BeFalse();
            fixture.ViewModel.AppearanceUpdate.IsCompleted.Should().BeFalse();
            fixture.ViewModel.IsNativePresenceRenderer.Should().BeFalse();
            fixture.ViewModel.SelectedPresenceRenderer.IsNative.Should().BeFalse();
            var settingsOpened = false;
            fixture.ViewModel.SettingsRequested += (_, _) => settingsOpened = true;
            fixture.ViewModel.ShowSettings();
            settingsOpened.Should().BeTrue();
            fixture.ViewModel.SelectedPresenceRenderer = fixture.ViewModel.PresenceRenderers[0];
            fixture.ViewModel.SelectedPresenceRenderer = fixture.ViewModel.PresenceRenderers[1];
            fixture.ViewModel.SelectedPresenceRenderer = fixture.ViewModel.PresenceRenderers[0];
            (await fixture.ViewModel.TryPrepareHandoffAsync()).Should().BeFalse();
        }
        finally
        {
            release.Set();
            await selection;
            await fixture.ViewModel.AppearanceUpdate;
        }
        fixture.ViewModel.IsNativePresenceRenderer.Should().BeFalse();
        fixture.AppearancePreferences.NativePresenceRendererEnabled.Should().BeFalse();
        fixture.Audit.Events.Count(item => string.Equals(item.ActionId, "configuration.native-presence-renderer", StringComparison.Ordinal)
            && item.Outcome == SecurityAuditOutcome.Succeeded).Should().Be(2);
    }

    [Fact]
    public async Task RendererSelectionRechecksPrivacyAfterRequestedAuditBeforePersistingOrActivating()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var token = TestContext.Current.CancellationToken;
        fixture.Audit.BeforeWrite = audit =>
        {
            if (!string.Equals(audit.ActionId, "configuration.native-presence-renderer", StringComparison.Ordinal)
                || audit.Outcome != SecurityAuditOutcome.Requested) { return; }
            entered.TrySetResult();
            release.Wait(token);
        };
        var selection = Task.Run(() =>
            fixture.ViewModel.SelectedPresenceRenderer = fixture.ViewModel.PresenceRenderers[1], token);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), token);
            await selection.WaitAsync(TimeSpan.FromSeconds(1), token);
            fixture.Session.IsUnlocked = false;
        }
        finally
        {
            release.Set();
            await selection;
            await fixture.ViewModel.AppearanceUpdate;
        }
        fixture.AppearancePreferences.NativePresenceRendererEnabled.Should().BeNull();
        fixture.ViewModel.IsNativePresenceRenderer.Should().BeFalse();
        fixture.ViewModel.AppearanceSettingStatus.Should().Contain("not confirmed");
        fixture.Audit.Events.Should().Contain(item => string.Equals(item.ActionId, "configuration.native-presence-renderer", StringComparison.Ordinal)
            && item.Outcome == SecurityAuditOutcome.Denied);
    }

    [Fact]
    public async Task DisposalJoinsTheRendererPreferenceAuditAndPreventsLateActivation()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var token = TestContext.Current.CancellationToken;
        fixture.Audit.BeforeWrite = audit =>
        {
            if (!string.Equals(audit.ActionId, "configuration.native-presence-renderer", StringComparison.Ordinal)
                || audit.Outcome != SecurityAuditOutcome.Requested) { return; }
            entered.TrySetResult();
            release.Wait(token);
        };
        fixture.ViewModel.SelectedPresenceRenderer = fixture.ViewModel.PresenceRenderers[1];
        Task disposal = Task.CompletedTask;
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), token);
            disposal = fixture.ViewModel.DisposeAsync().AsTask();
            disposal.IsCompleted.Should().BeFalse();
        }
        finally
        {
            release.Set();
            var observeDisposal = () => disposal;
            await observeDisposal.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("*no longer eligible*");
            await fixture.ViewModel.AppearanceUpdate;
        }
        fixture.AppearancePreferences.NativePresenceRendererEnabled.Should().BeNull();
        fixture.ViewModel.IsNativePresenceRenderer.Should().BeFalse();
        fixture.Audit.Events.Should().Contain(item => string.Equals(item.ActionId, "configuration.native-presence-renderer", StringComparison.Ordinal)
            && item.Outcome == SecurityAuditOutcome.Denied);
    }
}
