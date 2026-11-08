using AwesomeAssertions;
using Kora.Application.Communication;
using Kora.Application.Configuration;
using Kora.Application.Configuration.SettingInspection;
using Kora.Core.Auditing;
using Kora.Core.Communication;
using Kora.Core.Configuration;
using Kora.Core.Configuration.RegisteredSettings;
using Kora.Core.Configuration.SettingInspection;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Voice;

namespace Kora.Application.UnitTests.Configuration.SettingInspection;

[Collection("Host tracing")]
public sealed class ConfirmedWindowsSpeechRateTests
{
    internal const string Question = "Which Windows-native rate preference is saved, what configured rate is effective, and why does it not change Kokoro?";
    private static void Initialize(SettingInspectionFixture f, string provider = SpeechProviderIds.Windows,
        int? desired = null, SpeechRateSupport support = SpeechRateSupport.WindowsNative, bool installed = true)
    {
        f.Store.Lines["speech-selection.txt"] = ["1", provider, "voice"];
        if (desired is { } saved) { f.Store.Lines["speech-windows-rate.txt"] = ["1", saved.ToString(System.Globalization.CultureInfo.InvariantCulture)]; }
        f.Catalog.Providers = [new(provider, "Private provider", "Private path", installed, false, null, "voice") { RateSupport = support }];
        f.Catalog.Voices = [new("voice", "Private voice", "en-US", SpeechVoiceGender.Female) { ProviderId = provider }];
        f.Observe();
        f.Rate.Observe();
        f.Store.Reads.Clear();
        f.Playback.Effects = 0;
        f.Catalog.Reads = 0;
    }

