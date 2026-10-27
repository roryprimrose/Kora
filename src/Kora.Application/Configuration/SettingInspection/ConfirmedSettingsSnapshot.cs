using Kora.Core.Configuration.SettingInspection;

namespace Kora.Application.Configuration.SettingInspection;

public sealed class ConfirmedSettingsSnapshot
{
    internal ConfirmedSettingsSnapshot(ConfirmedSettingRead appearance, ConfirmedSettingRead speech,
        ConfirmedSettingRead volume, ConfirmedSettingRead response)
    {
        Appearance = appearance;
        Speech = speech;
        Volume = volume;
        Response = response;
    }

    internal ConfirmedSettingRead Appearance { get; }
    internal ConfirmedSettingRead Speech { get; }
    internal ConfirmedSettingRead Volume { get; }
    internal ConfirmedSettingRead Response { get; }
    public IEnumerable<SettingObservation> Observations =>
        Appearance.Observations.Concat(Speech.Observations).Concat(Volume.Observations).Concat(Response.Observations);
}
