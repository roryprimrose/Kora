using System.Diagnostics;

using AwesomeAssertions;

using Kora.Application.Communication;
using Kora.Application.Configuration;
using Kora.Application.Configuration.RegisteredSettings;
using Kora.Application.Hosting;
using Kora.Application.Voice;
using Kora.Core.Auditing;
using Kora.Core.Communication;
using Kora.Core.Configuration;
using Kora.Core.Configuration.RegisteredSettings;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Voice;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Application.UnitTests.Configuration;

[Collection("Host tracing")]
public sealed class RegisteredSettingUndoTests
{
    public static IEnumerable<object[]> RegisteredCases =>
        RegisteredSettingRegistry.Settings.Where(item => item.Setting != RegisteredSetting.WindowsSpeechRate).SelectMany(item => new[]
        {
            new object[] { item.Setting, false }, new object[] { item.Setting, true },
        });

    [Theory]
    [MemberData(nameof(RegisteredCases))]
    public async Task UndoRestoresExactSavedOrDefaultStateThroughNewAuditedWriteOnce(RegisteredSetting setting, bool saved)
    {
        await using var f = new Fixture();
        f.Undo.CanReview.Should().BeFalse();
        if (saved) { await f.Set(setting, alternative: true); }
        var before = f.PreferenceSnapshot();
        await f.Set(setting, alternative: !saved);
        f.Undo.CanReview.Should().BeTrue();
        var writes = f.Store.Writes;
        var audits = f.Audit.Events.Count;
        f.Review();
        f.Undo.Description.Should().Contain("current").And.Contain("restore").And.Contain("shared device-local")
            .And.Contain("future eligible").And.Contain("No speech replay");
        f.Store.Writes.Should().Be(writes, "review is inert");
        (await f.Confirm()).Should().BeTrue();
        f.PreferenceSnapshot().Should().BeEquivalentTo(before);
        f.Audit.Events.Skip(audits).Select(item => item.Outcome).Should()
            .Equal(SecurityAuditOutcome.Requested, SecurityAuditOutcome.Succeeded);
        f.Undo.CanReview.Should().BeFalse();
        f.Undo.CanConfirm.Should().BeFalse();
        f.Undo.Description.Should().Contain("new audited write").And.Contain("no redo");
        await FluentActions.Awaiting(() => f.Confirm()).Should().ThrowAsync<InvalidOperationException>();
        f.Store.Writes.Should().Be(writes + 1);
        f.Playback.SpeechCalls.Should().Be(0);
        f.Store.Text.Keys.Should().NotContain(key => key.Contains("undo", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(SpeechOption.SummaryWords)]
    [InlineData(SpeechOption.SummarySentences)]
    public async Task SummaryUndoPreservesBothCompanionCapsAndSelection(SpeechOption option)
    {
        await using var f = new Fixture();
        f.SpeechPreferences.SaveSummaryLimits(new(2, 67));
        f.SpeechPreferences.SaveSelection(new(SpeechProviderIds.Windows, "local"));
        f.Speech.Reload();
        var before = f.PreferenceSnapshot();
        await f.Set(option == SpeechOption.SummaryWords ? RegisteredSetting.SummaryWords : RegisteredSetting.SummarySentences, true);
        f.Review();
        f.Undo.Description.Should().Contain("2 sentences / 67 words");
        (await f.Confirm()).Should().BeTrue();
        f.PreferenceSnapshot().Should().BeEquivalentTo(before);
        f.SpeechPreferences.LoadSelection().Should().Be(new SpeechSelection(SpeechProviderIds.Windows, "local"));
        f.Playback.SpeechCalls.Should().Be(0);
    }

    [Theory]
    [InlineData("value")]
    [InlineData("source")]
    [InlineData("companion")]
    [InlineData("revision")]
    [InlineData("expiry")]
    [InlineData("privacy")]
    [InlineData("call")]
    [InlineData("clear")]
    public async Task StaleReviewCannotOverwriteDriftOrRevokedAdmission(string drift)
    {
        await using var f = new Fixture();
        var setting = drift is "companion" ? RegisteredSetting.SummaryWords : RegisteredSetting.Theme;
        await f.Set(setting, true);
        if (drift is "source") { await f.Set(setting, false); }
        f.Review();
        switch (drift)
        {
            case "value": f.Store.Text["appearance-theme.txt"] = "Light"; break;
            case "source": f.Store.Text.Remove("appearance-theme.txt"); break;
            case "companion": f.SpeechPreferences.SaveSummaryLimits(new(1, 40)); break;
            case "revision": await f.Set(RegisteredSetting.PresenceSize, true); break;
            case "expiry": f.Clock.Now = f.Clock.Now.AddSeconds(60); break;
            case "privacy": f.Eligible = false; break;
            case "call": f.Call.State = CallState.Active; break;
            case "clear": f.Undo.Clear(); break;
        }
        var before = f.PreferenceSnapshot();
        var writes = f.Store.Writes;
        await FluentActions.Awaiting(() => f.Confirm()).Should().ThrowAsync<InvalidOperationException>();
        f.PreferenceSnapshot().Should().BeEquivalentTo(before);
        f.Store.Writes.Should().Be(writes);
        f.Playback.SpeechCalls.Should().Be(0);
    }

    [Theory]
    [InlineData(RegisteredSetting.Theme)]
    [InlineData(RegisteredSetting.SummaryWords)]
    [InlineData(RegisteredSetting.PlaybackVolume)]
    [InlineData(RegisteredSetting.ResponseMode)]
    public async Task DriftBeforeReviewAndRequestedAuditNeverProducesRollback(RegisteredSetting setting)
    {
        await using var f = new Fixture();
        await f.Set(setting, true);
        f.Review();
        var before = f.PreferenceSnapshot();
        f.Audit.Before = audit =>
        {
            if (audit.Outcome == SecurityAuditOutcome.Requested) { f.Eligible = false; }
        };
        try { (await f.Confirm()).Should().BeFalse(); }
        catch (InvalidOperationException) { }
        f.PreferenceSnapshot().Should().BeEquivalentTo(before);
        f.Undo.CanReview.Should().BeFalse();
        f.Undo.Description.Should().Contain("unconfirmed");
        f.Playback.SpeechCalls.Should().Be(0);
    }

    [Theory]
    [InlineData(RegisteredSetting.Theme, "requested")]
    [InlineData(RegisteredSetting.Theme, "success")]
    [InlineData(RegisteredSetting.Theme, "write")]
    [InlineData(RegisteredSetting.ReducedMotion, "requested")]
    [InlineData(RegisteredSetting.ReducedMotion, "success")]
    [InlineData(RegisteredSetting.ReducedMotion, "write")]
    [InlineData(RegisteredSetting.Theme, "readback")]
    [InlineData(RegisteredSetting.ReaderTextScale, "success")]
    [InlineData(RegisteredSetting.SummaryWords, "success")]
    [InlineData(RegisteredSetting.SummaryWords, "write")]
    [InlineData(RegisteredSetting.SummaryWords, "readback")]
    [InlineData(RegisteredSetting.PlaybackVolume, "success")]
    [InlineData(RegisteredSetting.PlaybackVolume, "confirm")]
    [InlineData(RegisteredSetting.ResponseMode, "success")]
    [InlineData(RegisteredSetting.ResponseMode, "confirm")]
    public async Task UncertainWriteNeverCreatesOrRestoresUndoAuthority(RegisteredSetting setting, string failure)
    {
        await using var f = new Fixture();
        await f.Set(setting, true);
        f.Review();
        var before = f.PreferenceSnapshot();
        f.Audit.Fail = failure is "requested" ? SecurityAuditOutcome.Requested
            : failure is "success" ? SecurityAuditOutcome.Succeeded : null;
        f.Store.FailWrite = failure is "write";
        f.Store.SuppressDelete = failure is "readback";
        f.Store.FailDelete = failure is "confirm";
        var attempt = async () => { _ = await f.Confirm(); };
        try { await attempt(); }
        catch (Exception exception) when (exception is IOException or InvalidDataException or InvalidOperationException) { }
        f.Undo.CanReview.Should().BeFalse();
        f.Undo.CanConfirm.Should().BeFalse();
        f.Undo.Description.Should().Contain("unconfirmed");
        if (failure is "requested" or "write" or "readback")
        { f.PreferenceSnapshot().Should().BeEquivalentTo(before); }
        f.Playback.SpeechCalls.Should().Be(0);
        f.Store.FailWrite = f.Store.FailDelete = f.Store.SuppressDelete = false;
        f.Audit.Fail = null;
        using var restarted = new RegisteredSettingUndo(f.Appearance, f.Speech, f.Volume, f.Response, f.Policy, f.Clock);
        restarted.CanReview.Should().BeFalse();
        await FluentActions.Awaiting(() => f.Confirm()).Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task CancelIsInertAndFreshReviewCanRestoreOneCurrentReceipt()
    {
        await using var f = new Fixture();
        await f.Set(RegisteredSetting.Theme, true);
        var before = f.PreferenceSnapshot();
        f.Review();
        f.Undo.Cancel();
        f.Undo.CanConfirm.Should().BeFalse();
        f.Undo.CanReview.Should().BeTrue();
        f.PreferenceSnapshot().Should().BeEquivalentTo(before);
        f.Review();
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await FluentActions.Awaiting(() => f.Confirm(token: cancelled.Token)).Should().ThrowAsync<OperationCanceledException>();
        f.PreferenceSnapshot().Should().BeEquivalentTo(before);
        f.Undo.CanReview.Should().BeTrue();
        f.Review();
        (await f.Confirm()).Should().BeTrue();
    }

    [Fact]
    public async Task ReviewAndConfirmationCannotShareARequestOrRelabelVoiceAsUi()
    {
        await using var f = new Fixture();
        await f.Set(RegisteredSetting.SummaryWords, true);
        using (var request = Fixture.Input(RequestOrigin.ActivatedVoice))
        {
            f.Undo.ReviewLast(SecurityAuditInitiator.VoiceCommand, () => f.Eligible);
            await FluentActions.Awaiting(() => f.Undo.ConfirmAsync(SecurityAuditInitiator.VoiceCommand, f.Token))
                .Should().ThrowAsync<InvalidOperationException>();
        }
        await FluentActions.Awaiting(() => f.Confirm()).Should().ThrowAsync<InvalidOperationException>();
        (await f.Confirm(SecurityAuditInitiator.VoiceCommand)).Should().BeTrue();
    }

    [Theory]
    [InlineData(RegisteredSetting.SummaryWords)]
    [InlineData(RegisteredSetting.PlaybackVolume)]
    [InlineData(RegisteredSetting.ResponseMode)]
    public async Task ProtectedCallRejectsOriginalVoiceUndoButAllowsNewNativeReview(RegisteredSetting setting)
    {
        await using var f = new Fixture();
        await f.Set(setting, true);
        f.Call.State = CallState.Active;
        using (var voice = Fixture.Input(RequestOrigin.ActivatedVoice))
        {
            FluentActions.Invoking(() => f.Undo.ReviewLast(SecurityAuditInitiator.VoiceCommand, () => f.Eligible))
                .Should().Throw<InvalidOperationException>();
        }
        f.Undo.CanConfirm.Should().BeFalse();
        f.Review();
        (await f.Confirm()).Should().BeTrue();
        f.Playback.SpeechCalls.Should().Be(0);
    }

    [Theory]
    [InlineData(SecurityAuditInitiator.TypedCommand)]
    [InlineData(SecurityAuditInitiator.VoiceCommand)]
    public async Task AppearanceUndoRetainsNativeOnlyReview(SecurityAuditInitiator initiator)
    {
        await using var f = new Fixture();
        await f.Set(RegisteredSetting.Theme, true);
        using var request = Fixture.Input(initiator == SecurityAuditInitiator.VoiceCommand ? RequestOrigin.ActivatedVoice : RequestOrigin.LocalUi);
        FluentActions.Invoking(() => f.Undo.ReviewLast(initiator, () => f.Eligible)).Should().Throw<InvalidOperationException>();
        f.Undo.CanConfirm.Should().BeFalse();
    }

    [Theory]
    [InlineData("undo the last settings change", RegisteredSettingSuggestionOperation.Review)]
    [InlineData("Nova, CONFIRM SETTINGS UNDO", RegisteredSettingSuggestionOperation.Confirm)]
    [InlineData("Nova\tcancel settings undo", RegisteredSettingSuggestionOperation.Cancel)]
    [InlineData("Novax confirm settings undo", null)]
    [InlineData("yes", null)]
    [InlineData("undo the last settings change and confirm", null)]
    [InlineData("Nova", null)]
    public void UndoCommandsAreClosedAndCurrentNameAware(string input, RegisteredSettingSuggestionOperation? expected)
    {
        RegisteredSettingUndoCommand.Parse(input, "Nova").Should().Be(expected);
    }

    [Theory]
    [InlineData(RegisteredSetting.Theme)]
    [InlineData(RegisteredSetting.SummaryWords)]
    [InlineData(RegisteredSetting.PlaybackVolume)]
    [InlineData(RegisteredSetting.ResponseMode)]
    public async Task InternalReceiptRejectsChangedSourceEvenWithoutPresentationReview(RegisteredSetting setting)
    {
        await using var f = new Fixture();
        await f.Set(setting, true);
        var receipt = f.LastChange!;
        await f.Set(setting, false);
        receipt.Matches().Should().BeFalse();
        var snapshot = f.PreferenceSnapshot();
        await FluentActions.Awaiting(() => receipt.Restore(RequestOrigin.LocalUi, SecurityAuditInitiator.LocalUser,
            f.Policy, f.Policy.Current.Revision, () => f.Eligible, f.Token)).Should().ThrowAsync<InvalidOperationException>();
        f.PreferenceSnapshot().Should().BeEquivalentTo(snapshot);
    }

    [Theory]
    [InlineData(RegisteredSetting.PlaybackVolume, "speech-playback-volume.txt", "1\n31")]
    [InlineData(RegisteredSetting.ResponseMode, "response-output-mode.txt", "VoiceOnly")]
    [InlineData(RegisteredSetting.SummaryWords, "speech-summary-limits.txt", "1\n2\n40")]
    public async Task ExternalDriftCannotQualifyAReceiptWithoutAnObservedRevisionChange(
        RegisteredSetting setting, string file, string external)
    {
        await using var f = new Fixture();
        await f.Set(setting, true);
        var receipt = f.LastChange!;
        f.Store.Text[file] = external;
        receipt.Matches().Should().BeFalse();
        using var request = Fixture.Input();
        FluentActions.Invoking(() => f.Undo.ReviewLast(SecurityAuditInitiator.LocalUser, () => f.Eligible))
            .Should().Throw<InvalidOperationException>();
        f.Store.Text[file].Should().Be(external);
    }

    [Fact]
    public async Task UndoOfANativeVolumeResetRestoresItsExactPreviousSavedOverride()
    {
        await using var f = new Fixture();
        await f.Set(RegisteredSetting.PlaybackVolume, true);
        using (var request = Fixture.Input())
        {
            await f.Volume.RefreshAsync(RequestOrigin.LocalUi, () => f.Eligible, f.Token);
            var reset = f.Volume.Propose(null, f.Volume.Get().Revision, f.Policy.Current.Revision);
            (await f.Volume.ApplyAsync(reset, RequestOrigin.LocalUi, SecurityAuditInitiator.LocalUser,
                f.Policy, () => f.Eligible, f.Token)).Should().BeTrue();
        }
        f.LastChange!.Matches().Should().BeTrue();
        f.Volume.Get().Source.Should().Be("default");
        f.Review();
        (await f.Confirm()).Should().BeTrue();
        f.Volume.Get().Source.Should().Be("saved");
        f.Volume.Get().Desired.Should().Be(new PlaybackVolume(30));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NullAndSavedVolumeSourcesCannotBeExchangedOutsideTheWriter(bool reset)
    {
        await using var f = new Fixture();
        await f.Set(RegisteredSetting.PlaybackVolume, true);
        if (reset)
        {
            using var request = Fixture.Input();
            await f.Volume.RefreshAsync(RequestOrigin.LocalUi, () => f.Eligible, f.Token);
            var candidate = f.Volume.Propose(null, f.Volume.Get().Revision, f.Policy.Current.Revision);
            (await f.Volume.ApplyAsync(candidate, RequestOrigin.LocalUi, SecurityAuditInitiator.LocalUser,
                f.Policy, () => f.Eligible, f.Token)).Should().BeTrue();
            f.Store.Text["speech-playback-volume.txt"] = "1\n100";
        }
        else { f.Store.Text.Remove("speech-playback-volume.txt"); }
        f.LastChange!.Matches().Should().BeFalse();
    }

    [Theory]
    [InlineData(RequestOrigin.ActivatedVoice, SecurityAuditInitiator.VoiceCommand)]
    [InlineData(RequestOrigin.LocalUi, SecurityAuditInitiator.TypedCommand)]
    public async Task AppearanceReceiptCannotBeRelabelledOutsideNativeConfirmation(RequestOrigin origin, SecurityAuditInitiator initiator)
    {
        await using var f = new Fixture();
        await f.Set(RegisteredSetting.Theme, true);
        var receipt = f.LastChange!;
        using var request = Fixture.Input(origin);
        await FluentActions.Awaiting(() => receipt.Restore(origin, initiator, f.Policy,
            f.Policy.Current.Revision, () => f.Eligible, f.Token)).Should().ThrowAsync<InvalidOperationException>();
    }

    [Theory]
    [InlineData(RegisteredSetting.Theme)]
    [InlineData(RegisteredSetting.SummaryWords)]
    public async Task TerminalAuditDriftCannotCreateAReceiptOrOverrideACompanion(RegisteredSetting setting)
    {
        await using var f = new Fixture();
        f.Audit.Before = audit =>
        {
            if (audit.Outcome == SecurityAuditOutcome.Succeeded)
            {
                if (setting == RegisteredSetting.Theme) { f.Store.Text["appearance-theme.txt"] = "Light"; }
                else { f.SpeechPreferences.SaveSummaryLimits(new(2, 60)); }
            }
        };
        await FluentActions.Awaiting(() => f.Set(setting, true)).Should().ThrowAsync<InvalidDataException>();
        f.Undo.CanReview.Should().BeFalse();
        f.Undo.Description.Should().Contain("unconfirmed");
    }

    [Theory]
    [InlineData(RegisteredSetting.PlaybackVolume)]
    [InlineData(RegisteredSetting.ResponseMode)]
    public async Task DriftDuringFreshAudioAdmissionCannotBeReinterpretedAsUndoAuthority(RegisteredSetting setting)
    {
        await using var f = new Fixture();
        await f.Set(setting, true);
        f.Review();
        f.ControlStore.BeforeControlIntent = _ =>
        {
            if (setting == RegisteredSetting.PlaybackVolume)
            { f.Store.Text["speech-playback-volume.txt"] = "1\n66"; }
            else { f.Store.Text["response-output-mode.txt"] = "VoiceOnly"; }
        };
        await FluentActions.Awaiting(() => f.Confirm()).Should().ThrowAsync<InvalidOperationException>();
        f.Undo.CanReview.Should().BeFalse();
        f.Playback.SpeechCalls.Should().Be(0);
    }

    [Theory]
    [InlineData(RequestOrigin.HostSystem, SecurityAuditInitiator.LocalUser)]
    [InlineData(RequestOrigin.LocalUi, SecurityAuditInitiator.ModelSuggestion)]
    [InlineData(RequestOrigin.LocalUi, SecurityAuditInitiator.VoiceCommand)]
    [InlineData(RequestOrigin.ActivatedVoice, SecurityAuditInitiator.LocalUser)]
    public async Task NonoriginalInputNeverGrantsReviewAuthority(RequestOrigin origin, SecurityAuditInitiator initiator)
    {
        await using var f = new Fixture();
        await f.Set(RegisteredSetting.SummaryWords, true);
        using var request = Fixture.Input(origin);
        FluentActions.Invoking(() => f.Undo.ReviewLast(initiator, () => f.Eligible))
            .Should().Throw<InvalidOperationException>();
        f.Undo.CanConfirm.Should().BeFalse();
    }

    [Fact]
    public async Task MissingExpiredCompletedAndIneligibleReviewInputsFailClosed()
    {
        await using var f = new Fixture();
        using (var request = Fixture.Input())
        {
            FluentActions.Invoking(() => f.Undo.ReviewLast(SecurityAuditInitiator.LocalUser, () => f.Eligible))
                .Should().Throw<InvalidOperationException>();
        }
        await f.Set(RegisteredSetting.SummaryWords, true);
        f.Eligible = false;
        using (var request = Fixture.Input())
        {
            FluentActions.Invoking(() => f.Undo.ReviewLast(SecurityAuditInitiator.LocalUser, () => f.Eligible))
                .Should().Throw<InvalidOperationException>();
        }
        f.Eligible = true;
        using (var request = Fixture.Input())
        {
            request.Complete(HostOperationOutcome.Completed);
            FluentActions.Invoking(() => f.Undo.ReviewLast(SecurityAuditInitiator.LocalUser, () => f.Eligible))
                .Should().Throw<InvalidOperationException>();
        }
        f.Undo.Dispose();
        f.Undo.CanReview.Should().BeFalse();
        f.Undo.CanConfirm.Should().BeFalse();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RemovingAnOverrideWithTheSameEffectiveDefaultPreservesSourceSemantics(bool saved)
    {
        await using var f = new Fixture();
        if (saved) { f.AppearancePreferences.SaveThemeMode(ApplicationThemeMode.System); }
        var state = f.Appearance.Get(AppearanceOption.Theme);
        var admitted = f.Appearance.Propose(AppearanceOption.Theme, state.Value, state.Revision,
            SecurityAuditInitiator.LocalUser, RequestOrigin.LocalUi, () => f.Eligible);
        var proposal = new AppearanceProposal(admitted.Owner, admitted.Option, admitted.Value, admitted.Revision,
            admitted.Initiator, RequestOrigin.LocalUi, () => f.Eligible) { RestoreDefault = true };
        (await f.Appearance.ApplyAsync(proposal, f.Token)).Succeeded.Should().BeTrue();
        f.AppearancePreferences.LoadThemeMode().Should().BeNull();
        f.Appearance.Get(AppearanceOption.Theme).IsSaved.Should().BeFalse();
        f.Audit.Events.Should().HaveCount(saved ? 2 : 0);
    }

    [Theory]
    [InlineData(AppearanceOption.NativePresenceRenderer)]
    [InlineData(AppearanceOption.ResponseTimeout)]
    [InlineData((AppearanceOption)(-1))]
    public async Task ResetCannotBroadenTheRegisteredPreferenceSubset(AppearanceOption option)
    {
        await using var f = new Fixture();
        FluentActions.Invoking(() => f.AppearancePreferences.ResetRegisteredOption(option))
            .Should().Throw<ArgumentOutOfRangeException>();
        f.Store.Text.Should().BeEmpty();
    }

    [Fact]
    public async Task StaleExternalAppearanceBeforeSetterAdmissionDoesNotWriteOrAudit()
    {
        await using var f = new Fixture();
        var current = f.Appearance.Get(AppearanceOption.Theme);
        var proposal = f.Appearance.Propose(AppearanceOption.Theme, new AppearanceValue.Theme(ApplicationThemeMode.Dark),
            current.Revision, SecurityAuditInitiator.LocalUser, RequestOrigin.LocalUi, () => f.Eligible);
        f.AppearancePreferences.SaveThemeMode(ApplicationThemeMode.Light);
        var writes = f.Store.Writes;
        await FluentActions.Awaiting(() => f.Appearance.ApplyAsync(proposal, f.Token)).Should().ThrowAsync<InvalidOperationException>();
        f.AppearancePreferences.LoadThemeMode().Should().Be(ApplicationThemeMode.Light);
        f.Store.Writes.Should().Be(writes);
        f.Audit.Events.Should().BeEmpty();
    }

    [Theory]
    [InlineData("drift")]
    [InlineData("replacement")]
    [InlineData("disposed")]
    public async Task SourceReadCannotPublishAReviewAfterReceiptLoss(string loss)
    {
        await using var f = new Fixture();
        await f.Set(RegisteredSetting.Theme, true);
        if (loss is "drift") { f.Store.Text["appearance-theme.txt"] = "Light"; }
        if (loss is "disposed") { f.Undo.Dispose(); }
        if (loss is "replacement")
        {
            f.Store.BeforeRead = () =>
            {
                f.Store.BeforeRead = null;
                f.Undo.Clear();
            };
        }
        using var request = Fixture.Input();
        FluentActions.Invoking(() => f.Undo.ReviewLast(SecurityAuditInitiator.LocalUser, () => f.Eligible))
            .Should().Throw<InvalidOperationException>();
        f.Undo.CanConfirm.Should().BeFalse();
    }

    [Fact]
    public async Task CancellationAndRepeatedConfirmationCannotClaimRollbackWhileAdmissionIsPending()
    {
        await using var f = new Fixture();
        await f.Set(RegisteredSetting.PlaybackVolume, true);
        f.Review();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        f.ControlStore.BeforeOperation = () =>
        {
            f.ControlStore.BeforeOperation = null;
            entered.TrySetResult();
            release.Wait(f.Token);
        };
        var pending = Task.Run(() => f.Confirm(), f.Token);
        try
        {
            await entered.Task.WaitAsync(f.Token);
            pending.IsCompleted.Should().BeFalse();
            f.Undo.CanReview.Should().BeFalse();
            FluentActions.Invoking(f.Undo.Cancel).Should().Throw<InvalidOperationException>();
            await FluentActions.Awaiting(() => f.Confirm()).Should().ThrowAsync<InvalidOperationException>();
        }
        finally { release.Set(); }
        (await pending).Should().BeTrue();
        f.Undo.CanReview.Should().BeFalse();
    }

    [Fact]
    public async Task DisposedOwnerIgnoresAChangeAlreadyCapturedByAnEventPublisher()
    {
        await using var f = new Fixture();
        RegisteredSettingUndo? closing = null;
        f.Appearance.UndoChanged += _ => closing?.Dispose();
        closing = new(f.Appearance, f.Speech, f.Volume, f.Response, f.Policy, f.Clock);
        await f.Set(RegisteredSetting.Theme, true);
        closing.CanReview.Should().BeFalse();
        closing.CanConfirm.Should().BeFalse();
        closing.Dispose();
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly ActivityListener listener = new()
        {
            ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
        };
        internal Fixture()
        {
            ActivitySource.AddActivityListener(listener);
            Policy = new(Call);
            Admission = new(ControlStore, ControlStore, new HostTaskCoordinator(ControlStore));
            AppearancePreferences = new(Store, NullLogger<LocalAppearancePreferences>.Instance);
            SpeechPreferences = new(Store, NullLogger<LocalTextToSpeechPreferences>.Instance);
            Appearance = new(AppearancePreferences, Audit, NullLogger<AppearanceConfigurationService>.Instance);
            Speech = new(SpeechPreferences, Catalog, Audit, NullLogger<SpeechConfigurationService>.Instance);
            Volume = new(new LocalPlaybackVolumePreferences(Store), Playback, Admission, Audit);
            Response = new(new LocalResponseOutputPreferences(Store, NullLogger<LocalResponseOutputPreferences>.Instance),
                Admission, Audit, Playback);
            Volume.Observe();
            Response.Observe();
            Undo = new(Appearance, Speech, Volume, Response, Policy, Clock);
            Appearance.UndoChanged += Capture;
            Speech.UndoChanged += Capture;
            Volume.UndoChanged += Capture;
            Response.UndoChanged += Capture;
        }
        private void Capture(RegisteredSettingChange? value) => LastChange = value;
        internal RegisteredSettingChange? LastChange { get; private set; }
        internal CancellationToken Token => TestContext.Current.CancellationToken;
        internal MemoryPreferences Store { get; } = new();
        internal Audit Audit { get; } = new();
        internal Call Call { get; } = new();
        internal Clock Clock { get; } = new();
        internal Catalog Catalog { get; } = new();
        internal Playback Playback { get; } = new();
        internal AudioControlTestStore ControlStore { get; } = new();
        internal AudioControlAdmission Admission { get; }
        internal CallCommunicationPolicy Policy { get; }
        internal LocalAppearancePreferences AppearancePreferences { get; }
        internal LocalTextToSpeechPreferences SpeechPreferences { get; }
        internal AppearanceConfigurationService Appearance { get; }
        internal SpeechConfigurationService Speech { get; }
        internal PlaybackVolumeConfigurationService Volume { get; }
        internal ResponseModeConfigurationService Response { get; }
        internal RegisteredSettingUndo Undo { get; }
        internal bool Eligible { get; set; } = true;
        internal Dictionary<string, string> PreferenceSnapshot() =>
            Store.Text.Where(item => !item.Key.Contains("unconfirmed", StringComparison.Ordinal))
                .ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        internal static HostActivity Input(RequestOrigin origin = RequestOrigin.LocalUi) =>
            HostActivity.BeginRoot(HostRequest.Create(origin), HostActivityLayer.Application, HostOperation.Request);
        internal void Review()
        {
            using var request = Input();
            Undo.ReviewLast(SecurityAuditInitiator.LocalUser, () => Eligible);
        }
        internal async Task<bool> Confirm(SecurityAuditInitiator initiator = SecurityAuditInitiator.LocalUser,
            CancellationToken? token = null)
        {
            using var request = Input(initiator == SecurityAuditInitiator.VoiceCommand ? RequestOrigin.ActivatedVoice : RequestOrigin.LocalUi);
            return await Undo.ConfirmAsync(initiator, token ?? Token);
        }
        internal async Task Set(RegisteredSetting setting, bool alternative)
        {
            using var request = Input();
            var descriptor = RegisteredSettingRegistry.Get(setting);
            if (descriptor.RequiresNativeReview)
            {
                var option = AppearanceOptionRegistry.Options.Single(item => string.Equals(item.Id, descriptor.Id, StringComparison.Ordinal));
                var value = !alternative ? option.Default : option.Default switch
                {
                    AppearanceValue.Theme => new AppearanceValue.Theme(ApplicationThemeMode.Dark),
                    AppearanceValue.Toggle toggle => new AppearanceValue.Toggle(!toggle.Value),
                    AppearanceValue.Number => new AppearanceValue.Number(option.Maximum!.Value),
                    _ => throw new InvalidOperationException(),
                };
                var proposal = Appearance.Propose(option.Option, value, Appearance.Get(option.Option).Revision,
                    SecurityAuditInitiator.LocalUser, RequestOrigin.LocalUi, () => Eligible);
                (await Appearance.ApplyAsync(proposal, Token)).Succeeded.Should().BeTrue();
            }
            else if (setting is RegisteredSetting.SummaryWords or RegisteredSetting.SummarySentences)
            {
                var option = setting == RegisteredSetting.SummaryWords ? SpeechOption.SummaryWords : SpeechOption.SummarySentences;
                var proposal = Speech.Propose(option, alternative ? option == SpeechOption.SummaryWords ? "40" : "1" : null,
                    Speech.Get().Revision, Policy.Current.Revision, RequestOrigin.LocalUi, SecurityAuditInitiator.LocalUser);
                Speech.Apply(proposal, Policy, () => Eligible, Token).Succeeded.Should().BeTrue();
            }
            else if (setting == RegisteredSetting.PlaybackVolume)
            {
                await Volume.RefreshAsync(RequestOrigin.LocalUi, () => Eligible, Token);
                var proposal = Volume.Propose(alternative ? "30" : "100", Volume.Get().Revision, Policy.Current.Revision);
                (await Volume.ApplyAsync(proposal, RequestOrigin.LocalUi, SecurityAuditInitiator.LocalUser, Policy, () => Eligible, Token))
                    .Should().BeTrue();
            }
            else
            {
                await Response.RefreshAsync(RequestOrigin.LocalUi, () => Eligible, Token);
                var choice = Response.Choices.Single(item => item.Mode == (alternative ? ResponseOutputMode.VisualOnly : ResponseOutputMode.Hybrid));
                (await Response.SelectAsync(choice, Policy.Current.Revision, RequestOrigin.LocalUi,
                    SecurityAuditInitiator.LocalUser, Policy, () => Eligible, Token)).Should().BeTrue();
            }
        }
        public async ValueTask DisposeAsync()
        {
            Undo.Dispose();
            Policy.Dispose();
            await Admission.DisposeAsync();
            listener.Dispose();
        }
    }

    private sealed class MemoryPreferences : IPreferenceStore
    {
        internal Dictionary<string, string> Text { get; } = new(StringComparer.Ordinal);
        internal int Writes { get; private set; }
        internal bool FailWrite { get; set; }
        internal bool FailDelete { get; set; }
        internal bool SuppressDelete { get; set; }
        internal Action? BeforeRead { get; set; }
        public string? ReadText(string fileName) { BeforeRead?.Invoke(); return Text.GetValueOrDefault(fileName); }
        public string[]? ReadLines(string fileName) => ReadText(fileName)?.Split('\n');
        public void WriteText(string fileName, string contents)
        {
            if (FailWrite) { throw new IOException("fixture write failure"); }
            Text[fileName] = contents;
            if (!fileName.Contains("unconfirmed", StringComparison.Ordinal)) { Writes++; }
        }
        public void WriteLines(string fileName, IEnumerable<string> contents) => WriteText(fileName, string.Join('\n', contents));
        public void Delete(string fileName)
        {
            if (FailWrite || FailDelete) { throw new IOException("fixture delete failure"); }
            if (SuppressDelete) { return; }
            Text.Remove(fileName);
            if (!fileName.Contains("unconfirmed", StringComparison.Ordinal)) { Writes++; }
        }
    }

    private sealed class Audit : ISecurityAuditLog
    {
        internal List<SecurityAuditEvent> Events { get; } = [];
        internal SecurityAuditOutcome? Fail { get; set; }
        internal Action<SecurityAuditEvent>? Before { get; set; }
        public void Write(SecurityAuditEvent auditEvent)
        {
            Before?.Invoke(auditEvent);
            if (Fail == auditEvent.Outcome) { throw new IOException("fixture audit failure"); }
            Events.Add(auditEvent);
        }
    }

    private sealed class Call : ICallStateService
    {
        private CallState state = CallState.Clear;
        internal CallState State { set { state = value; StateChanged?.Invoke(this, new(value)); } }
        public CallState CurrentState => state;
        public event EventHandler<CallStateChangedEventArgs>? StateChanged;
    }

    private sealed class Clock : TimeProvider
    {
        internal DateTimeOffset Now { get; set; } = new(2026, 10, 29, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class Catalog : ISpeechCatalog
    {
        internal int Reads { get; private set; }
        public IReadOnlyList<SpeechProvider> GetProviders()
        {
            Reads++;
            return [new(SpeechProviderIds.Windows, "Local", "Installed", true, true, null, "local")];
        }
        public IReadOnlyList<SpeechVoice> GetVoices() => [new("local", "Local", "en-US", SpeechVoiceGender.Neutral)];
        public SpeechVoice? GetDefaultVoice() => throw new NotSupportedException();
    }

    private sealed class Playback : ISpeechPlaybackService, IPlaybackVolumeControl
    {
        internal int SpeechCalls { get; private set; }
        public bool IsSpeaking => false;
        public SpeechPlaybackFrame PlaybackFrame => SpeechPlaybackFrame.Inactive;
        public void InvalidateOutput() { }
        public void SetPlaybackVolume(PlaybackVolume? volume) { }
        public Task SpeakAsync(string text, SpeechVoice voice, AudioOutputDevice outputDevice, CancellationToken cancellationToken = default)
        { SpeechCalls++; throw new InvalidOperationException("Undo must never speak."); }
        public Task StopAsync(CancellationToken cancellationToken = default) => throw new InvalidOperationException("Undo must not reverse effects.");
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
