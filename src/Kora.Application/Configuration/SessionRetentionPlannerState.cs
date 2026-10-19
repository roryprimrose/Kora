using Kora.Core.Configuration;
using Kora.Core.Storage;

namespace Kora.Application.Configuration;

public sealed record SessionRetentionPlannerState(string Status, SessionRetentionSettings? Proposed = null,
    DateTimeOffset? CutoffUtc = null, SessionRetentionConfigurationObservation? Preference = null,
    SessionRetentionPlanResult? Result = null, DateTimeOffset? ExpiresUtc = null,
    SessionRetentionPlannerAdmission? Admission = null);
