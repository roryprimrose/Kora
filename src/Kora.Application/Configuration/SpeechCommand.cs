using Kora.Core.Configuration;

namespace Kora.Application.Configuration;

public sealed record SpeechCommand(AppearanceCommandOperation Operation,
    SpeechOptionDescriptor? Descriptor = null, string? Value = null, string? Error = null)
{
    public const int MaximumLength = 320;
    private static readonly string[] Prefixes = ["list speech", "get speech", "set speech", "reset speech"];
    public const string Syntax = "Use list speech settings, get <speech.id>, set <speech.id> to <exact installed ID>, or reset <speech.id>. Listed spoken names also work. Voice choices accept provider / ID, or an unambiguous exact voice ID, and explicitly select that provider too. Reset provider restores Windows/default voice; reset voice restores that provider's advertised default.";

    public static SpeechCommand? Parse(string transcript, string assistantName)
    {
        var text = transcript.Trim();
        var name = AssistantNameRules.Normalize(assistantName);
        if (text.StartsWith(name, StringComparison.OrdinalIgnoreCase) && text.Length > name.Length
            && (char.IsWhiteSpace(text[name.Length]) || text[name.Length] == ','))
        {
            text = text[name.Length..].TrimStart(' ', ',', '\t');
        }
        if (!Prefixes.Any(prefix => text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))) { return null; }
        if (text.Length > MaximumLength) { return new(AppearanceCommandOperation.Clarify, Error: "Speech command is too long. " + Syntax); }
        if (text.Equals("list speech settings", StringComparison.OrdinalIgnoreCase)) { return new(AppearanceCommandOperation.List); }
        var separator = text.IndexOf(' ', StringComparison.Ordinal);
        var operation = text[..separator];
        var target = text[(separator + 1)..].Trim();
        string? value = null;
        var valueSeparator = target.IndexOf(" to ", StringComparison.OrdinalIgnoreCase);
        if (operation.Equals("set", StringComparison.OrdinalIgnoreCase) && valueSeparator >= 0)
        {
            value = target[(valueSeparator + 4)..];
            target = target[..valueSeparator];
        }
        var descriptor = SpeechOptionRegistry.Options.FirstOrDefault(item =>
            item.Id.Equals(target, StringComparison.OrdinalIgnoreCase)
            || item.SpokenName.Equals(target, StringComparison.OrdinalIgnoreCase));
        if (descriptor is null) { return new(AppearanceCommandOperation.Clarify, Error: Syntax); }
        if (operation.Equals("get", StringComparison.OrdinalIgnoreCase)) { return new(AppearanceCommandOperation.Get, descriptor); }
        if (operation.Equals("reset", StringComparison.OrdinalIgnoreCase)) { return new(AppearanceCommandOperation.Reset, descriptor); }
        if (string.IsNullOrWhiteSpace(value)) { return new(AppearanceCommandOperation.Clarify, descriptor, Error: Syntax); }
        return new(AppearanceCommandOperation.Set, descriptor, value);
    }
}
