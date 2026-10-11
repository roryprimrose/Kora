using System.Globalization;
using System.Text;
using Kora.Core.Commands;
using Kora.Core.Configuration;
using Kora.Core.Providers;

namespace Kora.Application.Configuration;

public sealed record SessionProviderProfileCommand(AppearanceCommandOperation Operation, Guid? Session = null,
    SessionProviderField? Field = null, ProviderService? Service = null, string? Model = null,
    string? Reasoning = null, string? Context = null, string? Account = null, long CatalogueRevision = 0,
    Guid? Confirmation = null, string? Error = null, string? Choice = null)
{
    public const string Syntax = "get providers.session.profile for <session-id> | "
        + "set providers.session.service for <session-id> to Unset|Local|Copilot|Claude|Codex | "
        + "set providers.session.<model|reasoning|context> for <session-id> to <exact-option-id> for <account-id> at <catalogue-revision> | "
        + "set providers.session.<model|reasoning|context|profile> for <session-id> to <model-id> <reasoning-id> <context-id> for <account-id> at <catalogue-revision> | "
        + "reset providers.session.<service|model|reasoning|context|profile> for <session-id> | "
        + "confirm providers.session.profile <host-issued-confirmation-id>. "
        + "Session scope only; profile reset means Use app defaults for this session. "
        + "No selected/exact session means select one; never app fallback. Generic multi-action language, one-turn choices and sign-in are unavailable.";
    public static IReadOnlyList<string> FixedPhrases { get; } = ["get providers.session.profile", "status providers.session.profile"];

    public static SessionProviderProfileCommand? Parse(string input, string assistantName)
    {
        var text = input.Trim();
        var name = AssistantNameRules.Normalize(assistantName);
        if (text.StartsWith(name, StringComparison.OrdinalIgnoreCase) && text.Length > name.Length
            && (char.IsWhiteSpace(text[name.Length]) || text[name.Length] == ','))
        { text = text[name.Length..].TrimStart(' ', ',', '\t'); }
        if (!text.Contains("providers.session.", StringComparison.OrdinalIgnoreCase)) { return null; }
        static SessionProviderProfileCommand Invalid() => new(AppearanceCommandOperation.Clarify, Error: Syntax);
        if (Encoding.UTF8.GetByteCount(input) > SessionCommand.MaximumInputBytes || input.Any(char.IsControl)) { return Invalid(); }
        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 3 && parts[0].Equals("confirm", StringComparison.OrdinalIgnoreCase)
            && parts[1].Equals("providers.session.profile", StringComparison.OrdinalIgnoreCase) && ExactId(parts[2]) is { } confirmation)
        { return new(AppearanceCommandOperation.Set, Confirmation: confirmation); }
        if (parts.Length < 4 || !parts[1].StartsWith("providers.session.", StringComparison.OrdinalIgnoreCase)
            || !parts[2].Equals("for", StringComparison.OrdinalIgnoreCase) || ExactId(parts[3]) is not { } session) { return Invalid(); }
        var option = parts[1]["providers.session.".Length..];
        SessionProviderField? field = null;
        if (!option.Equals("profile", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var value in Enum.GetValues<SessionProviderField>())
            { if (value.ToString().Equals(option, StringComparison.OrdinalIgnoreCase)) { field = value; break; } }
            if (field is null) { return Invalid(); }
        }
        if (parts.Length == 4 && (parts[0].Equals("get", StringComparison.OrdinalIgnoreCase)
            || parts[0].Equals("status", StringComparison.OrdinalIgnoreCase)))
        { return new(AppearanceCommandOperation.Get, session, field); }
        if (parts.Length == 4 && parts[0].Equals("reset", StringComparison.OrdinalIgnoreCase))
        { return new(AppearanceCommandOperation.Reset, session, field); }
        if (!parts[0].Equals("set", StringComparison.OrdinalIgnoreCase) || parts.Length < 6
            || !parts[4].Equals("to", StringComparison.OrdinalIgnoreCase)) { return Invalid(); }
        if (field == SessionProviderField.Service && parts.Length == 6)
        {
            foreach (var service in Enum.GetValues<ProviderService>())
            { if (service.ToString().Equals(parts[5], StringComparison.OrdinalIgnoreCase)) { return new(AppearanceCommandOperation.Set, session, field, service); } }
            return Invalid();
        }
        if (field is SessionProviderField.Model or SessionProviderField.Reasoning or SessionProviderField.Context
            && parts.Length == 10 && string.Equals(parts[6], "for", StringComparison.Ordinal) && string.Equals(parts[8], "at", StringComparison.Ordinal)
            && long.TryParse(parts[9], NumberStyles.None, CultureInfo.InvariantCulture, out var fieldRevision) && fieldRevision > 0
            && string.Equals(parts[9], fieldRevision.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal))
        {
            try
            {
                ProviderProfileRules.ValidateId(parts[5]);
                ProviderProfileRules.ValidateId(parts[7]);
                return new(AppearanceCommandOperation.Set, session, field, Account: parts[7],
                    CatalogueRevision: fieldRevision, Choice: parts[5]);
            }
            catch (InvalidDataException) { return Invalid(); }
        }
        if (field == SessionProviderField.Service || parts.Length != 12 || !string.Equals(parts[8], "for", StringComparison.Ordinal) || !string.Equals(parts[10], "at", StringComparison.Ordinal)
            || !long.TryParse(parts[11], NumberStyles.None, CultureInfo.InvariantCulture, out var revision) || revision <= 0
            || !string.Equals(parts[11], revision.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)) { return Invalid(); }
        try
        {
            foreach (var id in new[] { parts[5], parts[6], parts[7], parts[9] }) { ProviderProfileRules.ValidateId(id); }
            return new(AppearanceCommandOperation.Set, session, field, Model: parts[5], Reasoning: parts[6],
                Context: parts[7], Account: parts[9], CatalogueRevision: revision);
        }
        catch (InvalidDataException) { return Invalid(); }
    }

    private static Guid? ExactId(string value) => Guid.TryParseExact(value, "D", out var id)
        && id != Guid.Empty && string.Equals(value, id.ToString("D"), StringComparison.Ordinal) ? id : null;
}
