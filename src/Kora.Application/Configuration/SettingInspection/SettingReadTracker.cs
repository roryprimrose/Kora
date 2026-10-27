using System.Collections.Immutable;
using Kora.Core.Configuration.SettingInspection;

namespace Kora.Application.Configuration.SettingInspection;

/// <summary>Used only under the contributing service's gate. An observed failure retires earlier reads, including ABA.</summary>
internal sealed class SettingReadTracker
{
    private ConfirmedSettingRead current = new(SettingReadAvailability.NotObserved, [], 0);

    internal ConfirmedSettingRead Read(Func<(SettingReadAvailability Availability, ImmutableArray<SettingObservation> Values)> read)
    {
        SettingReadAvailability availability;
        ImmutableArray<SettingObservation> values;
        try { (availability, values) = read(); }
        catch (Exception exception) when (exception is IOException or InvalidDataException
            or UnauthorizedAccessException or ArgumentOutOfRangeException)
        { availability = SettingReadAvailability.Unconfirmed; values = []; }
        if (availability != current.Availability || !values.SequenceEqual(current.Observations))
        { current = new(availability, values, checked(current.ObservationRevision + 1)); }
        return current;
    }

    internal bool IsCurrent(ConfirmedSettingRead read) =>
        read.Availability == SettingReadAvailability.Confirmed && ReferenceEquals(read, current);
}
