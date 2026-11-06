using AwesomeAssertions;
using Kora.Application.Configuration.SettingInspection;
using Kora.Core.Auditing;
using Kora.Core.Configuration;
using Kora.Core.Configuration.RegisteredSettings;
using Kora.Core.Voice;

namespace Kora.Application.UnitTests.Configuration.SettingInspection;

[Collection("Host tracing")]
public sealed class ConfirmedSettingsTests
{
    [Fact]
    public async Task InstalledPairObservationDoesNotEnumerateOrDiscloseCatalogueMetadata()
    {
        await using var f = new SettingInspectionFixture();
        f.Store.Lines["speech-selection.txt"] = ["1", "kokoro", "af_heart"];
        f.Catalog.Providers = [new("kokoro", "Private provider label", "Private catalogue description", true, false, null, "af_heart")];
        f.Catalog.Voices = [new("af_heart", "Private voice label", "en-US", SpeechVoiceGender.Female) { ProviderId = "kokoro" }];
        f.Observe();
        f.Speech.Get().EffectiveVoice.Should().NotBeNull();
        var snapshot = await f.Source.CaptureAsync(f.Token);
        var selection = snapshot.Observations.Single(item => item.Setting == RegisteredSetting.SpeechVoice);
        selection.Selection.Should().Be(new SpeechSelection("kokoro", "af_heart"));
        selection.Text.Should().Contain("confirmed saved").And.Contain("no runtime claim")
            .And.NotContain("Private").And.NotContain("en-US");
        await f.Source.ValidateAsync(snapshot, f.Token);
        f.Catalog.Reads.Should().Be(0);
        f.Store.Writes.Should().Be(0);
        f.Playback.Effects.Should().Be(0);
        f.Events.Should().BeEmpty();
    }

    [Theory]
    [InlineData(null, null, false)]
    [InlineData("windows-sapi", null, false)]
    [InlineData("windows-sapi", "Exact voice", false)]
    [InlineData("kokoro", "af_heart", false)]
    [InlineData("kokoro", "af_heart", true)]
    [InlineData(null, "Legacy voice", true)]
    public async Task SpeechSelectionUsesAuthoritativeAtomicOrLegacyPreferenceWithoutCatalogueOrRuntimeInference(
        string? provider, string? voice, bool legacy)
    {
        await using var f = new SettingInspectionFixture();
        var saved = provider is not null || voice is not null;
        if (legacy)
        {
            if (provider is not null) { f.Store.Text["speech-provider.txt"] = provider; }
            if (voice is not null) { f.Store.Text["speech-voice.txt"] = voice; }
        }
        else if (saved) { f.Store.Lines["speech-selection.txt"] = ["1", provider!, voice ?? string.Empty]; }
        f.Observe();
        f.Speech.Get().EffectiveVoice.Should().BeNull("no runtime catalogue is available, but preferences can still be confirmed");
        var snapshot = await f.Source.CaptureAsync(f.Token);
        var observation = snapshot.Observations.Single(item => item.Setting == RegisteredSetting.SpeechVoice);
        observation.Selection.Should().Be(new SpeechSelection(provider ?? "windows-sapi", voice));
        observation.Value.Should().BeNull();
        observation.IsSaved.Should().Be(saved);
        observation.Revision.Should().Be(f.Speech.Get().Revision);
        observation.Text.Should().Contain("no runtime claim");
        await f.Source.ValidateAsync(snapshot, f.Token);
        f.Catalog.Reads.Should().Be(0);
        f.Store.Writes.Should().Be(0);
        f.Playback.Effects.Should().Be(0);
        f.Events.Should().BeEmpty();
    }

