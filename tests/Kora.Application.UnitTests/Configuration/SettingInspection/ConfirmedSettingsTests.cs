using AwesomeAssertions;
using Kora.Application.Configuration.SettingInspection;
using Kora.Core.Auditing;
using Kora.Core.Configuration;
using Kora.Core.Configuration.RegisteredSettings;

namespace Kora.Application.UnitTests.Configuration.SettingInspection;

[Collection("Host tracing")]
public sealed class ConfirmedSettingsTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ActualServicesConfirmSavedOrDefaultProvenanceWithoutAnyWritesEffectsOrOtherReads(bool saved)
    {
        await using var f = new SettingInspectionFixture();
        if (saved)
        {
            f.Store.Text["appearance-theme.txt"] = "Dark";
            f.Store.Text["reader-text-scale-percent.txt"] = "125";
            f.Store.Lines["speech-summary-limits.txt"] = ["1", "2", "40"];
            f.Store.Lines["speech-playback-volume.txt"] = ["1", "60"];
            f.Store.Text["response-output-mode.txt"] = "VisualOnly";
        }
        f.Observe();
        var before = f.Appearance.Get(AppearanceOption.Theme);
        var snapshot = await f.Source.CaptureAsync(f.Token);
        snapshot.Observations.Should().HaveCount(6);
        snapshot.Observations.Should().OnlyContain(item => item.IsSaved == saved);
        snapshot.Observations.Single(item => item.Setting == RegisteredSetting.ReaderTextScale).Value.Should().Be(saved ? 125 : 100);
        f.Source.IsCurrent(snapshot).Should().BeTrue();
        await f.Source.ValidateAsync(snapshot, f.Token);
        f.Appearance.Get(AppearanceOption.Theme).Should().Be(before);
        f.Store.Writes.Should().Be(0);
        f.Playback.Effects.Should().Be(0);
        f.Catalog.Reads.Should().Be(0);
        f.Events.Should().BeEmpty();
        f.Store.Reads.Distinct(StringComparer.Ordinal).Should().BeEquivalentTo(
            ["appearance-theme.txt", "reader-text-scale-unconfirmed.txt", "reader-text-scale-percent.txt",
             "speech-summary-limits.txt", "speech-playback-volume-unconfirmed.txt", "speech-playback-volume.txt",
             "response-output-mode-unconfirmed.txt", "response-output-mode.txt"]);
    }

    [Theory]
    [InlineData("appearance-theme.txt", "Dark")]
    [InlineData("reader-text-scale-percent.txt", "125")]
    [InlineData("speech-summary-limits.txt", "changed")]
    [InlineData("speech-playback-volume.txt", "changed")]
    [InlineData("response-output-mode.txt", "VisualOnly")]
    [InlineData("reader-text-scale-unconfirmed.txt", "1")]
    [InlineData("speech-playback-volume-unconfirmed.txt", "1")]
    [InlineData("response-output-mode-unconfirmed.txt", "1")]
    public async Task ExternalChangeOrUnconfirmedStateInvalidatesPriorSourceIncludingObservedAba(string file, string value)
    {
        await using var f = new SettingInspectionFixture();
        f.Observe();
        var original = await f.Source.CaptureAsync(f.Token);
        if (file is "speech-summary-limits.txt") { f.Store.Lines[file] = ["1", "1", "20"]; }
        else if (file is "speech-playback-volume.txt") { f.Store.Lines[file] = ["1", "40"]; }
        else { f.Store.Text[file] = value; }
        await FluentActions.Awaiting(() => f.Source.ValidateAsync(original, f.Token)).Should().ThrowAsync<InvalidDataException>();
        f.Source.IsCurrent(original).Should().BeFalse();
        f.Store.Text.Remove(file);
        f.Store.Lines.Remove(file);
        await FluentActions.Awaiting(() => f.Source.ValidateAsync(original, f.Token)).Should().ThrowAsync<InvalidDataException>();
        var fresh = await f.Source.CaptureAsync(f.Token);
        f.Source.IsCurrent(fresh).Should().BeTrue();
        f.Source.IsCurrent(original).Should().BeFalse();
        f.Store.Writes.Should().Be(0);
        f.Playback.Effects.Should().Be(0);
    }

    [Theory]
    [InlineData("appearance-theme.txt")]
    [InlineData("reader-text-scale-percent.txt")]
    [InlineData("speech-summary-limits.txt")]
    [InlineData("speech-playback-volume.txt")]
    [InlineData("response-output-mode.txt")]
    public async Task MalformedConfirmedStateNeverBecomesDefaultOrRenewsOldObservation(string file)
    {
        await using var f = new SettingInspectionFixture();
        f.Observe();
        var snapshot = await f.Source.CaptureAsync(f.Token);
        if (file is "speech-summary-limits.txt" or "speech-playback-volume.txt") { f.Store.Lines[file] = ["bad"]; }
        else { f.Store.Text[file] = "bad"; }
        await FluentActions.Awaiting(() => f.Source.CaptureAsync(f.Token)).Should().ThrowAsync<InvalidDataException>();
        f.Source.IsCurrent(snapshot).Should().BeFalse();
        f.Store.Writes.Should().Be(0);
        f.Playback.Effects.Should().Be(0);
    }

    [Fact]
    public async Task UnobservedDefaultsAndHeldServicesAreUnavailableAndCurrentServiceRevisionCannotAba()
    {
        await using var f = new SettingInspectionFixture();
        await FluentActions.Awaiting(() => f.Source.CaptureAsync(f.Token)).Should().ThrowAsync<InvalidDataException>();
        f.Observe();
        var snapshot = await f.Source.CaptureAsync(f.Token);
        var current = f.Appearance.Get(AppearanceOption.Theme);
        f.Appearance.Apply(f.Appearance.Propose(AppearanceOption.Theme, new AppearanceValue.Theme(ApplicationThemeMode.Dark),
            current.Revision, SecurityAuditInitiator.LocalUser), f.Token);
        f.Source.IsCurrent(snapshot).Should().BeFalse();
        current = f.Appearance.Get(AppearanceOption.Theme);
        f.Appearance.Apply(f.Appearance.Propose(AppearanceOption.Theme, new AppearanceValue.Theme(ApplicationThemeMode.System),
            current.Revision, SecurityAuditInitiator.LocalUser), f.Token);
        await FluentActions.Awaiting(() => f.Source.ValidateAsync(snapshot, f.Token)).Should().ThrowAsync<InvalidDataException>();
        f.Volume.HoldUnavailable();
        (await f.Volume.ReadConfirmedSettingsAsync(f.Token)).Availability.Should().Be(SettingReadAvailability.NotObserved);
        f.Response.HoldUnavailable();
        (await f.Response.ReadConfirmedSettingsAsync(f.Token)).Availability.Should().Be(SettingReadAvailability.NotObserved);
    }

    [Theory]
    [InlineData("io")]
    [InlineData("access")]
    [InlineData("range")]
    public async Task ReadErrorsInvalidateWithoutApplyingOrLoggingSavedContent(string failure)
    {
        await using var f = new SettingInspectionFixture();
        f.Observe();
        var snapshot = await f.Source.CaptureAsync(f.Token);
        f.Store.FailName = "appearance-theme.txt";
        f.Store.Failure = failure switch
        {
            "io" => new IOException("private"),
            "access" => new UnauthorizedAccessException("private"),
            _ => new ArgumentOutOfRangeException(nameof(failure)),
        };
        await FluentActions.Awaiting(() => f.Source.ValidateAsync(snapshot, f.Token)).Should().ThrowAsync<InvalidDataException>();
        f.Source.IsCurrent(snapshot).Should().BeFalse();
        f.Store.Writes.Should().Be(0);
        f.Playback.Effects.Should().Be(0);
    }
}
