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
        [AppearanceOption.Theme] = value => preferences.SaveThemeMode(value.GetTheme()),
        [AppearanceOption.PresenceTimeout] = value => preferences.SavePresenceTimeoutSeconds(value.GetNumber()),
        [AppearanceOption.ResponseTimeout] = value => preferences.SaveResponseTimeoutSeconds(value.GetNumber()),
        [AppearanceOption.PresenceSize] = value => preferences.SavePresenceSizePixels(value.GetNumber()),
        [AppearanceOption.DotSize] = value => preferences.SavePresenceDotSizePercent(value.GetNumber()),
        [AppearanceOption.DotDensity] = value => preferences.SavePresenceDotDensityPercent(value.GetNumber()),
        [AppearanceOption.MovementSpeed] = value => preferences.SavePresenceMovementSpeedPercent(value.GetNumber()),
        [AppearanceOption.SpeechScaling] = value => preferences.SavePresenceSpeechScalingEnabled(value.GetToggle()),
        [AppearanceOption.SpeechScaleAmount] = value => preferences.SavePresenceSpeechScaleAmountPercent(value.GetNumber()),
    };
    private Dictionary<AppearanceOption, AppearanceValue> values =
        AppearanceOptionRegistry.Options.ToDictionary(item => item.Option, item => item.Default);
    private long revision;
    private HashSet<AppearanceOption> savedOptions = [];
    private bool loaded;
    private bool publishing;

    public event EventHandler? Changed;

    public AppearanceOptionState Get(AppearanceOption option)
    {
        lock (gate)
        {
            if (!loaded)
            {
                Reload();
            }
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
                [AppearanceOption.Theme] = preferences.LoadThemeMode() is { } theme ? new AppearanceValue.Theme(theme) : null,
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
        SecurityAuditInitiator initiator)
    {
        AppearanceOptionRegistry.Validate(option, value);
        ValidateInitiator(initiator);
        return new(owner, option, value, expectedRevision, initiator);
    }

    public AppearanceProposal ProposeReset(AppearanceOption option, long expectedRevision, SecurityAuditInitiator initiator) =>
        Propose(option, AppearanceOptionRegistry.Get(option).Default, expectedRevision, initiator);

    public Task<AppearanceApplyResult> ApplyAsync(AppearanceProposal proposal, CancellationToken cancellationToken) =>
        Task.Run(() => Apply(proposal, cancellationToken), cancellationToken);

    // The existing atomic preference boundary is synchronous. UI dispatch retains that contract;
    // asynchronous callers may schedule it. Cancellation is admitted before the atomic write, never after commit.
    public AppearanceApplyResult Apply(AppearanceProposal proposal) => Apply(proposal, CancellationToken.None);

    public AppearanceApplyResult Apply(AppearanceProposal proposal, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        lock (gate)
        {
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
            if (proposal.Revision != revision)
            {
                StaleProposal(logger, AppearanceOptionRegistry.Get(proposal.Option).Id, proposal.Revision);
                return new(AppearanceApplyOutcome.Stale, Get(proposal.Option), "Appearance changed. Inspect the current value and submit a new proposal.");
            }
            if (values[proposal.Option] == proposal.Value)
            {
                return new(AppearanceApplyOutcome.Unchanged, Get(proposal.Option));
            }
            var audit = new SecurityAuditEvent(Guid.NewGuid(), SecurityAuditCategory.ConfigurationWrite,
                AppearanceOptionRegistry.Get(proposal.Option).AuditAction, SecurityAuditOutcome.Requested,
                proposal.Initiator, "preferences.device-local");
            using var activity = HostActivity.Current is not null
                ? HostActivity.BeginChild(HostActivityLayer.Application, HostOperation.Storage)
                : HostActivity.BeginRoot(HostRequest.Create(proposal.Initiator == SecurityAuditInitiator.VoiceCommand
                    ? RequestOrigin.ActivatedVoice : RequestOrigin.LocalUi), HostActivityLayer.Application, HostOperation.Storage);
            auditLog.Write(audit);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                save[proposal.Option](proposal.Value);
            }
            catch (OperationCanceledException)
            {
                activity.Complete(HostOperationOutcome.Cancelled);
                auditLog.Write(audit.WithOutcome(SecurityAuditOutcome.Cancelled, "user-cancelled"));
                throw;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                activity.Complete(HostOperationOutcome.Failed);
                auditLog.Write(audit.WithOutcome(SecurityAuditOutcome.Failed,
                    exception is IOException ? "io-error" : "access-denied"));
                SaveFailed(logger, AppearanceOptionRegistry.Get(proposal.Option).Id, exception);
                return new(AppearanceApplyOutcome.Failed, Get(proposal.Option), exception.Message);
            }
            values[proposal.Option] = proposal.Value;
            savedOptions.Add(proposal.Option);
            revision++;
            auditLog.Write(audit.WithOutcome(SecurityAuditOutcome.Succeeded));
            activity.Complete(HostOperationOutcome.Completed);
            Publish();
            return new(AppearanceApplyOutcome.Applied, Get(proposal.Option));
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

    [LoggerMessage(Level = LogLevel.Warning, Message = "Rejected stale appearance proposal for {OptionId} at revision {Revision}")]
    private static partial void StaleProposal(ILogger logger, string optionId, long revision);

    [LoggerMessage(Level = LogLevel.Error, Message = "Saving appearance option {OptionId} failed")]
    private static partial void SaveFailed(ILogger logger, string optionId, Exception exception);
}