    [Theory]
    [InlineData(null, true, SpeechProviderIds.Windows, SpeechRateSupport.WindowsNative, true, 0)]
    [InlineData(-10, true, SpeechProviderIds.Windows, SpeechRateSupport.WindowsNative, true, -10)]
    [InlineData(10, true, SpeechProviderIds.Windows, SpeechRateSupport.WindowsNative, true, 10)]
    [InlineData(4, false, SpeechProviderIds.Windows, SpeechRateSupport.WindowsNative, true, null)]
    [InlineData(4, true, SpeechProviderIds.Windows, SpeechRateSupport.Unsupported, true, null)]
    [InlineData(4, true, SpeechProviderIds.Windows, SpeechRateSupport.WindowsNative, false, null)]
    [InlineData(4, true, SpeechProviderIds.Kokoro, SpeechRateSupport.Unsupported, true, null)]
    public async Task DedicatedSourceReadsOnlyConfirmedSelectionAndRateWithoutObservationOrEffects(int? saved, bool control,
        string provider, SpeechRateSupport advertised, bool installed, int? effective)
    {
        await using var f = new SettingInspectionFixture(rateControl: control);
        Initialize(f, provider, saved, advertised, installed);
        var state = f.Rate.Get();
        var speech = f.Speech.Get();
        var changes = 0;
        f.Rate.Changed += (_, _) => changes++;
        var snapshot = await f.Source.CaptureAsync(f.Token, SettingInspectionProfile.WindowsRate);
        snapshot.Profile.Should().Be(SettingInspectionProfile.WindowsRate);
        snapshot.Observations.Select(item => item.Setting).Should().Equal(SettingInspectionReview.WindowsRateSettings);
        var rate = snapshot.Rate.Observations.Single().WindowsRate!;
        rate.Desired.Should().Be(saved ?? 0);
        rate.Effective.Should().Be(effective);
        rate.IsSaved.Should().Be(saved.HasValue);
        rate.ProviderId.Should().Be(provider);
        rate.Support.Should().Be(state.Support);
        rate.ProviderRevision.Should().Be(speech.Revision);
        rate.Revision.Should().Be(state.Revision);
        f.Source.IsCurrent(snapshot).Should().BeTrue();
        await f.Source.ValidateAsync(snapshot, f.Token);
        f.Rate.Get().Should().BeSameAs(state);
        f.Speech.Get().Should().BeSameAs(speech);
        f.Store.Reads.Distinct(StringComparer.Ordinal).Should().BeEquivalentTo("speech-selection.txt",
            "speech-windows-rate.txt", "speech-windows-rate-unconfirmed.txt");
        f.Store.Writes.Should().Be(0);
        f.Catalog.Reads.Should().Be(0);
        f.Playback.Effects.Should().Be(0);
        f.Events.Should().BeEmpty();
        changes.Should().Be(0);
        FluentActions.Invoking(() => snapshot.Appearance).Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData("unobserved")]
    [InlineData("held")]
    [InlineData("pending")]
    [InlineData("range")]
    [InlineData("format")]
    [InlineData("changed")]
    [InlineData("saved-default")]
    [InlineData("selection")]
    [InlineData("selection-malformed")]
    [InlineData("selection-held")]
    [InlineData("provider-revision")]
    [InlineData("summary")]
    public async Task UnavailableRateIsWholeSourceDenialWithoutDefaultProfileDependencies(string defect)
    {
        await using var f = new SettingInspectionFixture();
        Initialize(f);
        if (string.Equals(defect, "unobserved", StringComparison.Ordinal))
        {
            var rate = new WindowsSpeechRateConfigurationService(new LocalWindowsSpeechRatePreferences(f.Store),
                f.Playback, f.Speech, f.Admission, f, Microsoft.Extensions.Logging.Abstractions.NullLogger<WindowsSpeechRateConfigurationService>.Instance);
            var source = new ConfirmedSettingsSource(f.Appearance, f.Speech, f.Volume, f.Response, f.QuietHours, f.Timing, f.Details, rate);
            await FluentActions.Awaiting(() => source.CaptureAsync(f.Token, SettingInspectionProfile.WindowsRate))
                .Should().ThrowAsync<InvalidDataException>();
            return;
        }
        var original = await f.Source.CaptureAsync(f.Token, SettingInspectionProfile.WindowsRate);
        switch (defect)
        {
            case "held": f.Rate.HoldUnavailable(); break;
            case "pending": f.Store.Text["speech-windows-rate-unconfirmed.txt"] = "1"; break;
            case "range": f.Store.Lines["speech-windows-rate.txt"] = ["1", "11"]; break;
            case "format": f.Store.Lines["speech-windows-rate.txt"] = ["99", "0"]; break;
            case "changed": f.Store.Lines["speech-windows-rate.txt"] = ["1", "5"]; break;
            case "saved-default": f.Store.Lines["speech-windows-rate.txt"] = ["1", "0"]; break;
            case "selection": f.Store.Lines["speech-selection.txt"] = ["1", SpeechProviderIds.Kokoro, "voice"]; break;
            case "selection-malformed": f.Store.Lines["speech-selection.txt"] = ["99"]; break;
            case "selection-held": f.Speech.HoldUnavailable("Private held error"); break;
            case "provider-revision":
                f.Catalog.Providers = [f.Catalog.Providers[0] with { RateSupport = SpeechRateSupport.Unsupported }];
                f.Speech.Reload();
                break;
            case "summary": f.Store.Lines["speech-summary-limits.txt"] = ["invalid"]; break;
        }
        f.Playback.Effects = 0;
        f.Catalog.Reads = 0;
        if (string.Equals(defect, "summary", StringComparison.Ordinal))
        {
            await f.Source.ValidateAsync(original, f.Token);
            f.Source.IsCurrent(original).Should().BeTrue();
        }
        else
        {
            await FluentActions.Awaiting(() => f.Source.ValidateAsync(original, f.Token)).Should().ThrowAsync<InvalidDataException>();
            f.Source.IsCurrent(original).Should().BeFalse();
            await FluentActions.Awaiting(() => f.Source.CaptureAsync(f.Token, SettingInspectionProfile.WindowsRate))
                .Should().ThrowAsync<InvalidDataException>();
        }
        f.Store.Writes.Should().Be(0);
        f.Catalog.Reads.Should().Be(0);
        f.Playback.Effects.Should().Be(0);
    }

