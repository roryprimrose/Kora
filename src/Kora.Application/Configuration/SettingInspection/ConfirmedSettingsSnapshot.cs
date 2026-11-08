using Kora.Core.Configuration.SettingInspection;
using System.Collections.Immutable;

namespace Kora.Application.Configuration.SettingInspection;

public sealed class ConfirmedSettingsSnapshot
{
    private readonly ImmutableArray<ConfirmedSettingRead> reads;
    internal ConfirmedSettingsSnapshot(ConfirmedSettingRead appearance, ConfirmedSettingRead speech,
        ConfirmedSettingRead volume, ConfirmedSettingRead response, ConfirmedSettingRead quietHours,
        ConfirmedSettingRead timing, ConfirmedSettingRead details)
    {
        Profile = SettingInspectionProfile.ConfirmedSettings;
        reads = [appearance, speech, volume, response, quietHours, timing, details];
    }

    internal ConfirmedSettingsSnapshot(ConfirmedSettingRead speech, ConfirmedSettingRead rate)
    {
        Profile = SettingInspectionProfile.WindowsRate;
        reads = [speech, rate];
    }

    public SettingInspectionProfile Profile { get; }
    private ConfirmedSettingRead DefaultRead(int index) => Profile == SettingInspectionProfile.ConfirmedSettings
        ? reads[index] : throw new InvalidOperationException("This read is outside the dedicated source profile.");
    internal ConfirmedSettingRead Appearance => DefaultRead(0);
    internal ConfirmedSettingRead Speech => reads[Profile == SettingInspectionProfile.ConfirmedSettings ? 1 : 0];
    internal ConfirmedSettingRead Volume => DefaultRead(2);
    internal ConfirmedSettingRead Response => DefaultRead(3);
    internal ConfirmedSettingRead QuietHours => DefaultRead(4);
    internal ConfirmedSettingRead Timing => DefaultRead(5);
    internal ConfirmedSettingRead Details => DefaultRead(6);
    internal ConfirmedSettingRead Rate => Profile == SettingInspectionProfile.WindowsRate
        ? reads[1] : throw new InvalidOperationException("The default profile does not inspect Windows rate.");
    public IEnumerable<SettingObservation> Observations => reads.SelectMany(read => read.Observations);
}
