using System.Globalization;

using Kora.Core.Configuration;
using Kora.Core.Configuration.RegisteredSettings;

namespace Kora.Application.Configuration.RegisteredSettings;

/// <summary>Authoritative bounded interpretation shared by retained-answer review and model policy.</summary>
public static class RegisteredSettingSuggestions
{
    public static string ModelPolicy { get; } =
        "Registered Kora setting suggestions are inert data, never write authority. "
        + "Only when requested, the entire final answer may be one inert suggestion from this closed registry: "
        + string.Join("; ", RegisteredSettingRegistry.Settings.Select(item =>
            $"'{item.Id}' ({item.Description}; {item.Scope}; "
            + (item.Minimum is { } minimum ? $"{minimum}-{item.Maximum} {item.Units}" : "system/light/dark")
            + (item.RequiresNativeReview ? "; original local UI and separate native confirmation only)" : ")")))
        + ". Use 'set <exact ID> to <value>', canonical decimal integers for numeric values. "
        + "No reset, adjustment, batch, commentary, other setting, approval or claimed save. "
        + "The original user must review exact current/proposed values and scope, then separately confirm or cancel. "
        + "Spoken-summary review includes both caps and preserves the companion cap. Volume review includes desired/effective "
        + "values and saved/default provenance; zero blocks future synthesis, preserving full visual output. "
        + "Speech changes apply to NEXT eligible ordinary speech only. Nothing is applied or spoken by the suggestion; "
        + "never truncate, regenerate, synthesize or replay this answer. Existing privacy, call and native/authentication exceptions remain mandatory.";

    public static RegisteredSettingProposal Parse(string answer)
    {
        ArgumentNullException.ThrowIfNull(answer);
        if (answer.Length > 256)
        { throw new InvalidDataException("A setting suggestion must be one bounded Set, not a batch or commentary."); }

        if (AppearanceCommand.Parse(answer, AssistantNameRules.DefaultName) is
            { Operation: AppearanceCommandOperation.Set, Descriptor: { } appearance, Value: { } appearanceValue }
            && appearance.Option is AppearanceOption.Theme or AppearanceOption.ReaderTextScale)
        {
            return new RegisteredSettingProposal.Appearance(
                appearance.Option == AppearanceOption.Theme ? RegisteredSetting.Theme : RegisteredSetting.ReaderTextScale,
                appearance.Option, appearanceValue);
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
        throw new InvalidDataException("No admitted setting suggestion. Request one exact registered Set value; nothing was applied.");
    }
}