    [Fact]
    public async Task DefaultFullSourceNeverLoadsOrRequiresRateAndDedicatedNeedsExplicitComposition()
    {
        await using var f = new SettingInspectionFixture();
        f.Observe();
        f.Store.Text["speech-windows-rate-unconfirmed.txt"] = "1";
        var snapshot = await f.Source.CaptureAsync(f.Token);
        snapshot.Observations.Select(item => item.Setting).Should().BeEquivalentTo(SettingInspectionReview.InspectedSettings);
        snapshot.Observations.Should().NotContain(item => item.Setting == RegisteredSetting.WindowsSpeechRate);
        f.Store.Reads.Should().NotContain(name => name.StartsWith("speech-windows-rate", StringComparison.Ordinal));
        FluentActions.Invoking(() => snapshot.Rate).Should().Throw<InvalidOperationException>();
        var oldComposition = new ConfirmedSettingsSource(f.Appearance, f.Speech, f.Volume, f.Response, f.QuietHours, f.Timing, f.Details);
        await FluentActions.Awaiting(() => oldComposition.CaptureAsync(f.Token, SettingInspectionProfile.WindowsRate))
            .Should().ThrowAsync<InvalidOperationException>();
    }

    [Theory]
    [InlineData(false, ModelToolMode.None, RequestOrigin.LocalUi)]
    [InlineData(true, ModelToolMode.None, RequestOrigin.ActivatedVoice)]
    [InlineData(false, ModelToolMode.SettingsDiscovery, RequestOrigin.ActivatedVoice)]
    [InlineData(true, ModelToolMode.SettingsDiscovery, RequestOrigin.LocalUi)]
    public async Task OriginalConfirmationOneUseAuditAndJoinedAnswerRejectRateChangesIncludingAfterFinalAwait(bool late,
        ModelToolMode mode, RequestOrigin origin)
    {
        await using var f = new SettingInspectionFixture();
        Initialize(f, SpeechProviderIds.Kokoro, 5, SpeechRateSupport.Unsupported);
        using var root = HostActivity.BeginRoot(HostRequest.Create(origin), HostActivityLayer.Application, HostOperation.Request);
        var session = await f.Workflow.ReviewAsync(Question, () => f.Eligible, f.Token, mode, SettingInspectionProfile.WindowsRate);
        session.Review.Original.Origin.Should().Be(origin);
        f.Calls.Should().Be(0);
        f.Events.Should().BeEmpty();
        void Change() => f.Store.Lines["speech-windows-rate.txt"] = ["1", "4"];
        if (late) { f.BeforeReturn = Change; } else { Change(); }
        var result = await f.Workflow.AnswerAsync(session, () => f.Eligible, f.Token);
        result.Outcome.Should().Be(SettingInspectionOutcome.Unavailable);
        result.Receipt.Should().BeNull();
        f.Joins.Should().Be(late ? 1 : 0);
        f.Calls.Should().Be(late ? 1 : 0);
        f.Events.Select(item => item.Outcome).Should().Equal(SecurityAuditOutcome.Requested, SecurityAuditOutcome.Failed);
        f.Store.Writes.Should().Be(0);
        f.Playback.Effects.Should().Be(0);
    }

