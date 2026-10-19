using Kora.Core.Configuration;

namespace Kora.Application.Configuration;

public sealed record SessionRetentionConfigurationObservation(SessionRetentionSettings? Saved,
    SessionRetentionSettings Effective, long PreferenceRevision, long PolicyRevision);
