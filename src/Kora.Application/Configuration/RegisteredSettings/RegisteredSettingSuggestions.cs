using System.Globalization;

using Kora.Core.Configuration;
using Kora.Core.Configuration.RegisteredSettings;
using Kora.Core.Voice;

namespace Kora.Application.Configuration.RegisteredSettings;

/// <summary>Authoritative bounded interpretation shared by retained-answer review and model policy.</summary>
public static class RegisteredSettingSuggestions
{
    public static string ModelPolicy { get; } =
        "Registered Kora setting suggestions are inert data, never write authority. "
        + "Only when requested, the entire final answer may be one inert suggestion from this closed registry: "
        + string.Join("; ", RegisteredSettingRegistry.Settings.Select(item =>
            $"'{item.Id}' ({item.Description}; {item.Scope}; "
            + item.SuggestedValues
            + (item.RequiresNativeReview ? "; original local UI and separate native confirmation only)" : ")")))
        + ". Use 'set <exact ID> to <value>', canonical decimal integers for numeric values and true/false for booleans. "
        + "Presence and response-timeout suggestions require that exact syntax, ID and canonical value, without aliases or surrounding text. "
        + "No reset, adjustment, batch, commentary, other setting, approval or claimed save. "
        + "Undo is a separate host-owned original-user operation: 'undo the last settings change' opens review, "
        + "'confirm settings undo' or 'cancel settings undo' is separate. Only one known successful compatible registered "
        + "preference in this run can qualify; never infer previous values or undo effects, consent or grants. "
        + "Appearance undo remains native-only except response timeout. A model suggestion cannot create or confirm an undo receipt. "
        + "Response timeout is only the existing response-window inactivity hide delay, 1-60 seconds, default 5. "
        + "It never changes provider deadlines, Always show, presence timeout, questions or approvals. "
        + "The original user must review exact current/proposed values and scope, then separately confirm or cancel. "
        + "Spoken-summary review includes both caps and preserves the companion cap. Volume review includes desired/effective "
        + "values and saved/default provenance; zero blocks future synthesis, preserving full visual output. "
        + "Windows rate uses exact 'set speech.windows-rate to <canonical integer>' from -10 through 10, normal 0, "
        + "not percent or a common speed scale. Review shows provider/support, desired/effective native units and saved/default "
        + "provenance; only the already available Windows adapter can apply. Kokoro remains unchanged. "
        + "Response-mode review shows saved/default, desired and configured effective modes. Use exact canonical "
        + "Hybrid, VoiceOnly or VisualOnly; mandatory full visual warnings, questions, approvals, security, speech-unavailable "
        + "fallback, privacy and call restrictions always win, including VoiceOnly. Response mode affects future eligible output only. "
        + "Speech changes apply to NEXT eligible ordinary speech only. Nothing is applied or spoken by the suggestion; "
        + "never truncate, regenerate, synthesize or replay this answer. Existing privacy, call and native/authentication exceptions remain mandatory.";

    public static RegisteredSettingProposal Parse(string answer)
    {
        ArgumentNullException.ThrowIfNull(answer);
        if (answer.Length > 256)
        { throw new InvalidDataException("A setting suggestion must be one bounded Set, not a batch or commentary."); }

        if (AppearanceCommand.Parse(answer, AssistantNameRules.DefaultName) is
            { Operation: AppearanceCommandOperation.Set, Descriptor: { } appearance, Value: { } appearanceValue }
            && RegisteredSettingRegistry.Settings.FirstOrDefault(item =>
                string.Equals(item.Id, appearance.Id, StringComparison.Ordinal)) is { } setting
            && (appearance.Option is AppearanceOption.Theme or AppearanceOption.ReaderTextScale
                || string.Equals(answer, $"set {appearance.Id} to {AppearanceCommand.Format(appearanceValue)}", StringComparison.Ordinal)))
        {
            return new RegisteredSettingProposal.Appearance(setting.Setting, appearance.Option, appearanceValue);
        }
        if (SpeechCommand.Parse(answer, AssistantNameRules.DefaultName) is
            { Operation: AppearanceCommandOperation.Set, Descriptor.IsSummaryLimit: true, Value: { } summaryValue } speech
            && string.Equals(answer.Trim(), $"set {speech.Descriptor.Id} to {summaryValue}", StringComparison.OrdinalIgnoreCase))
        {
            var limits = SpokenSummaryLimits.Default.With(speech.Descriptor.Option, summaryValue);
            var number = speech.Descriptor.Option == SpeechOption.SummaryWords ? limits.Words : limits.Sentences;
            if (string.Equals(summaryValue, number.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal))
            {
                return new RegisteredSettingProposal.Summary(
                    speech.Descriptor.Option == SpeechOption.SummaryWords ? RegisteredSetting.SummaryWords : RegisteredSetting.SummarySentences,
                    speech.Descriptor.Option, summaryValue);
            }
        }
        if (PlaybackVolumeCommand.Parse(answer, AssistantNameRules.DefaultName) is
            { Operation: AppearanceCommandOperation.Set, Adjustment: null, Value: { } volumeValue })
        {
            var volume = PlaybackVolume.Parse(volumeValue);
            if (string.Equals(answer, $"set {PlaybackVolumeCommand.OptionId} to {volume.Percent.ToString(CultureInfo.InvariantCulture)}",
                StringComparison.Ordinal))
            { return new RegisteredSettingProposal.Volume(volume); }
        }
        foreach (var mode in Enum.GetValues<ResponseOutputMode>())
        {
            if (string.Equals(answer, $"set {ResponseModeCommand.OptionId} to {mode}", StringComparison.Ordinal))
            { return new RegisteredSettingProposal.ResponseMode(mode); }
        }
        const string ratePrefix = "set speech.windows-rate to ";
        if (answer.StartsWith(ratePrefix, StringComparison.Ordinal))
        { return new RegisteredSettingProposal.WindowsRate(WindowsSpeechRate.Parse(answer[ratePrefix.Length..])); }
        throw new InvalidDataException("No admitted setting suggestion. Request one exact registered Set value; nothing was applied.");
    }
}
