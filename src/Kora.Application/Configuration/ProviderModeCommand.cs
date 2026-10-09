using System.Text;
using Kora.Core.Commands;
using Kora.Core.Configuration;
using Kora.Core.Dependencies;

namespace Kora.Application.Configuration;

public sealed record ProviderModeCommand(AppearanceCommandOperation Operation, ModelProviderMode? Value = null, string? Error = null)
{
    public const string OptionId = "providers.default-mode";
    public const string Syntax = "list provider settings | get providers.default-mode | status providers.default-mode | "
        + "set providers.default-mode to LocalOnly|LocalFirst|HostedPreferred | reset providers.default-mode";
    public static IReadOnlyList<string> FixedPhrases { get; } =
        ["list provider settings", "get " + OptionId, "status " + OptionId, "reset " + OptionId,
            "set " + OptionId + " to LocalOnly", "set " + OptionId + " to LocalFirst", "set " + OptionId + " to HostedPreferred"];

    public static ProviderModeCommand? Parse(string input, string assistantName)
    {
        var text = input.Trim();
        var name = AssistantNameRules.Normalize(assistantName);
        if (text.StartsWith(name, StringComparison.OrdinalIgnoreCase) && text.Length > name.Length
            && (char.IsWhiteSpace(text[name.Length]) || text[name.Length] == ','))
        {
            text = text[name.Length..].TrimStart(' ', ',', '\t');
        }
        if (!text.StartsWith("list provider", StringComparison.OrdinalIgnoreCase)
            && !text.StartsWith("get providers.", StringComparison.OrdinalIgnoreCase)
            && !text.StartsWith("status providers.", StringComparison.OrdinalIgnoreCase)
            && !text.StartsWith("set providers.", StringComparison.OrdinalIgnoreCase)
            && !text.StartsWith("reset providers.", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }
        if (Encoding.UTF8.GetByteCount(input) > SessionCommand.MaximumInputBytes || input.Any(char.IsControl))
        {
            return new(AppearanceCommandOperation.Clarify, Error: "Provider input exceeds 1024 UTF-8 bytes or contains controls. " + Syntax);
        }
        if (text.Equals("list provider settings", StringComparison.OrdinalIgnoreCase)) { return new(AppearanceCommandOperation.List); }
        if (text.Equals("get " + OptionId, StringComparison.OrdinalIgnoreCase)
            || text.Equals("status " + OptionId, StringComparison.OrdinalIgnoreCase)) { return new(AppearanceCommandOperation.Get); }
        if (text.Equals("reset " + OptionId, StringComparison.OrdinalIgnoreCase)) { return new(AppearanceCommandOperation.Reset); }
        var prefix = "set " + OptionId + " to ";
        if (text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            var value = text[prefix.Length..];
            foreach (var mode in ModelProviderModePreference.Choices)
            {
                if (value.Equals(mode.ToString(), StringComparison.OrdinalIgnoreCase)) { return new(AppearanceCommandOperation.Set, mode); }
            }
        }
        return new(AppearanceCommandOperation.Clarify, Error: Syntax);
    }
}
