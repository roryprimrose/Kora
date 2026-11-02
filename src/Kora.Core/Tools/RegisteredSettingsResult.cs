using System.Collections.Immutable;
using Kora.Core.Configuration.SettingInspection;

namespace Kora.Core.Tools;

public sealed record RegisteredSettingsResult(ImmutableArray<RegisteredSettingMetadata> Records,
    int TotalRecords, int? NextOffset, SettingObservation? Observation = null);
