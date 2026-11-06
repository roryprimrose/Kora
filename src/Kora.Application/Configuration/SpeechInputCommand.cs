using System.Text;

using Kora.Core.Commands;
using Kora.Core.Configuration;
using Kora.Core.Voice;

namespace Kora.Application.Configuration;

public sealed record SpeechInputCommand(AppearanceCommandOperation Operation, SpeechInputSelection? Selection = null, string? Error = null,
    bool? ReviewDecision = null)
{
    public const string Syntax = "get speech.input | set speech.input to windows-activated-v1 WindowsManaged CaptureLifetime. "
        + "For an independently host-qualified configured name: set speech.input to pocketsphinx-5-1-1-en-us-cpu-v1 Cpu CaptureLifetime. "
        + "Review, then confirm speech.input or cancel speech.input. "
        + "Only exact supported, installed and independently qualified tuples can be confirmed. Existing unavailable desired preferences are retained until an explicit compatible switch. No download, listening enablement, capture or fallback.";
    public static IReadOnlyList<string> FixedPhrases { get; } =
        ["get speech.input", "status speech.input", "set speech.input to windows-activated-v1 WindowsManaged CaptureLifetime",
            "set speech.input to pocketsphinx-5-1-1-en-us-cpu-v1 Cpu CaptureLifetime",
            "confirm speech.input", "cancel speech.input"];

    public static SpeechInputCommand? Parse(string input, string assistantName)
    {
        var text = input.Trim();
        var name = AssistantNameRules.Normalize(assistantName);
        if (text.StartsWith(name, StringComparison.OrdinalIgnoreCase) && text.Length > name.Length
            && (char.IsWhiteSpace(text[name.Length]) || text[name.Length] == ','))
        {
            text = text[name.Length..].TrimStart(' ', ',', '\t');
        }
        if (!TargetsProfile("get speech.input") && !TargetsProfile("status speech.input")
            && !TargetsProfile("set speech.input") && !TargetsProfile("confirm speech.input")
            && !TargetsProfile("cancel speech.input")) { return null; }
        bool TargetsProfile(string prefix) => text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            && (text.Length == prefix.Length || char.IsWhiteSpace(text[prefix.Length]));
        if (Encoding.UTF8.GetByteCount(input) > SessionCommand.MaximumInputBytes || input.Any(char.IsControl))
        { return new(AppearanceCommandOperation.Clarify, Error: Syntax); }
        if (text.Equals("get speech.input", StringComparison.OrdinalIgnoreCase)
            || text.Equals("status speech.input", StringComparison.OrdinalIgnoreCase)) { return new(AppearanceCommandOperation.Get); }
        if (text.Equals("confirm speech.input", StringComparison.OrdinalIgnoreCase))
        { return new(AppearanceCommandOperation.Set, ReviewDecision: true); }
        if (text.Equals("cancel speech.input", StringComparison.OrdinalIgnoreCase))
        { return new(AppearanceCommandOperation.Set, ReviewDecision: false); }
        const string prefix = "set speech.input to ";
        if (text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            var parts = text[prefix.Length..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 3 && parts[0] is (SpeechInputProfileCatalogue.WindowsProfileId or SpeechInputProfileCatalogue.NativeWakeProfileId)
                && Enum.TryParse<SpeechInputResource>(parts[1], ignoreCase: true, out var resource) && Enum.IsDefined(resource)
                && Enum.TryParse<SpeechInputResidency>(parts[2], ignoreCase: true, out var residency) && Enum.IsDefined(residency)
                && string.Equals(parts[1], resource.ToString(), StringComparison.OrdinalIgnoreCase)
                && string.Equals(parts[2], residency.ToString(), StringComparison.OrdinalIgnoreCase))
            { return new(AppearanceCommandOperation.Set, new(parts[0], resource, residency)); }
        }
        return new(AppearanceCommandOperation.Clarify, Error: Syntax);
    }
}