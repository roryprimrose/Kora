using Kora.Core.Configuration.SettingInspection;

namespace Kora.Application.Configuration.SettingInspection;

public sealed class ConfirmedSettingsSnapshot
{
    internal ConfirmedSettingsSnapshot(ConfirmedSettingRead appearance, ConfirmedSettingRead speech,
        ConfirmedSettingRead volume, ConfirmedSettingRead response, ConfirmedSettingRead quietHours,
        ConfirmedSettingRead timing, ConfirmedSettingRead details)
    {
        Appearance = appearance;
        Speech = speech;
        Volume = volume;
        Response = response;
        QuietHours = quietHours;
        Timing = timing;
        Details = details;
    }

    internal ConfirmedSettingRead Appearance { get; }
    internal ConfirmedSettingRead Speech { get; }
    internal ConfirmedSettingRead Volume { get; }
    internal ConfirmedSettingRead Response { get; }
    internal ConfirmedSettingRead QuietHours { get; }
    internal ConfirmedSettingRead Timing { get; }
    internal ConfirmedSettingRead Details { get; }
    public IEnumerable<SettingObservation> Observations =>
        Appearance.Observations.Concat(Speech.Observations).Concat(Volume.Observations).Concat(Response.Observations)
            .Concat(QuietHours.Observations).Concat(Timing.Observations).Concat(Details.Observations);
}
