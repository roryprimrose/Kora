using System.Globalization;
using Kora.Core.Configuration;

namespace Kora.Application.Configuration;

public sealed record AppearanceCommand(AppearanceCommandOperation Operation,
    AppearanceOptionDescriptor? Descriptor = null, AppearanceValue? Value = null, string? Error = null)
{
    public const string Syntax = "Use list appearance settings, get <appearance.id>, set <appearance.id> to <value>, or reset <appearance.id>. The listed spoken name also works instead of the ID. Integers use the listed units, theme is system/light/dark, and booleans are true/false. Reset affects only that option.";

    public static AppearanceCommand? Parse(string transcript, string assistantName)
    {
        var text = transcript.Trim();
        var name = AssistantNameRules.Normalize(assistantName);
        if (text.StartsWith(name, StringComparison.OrdinalIgnoreCase)
            && text.Length > name.Length && (char.IsWhiteSpace(text[name.Length]) || text[name.Length] == ','))
        {
            text = text[name.Length..].TrimStart(' ', ',', '\t');
        }
        if (!text.StartsWith("list appearance", StringComparison.OrdinalIgnoreCase)
            && !text.StartsWith("get appearance", StringComparison.OrdinalIgnoreCase)
            && !text.StartsWith("set appearance", StringComparison.OrdinalIgnoreCase)
            && !text.StartsWith("reset appearance", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }
        if (text.Length > 256)
        {
            return new(AppearanceCommandOperation.Clarify, Error: "The appearance command is too long. " + Syntax);
        }
        if (string.Equals(text, "list appearance settings", StringComparison.OrdinalIgnoreCase))
        {
            return new(AppearanceCommandOperation.List);
        }
        var firstSpace = text.IndexOf(' ', StringComparison.Ordinal);
        var operation = text[..firstSpace];
        var target = text[(firstSpace + 1)..].Trim();
        string? valueText = null;
        if (string.Equals(operation, "set", StringComparison.OrdinalIgnoreCase))
        {
            var separator = target.IndexOf(" to ", StringComparison.OrdinalIgnoreCase);
            if (separator >= 0)
            {
                valueText = target[(separator + 4)..];
                target = target[..separator];
            }
        }
        var descriptor = AppearanceOptionRegistry.Options.FirstOrDefault(item =>
            string.Equals(item.Id, target, StringComparison.OrdinalIgnoreCase)
            || string.Equals(item.SpokenName, target, StringComparison.OrdinalIgnoreCase));
        if (descriptor is null)
        {
            return new(AppearanceCommandOperation.Clarify, Error: "Choose an exact admitted appearance ID. " + Syntax);
        }
        if (string.Equals(operation, "get", StringComparison.OrdinalIgnoreCase))
        {
            return new(AppearanceCommandOperation.Get, descriptor);
        }
        if (string.Equals(operation, "reset", StringComparison.OrdinalIgnoreCase))
        {
            return new(AppearanceCommandOperation.Reset, descriptor);
        }
        if (valueText is not null)
        {
            AppearanceValue? value = descriptor.Type switch
            {
                AppearanceValueType.Number when int.TryParse(valueText, NumberStyles.AllowLeadingSign,
                    CultureInfo.InvariantCulture, out var integer) => new AppearanceValue.Number(integer),
                AppearanceValueType.Toggle when bool.TryParse(valueText, out var boolean) => new AppearanceValue.Toggle(boolean),
                AppearanceValueType.Theme when string.Equals(valueText, "system", StringComparison.OrdinalIgnoreCase) => new AppearanceValue.Theme(ApplicationThemeMode.System),
                AppearanceValueType.Theme when string.Equals(valueText, "light", StringComparison.OrdinalIgnoreCase) => new AppearanceValue.Theme(ApplicationThemeMode.Light),
                AppearanceValueType.Theme when string.Equals(valueText, "dark", StringComparison.OrdinalIgnoreCase) => new AppearanceValue.Theme(ApplicationThemeMode.Dark),
                _ => null,
            };
            if (value is not null)
            {
                try
                {
                    AppearanceOptionRegistry.Validate(descriptor.Option, value);
                    return new(AppearanceCommandOperation.Set, descriptor, value);
                }
                catch (ArgumentOutOfRangeException exception)
                {
                    return new(AppearanceCommandOperation.Clarify, descriptor, Error: exception.Message);
                }
            }
        }
        return new(AppearanceCommandOperation.Clarify, descriptor, Error: Syntax);
    }

    public static string Format(AppearanceValue value) => value switch
    {
        AppearanceValue.Number integer => integer.Value.ToString(CultureInfo.InvariantCulture),
        AppearanceValue.Toggle boolean => boolean.Value ? "true" : "false",
        AppearanceValue.Theme theme => theme.Value.ToString().ToLowerInvariant(),
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };
}
