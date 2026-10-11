using System.Globalization;
using System.Text;
using Kora.Core.Commands;
using Kora.Core.Configuration;
using Kora.Core.Providers;

namespace Kora.Application.Configuration;

/// <summary>Only explicitly app-scoped directives are admitted; session/one-turn overrides remain unavailable.</summary>
public sealed record ProviderProfileCommand(AppearanceCommandOperation Operation, ProviderService? Service = null,
    string? Model = null, string? Reasoning = null, string? Context = null, string? Account = null, long CatalogueRevision = 0,
    bool TupleOnly = false, bool SignIn = false, string? Error = null)
{
    public const string ServiceOption = "providers.app.default-service";
    public const string ProfileOption = "providers.app.profile";
    public const string Syntax = "list app provider settings | get/status providers.app.profile | "
        + "set providers.app.default-service to Unset|Local|Copilot|Claude|Codex | reset providers.app.default-service | "
        + "set providers.app.profile to <model-id> <reasoning-id> <context-id> for <account-id> at <catalogue-revision> | "
        + "reset providers.app.profile | sign in providers.app.service (Unavailable)";
    public static IReadOnlyList<string> FixedPhrases { get; } = ["list app provider settings", "get " + ProfileOption,
        "status " + ProfileOption, "reset " + ServiceOption, "reset " + ProfileOption, "sign in providers.app.service",
        "set " + ServiceOption + " to Unset", "set " + ServiceOption + " to Local",
        "set " + ServiceOption + " to Copilot", "set " + ServiceOption + " to Claude", "set " + ServiceOption + " to Codex"];

    public static ProviderProfileCommand? Parse(string input, string assistantName)
    {
        var text = input.Trim();
        var name = AssistantNameRules.Normalize(assistantName);
        if (text.StartsWith(name, StringComparison.OrdinalIgnoreCase) && text.Length > name.Length
            && (char.IsWhiteSpace(text[name.Length]) || text[name.Length] == ','))
        { text = text[name.Length..].TrimStart(' ', ',', '\t'); }
        if (!text.StartsWith("list app provider", StringComparison.OrdinalIgnoreCase)
            && !text.Contains("providers.app.", StringComparison.OrdinalIgnoreCase)) { return null; }
        if (Encoding.UTF8.GetByteCount(input) > SessionCommand.MaximumInputBytes || input.Any(char.IsControl))
        { return new(AppearanceCommandOperation.Clarify, Error: "App provider input exceeds 1024 UTF-8 bytes or contains controls. " + Syntax); }
        if (text.Equals("list app provider settings", StringComparison.OrdinalIgnoreCase)) { return new(AppearanceCommandOperation.List); }
        if (text.Equals("get " + ProfileOption, StringComparison.OrdinalIgnoreCase)
            || text.Equals("status " + ProfileOption, StringComparison.OrdinalIgnoreCase)
            || text.Equals("get " + ServiceOption, StringComparison.OrdinalIgnoreCase))
        { return new(AppearanceCommandOperation.Get); }
        if (text.Equals("sign in providers.app.service", StringComparison.OrdinalIgnoreCase))
        { return new(AppearanceCommandOperation.Get, SignIn: true); }
        if (text.Equals("reset " + ServiceOption, StringComparison.OrdinalIgnoreCase)) { return new(AppearanceCommandOperation.Reset); }
        if (text.Equals("reset " + ProfileOption, StringComparison.OrdinalIgnoreCase))
        { return new(AppearanceCommandOperation.Reset, TupleOnly: true); }
        var prefix = "set " + ServiceOption + " to ";
        if (text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            foreach (var service in Enum.GetValues<ProviderService>())
            {
                if (text[prefix.Length..].Equals(service.ToString(), StringComparison.OrdinalIgnoreCase))
                { return new(AppearanceCommandOperation.Set, service); }
            }
        }
        prefix = "set " + ProfileOption + " to ";
        if (text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            var parts = text[prefix.Length..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 7 && string.Equals(parts[3], "for", StringComparison.Ordinal) && string.Equals(parts[5], "at", StringComparison.Ordinal)
                && long.TryParse(parts[6], NumberStyles.None, CultureInfo.InvariantCulture, out var revision) && revision > 0
                && string.Equals(parts[6], revision.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal))
            {
                try
                {
                    foreach (var id in new[] { parts[0], parts[1], parts[2], parts[4] }) { ProviderProfileRules.ValidateId(id); }
                    return new(AppearanceCommandOperation.Set, Model: parts[0], Reasoning: parts[1], Context: parts[2],
                        Account: parts[4], CatalogueRevision: revision);
                }
                catch (InvalidDataException) { }
            }
        }
        return new(AppearanceCommandOperation.Clarify, Error: Syntax);
    }
}
