using System.Collections.Immutable;
using Kora.Core.Configuration.SettingInspection;

namespace Kora.Application.Configuration.SettingInspection;

public sealed record ConfirmedSettingRead(SettingReadAvailability Availability,
    ImmutableArray<SettingObservation> Observations, long ObservationRevision);
