using Kora.Core.Configuration.SettingInspection;

namespace Kora.Application.Configuration.SettingInspection;

public sealed class SettingInspectionSession
{
    internal SettingInspectionSession(SettingInspectionReview review, ConfirmedSettingsSnapshot snapshot, Func<bool> current)
    { Review = review; Snapshot = snapshot; Current = current; }

    public SettingInspectionReview Review { get; }
    internal ConfirmedSettingsSnapshot Snapshot { get; }
    internal Func<bool> Current { get; }
}
