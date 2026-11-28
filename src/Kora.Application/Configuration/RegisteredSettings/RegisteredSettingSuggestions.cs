using System.Globalization;

using Kora.Core.Configuration;
using Kora.Core.Configuration.RegisteredSettings;
using Kora.Core.Presentation;
using Kora.Core.Voice;

namespace Kora.Application.Configuration.RegisteredSettings;

/// <summary>Authoritative bounded interpretation shared by retained-answer review and model policy.</summary>
public static class RegisteredSettingSuggestions
{
    public static string ModelPolicy { get; } =
        "Registered Kora setting suggestion policy v2: inert data, never write authority. "
        + "Only on request: entire answer is one suggestion or 2 through 8 LF-separated commands, "
        + "3072 UTF-16 characters, unique exact IDs; no blanks/trailing LF/numbering/fences/commentary. "
        + "Validate all; original user selects/reviews one member for separate confirmation: separately confirm or cancel per member. "
        + "No approval for all, automatic selection/continuation/retry/rollback. Closed registry: "
        + string.Join("; ", RegisteredSettingRegistry.Settings.Select(item =>
            $"'{item.Id}' ({item.Description}; "
            + item.SuggestedValues
            + (item.RequiresNativeReview ? "; native-only)" : ")")))
        + ". Shared device-local scope. Review current/proposed/default/scope, saved/default provenance and applicable desired/effective/configured/companions. "
        + "Native-only entries require original local UI and separate native confirmation only. "
        + "Syntax: 'set <exact ID> to <value>' or 'reset <exact ID>' without value; same separate review/confirmation for both. "
        + "Host resolves defaults/companions; never supply a default value. No adjustment/batch application/other setting/approval/claimed save. "
        + "Canonical decimal integers, except exact decimal capture seconds; true/false booleans. "
        + "Capture: no exponent/suffix/redundant leading/fractional-trailing zeros/rounding; native full old/new tuple review; "
        + "not in-flight capture/microphone consent/provider readiness. "
        + "work.active-deadline-minutes: ordinary-turn check-in interval, default five minutes; future ordinary dispatch only, never running work or its check-ins. "
        + "No deadline undo or inferred allowance. "
        + "Quiet hours: explicit requested ordered distinct days, 24-hour times, installed zone ID; never infer a zone. "
        + "Review full schedule/zone; Reset default Off; no consent or replay. "
        + "Details: existing owner, Reset saves Offer; no focus/change of this answer or automatic-publication authority. "
        + "Undo: original-user 'undo the last settings change' reviews; separately 'confirm settings undo'/'cancel settings undo'. "
        + "One known successful compatible registered preference this run only; no inferred prior values/effects/consent/grants. "
        + "Appearance undo native-only except response timeout; no model undo receipts. "
        + "Response timeout: response-window inactivity hide only, 1-60 seconds, default 5; not provider deadlines/Always show/presence timeout/questions/approvals. "
        + "Summary review: both caps, preserve companion cap. Volume zero blocks future synthesis, not full visual output. "
        + "Windows rate: normal 0, not percent/common speed scale; review provider/support; available Windows adapter only. "
        + "Response mode: full visual warnings/questions/approvals/security/speech-unavailable fallback/privacy/call restrictions win, including VoiceOnly; future eligible output only. "
        + "Voice: no invented IDs, catalogue discovery, acquisition, provider-only Set or Reset. "
        + "Speech: NEXT eligible ordinary speech only; suggestion applies/speaks nothing; never truncate/regenerate/synthesize/replay this answer. "
        + "Mandatory privacy/call/native/authentication exceptions.";