    [Theory]
    [InlineData("atomic")]
    [InlineData("legacy-provider")]
    [InlineData("legacy-voice")]
    [InlineData("saved-default")]
    public async Task ChangedSpeechValueOrProvenanceRetiresSourceEvenAfterObservedReturn(string change)
    {
        await using var f = new SettingInspectionFixture();
        f.Observe();
        var original = await f.Source.CaptureAsync(f.Token);
        if (change is "atomic") { f.Store.Lines["speech-selection.txt"] = ["1", "kokoro", "af_heart"]; }
        else if (change is "saved-default") { f.Store.Lines["speech-selection.txt"] = ["1", "windows-sapi", ""]; }
        else { f.Store.Text[change is "legacy-provider" ? "speech-provider.txt" : "speech-voice.txt"] =
            change is "legacy-provider" ? "kokoro" : "Exact voice"; }
        await FluentActions.Awaiting(() => f.Source.ValidateAsync(original, f.Token)).Should().ThrowAsync<InvalidDataException>();
        f.Store.Lines.Clear();
        f.Store.Text.Clear();
        await FluentActions.Awaiting(() => f.Source.ValidateAsync(original, f.Token)).Should().ThrowAsync<InvalidDataException>();
        var fresh = await f.Source.CaptureAsync(f.Token);
        f.Source.IsCurrent(fresh).Should().BeTrue();
        f.Source.IsCurrent(original).Should().BeFalse();
        f.Catalog.Reads.Should().Be(0);
        f.Store.Writes.Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HeldOrInvalidObservedSpeechCannotBecomeConfirmedDefault(bool invalid)
    {
        await using var f = new SettingInspectionFixture();
        f.Observe();
        var original = await f.Source.CaptureAsync(f.Token);
        if (invalid)
        {
            f.Store.Lines["speech-selection.txt"] = ["unknown"];
            f.Speech.Reload();
        }
        else { f.Speech.HoldUnavailable("Unconfirmed saved selection requires explicit recovery."); }
        (await f.Speech.ReadConfirmedSettingsAsync(f.Token)).Availability.Should().Be(SettingReadAvailability.NotObserved);
        f.Source.IsCurrent(original).Should().BeFalse();
        await FluentActions.Awaiting(() => f.Source.CaptureAsync(f.Token)).Should().ThrowAsync<InvalidDataException>();
        f.Store.Writes.Should().Be(0);
    }

    [Fact]
    public async Task SpeechServiceRevisionChangeAndReturnCannotReviveAnOldSource()
    {
        await using var f = new SettingInspectionFixture();
        f.Observe();
        var original = await f.Source.CaptureAsync(f.Token);
        f.Store.Lines["speech-selection.txt"] = ["1", "kokoro", "af_heart"];
        f.Speech.Reload();
        f.Store.Lines.Clear();
        f.Speech.Reload();
        f.Source.IsCurrent(original).Should().BeFalse();
        await FluentActions.Awaiting(() => f.Source.ValidateAsync(original, f.Token)).Should().ThrowAsync<InvalidDataException>();
        f.Store.Writes.Should().Be(0);
    }

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
            f.Store.Text["presence-display-enabled.txt"] = "False";
            f.Store.Text["presence-inactivity-timeout-seconds.txt"] = "5";
            f.Store.Text["presence-size-pixels.txt"] = "480";
            f.Store.Text["presence-dot-size-percent.txt"] = "75";
            f.Store.Text["presence-dot-density-percent.txt"] = "50";
            f.Store.Text["presence-movement-speed-percent.txt"] = "125";
            f.Store.Text["presence-speech-scaling-enabled.txt"] = "False";
            f.Store.Text["presence-speech-scale-amount-percent.txt"] = "150";
            f.Store.Text["appearance-reduced-motion-enabled.txt"] = "True";
            f.Store.Lines["speech-summary-limits.txt"] = ["1", "2", "40"];
            f.Store.Lines["speech-selection.txt"] = ["1", "kokoro", "af_heart"];
            f.Store.Lines["speech-playback-volume.txt"] = ["1", "60"];
            f.Store.Text["response-output-mode.txt"] = "VisualOnly";
        }
        f.Observe();
        var before = f.Appearance.Get(AppearanceOption.Theme);
        var snapshot = await f.Source.CaptureAsync(f.Token);
        snapshot.Observations.Should().HaveCount(16).And.OnlyHaveUniqueItems(item => item.Setting);
        snapshot.Observations.Should().OnlyContain(item => item.IsSaved == saved);
        snapshot.Observations.Single(item => item.Setting == RegisteredSetting.ReaderTextScale).Value.Should().Be(saved ? 125 : 100);
        f.Source.IsCurrent(snapshot).Should().BeTrue();
        await f.Source.ValidateAsync(snapshot, f.Token);
        f.Appearance.Get(AppearanceOption.Theme).Should().Be(before);
        f.Store.Writes.Should().Be(0);
        f.Playback.Effects.Should().Be(0);
        f.Catalog.Reads.Should().Be(0);
        f.Events.Should().BeEmpty();
        List<string> expectedReads =
            ["appearance-theme.txt", "reader-text-scale-unconfirmed.txt", "reader-text-scale-percent.txt", "presence-display-enabled.txt",
             "speech-summary-limits.txt", "speech-selection.txt", "speech-playback-volume-unconfirmed.txt", "speech-playback-volume.txt",
             "response-output-mode-unconfirmed.txt", "response-output-mode.txt",
             "presence-inactivity-timeout-seconds.txt", "presence-size-pixels.txt", "presence-dot-size-percent.txt",
             "presence-dot-density-percent.txt", "presence-movement-speed-percent.txt", "presence-speech-scaling-enabled.txt",
             "presence-speech-scale-amount-percent.txt", "appearance-reduced-motion-enabled.txt"];
        if (!saved) { expectedReads.Add("speech-provider.txt"); expectedReads.Add("speech-voice.txt"); }
        f.Store.Reads.Distinct(StringComparer.Ordinal).Should().BeEquivalentTo(expectedReads);
        (RegisteredSetting Setting, AppearanceOption Option, int Default, int Saved)[] presence =
        [
            (RegisteredSetting.PresenceTimeout, AppearanceOption.PresenceTimeout, 10, 5),
            (RegisteredSetting.PresenceSize, AppearanceOption.PresenceSize, 360, 480),
            (RegisteredSetting.DotSize, AppearanceOption.DotSize, 100, 75),
            (RegisteredSetting.DotDensity, AppearanceOption.DotDensity, 100, 50),
            (RegisteredSetting.MovementSpeed, AppearanceOption.MovementSpeed, 100, 125),
            (RegisteredSetting.SpeechScaling, AppearanceOption.SpeechScaling, 1, 0),
            (RegisteredSetting.SpeechScaleAmount, AppearanceOption.SpeechScaleAmount, 100, 150),
            (RegisteredSetting.ReducedMotion, AppearanceOption.ReducedMotion, 0, 1),
        ];
        foreach (var item in presence)
        {
            var observation = snapshot.Observations.Single(value => value.Setting == item.Setting);
            observation.Value.Should().Be(saved ? item.Saved : item.Default);
            observation.Revision.Should().Be(f.Appearance.Get(item.Option).Revision);
        }
    }

    [Theory]
    [InlineData("appearance-theme.txt", "Dark")]
    [InlineData("reader-text-scale-percent.txt", "125")]
    [InlineData("presence-display-enabled.txt", "False")]
    [InlineData("presence-display-enabled.txt", "True")]
    [InlineData("presence-inactivity-timeout-seconds.txt", "5")]
    [InlineData("presence-size-pixels.txt", "480")]
    [InlineData("presence-dot-size-percent.txt", "75")]
    [InlineData("presence-dot-density-percent.txt", "50")]
    [InlineData("presence-movement-speed-percent.txt", "125")]
    [InlineData("presence-speech-scaling-enabled.txt", "False")]
    [InlineData("presence-speech-scale-amount-percent.txt", "150")]
    [InlineData("appearance-reduced-motion-enabled.txt", "True")]
    [InlineData("presence-inactivity-timeout-seconds.txt", "10")]
    [InlineData("presence-size-pixels.txt", "360")]
    [InlineData("presence-dot-size-percent.txt", "100")]
    [InlineData("presence-dot-density-percent.txt", "100")]
    [InlineData("presence-movement-speed-percent.txt", "100")]
    [InlineData("presence-speech-scaling-enabled.txt", "True")]
    [InlineData("presence-speech-scale-amount-percent.txt", "100")]
    [InlineData("appearance-reduced-motion-enabled.txt", "False")]
    [InlineData("speech-summary-limits.txt", "changed")]
    [InlineData("speech-selection.txt", "changed")]
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
        else if (file is "speech-selection.txt") { f.Store.Lines[file] = ["1", "kokoro", "af_heart"]; }
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
    [InlineData("presence-display-enabled.txt")]
    [InlineData("presence-inactivity-timeout-seconds.txt")]
    [InlineData("presence-size-pixels.txt")]
    [InlineData("presence-dot-size-percent.txt")]
    [InlineData("presence-dot-density-percent.txt")]
    [InlineData("presence-movement-speed-percent.txt")]
    [InlineData("presence-speech-scaling-enabled.txt")]
    [InlineData("presence-speech-scale-amount-percent.txt")]
    [InlineData("appearance-reduced-motion-enabled.txt")]
    [InlineData("speech-summary-limits.txt")]
    [InlineData("speech-selection.txt")]
    [InlineData("speech-playback-volume.txt")]
    [InlineData("response-output-mode.txt")]
    public async Task MalformedConfirmedStateNeverBecomesDefaultOrRenewsOldObservation(string file)
    {
        await using var f = new SettingInspectionFixture();
        f.Observe();
        var snapshot = await f.Source.CaptureAsync(f.Token);
        if (file is "speech-summary-limits.txt" or "speech-playback-volume.txt" or "speech-selection.txt") { f.Store.Lines[file] = ["bad"]; }
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
    [InlineData("io", "appearance-theme.txt")]
    [InlineData("access", "appearance-theme.txt")]
    [InlineData("range", "appearance-theme.txt")]
    [InlineData("io", "presence-display-enabled.txt")]
    [InlineData("access", "presence-display-enabled.txt")]
    [InlineData("range", "presence-display-enabled.txt")]
    [InlineData("io", "speech-selection.txt")]
    [InlineData("access", "speech-selection.txt")]
    [InlineData("range", "speech-selection.txt")]
    public async Task ReadErrorsInvalidateWithoutApplyingOrLoggingSavedContent(string failure, string file)
    {
        await using var f = new SettingInspectionFixture();
        f.Observe();
        var snapshot = await f.Source.CaptureAsync(f.Token);
        f.Store.FailName = file;
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

    [Theory]
    [InlineData(null, true)]
    [InlineData("True", true)]
    [InlineData("False", false)]
    public async Task PresenceReadUsesExactAuthoritativeSavedBooleanOrConfirmedDefault(string? saved, bool expected)
    {
        await using var f = new SettingInspectionFixture();
        if (saved is not null) { f.Store.Text["presence-display-enabled.txt"] = saved; }
        f.Observe();
        var state = f.Appearance.Get(AppearanceOption.PresenceDisplay);
        var snapshot = await f.Source.CaptureAsync(f.Token);
        var display = snapshot.Observations.Single(item => item.Setting == RegisteredSetting.PresenceDisplay);
        display.Value.Should().Be(expected ? 1 : 0);
        display.IsSaved.Should().Be(saved is not null);
        display.Revision.Should().Be(state.Revision);
        state.Value.Should().Be(new AppearanceValue.Toggle(expected));
        display.Text.Should().Be($"appearance.presence-display = {(expected ? "true" : "false")} boolean "
            + (saved is null ? "(confirmed absent; live default)" : "(confirmed saved; matches live service)"));
        await f.Source.ValidateAsync(snapshot, f.Token);
        f.Appearance.Get(AppearanceOption.PresenceDisplay).Should().Be(state);
        f.Store.Writes.Should().Be(0);
        f.Playback.Effects.Should().Be(0);
        f.Events.Should().BeEmpty();
    }

    [Theory]
    [InlineData("True")]
    [InlineData("False")]
    public async Task RemovingSavedPresenceRequiresExplicitRecoveryAndCannotReuseOldSource(string saved)
    {
        await using var f = new SettingInspectionFixture();
        f.Store.Text["presence-display-enabled.txt"] = saved;
        f.Observe();
        var snapshot = await f.Source.CaptureAsync(f.Token);
        f.Store.Text.Remove("presence-display-enabled.txt");
        await FluentActions.Awaiting(() => f.Source.ValidateAsync(snapshot, f.Token)).Should().ThrowAsync<InvalidDataException>();
        f.Source.IsCurrent(snapshot).Should().BeFalse();
        f.Appearance.Reload();
        var fresh = await f.Source.CaptureAsync(f.Token);
        fresh.Observations.Single(item => item.Setting == RegisteredSetting.PresenceDisplay).IsSaved.Should().BeFalse();
        f.Source.IsCurrent(snapshot).Should().BeFalse();
        f.Store.Writes.Should().Be(0);
    }

    [Theory]
    [InlineData("presence-display-enabled.txt", "False")]
    [InlineData("presence-inactivity-timeout-seconds.txt", "5")]
    [InlineData("presence-size-pixels.txt", "480")]
    [InlineData("presence-dot-size-percent.txt", "75")]
    [InlineData("presence-dot-density-percent.txt", "50")]
    [InlineData("presence-movement-speed-percent.txt", "125")]
    [InlineData("presence-speech-scaling-enabled.txt", "False")]
    [InlineData("presence-speech-scale-amount-percent.txt", "150")]
    [InlineData("appearance-reduced-motion-enabled.txt", "True")]
    public async Task LivePresenceChangeAndReturnCannotRenewEarlierServiceRevision(string file, string value)
    {
        await using var f = new SettingInspectionFixture();
        f.Observe();
        var snapshot = await f.Source.CaptureAsync(f.Token);
        f.Store.Text[file] = value;
        f.Appearance.Reload();
        f.Store.Text.Remove(file);
        f.Appearance.Reload();
        f.Source.IsCurrent(snapshot).Should().BeFalse();
        await FluentActions.Awaiting(() => f.Source.ValidateAsync(snapshot, f.Token)).Should().ThrowAsync<InvalidDataException>();
        f.Store.Writes.Should().Be(0);
    }
}
