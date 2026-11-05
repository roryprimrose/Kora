using AwesomeAssertions;
using Kora.Application.Configuration.SettingInspection;
using Kora.Core.Authorization;
using Kora.Core.Configuration;
using Kora.Core.Configuration.RegisteredSettings;

namespace Kora.Application.UnitTests.Configuration.SettingInspection;

[Collection("Host tracing")]
public sealed class QuietHoursConfirmedSettingsTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("1\nOff")]
    [InlineData("1\nweekly Mon,Wed,Fri 22:00 07:00 UTC")]
    public async Task ReadOnlyObservationMatchesAuthoritativeIntentWithoutRenewalOrSpeechAdmissionEffects(string? saved)
    {
        await using var f = new SettingInspectionFixture(saved);
        f.Observe();
        var state = f.QuietHours.Get();
        var events = 0;
        f.QuietHours.SpeechAdmissionChanged += () => events++;
        var snapshot = await f.Source.CaptureAsync(f.Token);
        var quiet = snapshot.Observations.Single(item => item.Setting == RegisteredSetting.QuietHours);
        quiet.QuietHours.Should().Be(state.Effective);
        quiet.IsSaved.Should().Be(saved is not null);
        quiet.Revision.Should().Be(state.Revision);
        await f.Source.ValidateAsync(snapshot, f.Token);
        f.QuietHours.Get().Should().BeSameAs(state);
        events.Should().Be(0);
        f.Store.Writes.Should().Be(0);
        f.Playback.Effects.Should().Be(0);
        f.Events.Should().BeEmpty();
    }

    [Theory]
    [InlineData("bad")]
    [InlineData("2\nOff")]
    [InlineData("1\nweekly Mon 22:00 07:00 no-such-zone")]
    public async Task InitiallyHeldStateCannotBeRecoveredOrDefaultedByReading(string saved)
    {
        await using var f = new SettingInspectionFixture(saved);
        f.Observe();
        var state = f.QuietHours.Get();
        f.Store.Text.Remove("notices-quiet-hours.txt");
        await FluentActions.Awaiting(() => f.Source.CaptureAsync(f.Token)).Should().ThrowAsync<InvalidDataException>();
        f.QuietHours.Get().Should().BeSameAs(state);
        state.Effective.Should().BeNull();
        f.Store.Writes.Should().Be(0);
        f.Events.Should().BeEmpty();
    }

    [Theory]
    [InlineData("1\nOff", null)]
    [InlineData("1\nweekly Mon 22:00 07:00 UTC", "1\nOff")]
    public async Task SavedChangeAndReturnRetiresSourceWithoutReloadingLiveIntent(string saved, string? changed)
    {
        await using var f = new SettingInspectionFixture(saved);
        f.Observe();
        var before = f.QuietHours.Get();
        var snapshot = await f.Source.CaptureAsync(f.Token);
        if (changed is null) { f.Store.Text.Remove("notices-quiet-hours.txt"); }
        else { f.Store.Text["notices-quiet-hours.txt"] = changed; }
        await FluentActions.Awaiting(() => f.Source.ValidateAsync(snapshot, f.Token)).Should().ThrowAsync<InvalidDataException>();
        f.Store.Text["notices-quiet-hours.txt"] = saved;
        await FluentActions.Awaiting(() => f.Source.ValidateAsync(snapshot, f.Token)).Should().ThrowAsync<InvalidDataException>();
        var fresh = await f.Source.CaptureAsync(f.Token);
        f.Source.IsCurrent(fresh).Should().BeTrue();
        f.Source.IsCurrent(snapshot).Should().BeFalse();
        f.QuietHours.Get().Should().BeSameAs(before);
        f.Store.Writes.Should().Be(0);
    }

    [Fact]
    public async Task CancellationDoesNotRefreshAnyConfirmedIntent()
    {
        await using var f = new SettingInspectionFixture();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await FluentActions.Awaiting(() => f.QuietHours.ReadConfirmedSettingsAsync(cancellation.Token))
            .Should().ThrowAsync<OperationCanceledException>();
        f.QuietHours.Get().Effective.Should().Be(QuietHoursSchedule.Off);
        f.Store.Writes.Should().Be(0);
    }

    [Fact]
    public async Task ObservedPublishingStateRetiresReadEvenWhenControlOnlyReviewsWithoutWriting()
    {
        await using var f = new QuietHoursConfigurationServiceTests.Fixture();
        var original = await f.Service.ReadConfirmedSettingsAsync(f.Token);
        f.Service.IsInspectionCurrent(original).Should().BeTrue();
        var release = new TaskCompletionSource<WorkSessionAuthorization>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Store.PendingCreation = release.Task;
        var review = f.Review();
        try
        {
            f.Service.IsInspectionCurrent(original).Should().BeFalse();
            (await f.Service.ReadConfirmedSettingsAsync(f.Token)).Availability.Should().Be(SettingReadAvailability.NotObserved);
        }
        finally { release.SetResult(f.Store.Authority!); }
        await review;
        f.Service.IsInspectionCurrent(original).Should().BeFalse();
        var fresh = await f.Service.ReadConfirmedSettingsAsync(f.Token);
        f.Service.IsInspectionCurrent(fresh).Should().BeTrue();
        f.Preferences.Writes.Should().Be(0);
        f.Audit.Records.Should().BeEmpty();
    }

    [Fact]
    public async Task RealServiceRevisionChangeCannotReuseEarlierConfirmedSchedule()
    {
        await using var f = new QuietHoursConfigurationServiceTests.Fixture();
        var original = await f.Service.ReadConfirmedSettingsAsync(f.Token);
        var review = await f.Review();
        (await f.Confirm(review)).Should().BeTrue();
        f.Service.IsInspectionCurrent(original).Should().BeFalse();
        var fresh = await f.Service.ReadConfirmedSettingsAsync(f.Token);
        fresh.Observations.Single().QuietHours.Should().Be(f.Schedule);
        f.Service.IsInspectionCurrent(fresh).Should().BeTrue();
    }
}
