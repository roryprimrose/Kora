using AwesomeAssertions;

using Kora.Core.Configuration;
using Kora.Application.ViewModels;
using Kora.Core.Auditing;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
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
}
