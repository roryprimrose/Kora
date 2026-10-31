using System.Globalization;

using Kora.Application.Configuration.RegisteredSettings;
using Kora.Core.Configuration;
using Kora.Core.Configuration.RegisteredSettings;

namespace Kora.Application.Configuration;

public sealed partial class PlaybackVolumeConfigurationService
{
    internal event Action<RegisteredSettingChange?>? UndoChanged;

    private bool MatchesVolume(PlaybackVolumeState expected, PlaybackVolume? saved)
    {
        lock (gate) { return state == expected && preferences.Load() == saved; }
    }

    private void RecordVolumeUndo(PlaybackVolume? before, PlaybackVolume? after)
    {
        if (before == after) { return; }
        var committed = state;
        UndoChanged?.Invoke(new(RegisteredSettingRegistry.Get(RegisteredSetting.PlaybackVolume),
            FormatVolume(before), $"{FormatVolume(after)}; desired {committed.Desired!.Value.Percent}%, "
                + $"effective {committed.Effective!.Value.Percent}%", () => MatchesVolume(committed, after),
            async (origin, initiator, policy, call, eligible, token) =>
            {
                if (!MatchesVolume(committed, after)) { throw new InvalidOperationException("Volume changed; undo is unavailable."); }
                await RefreshAsync(origin, eligible, token, committed.Revision).ConfigureAwait(false);
                var refreshed = Get();
                if (refreshed with { Revision = committed.Revision } != committed || preferences.Load() != after)
                { throw new InvalidOperationException("Volume changed during fresh undo admission; nothing was applied."); }
                var candidate = Propose(before?.Percent.ToString(CultureInfo.InvariantCulture), Get().Revision, call);
                return await ApplyAsync(candidate, origin, initiator, policy, eligible, token).ConfigureAwait(false);
            }));
    }

    private static string FormatVolume(PlaybackVolume? value) => value is { } volume
        ? $"{volume.Percent}% (saved)" : $"{PlaybackVolume.Default.Percent}% (default; unsaved)";
}