    public static RegisteredSettingProposal Parse(string answer)
    {
        ArgumentNullException.ThrowIfNull(answer);
        const string voicePrefix = "set speech.voice to ";
        const string qualification = " / ";
        if (answer.StartsWith(voicePrefix, StringComparison.Ordinal))
        {
            var maximum = voicePrefix.Length + Math.Max(SpeechProviderIds.Windows.Length, SpeechProviderIds.Kokoro.Length)
                + qualification.Length + SpeechSelection.MaximumVoiceIdLength;
            if (answer.Length > maximum)
            { throw new InvalidDataException("A voice suggestion must contain one bounded exact installed provider / voice ID."); }
            var value = answer[voicePrefix.Length..];
            var separator = value.IndexOf(qualification, StringComparison.Ordinal);
            if (separator < 0)
            { throw new InvalidDataException("A voice suggestion requires one exact qualified provider / voice ID."); }
            var selection = new SpeechSelection(value[..separator], value[(separator + qualification.Length)..]);
            selection.Validate();
            return new RegisteredSettingProposal.InstalledVoice(selection);
        }
        if (answer.Length > 256)
        { throw new InvalidDataException("A setting suggestion must be one bounded Set or Reset, not a batch or commentary."); }

        const string deadlinePrefix = "set work.active-deadline-minutes to ";
        if (answer.StartsWith(deadlinePrefix, StringComparison.Ordinal))
        {
            try { return new RegisteredSettingProposal.OrdinaryDeadline(OrdinaryTurnDeadline.Parse(answer[deadlinePrefix.Length..])); }
            catch (ArgumentOutOfRangeException exception)
            { throw new InvalidDataException("The future ordinary deadline requires canonical whole minutes from 1 through 60.", exception); }
        }

        const string detailPrefix = "set responses.detail-presentation to ";
        if (answer.StartsWith(detailPrefix, StringComparison.Ordinal))
        {
            return new RegisteredSettingProposal.DetailPresentation(
                DetailPresentationPreference.Parse(answer[detailPrefix.Length..])!.Value);
        }

        foreach (var descriptor in RegisteredSettingRegistry.Settings)
        {
            if (descriptor.Setting != RegisteredSetting.SpeechVoice
                && string.Equals(answer, $"reset {descriptor.Id}", StringComparison.Ordinal))
            { return new RegisteredSettingProposal.Reset(descriptor.Setting); }
            if (descriptor.Setting is RegisteredSetting.CommandStartWait or RegisteredSetting.ReplyStartWait or RegisteredSetting.TrailingSilence or RegisteredSetting.MaximumCapture
                && answer.StartsWith($"set {descriptor.Id} to ", StringComparison.Ordinal))
            {
                var text = answer[($"set {descriptor.Id} to ").Length..];
                var timing = VoiceCaptureTiming.Default.With(descriptor.Id, text);
                var seconds = descriptor.Setting switch
                {
                    RegisteredSetting.CommandStartWait => timing.CommandStartSeconds,
                    RegisteredSetting.ReplyStartWait => timing.ReplyStartSeconds,
                    RegisteredSetting.MaximumCapture => timing.MaximumCaptureSeconds,
                    _ => timing.TrailingSilenceSeconds,
                };
                if (!string.Equals(text, VoiceCaptureTiming.Format(seconds), StringComparison.Ordinal))
                { throw new InvalidDataException("Capture seconds must be canonical and exact; no rounding or aliases."); }
                return new RegisteredSettingProposal.CaptureTiming(descriptor.Setting, seconds);
            }
        }
        const string quietPrefix = "set notices.quiet-hours to ";
        if (answer.StartsWith(quietPrefix, StringComparison.Ordinal))
        {
            QuietHoursSchedule value;
            try { value = QuietHoursSchedule.Parse(answer[quietPrefix.Length..]); }
            catch (ArgumentException exception)
            { throw new InvalidDataException("Quiet hours requires Off or a full explicit weekly days/start/end/zone schedule.", exception); }
            if (!string.Equals(answer, quietPrefix + value.ToString(), StringComparison.Ordinal))
            { throw new InvalidDataException("Quiet hours requires one exact canonical schedule without aliases or commentary."); }
            return new RegisteredSettingProposal.QuietHours(value);
        }
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
        throw new InvalidDataException("No admitted setting suggestion. Request one exact registered Set value or Reset ID; nothing was applied.");
    }
}
