using Kora.Application.Configuration.RegisteredSettings;
using Kora.Core.Auditing;
using Kora.Core.Configuration;
using Kora.Core.Configuration.RegisteredSettings;
using Kora.Core.Hosting;

namespace Kora.Application.Configuration;

public sealed partial class AppearanceConfigurationService
{
    internal event Action<RegisteredSettingChange?>? UndoChanged;

    private readonly Dictionary<AppearanceOption, Func<AppearanceValue?>> readRegistered = new()
    {
        [AppearanceOption.Theme] = () => preferences.LoadThemeMode() is { } theme ? new AppearanceValue.Theme(theme) : null,
        [AppearanceOption.ReducedMotion] = () => preferences.LoadReducedMotionEnabled() is { } reducedMotion
            ? new AppearanceValue.Toggle(reducedMotion) : null,
        [AppearanceOption.ReaderTextScale] = () => Number(preferences.LoadReaderTextScalePercent()),
        [AppearanceOption.PresenceDisplay] = () => preferences.LoadPresenceDisplayEnabled() is { } display ? new AppearanceValue.Toggle(display) : null,
        [AppearanceOption.PresenceTimeout] = () => Number(preferences.LoadPresenceTimeoutSeconds()),
        [AppearanceOption.PresenceSize] = () => Number(preferences.LoadPresenceSizePixels()),
        [AppearanceOption.DotSize] = () => Number(preferences.LoadPresenceDotSizePercent()),
        [AppearanceOption.DotDensity] = () => Number(preferences.LoadPresenceDotDensityPercent()),
        [AppearanceOption.MovementSpeed] = () => Number(preferences.LoadPresenceMovementSpeedPercent()),
        [AppearanceOption.SpeechScaling] = () => preferences.LoadPresenceSpeechScalingEnabled() is { } scaling ? new AppearanceValue.Toggle(scaling) : null,
        [AppearanceOption.SpeechScaleAmount] = () => Number(preferences.LoadPresenceSpeechScaleAmountPercent()),
    };

    private AppearanceValue? ReadRegistered(AppearanceOption option) => readRegistered[option]();
    private static AppearanceValue.Number? Number(int? number) => number is { } value ? new AppearanceValue.Number(value) : null;

    private bool MatchesRegistered(AppearanceOptionState expected)
    {
        lock (gate)
        {
            return Get(expected.Descriptor.Option) == expected
                && ReadRegistered(expected.Descriptor.Option) == (expected.IsSaved ? expected.Value : null);
        }
    }

    private void RecordUndo(AppearanceOptionState before)
    {
        var descriptor = RegisteredSettingRegistry.Settings.Single(item =>
            string.Equals(item.Id, before.Descriptor.Id, StringComparison.Ordinal));
        var after = Get(before.Descriptor.Option);
        if (!MatchesRegistered(after)) { throw new InvalidDataException("Appearance preference readback changed after commit."); }
        UndoChanged?.Invoke(new(descriptor, Format(before), Format(after), () => MatchesRegistered(after),
            async (origin, initiator, policy, call, eligible, token) =>
            {
                if (origin != RequestOrigin.LocalUi || initiator != SecurityAuditInitiator.LocalUser)
                { throw new InvalidOperationException("Registered appearance undo requires a new native review and confirmation."); }
                bool Current() => eligible() && MatchesRegistered(after)
                    && policy.CheckMutation(origin, call, eligible) is null;
                var proposal = Propose(before.Descriptor.Option, before.Value, after.Revision, initiator, origin, Current);
                proposal = new(owner, proposal.Option, proposal.Value, proposal.Revision, initiator, origin, Current)
                { RestoreDefault = !before.IsSaved };
                return (await ApplyAsync(proposal, token).ConfigureAwait(false)).Succeeded;
            }));
    }

    private static string Format(AppearanceOptionState value) =>
        $"{AppearanceCommand.Format(value.Value)} ({(value.IsSaved ? "saved" : "default")})";
}
