using Kora.Application.Configuration.RegisteredSettings;
using Kora.Core.Configuration.RegisteredSettings;
using Kora.Core.Voice;

namespace Kora.Application.Configuration;

public sealed partial class ResponseModeConfigurationService
{
    internal event Action<RegisteredSettingChange?>? UndoChanged;

    private bool MatchesMode(ResponseModeCommandResult expected, ResponseOutputMode? value)
    {
        lock (gate) { return Get() == expected && !held && Read() == value; }
    }

    private void RecordModeUndo(ResponseOutputMode? before, ResponseOutputMode? after)
    {
        if (before == after) { return; }
        var committed = Get();
        UndoChanged?.Invoke(new(RegisteredSettingRegistry.Get(RegisteredSetting.ResponseMode),
            FormatMode(before), $"{FormatMode(after)}; desired {committed.Desired}; configured effective {committed.Effective}",
            () => MatchesMode(committed, after),
            async (origin, initiator, policy, call, eligible, token) =>
            {
                if (!MatchesMode(committed, after)) { throw new InvalidOperationException("Response mode changed; undo is unavailable."); }
                await RefreshAsync(origin, eligible, token, committed.Revision).ConfigureAwait(false);
                var refreshed = Get();
                if (refreshed with { Revision = committed.Revision } != committed || Read() != after)
                { throw new InvalidOperationException("Response mode changed during fresh undo admission; nothing was applied."); }
                var choice = Choices.Single(item => item.Mode == (before ?? ResponseOutputMode.Hybrid));
                return await SelectCoreAsync(choice, call, origin, initiator, policy, eligible,
                    restoreDefault: before is null, token).ConfigureAwait(false);
            }));
    }

    private static string FormatMode(ResponseOutputMode? value) => value is { } mode
        ? $"{mode} (saved)" : "Hybrid (default; unsaved)";
}
