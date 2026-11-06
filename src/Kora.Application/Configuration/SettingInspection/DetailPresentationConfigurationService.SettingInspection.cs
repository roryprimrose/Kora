using Kora.Application.Configuration.SettingInspection;
using Kora.Core.Configuration.RegisteredSettings;
using Kora.Core.Presentation;

namespace Kora.Application.Configuration;

public sealed partial class DetailPresentationConfigurationService
{
    private readonly SettingReadTracker inspectionRead = new();

    public Task<ConfirmedSettingRead> ReadConfirmedSettingsAsync(CancellationToken token) => Task.Run(() =>
    {
        lock (gate)
        {
            token.ThrowIfCancellationRequested();
            return inspectionRead.Read(() =>
            {
                if (!available || busy) { return (SettingReadAvailability.NotObserved, []); }
                var persisted = Read();
                if (persisted != saved) { return (SettingReadAvailability.Changed, []); }
                return (SettingReadAvailability.Confirmed,
                    [new(RegisteredSetting.DetailPresentation, (int)(persisted ?? DetailPresentationPreference.Default),
                        persisted.HasValue, revision)]);
            });
        }
    }, token);

    internal bool IsInspectionCurrent(ConfirmedSettingRead read)
    {
        lock (gate)
        {
            return available && !busy && inspectionRead.IsCurrent(read)
                && read.Observations.All(item => item.Revision == revision);
        }
    }
}
