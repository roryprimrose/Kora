using Kora.Core.Configuration;
using Kora.Core.Voice;

namespace Kora.Application.Configuration;

public sealed record SpeechConfigurationState(SpeechSelection? Selection, SpeechVoice? EffectiveVoice,
    IReadOnlyList<SpeechProvider> Providers, IReadOnlyList<SpeechVoice> Voices,
    long Revision, bool IsSaved, string? Recovery)
{
    public bool IsAvailable => EffectiveVoice is not null && Recovery is null;
    public SpokenSummaryLimits? SummaryLimits { get; init; } = SpokenSummaryLimits.Default;
    public bool AreSummaryLimitsSaved { get; init; }
    public string? SummaryLimitsRecovery { get; init; }
}
