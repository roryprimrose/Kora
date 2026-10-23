using System.Text;

using Kora.Core.Commands;
using Kora.Core.Configuration;
using Kora.Core.Voice;

namespace Kora.Application.Configuration;

public sealed record SpeechInputCommand(AppearanceCommandOperation Operation, SpeechInputSelection? Selection = null, string? Error = null)
{
    public const string Syntax = "get speech.input | set speech.input to windows-activated-v1 WindowsManaged CaptureLifetime. "
        + "Resource preferences Cpu/Gpu and residency KeepLoaded may be saved but are explicitly unavailable for the current adapter. No download, capture, wake-name activation or fallback.";
    public static IReadOnlyList<string> FixedPhrases { get; } =
        ["get speech.input", "status speech.input", "set speech.input to windows-activated-v1 WindowsManaged CaptureLifetime"];

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
            && !TargetsProfile("set speech.input")) { return null; }
        bool TargetsProfile(string prefix) => text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            && (text.Length == prefix.Length || char.IsWhiteSpace(text[prefix.Length]));
        if (Encoding.UTF8.GetByteCount(input) > SessionCommand.MaximumInputBytes || input.Any(char.IsControl))
        { return new(AppearanceCommandOperation.Clarify, Error: Syntax); }
        if (text.Equals("get speech.input", StringComparison.OrdinalIgnoreCase)
            || text.Equals("status speech.input", StringComparison.OrdinalIgnoreCase)) { return new(AppearanceCommandOperation.Get); }
        const string prefix = "set speech.input to ";
        if (text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            var parts = text[prefix.Length..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 3 && string.Equals(parts[0], SpeechInputProfileCatalogue.WindowsProfileId, StringComparison.Ordinal)
                && Enum.TryParse<SpeechInputResource>(parts[1], ignoreCase: true, out var resource) && Enum.IsDefined(resource)
                && Enum.TryParse<SpeechInputResidency>(parts[2], ignoreCase: true, out var residency) && Enum.IsDefined(residency)
                && string.Equals(parts[1], resource.ToString(), StringComparison.OrdinalIgnoreCase)
                && string.Equals(parts[2], residency.ToString(), StringComparison.OrdinalIgnoreCase))
            { return new(AppearanceCommandOperation.Set, new(parts[0], resource, residency)); }
        }
        return new(AppearanceCommandOperation.Clarify, Error: Syntax);
    }
}