    [Fact]
    public async Task ObservedFailureAndReturnCannotReviveOldRateReview()
    {
        await using var f = new SettingInspectionFixture();
        Initialize(f);
        var old = await f.Source.CaptureAsync(f.Token, SettingInspectionProfile.WindowsRate);
        f.Store.Text["speech-windows-rate-unconfirmed.txt"] = "1";
        await FluentActions.Awaiting(() => f.Source.ValidateAsync(old, f.Token)).Should().ThrowAsync<InvalidDataException>();
        f.Store.Text.Remove("speech-windows-rate-unconfirmed.txt");
        var fresh = await f.Source.CaptureAsync(f.Token, SettingInspectionProfile.WindowsRate);
        f.Source.IsCurrent(old).Should().BeFalse();
        f.Source.IsCurrent(fresh).Should().BeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProviderAndRateNotificationsCloseOldInspectionWithoutReadingOrWritingDuringNotification(bool provider)
    {
        await using var f = new SettingInspectionFixture();
        Initialize(f);
        var snapshot = await f.Source.CaptureAsync(f.Token, SettingInspectionProfile.WindowsRate);
        var notified = false;
        void Check()
        {
            var reads = f.Store.Reads.Count;
            var effects = f.Playback.Effects;
            f.Source.IsCurrent(snapshot).Should().BeFalse();
            f.Store.Reads.Count.Should().Be(reads);
            f.Playback.Effects.Should().Be(effects);
            notified = true;
        }
        if (provider)
        {
            f.Speech.Changed += (_, _) => Check();
            f.Catalog.Providers = [f.Catalog.Providers[0] with { RateSupport = SpeechRateSupport.Unsupported }];
            f.Speech.Reload();
        }
        else
        {
            f.Rate.Changed += (_, _) => Check();
            f.Store.Lines["speech-windows-rate.txt"] = ["1", "4"];
            f.Rate.Observe();
        }
        notified.Should().BeTrue();
        f.Source.IsCurrent(snapshot).Should().BeFalse();
        f.Store.Writes.Should().Be(0);
    }

    [Fact]
    public async Task UnknownUnobservedSelectionNeverReloadsOrClaimsAConfiguredMapping()
    {
        await using var f = new SettingInspectionFixture();
        var rate = new WindowsSpeechRateObservation(0, null, false, SpeechProviderIds.Windows,
            SpeechRateSupport.Unsupported, 1, 1);
        f.Store.Reads.Clear();
        f.Speech.IsRateSelectionCurrent(new(SettingReadAvailability.NotObserved, [], 0), rate).Should().BeFalse();
        f.Store.Reads.Should().BeEmpty();
        f.Catalog.Reads.Should().Be(0);
        f.Playback.Effects.Should().Be(0);
    }

    [Fact]
    public async Task InspectionCurrentGateFailsClosedWhileRateApplyIsAwaitingRequestedAuditWithoutInspectionEffects()
    {
        await using var f = new SettingInspectionFixture();
        Initialize(f);
        await f.Rate.RefreshAsync(RequestOrigin.LocalUi, () => true, f.Token);
        var snapshot = await f.Source.CaptureAsync(f.Token, SettingInspectionProfile.WindowsRate);
        using var policy = new CallCommunicationPolicy(new ClearCall());
        var candidate = f.Rate.Propose("4", f.Rate.Get().Revision, policy.Current.Revision);
        var checkedApplying = false;
        f.BeforeAudit = audit =>
        {
            if (audit.Outcome != SecurityAuditOutcome.Requested
                || !string.Equals(audit.ActionId, "configuration.windows-speech-rate", StringComparison.Ordinal)) { return; }
            var reads = f.Store.Reads.Count;
            var effects = f.Playback.Effects;
            f.Source.IsCurrent(snapshot).Should().BeFalse();
            f.Store.Reads.Count.Should().Be(reads);
            f.Playback.Effects.Should().Be(effects);
            checkedApplying = true;
            throw new IOException("Requested audit unavailable before any rate write.");
        };
        await FluentActions.Awaiting(() => f.Rate.ApplyAsync(candidate, RequestOrigin.LocalUi,
            SecurityAuditInitiator.TypedCommand, policy, () => true, f.Token)).Should().ThrowAsync<IOException>();
        checkedApplying.Should().BeTrue();
        f.Store.Writes.Should().Be(0);
    }

    private sealed class ClearCall : ICallStateService
    {
        public CallState CurrentState => CallState.Clear;
        public event EventHandler<CallStateChangedEventArgs>? StateChanged { add { } remove { } }
    }
}
