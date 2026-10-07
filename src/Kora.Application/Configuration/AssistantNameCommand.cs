using Kora.Core.Configuration;

namespace Kora.Application.Configuration;

public sealed record AssistantNameCommand(
    AppearanceCommandOperation Operation, string? Value = null, string? Error = null)
{
    public const int MaximumLength = 320;
    public const string Syntax = "Use list assistant settings, get assistant.name, set assistant.name to <name>, or reset assistant.name. The spoken name assistant name also works. This is the display/PTT command-prefix name, not a production wake name.";
    public static IReadOnlyList<string> DiscoveryPhrases { get; } = Array.AsReadOnly(new[]
    {
        "list assistant settings", "get assistant name", "reset assistant name",
    });

    public static AssistantNameCommand? Parse(string transcript, string assistantName)
    {
        var text = transcript.Trim();
        var name = AssistantNameRules.Normalize(assistantName);
        if (text.StartsWith(name, StringComparison.OrdinalIgnoreCase) && text.Length > name.Length
            && (char.IsWhiteSpace(text[name.Length]) || text[name.Length] == ','))
        {
            text = text[name.Length..].TrimStart(' ', ',', '\t');
        }
        if (!(text.StartsWith("list assistant", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("get assistant", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("set assistant", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("reset assistant", StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }
        if (text.Length > MaximumLength)
        {
            return new(AppearanceCommandOperation.Clarify, Error: "Assistant command is too long. " + Syntax);
        }
        if (text.Equals("list assistant settings", StringComparison.OrdinalIgnoreCase))
        {
            return new(AppearanceCommandOperation.List);
        }
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
        if (!(target.Equals(AssistantNameOption.Id, StringComparison.OrdinalIgnoreCase)
            || target.Equals(AssistantNameOption.SpokenName, StringComparison.OrdinalIgnoreCase)))
        {
            return new(AppearanceCommandOperation.Clarify, Error: Syntax);
        }
        if (operation.Equals("get", StringComparison.OrdinalIgnoreCase))
        {
            return new(AppearanceCommandOperation.Get);
        }
        if (operation.Equals("reset", StringComparison.OrdinalIgnoreCase))
        {
            return new(AppearanceCommandOperation.Reset);
        }
        return value is null
            ? new(AppearanceCommandOperation.Clarify, Error: Syntax)
            : new(AppearanceCommandOperation.Set, value);
    }
}
