using System.Diagnostics;
using AwesomeAssertions;
using Kora.Application.Configuration.SettingInspection;
using Kora.Core.Configuration.RegisteredSettings;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Presentation;

namespace Kora.Application.UnitTests.Configuration.SettingInspection;

[Collection("Host tracing")]
public sealed class ConfirmedDetailPresentationTests
{
    [Theory]
    [InlineData(null, DetailPresentationMode.Offer)]
    [InlineData("Offer", DetailPresentationMode.Offer)]
    [InlineData("LinkOnly", DetailPresentationMode.LinkOnly)]
    [InlineData("OpenAutomatically", DetailPresentationMode.OpenAutomatically)]
    public async Task ExactSavedOrAbsentModeMatchesTheObservedServiceWithoutChangingPresentation(string? saved, DetailPresentationMode expected)
    {
        await using var f = new SettingInspectionFixture();
        if (saved is not null) { f.Store.Text["detail-presentation.txt"] = saved; }
        f.Observe();
        var epoch = f.Details.CaptureOfferEpoch();
        var automatic = f.Details.CaptureAutomaticPreference();
        var snapshot = await f.Source.CaptureAsync(f.Token);
        var value = snapshot.Observations.Single(item => item.Setting == RegisteredSetting.DetailPresentation);
        value.Value.Should().Be((int)expected);
        value.IsSaved.Should().Be(saved is not null);
        value.Revision.Should().BeGreaterThan(0);
        value.Text.Should().Be("responses.detail-presentation = " + DetailPresentationPreference.Format(expected)
            + " mode " + (saved is null ? "(confirmed absent; live default)" : "(confirmed saved; matches live service)"));
        await f.Source.ValidateAsync(snapshot, f.Token);
        f.Details.CaptureOfferEpoch().Should().Be(epoch);
        f.Details.CaptureAutomaticPreference()?.Epoch.Should().Be(automatic?.Epoch);
        f.Store.Writes.Should().Be(0);
        f.Playback.Effects.Should().Be(0);
        f.Events.Should().BeEmpty();
    }

