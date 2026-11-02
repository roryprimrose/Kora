using Kora.Core.Auditing;
using Kora.Core.Configuration;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

using Microsoft.Extensions.Logging;

namespace Kora.Application.Configuration;

public sealed partial class AppearanceConfigurationService(
    IAppearancePreferences preferences,
    ISecurityAuditLog auditLog,
    ILogger<AppearanceConfigurationService> logger)
{
    private readonly Lock gate = new();
    private readonly Guid owner = Guid.NewGuid();
    private readonly Dictionary<AppearanceOption, Action<AppearanceValue>> save = new()
    {
        [AppearanceOption.NativePresenceRenderer] = value => preferences.SaveNativePresenceRendererEnabled(value.GetToggle()),
        [AppearanceOption.ReducedMotion] = value => preferences.SaveReducedMotionEnabled(value.GetToggle()),
        [AppearanceOption.Theme] = value => preferences.SaveThemeMode(value.GetTheme()),
        [AppearanceOption.PresenceDisplay] = value => preferences.SavePresenceDisplayEnabled(value.GetToggle()),
        [AppearanceOption.PresenceTimeout] = value => preferences.SavePresenceTimeoutSeconds(value.GetNumber()),
        [AppearanceOption.ResponseTimeout] = value => preferences.SaveResponseTimeoutSeconds(value.GetNumber()),
        [AppearanceOption.PresenceSize] = value => preferences.SavePresenceSizePixels(value.GetNumber()),
        [AppearanceOption.DotSize] = value => preferences.SavePresenceDotSizePercent(value.GetNumber()),
        [AppearanceOption.DotDensity] = value => preferences.SavePresenceDotDensityPercent(value.GetNumber()),
        [AppearanceOption.MovementSpeed] = value => preferences.SavePresenceMovementSpeedPercent(value.GetNumber()),
        [AppearanceOption.SpeechScaling] = value => preferences.SavePresenceSpeechScalingEnabled(value.GetToggle()),
        [AppearanceOption.SpeechScaleAmount] = value => preferences.SavePresenceSpeechScaleAmountPercent(value.GetNumber()),
        [AppearanceOption.ReaderTextScale] = value => preferences.SaveReaderTextScalePercent(value.GetNumber()),
    };
    private Dictionary<AppearanceOption, AppearanceValue> values =
        AppearanceOptionRegistry.Options.ToDictionary(item => item.Option, item => item.Default);
    private long revision;
    private HashSet<AppearanceOption> savedOptions = [];
    private bool loaded;
    private bool publishing;
    private bool nativePresenceStartupDefault;

    public void ConfigureNativePresenceStartupDefault(bool useNative)
    {
        lock (gate)
        {
            if (loaded)
            {
                throw new InvalidOperationException("The renderer startup default must be configured before preferences are loaded.");
            }
            nativePresenceStartupDefault = useNative;
        }
    }

    public event EventHandler? Changed;

    public AppearanceOptionState Get(AppearanceOption option)
    {
        lock (gate)
        {
            if (!loaded)
            {
                Reload();
            }
            if (option == AppearanceOption.ReaderTextScale) { EnsureReaderState(); }
            return new(AppearanceOptionRegistry.Get(option), values[option], revision, savedOptions.Contains(option));
        }
    }

    public void Reload()
    {
        lock (gate)
        {
            EnsureNotPublishing();
            var saved = new Dictionary<AppearanceOption, AppearanceValue?>
            {
                [AppearanceOption.NativePresenceRenderer] = preferences.LoadNativePresenceRendererEnabled() is { } native
                    ? new AppearanceValue.Toggle(native) : null,
                [AppearanceOption.Theme] = preferences.LoadThemeMode() is { } theme ? new AppearanceValue.Theme(theme) : null,
                [AppearanceOption.ReducedMotion] = preferences.LoadReducedMotionEnabled() is { } reducedMotion
                    ? new AppearanceValue.Toggle(reducedMotion) : null,
                [AppearanceOption.ReaderTextScale] = preferences.LoadReaderTextScalePercent() is { } scale ? new AppearanceValue.Number(scale) : null,
                [AppearanceOption.PresenceDisplay] = preferences.LoadPresenceDisplayEnabled() is { } display ? new AppearanceValue.Toggle(display) : null,
                [AppearanceOption.PresenceTimeout] = preferences.LoadPresenceTimeoutSeconds() is { } presenceTimeout ? new AppearanceValue.Number(presenceTimeout) : null,
                [AppearanceOption.ResponseTimeout] = preferences.LoadResponseTimeoutSeconds() is { } responseTimeout ? new AppearanceValue.Number(responseTimeout) : null,
                [AppearanceOption.PresenceSize] = preferences.LoadPresenceSizePixels() is { } size ? new AppearanceValue.Number(size) : null,
                [AppearanceOption.DotSize] = preferences.LoadPresenceDotSizePercent() is { } dotSize ? new AppearanceValue.Number(dotSize) : null,
                [AppearanceOption.DotDensity] = preferences.LoadPresenceDotDensityPercent() is { } density ? new AppearanceValue.Number(density) : null,
                [AppearanceOption.MovementSpeed] = preferences.LoadPresenceMovementSpeedPercent() is { } speed ? new AppearanceValue.Number(speed) : null,
                [AppearanceOption.SpeechScaling] = preferences.LoadPresenceSpeechScalingEnabled() is { } scaling ? new AppearanceValue.Toggle(scaling) : null,
                [AppearanceOption.SpeechScaleAmount] = preferences.LoadPresenceSpeechScaleAmountPercent() is { } amount ? new AppearanceValue.Number(amount) : null,
            };
            var effective = saved.ToDictionary(item => item.Key, item => item.Value ?? AppearanceOptionRegistry.Get(item.Key).Default);
            if (saved[AppearanceOption.NativePresenceRenderer] is null)
            {
                effective[AppearanceOption.NativePresenceRenderer] = new AppearanceValue.Toggle(nativePresenceStartupDefault);
            }
            var persisted = saved.Where(item => item.Value is not null).Select(item => item.Key).ToHashSet();
            foreach (var item in effective)
            {
                try
                {
                    AppearanceOptionRegistry.Validate(item.Key, item.Value);
                }
                catch (ArgumentOutOfRangeException exception)
                {
                    throw new InvalidDataException("The saved appearance option is invalid.", exception);
                }
            }
            loaded = true;
            if (effective.Any(item => values[item.Key] != item.Value) || !savedOptions.SetEquals(persisted))
            {
                values = effective;
                savedOptions = persisted;
                revision++;
                Publish();
            }
        }
    }

    public AppearanceProposal Propose(AppearanceOption option, AppearanceValue value, long expectedRevision,
        SecurityAuditInitiator initiator, RequestOrigin? originalOrigin = null, Func<bool>? remainsEligible = null)
    {
        AppearanceOptionRegistry.Validate(option, value);
        ValidateInitiator(initiator);
        if (option == AppearanceOption.ReaderTextScale)
        {
            ValidateReaderAdmission(initiator, originalOrigin, remainsEligible);
        }
        return new(owner, option, value, expectedRevision, initiator, originalOrigin, remainsEligible);
    }

    public AppearanceProposal ProposeReset(AppearanceOption option, long expectedRevision, SecurityAuditInitiator initiator,
        RequestOrigin? originalOrigin = null, Func<bool>? remainsEligible = null) =>
        Propose(option, AppearanceOptionRegistry.Get(option).Default, expectedRevision, initiator, originalOrigin, remainsEligible);

    public AppearanceProposal ProposeReaderTextScaleAdjustment(int percentagePoints, SecurityAuditInitiator initiator,
        RequestOrigin originalOrigin, Func<bool> remainsEligible)
    {
        ValidateInitiator(initiator);
        ValidateReaderAdmission(initiator, originalOrigin, remainsEligible);
        ArgumentOutOfRangeException.ThrowIfZero(percentagePoints);
        var current = Get(AppearanceOption.ReaderTextScale);
        var value = new AppearanceValue.Number(checked(current.Value.GetNumber() + percentagePoints));
        return Propose(AppearanceOption.ReaderTextScale, value, current.Revision, initiator, originalOrigin, remainsEligible);
    }

    public Task<AppearanceApplyResult> ApplyAsync(AppearanceProposal proposal, CancellationToken cancellationToken) =>
        Task.Run(() => Apply(proposal, cancellationToken), cancellationToken);

    // Cancellation is admitted before the atomic write, never after commit.
    public AppearanceApplyResult Apply(AppearanceProposal proposal) => Apply(proposal, CancellationToken.None);

    public AppearanceApplyResult Apply(AppearanceProposal proposal, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        var timeRenderer = proposal.Option == AppearanceOption.NativePresenceRenderer;
        if (timeRenderer) { PresenceSwitchTiming.Mark(PresenceSwitchPhase.ApplyStarted); }
        lock (gate)
        {
            if (timeRenderer) { PresenceSwitchTiming.Mark(PresenceSwitchPhase.ConfigurationGateEntered); }
            EnsureNotPublishing();
            cancellationToken.ThrowIfCancellationRequested();
            if (proposal.Owner != owner)
            {
                throw new ArgumentException("The appearance proposal belongs to another host registry.", nameof(proposal));
            }
            ValidateInitiator(proposal.Initiator);
            if (!loaded)
            {
                Reload();
            }
            AppearanceOptionRegistry.Validate(proposal.Option, proposal.Value);
            RequireEligibility(proposal);
            if (proposal.Option == AppearanceOption.ReaderTextScale)
            {
                ValidateReaderAdmission(proposal.Initiator, proposal.OriginalOrigin, proposal.RemainsEligible);
                EnsureReaderState();
            }
            if (proposal.Revision != revision)
            {
                StaleProposal(logger, AppearanceOptionRegistry.Get(proposal.Option).Id, proposal.Revision);
                return new(AppearanceApplyOutcome.Stale, Get(proposal.Option), "Appearance changed. Inspect the current value and submit a new proposal.");
            }
            if (values[proposal.Option] == proposal.Value
                && (!proposal.RestoreDefault || !savedOptions.Contains(proposal.Option)))
            {
                return new(AppearanceApplyOutcome.Unchanged, Get(proposal.Option));
            }
            if (proposal.Option == AppearanceOption.ReaderTextScale)
            {
                return ApplyReaderScale(proposal, cancellationToken);
            }
            var before = Get(proposal.Option);
            var registered = Kora.Core.Configuration.RegisteredSettings.RegisteredSettingRegistry.Settings
                .Any(item => string.Equals(item.Id, before.Descriptor.Id, StringComparison.Ordinal));
            if (registered && !MatchesRegistered(before))
            { throw new InvalidOperationException("Saved appearance changed. Refresh before a new proposal."); }
            var audit = new SecurityAuditEvent(Guid.NewGuid(), SecurityAuditCategory.ConfigurationWrite,
                AppearanceOptionRegistry.Get(proposal.Option).AuditAction, SecurityAuditOutcome.Requested,
                proposal.Initiator, "preferences.device-local");
            using var activity = HostActivity.Current is not null
                ? HostActivity.BeginChild(HostActivityLayer.Application, HostOperation.Storage)
                : HostActivity.BeginRoot(HostRequest.Create(proposal.Initiator == SecurityAuditInitiator.VoiceCommand
                    ? RequestOrigin.ActivatedVoice : RequestOrigin.LocalUi), HostActivityLayer.Application, HostOperation.Storage);
            if (timeRenderer) { PresenceSwitchTiming.Mark(PresenceSwitchPhase.RequestedAuditStarted); }
            UndoChanged?.Invoke(null);
            auditLog.Write(audit);
            if (timeRenderer) { PresenceSwitchTiming.Mark(PresenceSwitchPhase.RequestedAuditReturned); }
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                RequireEligibility(proposal);
                if (timeRenderer) { PresenceSwitchTiming.Mark(PresenceSwitchPhase.PreferenceWriteStarted); }
                if (proposal.RestoreDefault) { preferences.ResetRegisteredOption(proposal.Option); }
                else { save[proposal.Option](proposal.Value); }
                if (registered && ReadRegistered(proposal.Option) != (proposal.RestoreDefault ? null : proposal.Value))
                { throw new InvalidDataException("Appearance readback does not match the exact preference."); }
                if (timeRenderer) { PresenceSwitchTiming.Mark(PresenceSwitchPhase.PreferenceWriteReturned); }
            }
            catch (OperationCanceledException)
            {
                activity.Complete(HostOperationOutcome.Cancelled);
                auditLog.Write(audit.WithOutcome(SecurityAuditOutcome.Cancelled, "user-cancelled"));
                throw;
            }
            catch (InvalidOperationException)
            {
                activity.Complete(HostOperationOutcome.Failed);
                auditLog.Write(audit.WithOutcome(SecurityAuditOutcome.Denied, "appearance-admission-changed"));
                throw;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                activity.Complete(HostOperationOutcome.Failed);
                auditLog.Write(audit.WithOutcome(SecurityAuditOutcome.Failed,
                    exception is InvalidDataException ? "invalid-data" : exception is IOException ? "io-error" : "access-denied"));
                SaveFailed(logger, AppearanceOptionRegistry.Get(proposal.Option).Id, exception);
                return new(AppearanceApplyOutcome.Failed, Get(proposal.Option), exception.Message);
            }
            values[proposal.Option] = proposal.Value;
            if (proposal.RestoreDefault) { savedOptions.Remove(proposal.Option); }
            else { savedOptions.Add(proposal.Option); }
            revision++;
            if (timeRenderer) { PresenceSwitchTiming.Mark(PresenceSwitchPhase.TerminalAuditStarted); }
            auditLog.Write(audit.WithOutcome(SecurityAuditOutcome.Succeeded));
            if (timeRenderer) { PresenceSwitchTiming.Mark(PresenceSwitchPhase.TerminalAuditReturned); }
            activity.Complete(HostOperationOutcome.Completed);
            if (timeRenderer) { PresenceSwitchTiming.Mark(PresenceSwitchPhase.PublicationStarted); }
            Publish();
            if (registered) { RecordUndo(before); }
            if (timeRenderer) { PresenceSwitchTiming.Mark(PresenceSwitchPhase.PublicationReturned); }
            if (timeRenderer) { PresenceSwitchTiming.Mark(PresenceSwitchPhase.ApplyReturning); }
            return new(AppearanceApplyOutcome.Applied, Get(proposal.Option));
        }
    }

    private AppearanceApplyResult ApplyReaderScale(AppearanceProposal proposal, CancellationToken token)
    {
        var before = Get(proposal.Option);
        using var activity = HostActivity.BeginOperation(HostActivityLayer.Application, HostOperation.Storage,
            proposal.OriginalOrigin!.Value);
        var audit = new SecurityAuditEvent(activity.Request.RequestId.Value,
            SecurityAuditCategory.ConfigurationWrite, AppearanceOptionRegistry.Get(proposal.Option).AuditAction,
            SecurityAuditOutcome.Requested, proposal.Initiator, "preferences.device-local");
        try
        {
            UndoChanged?.Invoke(null);
            auditLog.Write(audit);
            token.ThrowIfCancellationRequested();
            ValidateReaderAdmission(proposal.Initiator, proposal.OriginalOrigin, proposal.RemainsEligible);
            EnsureReaderState();
            preferences.BeginReaderTextScaleWrite();
            if (proposal.RestoreDefault) { preferences.ResetRegisteredOption(proposal.Option); }
            else { preferences.SaveReaderTextScalePercent(proposal.Value.GetNumber()); }
            if (preferences.ReadBackReaderTextScalePercent() != (proposal.RestoreDefault ? null : proposal.Value.GetNumber()))
            {
                throw new InvalidDataException("Native reader text scale readback did not match the requested percentage.");
            }
            auditLog.Write(audit.WithOutcome(SecurityAuditOutcome.Succeeded));
            preferences.ConfirmReaderTextScaleWrite();
        }
        catch (OperationCanceledException)
        {
            activity.Complete(HostOperationOutcome.Cancelled);
            auditLog.Write(audit.WithOutcome(SecurityAuditOutcome.Cancelled, "user-cancelled"));
            throw;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException
            or UnauthorizedAccessException or InvalidOperationException)
        {
            activity.Complete(HostOperationOutcome.Failed);
            auditLog.Write(audit.WithOutcome(SecurityAuditOutcome.Failed, "reader-scale-unconfirmed"));
            SaveFailed(logger, AppearanceOptionRegistry.Get(proposal.Option).Id, exception);
            throw;
        }
        values[proposal.Option] = proposal.Value;
        if (proposal.RestoreDefault) { savedOptions.Remove(proposal.Option); }
        else { savedOptions.Add(proposal.Option); }
        revision++;
        activity.Complete(HostOperationOutcome.Completed);
        Publish();
        RecordUndo(before);
        return new(AppearanceApplyOutcome.Applied, Get(proposal.Option));
    }

    private void EnsureReaderState()
    {
        var saved = preferences.LoadReaderTextScalePercent();
        if ((saved ?? ReaderTextScaleSettings.DefaultPercent) != values[AppearanceOption.ReaderTextScale].GetNumber()
            || saved.HasValue != savedOptions.Contains(AppearanceOption.ReaderTextScale))
        {
            throw new InvalidDataException("Native reader preference evidence changed; reload before proposing a new value.");
        }
    }

    private static void ValidateReaderAdmission(SecurityAuditInitiator initiator, RequestOrigin? originalOrigin,
        Func<bool>? remainsEligible)
    {
        if (originalOrigin is not (RequestOrigin.LocalUi or RequestOrigin.ActivatedVoice)
            || (initiator == SecurityAuditInitiator.VoiceCommand) != (originalOrigin == RequestOrigin.ActivatedVoice)
            || remainsEligible is null || !remainsEligible()
            || HostActivity.Current is { } current && current.Request.Origin != originalOrigin)
        {
            throw new InvalidOperationException("Native reader scale requires the original exact local input and the owning unlocked host.");
        }
    }
    private void EnsureNotPublishing()
    {
        if (publishing)
        {
            throw new InvalidOperationException("Appearance cannot be mutated during a change notification.");
        }
    }

    private static void ValidateInitiator(SecurityAuditInitiator initiator)
    {
        if (initiator is not (SecurityAuditInitiator.LocalUser or SecurityAuditInitiator.TypedCommand
            or SecurityAuditInitiator.VoiceCommand))
        {
            throw new ArgumentOutOfRangeException(nameof(initiator), "Only local UI and deterministic user commands are admitted.");
        }
    }

    private static void RequireEligibility(AppearanceProposal proposal)
    {
        if (proposal.RemainsEligible is { } eligible && !eligible())
        {
            throw new InvalidOperationException("The original appearance input is no longer eligible. Initiate a fresh change.");
        }
    }

    private void Publish()
    {
        publishing = true;
        try
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }

        finally
        {
            publishing = false;
        }
    }
}