using Kora.Application.Configuration.SettingInspection;
using Kora.Core.Configuration;

namespace Kora.Application.Configuration;

public sealed partial class QuietHoursConfigurationService
{
    private readonly SettingReadTracker inspectionRead = new();

    public Task<ConfirmedSettingRead> ReadConfirmedSettingsAsync(CancellationToken token) => Task.Run(() =>
    {
        lock (gate)
        {
            token.ThrowIfCancellationRequested();
            return inspectionRead.Read(() =>
            {
                if (busy || !state.SavedStateKnown || state.Effective is null)
                { return (SettingReadAvailability.NotObserved, []); }
                var persisted = preferences.Load();
                if (persisted != state.Saved || (persisted ?? QuietHoursSchedule.Off) != state.Effective)
                { return (SettingReadAvailability.Changed, []); }
                return (SettingReadAvailability.Confirmed,
                    [new(state.Effective, persisted is not null, state.Revision)]);
            });
        }
    }, token);

    internal bool IsInspectionCurrent(ConfirmedSettingRead read)
    {
        lock (gate)
        {
            return !busy && state.SavedStateKnown && state.Effective is not null && inspectionRead.IsCurrent(read)
                && read.Observations.All(item => item.Revision == state.Revision);
        }
    }
}