    [Theory]
    [InlineData("changed")]
    [InlineData("saved-default")]
    [InlineData("removed")]
    [InlineData("malformed")]
    [InlineData("marker")]
    [InlineData("io")]
    [InlineData("access")]
    [InlineData("domain")]
    public async Task ChangedOrUnconfirmedStorageRetiresTheSourceIncludingObservedReturn(string boundary)
    {
        await using var f = new SettingInspectionFixture();
        if (boundary is "removed") { f.Store.Text["detail-presentation.txt"] = "LinkOnly"; }
        f.Observe();
        var snapshot = await f.Source.CaptureAsync(f.Token);
        switch (boundary)
        {
            case "marker": f.Store.Text["detail-presentation-unconfirmed.txt"] = "1"; break;
            case "removed": f.Store.Text.Remove("detail-presentation.txt"); break;
            case "io":
            case "access":
            case "domain":
                f.Store.FailName = "detail-presentation.txt";
                f.Store.Failure = boundary switch
                {
                    "io" => new IOException(),
                    "access" => new UnauthorizedAccessException(),
                    _ => new ArgumentOutOfRangeException(nameof(boundary)),
                };
                break;
            default:
                f.Store.Text["detail-presentation.txt"] = boundary switch
                {
                    "changed" => "LinkOnly",
                    "saved-default" => "Offer",
                    _ => "invalid",
                };
                break;
        }
        await FluentActions.Awaiting(() => f.Source.ValidateAsync(snapshot, f.Token)).Should().ThrowAsync<InvalidDataException>();
        f.Source.IsCurrent(snapshot).Should().BeFalse();
        f.Store.Failure = null;
        f.Store.Text.Remove("detail-presentation-unconfirmed.txt");
        f.Store.Text.Remove("detail-presentation.txt");
        if (boundary is "removed") { f.Store.Text["detail-presentation.txt"] = "LinkOnly"; }
        await FluentActions.Awaiting(() => f.Source.ValidateAsync(snapshot, f.Token)).Should().ThrowAsync<InvalidDataException>();
        var fresh = await f.Source.CaptureAsync(f.Token);
        f.Source.IsCurrent(fresh).Should().BeTrue();
        f.Source.IsCurrent(snapshot).Should().BeFalse();
        f.Store.Writes.Should().Be(0);
        f.Playback.Effects.Should().Be(0);
        f.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task UnobservedHeldOrChangedServiceNeedsExplicitInspectionAndNeverRevivesOldRevision()
    {
        await using var f = new SettingInspectionFixture();
        (await f.Details.ReadConfirmedSettingsAsync(f.Token)).Availability.Should().Be(SettingReadAvailability.NotObserved);
        f.Observe();
        var snapshot = await f.Source.CaptureAsync(f.Token);
        f.Details.HoldUnavailable();
        f.Source.IsCurrent(snapshot).Should().BeFalse();
        (await f.Details.ReadConfirmedSettingsAsync(f.Token)).Availability.Should().Be(SettingReadAvailability.NotObserved);
        await FluentActions.Awaiting(() => f.Source.CaptureAsync(f.Token)).Should().ThrowAsync<InvalidDataException>();
        await f.Details.InspectAsync(RequestOrigin.LocalUi, () => true, f.Token);
        var fresh = await f.Source.CaptureAsync(f.Token);
        f.Store.Text["detail-presentation.txt"] = "LinkOnly";
        f.Details.Observe();
        f.Store.Text.Remove("detail-presentation.txt");
        f.Details.Observe();
        f.Source.IsCurrent(fresh).Should().BeFalse();
        await FluentActions.Awaiting(() => f.Source.ValidateAsync(fresh, f.Token)).Should().ThrowAsync<InvalidDataException>();
        f.Store.Writes.Should().Be(0);
    }

    [Fact]
    public async Task BusyInspectionAndCancelledReadCannotConfirmOrRenewAnEarlierObservation()
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
        };
        ActivitySource.AddActivityListener(listener);
        await using var f = new DetailPresentationConfigurationServiceTests.Fixture();
        f.Service.Observe();
        var original = await f.Service.ReadConfirmedSettingsAsync(f.Token);
        var release = new TaskCompletionSource<Kora.Core.Authorization.WorkSessionAuthorization>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Store.PendingCreation = release.Task;
        var inspection = f.Service.InspectAsync(RequestOrigin.LocalUi, () => true, f.Token);
        try
        {
            f.Service.IsInspectionCurrent(original).Should().BeFalse();
            (await f.Service.ReadConfirmedSettingsAsync(f.Token)).Availability.Should().Be(SettingReadAvailability.NotObserved);
        }
        finally { release.SetResult(f.Store.Authority!); await inspection; }
        f.Service.IsInspectionCurrent(original).Should().BeFalse();
        (await f.Service.ReadConfirmedSettingsAsync(f.Token)).Availability.Should().Be(SettingReadAvailability.Confirmed);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await FluentActions.Awaiting(() => f.Service.ReadConfirmedSettingsAsync(cancelled.Token)).Should().ThrowAsync<OperationCanceledException>();
        f.Preferences.Writes.Should().Be(0);
        f.Audit.Events.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false, ModelToolMode.None)]
    [InlineData(true, ModelToolMode.None)]
    [InlineData(false, ModelToolMode.SettingsDiscovery)]
    [InlineData(true, ModelToolMode.SettingsDiscovery)]
    public async Task DetailsChangeBeforeDispatchOrAcrossAnswerAwaitRejectsTheOriginalSource(bool late, ModelToolMode mode)
    {
        await using var f = new SettingInspectionFixture();
        f.Observe();
        using var root = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.ActivatedVoice),
            HostActivityLayer.Application, HostOperation.Request);
        var review = await f.Workflow.ReviewAsync("Which Details presentation mode is actually confirmed?", () => true, f.Token, mode);
        void Change() => f.Store.Text["detail-presentation.txt"] = "LinkOnly";
        if (late) { f.BeforeReturn = Change; }
        else { Change(); }
        var result = await f.Workflow.AnswerAsync(review, () => true, f.Token);
        result.Outcome.Should().Be(SettingInspectionOutcome.Unavailable);
        result.Receipt.Should().BeNull();
        f.Calls.Should().Be(late ? 1 : 0);
        f.Store.Writes.Should().Be(0);
        f.Playback.Effects.Should().Be(0);
        f.Events.Should().NotContain(item => item.Outcome == Kora.Core.Auditing.SecurityAuditOutcome.Succeeded);
    }
}
