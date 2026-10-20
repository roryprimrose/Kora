using System.Text;
using Kora.Core.Commands;
using Kora.Core.Configuration;
using Kora.Core.Voice;

namespace Kora.Application.Configuration;

public sealed record SpeechTextCommand(AppearanceCommandOperation Operation, SpeechTextMode? Value = null, string? Error = null,
    SpeechCaptionOption? CaptionOption = null, SpeechCaptionValue? CaptionValue = null, bool IsPinControl = false, bool? PinValue = null)
{
    public const string OptionId = "display.speech-text";
    public const string PlacementId = "display.speech-text-placement";
    public const string DelayId = "display.speech-text-dismissal-delay";
    public const string PinId = "display.speech-text-pin";
    public const string Syntax = "list speech text settings | get display.speech-text | status display.speech-text | "
        + "set display.speech-text to Off|CurrentUtterance | reset display.speech-text | "
        + "get/status/set/reset display.speech-text-placement (BottomRight|BottomLeft|TopRight|TopLeft) | "
        + "get/status/set/reset display.speech-text-dismissal-delay (canonical integer seconds 0-30; default 5) | "
        + "get/status/set/reset display.speech-text-pin (true|false; current caption only) | "
        + "turn on/off speech captions | show/hide speech captions | show/hide speech text | show the words you're saying "
        + "(show/on saves CurrentUtterance; hide/off saves Off, not a one-shot hide) | "
        + "list/inspect speech caption settings | status/reset speech captions (reset saves Off only) | "
        + "get/status/reset speech caption placement | get/status/reset speech caption dismissal delay | "
        + "put speech captions in the bottom-right|bottom-left|top-right|top-left | "
        + "keep speech text visible for <canonical integer 0-30> seconds (1 second) | "
        + "pin current speech caption | unpin current speech caption";
    private static Dictionary<string, string> NaturalAliases { get; } = CreateNaturalAliases();
    private static readonly string[] ReservedPrefixes =
    [
        "list speech text", "get display.speech-text", "status display.speech-text",
        "set display.speech-text", "reset display.speech-text",
        "turn on speech caption", "turn off speech caption", "show speech caption", "hide speech caption",
        "show speech text", "hide speech text", "show the words",
        "list speech caption", "inspect speech caption", "status speech caption", "reset speech caption",
        "get speech caption", "set speech caption", "put speech caption", "keep speech text",
        "pin current speech caption", "unpin current speech caption",
        "pin speech caption", "unpin speech caption", "pin that text", "unpin that text",
    ];
    public static IReadOnlyList<string> FixedPhrases { get; } =
        Array.AsReadOnly(new[] { "list speech text settings" }
            .Concat(new[] { OptionId, PlacementId, DelayId, PinId }
                .SelectMany(id => new[] { "get " + id, "status " + id, "reset " + id }))
            .Concat(Enum.GetValues<SpeechTextMode>().Select(mode => "set " + OptionId + " to " + mode))
            .Concat(Enum.GetValues<SpeechCaptionPlacement>().Select(placement => "set " + PlacementId + " to " + placement))
            .Concat(Enumerable.Range(SpeechCaptionOptions.MinimumDelaySeconds,
                SpeechCaptionOptions.MaximumDelaySeconds - SpeechCaptionOptions.MinimumDelaySeconds + 1)
                .Select(seconds => "set " + DelayId + " to " + new SpeechCaptionValue.Delay(seconds).Label))
            .Concat(["set " + PinId + " to true", "set " + PinId + " to false"])
            .Concat(NaturalAliases.Keys).ToArray());

    private static Dictionary<string, string> CreateNaturalAliases()
    {
        var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["turn on speech captions"] = "set " + OptionId + " to " + SpeechTextMode.CurrentUtterance,
            ["show speech captions"] = "set " + OptionId + " to " + SpeechTextMode.CurrentUtterance,
            ["show speech text"] = "set " + OptionId + " to " + SpeechTextMode.CurrentUtterance,
            ["show the words you're saying"] = "set " + OptionId + " to " + SpeechTextMode.CurrentUtterance,
            ["turn off speech captions"] = "set " + OptionId + " to " + SpeechTextMode.Off,
            ["hide speech captions"] = "set " + OptionId + " to " + SpeechTextMode.Off,
            ["hide speech text"] = "set " + OptionId + " to " + SpeechTextMode.Off,
            ["list speech caption settings"] = "list speech text settings",
            ["inspect speech caption settings"] = "get " + OptionId,
            ["status speech captions"] = "status " + OptionId,
            ["reset speech captions"] = "reset " + OptionId,
            ["pin current speech caption"] = "set " + PinId + " to true",
            ["unpin current speech caption"] = "set " + PinId + " to false",
        };
        foreach (var verb in new[] { "get", "status", "reset" })
        {
            aliases.Add(verb + " speech caption placement", verb + " " + PlacementId);
            aliases.Add(verb + " speech caption dismissal delay", verb + " " + DelayId);
        }
        foreach (var (corner, placement) in new[]
        {
            ("bottom-right", SpeechCaptionPlacement.BottomRight), ("bottom-left", SpeechCaptionPlacement.BottomLeft),
            ("top-right", SpeechCaptionPlacement.TopRight), ("top-left", SpeechCaptionPlacement.TopLeft),
        })
        {
            aliases.Add("put speech captions in the " + corner, "set " + PlacementId + " to " + placement);
        }
        foreach (var seconds in Enumerable.Range(SpeechCaptionOptions.MinimumDelaySeconds,
            SpeechCaptionOptions.MaximumDelaySeconds - SpeechCaptionOptions.MinimumDelaySeconds + 1))
        {
            var label = new SpeechCaptionValue.Delay(seconds).Label;
            aliases.Add("keep speech text visible for " + label + (seconds == 1 ? " second" : " seconds"),
                "set " + DelayId + " to " + label);
        }
        return aliases;
    }

    public static SpeechTextCommand? Parse(string input, string assistantName)
    {
        var text = input.Trim();
        var name = AssistantNameRules.Normalize(assistantName);
        if (text.StartsWith(name, StringComparison.OrdinalIgnoreCase) && text.Length > name.Length
            && (char.IsWhiteSpace(text[name.Length]) || text[name.Length] == ','))
        {
            text = text[name.Length..].TrimStart(' ', ',', '\t');
        }
        if (!ReservedPrefixes.Any(prefix => text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))) { return null; }
        if (Encoding.UTF8.GetByteCount(input) > SessionCommand.MaximumInputBytes || input.Any(char.IsControl))
        {
            return new(AppearanceCommandOperation.Clarify, Error: "Speech-text input exceeds its byte limit or contains controls. " + Syntax);
        }
        if (NaturalAliases.TryGetValue(text, out var canonical)) { text = canonical; }
        if (text.Equals("list speech text settings", StringComparison.OrdinalIgnoreCase)) { return new(AppearanceCommandOperation.List); }
        foreach (var option in Enum.GetValues<SpeechCaptionOption>())
        {
            var id = option == SpeechCaptionOption.Placement ? PlacementId : DelayId;
            if (text.Equals("get " + id, StringComparison.OrdinalIgnoreCase)
                || text.Equals("status " + id, StringComparison.OrdinalIgnoreCase))
            {
                return new(AppearanceCommandOperation.Get, CaptionOption: option);
            }
            if (text.Equals("reset " + id, StringComparison.OrdinalIgnoreCase))
            {
                return new(AppearanceCommandOperation.Reset, CaptionOption: option);
            }
            for (var index = 0; index < FixedPhrases.Count; index++)
            {
                var phrase = FixedPhrases[index];
                if (!phrase.StartsWith("set " + id + " to ", StringComparison.Ordinal)) { continue; }
                if (text.Equals(phrase, StringComparison.OrdinalIgnoreCase))
                {
                    var value = phrase[("set " + id + " to ").Length..];
                    SpeechCaptionValue selected = option == SpeechCaptionOption.Placement
                        ? new SpeechCaptionValue.Placement(Enum.Parse<SpeechCaptionPlacement>(value))
                        : new SpeechCaptionValue.Delay(int.Parse(value, System.Globalization.CultureInfo.InvariantCulture));
                    return new(AppearanceCommandOperation.Set, CaptionOption: option, CaptionValue: selected);
                }
            }
        }
        if (text.Equals("get " + PinId, StringComparison.OrdinalIgnoreCase)
            || text.Equals("status " + PinId, StringComparison.OrdinalIgnoreCase))
        {
            return new(AppearanceCommandOperation.Get, IsPinControl: true);
        }
        if (text.Equals("reset " + PinId, StringComparison.OrdinalIgnoreCase)
            || text.Equals("set " + PinId + " to false", StringComparison.OrdinalIgnoreCase))
        {
            return new(AppearanceCommandOperation.Set, IsPinControl: true, PinValue: false);
        }
        if (text.Equals("set " + PinId + " to true", StringComparison.OrdinalIgnoreCase))
        {
            return new(AppearanceCommandOperation.Set, IsPinControl: true, PinValue: true);
        }
        if (text.Equals("get " + OptionId, StringComparison.OrdinalIgnoreCase)
            || text.Equals("status " + OptionId, StringComparison.OrdinalIgnoreCase)) { return new(AppearanceCommandOperation.Get); }
        if (text.Equals("reset " + OptionId, StringComparison.OrdinalIgnoreCase)) { return new(AppearanceCommandOperation.Reset); }
        foreach (var mode in Enum.GetValues<SpeechTextMode>())
        {
            if (text.Equals("set " + OptionId + " to " + mode, StringComparison.OrdinalIgnoreCase))
            {
                return new(AppearanceCommandOperation.Set, mode);
            }
        }
        return new(AppearanceCommandOperation.Clarify, Error: Syntax);
    }
}
