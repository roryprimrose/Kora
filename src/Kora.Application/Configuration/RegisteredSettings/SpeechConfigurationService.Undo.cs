using Kora.Application.Configuration.RegisteredSettings;
using Kora.Core.Configuration;
using Kora.Core.Configuration.RegisteredSettings;

namespace Kora.Application.Configuration;

public sealed partial class SpeechConfigurationService
{
    internal event Action<RegisteredSettingChange?>? UndoChanged;

    private bool MatchesSummary(SpeechConfigurationState expected)
    {
        lock (gate)
        {
            return Get() == expected
                && preferences.LoadSummaryLimits() == (expected.AreSummaryLimitsSaved ? expected.SummaryLimits : null);
        }
    }

    private void RecordSummaryUndo(SpeechOption option, SpeechConfigurationState before)
    {
        var after = Get();
        if (before.SummaryLimits == after.SummaryLimits
            && before.AreSummaryLimitsSaved == after.AreSummaryLimitsSaved) { return; }
        before.SummaryLimits!.Validate();
        if (!MatchesSummary(after)) { throw new InvalidDataException("Summary preference changed after its completed write."); }
        var descriptor = RegisteredSettingRegistry.Get(option == SpeechOption.SummaryWords
            ? RegisteredSetting.SummaryWords : RegisteredSetting.SummarySentences);
        UndoChanged?.Invoke(new(descriptor, FormatSummary(before), FormatSummary(after), () => MatchesSummary(after),
            (origin, initiator, policy, call, eligible, token) =>
            {
                bool Current() => eligible() && MatchesSummary(after);
                if (!Current()) { throw new InvalidOperationException("The committed summary pair changed; undo is unavailable."); }
                var proposal = new SpeechProposal(owner, option, after.Selection ?? SpeechSelection.Default,
                    after.Revision, call, origin, initiator, before.SummaryLimits)
                { RestoreSummaryDefault = !before.AreSummaryLimitsSaved };
                return Task.FromResult(Apply(proposal, policy, Current, token).Succeeded);
            }));
    }

    private static string FormatSummary(SpeechConfigurationState value) =>
        $"{value.SummaryLimits!.Sentences} sentences / {value.SummaryLimits.Words} words "
        + $"({(value.AreSummaryLimitsSaved ? "saved" : "default")})";
}
