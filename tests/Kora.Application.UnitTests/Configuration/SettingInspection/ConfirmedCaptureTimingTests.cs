using AwesomeAssertions;
using Kora.Core.Configuration.RegisteredSettings;
using Kora.Application.Configuration.SettingInspection;
using Kora.Core.Configuration.SettingInspection;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Voice;

namespace Kora.Application.UnitTests.Configuration.SettingInspection;

[Collection("Host tracing")]
public sealed class ConfirmedCaptureTimingTests
{
    [Fact]
    public async Task ConfirmedSourceIncludesAllThreeExactDefaultTimingValuesWithoutEffects()
    {
        await using var f = new SettingInspectionFixture();
        f.Observe();
        var snapshot = await f.Source.CaptureAsync(f.Token);
        var timing = snapshot.Observations.Where(item => item.Setting is RegisteredSetting.CommandStartWait
            or RegisteredSetting.ReplyStartWait or RegisteredSetting.TrailingSilence).ToArray();
        timing.Should().HaveCount(3);
        timing.Select(item => item.Text).Should().Equal(
            "voice.command-start-wait = 5 seconds (confirmed absent; live default)",
            "voice.reply-start-wait = 15 seconds (confirmed absent; live default)",
            "voice.trailing-silence = 1 seconds (confirmed absent; live default)");
        f.Store.Writes.Should().Be(0);
        f.Playback.Effects.Should().Be(0);
        f.Catalog.Reads.Should().Be(0);
        f.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task SavedFractionalSecondsMatchAuthoritativeServiceWithoutRoundingOrMutation()
    {
        await using var f = new SettingInspectionFixture();
        var saved = new VoiceCaptureTiming(7.1234567m, 20.7654321m, 0.5000001m);
        f.Store.Text["voice-capture-timing.txt"] = saved.Serialize();
        f.Observe();
        var before = f.Timing.Get();
        var snapshot = await f.Source.CaptureAsync(f.Token);
        var values = snapshot.Timing.Observations;
        values.Select(item => item.Value).Should().Equal(7.1234567m, 20.7654321m, 0.5000001m);
        values.Should().OnlyContain(item => item.IsSaved && item.Revision == before.Revision);
        values[2].Text.Should().Be("voice.trailing-silence = 0.5000001 seconds (confirmed saved; matches live service)");
        await f.Source.ValidateAsync(snapshot, f.Token);
        f.Timing.Get().Should().Be(before);
        f.Store.Writes.Should().Be(0);
        f.Playback.Effects.Should().Be(0);
        f.Events.Should().BeEmpty();
    }

    [Theory]
    [InlineData("changed")]
    [InlineData("saved-default")]
    [InlineData("malformed")]
    [InlineData("marker")]
    [InlineData("io")]
    [InlineData("access")]
    [InlineData("domain")]
    public async Task ChangedOrUnconfirmedStorageRetiresSourceIncludingObservedChangeAndReturn(string boundary)
    {
        await using var f = new SettingInspectionFixture();
        f.Observe();
        var snapshot = await f.Source.CaptureAsync(f.Token);
        if (boundary is "marker") { f.Store.Text["voice-capture-timing-unconfirmed.txt"] = "1"; }
        else if (boundary is "io" or "access" or "domain")
        {
            f.Store.FailName = "voice-capture-timing.txt";
            f.Store.Failure = boundary switch
            {
                "io" => new IOException(),
                "access" => new UnauthorizedAccessException(),
                _ => new ArgumentOutOfRangeException(nameof(boundary)),
            };
        }
        else
        {
            f.Store.Text["voice-capture-timing.txt"] = boundary switch
            {
                "changed" => "1\n7\n20\n0.75",
                "saved-default" => VoiceCaptureTiming.Default.Serialize(),
                _ => "invalid",
            };
        }
        await FluentActions.Awaiting(() => f.Source.ValidateAsync(snapshot, f.Token)).Should().ThrowAsync<InvalidDataException>();
        f.Source.IsCurrent(snapshot).Should().BeFalse();
        f.Store.Failure = null;
        f.Store.Text.Remove("voice-capture-timing.txt");
        f.Store.Text.Remove("voice-capture-timing-unconfirmed.txt");
        await FluentActions.Awaiting(() => f.Source.ValidateAsync(snapshot, f.Token)).Should().ThrowAsync<InvalidDataException>();
        var fresh = await f.Source.CaptureAsync(f.Token);
        f.Source.IsCurrent(fresh).Should().BeTrue();
        f.Source.IsCurrent(snapshot).Should().BeFalse();
        f.Store.Writes.Should().Be(0);
        f.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task HeldServiceNeedsExplicitReloadAndReloadedChangeAndReturnCannotRenewRevision()
    {
        await using var f = new SettingInspectionFixture();
        f.Observe();
        var snapshot = await f.Source.CaptureAsync(f.Token);
        f.Store.Text["voice-capture-timing-unconfirmed.txt"] = "1";
        f.Timing.Reload();
        f.Source.IsCurrent(snapshot).Should().BeFalse();
        (await f.Timing.ReadConfirmedSettingsAsync(f.Token)).Availability.Should().Be(SettingReadAvailability.NotObserved);
        f.Store.Text.Remove("voice-capture-timing-unconfirmed.txt");
        await FluentActions.Awaiting(() => f.Source.CaptureAsync(f.Token)).Should().ThrowAsync<InvalidDataException>();
        f.Timing.Reload();
        var fresh = await f.Source.CaptureAsync(f.Token);
        f.Store.Text["voice-capture-timing.txt"] = "1\n7\n20\n0.75";
        f.Timing.Reload();
        f.Store.Text.Remove("voice-capture-timing.txt");
        f.Timing.Reload();
        f.Source.IsCurrent(fresh).Should().BeFalse();
        await FluentActions.Awaiting(() => f.Source.ValidateAsync(fresh, f.Token)).Should().ThrowAsync<InvalidDataException>();
        f.Store.Writes.Should().Be(0);
    }

    [Theory]
    [InlineData(false, ModelToolMode.None)]
    [InlineData(true, ModelToolMode.None)]
    [InlineData(false, ModelToolMode.SettingsDiscovery)]
    [InlineData(true, ModelToolMode.SettingsDiscovery)]
    public async Task TimingChangeBeforeDispatchOrFinalPublicationPreventsAnswer(bool late, ModelToolMode mode)
    {
        await using var f = new SettingInspectionFixture();
        f.Observe();
        using var root = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.ActivatedVoice),
            HostActivityLayer.Application, HostOperation.Request);
        var review = await f.Workflow.ReviewAsync("Which capture timings are configured?", () => true, f.Token, mode);
        void Change() => f.Store.Text["voice-capture-timing.txt"] = "1\n7\n20\n0.75";
        if (late) { f.BeforeReturn = Change; }
        else { Change(); }
        var result = await f.Workflow.AnswerAsync(review, () => true, f.Token);
        result.Outcome.Should().Be(SettingInspectionOutcome.Unavailable);
        result.Receipt.Should().BeNull();
        f.Calls.Should().Be(late ? 1 : 0);
        f.Events.Should().NotContain(item => item.Outcome == Kora.Core.Auditing.SecurityAuditOutcome.Succeeded);
        f.Store.Writes.Should().Be(0);
        f.Playback.Effects.Should().Be(0);
    }
}
