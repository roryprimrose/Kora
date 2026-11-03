using System.Globalization;

using Kora.Core.Configuration;

namespace Kora.Application.Configuration;

public static class NaturalAppearanceCommand
{
    private static readonly string[] Operations = ["get", "status", "reset", "set", "increase", "decrease"];
    private static readonly string[] Mutations = ["set", "increase", "decrease"];

    public const string Syntax = "Use dark mode, use light mode, or use system theme. Use reduced motion, or follow system motion. Use get/status reader text scale, reset reader text scale, set reader text scale to <integer> percent, or increase/decrease reader text scale by <positive integer> percentage points. Voice requires the current assistant-name prefix.";

    public static AppearanceCommand? Parse(string transcript, string assistantName, bool requireActivation = false)
    {
        var text = transcript.Trim();
        var name = AssistantNameRules.Normalize(assistantName);
        var activated = text.StartsWith(name, StringComparison.OrdinalIgnoreCase)
            && text.Length > name.Length && (char.IsWhiteSpace(text[name.Length]) || text[name.Length] == ',');
        if (activated)
        {
            text = text[name.Length..].TrimStart(' ', ',', '\t');
        }
        var theme = text.ToLowerInvariant() switch
        {
            "use dark mode" => ApplicationThemeMode.Dark,
            "use light mode" => ApplicationThemeMode.Light,
            "use system theme" => ApplicationThemeMode.System,
            _ => (ApplicationThemeMode?)null,
        };
        var reader = Operations.Any(operation =>
            text.Equals(operation + " reader text scale", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith(operation + " reader text scale ", StringComparison.OrdinalIgnoreCase));
        var reducedMotion = text.ToLowerInvariant() switch
        {
            "use reduced motion" => true,
            "follow system motion" => false,
            _ => (bool?)null,
        };
        if (theme is null && !reader && reducedMotion is null) { return null; }
        if (text.Length > 256 || text.Any(char.IsControl) || (requireActivation && !activated))
        {
            return Clarify();
        }
        if (reducedMotion is { } reduce)
        {
            return new(AppearanceCommandOperation.Set, AppearanceOptionRegistry.Get(AppearanceOption.ReducedMotion),
                new AppearanceValue.Toggle(reduce));
        }
        if (theme is { } mode)
        {
            return new(AppearanceCommandOperation.Set, AppearanceOptionRegistry.Get(AppearanceOption.Theme),
                new AppearanceValue.Theme(mode));
        }
        var descriptor = AppearanceOptionRegistry.Get(AppearanceOption.ReaderTextScale);
        if (text.Equals("get reader text scale", StringComparison.OrdinalIgnoreCase)
            || text.Equals("status reader text scale", StringComparison.OrdinalIgnoreCase))
        {
            return new(AppearanceCommandOperation.Get, descriptor);
        }
        if (text.Equals("reset reader text scale", StringComparison.OrdinalIgnoreCase))
        {
            return new(AppearanceCommandOperation.Reset, descriptor);
        }
        foreach (var operation in Mutations)
        {
            var absolute = string.Equals(operation, "set", StringComparison.Ordinal);
            var prefix = operation + (absolute ? " reader text scale to " : " reader text scale by ");
            var suffix = absolute ? " percent" : " percentage points";
            if (!text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                || !text.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            if (text.Length <= prefix.Length + suffix.Length) { return Clarify(); }
            var number = text.AsSpan(prefix.Length, text.Length - prefix.Length - suffix.Length);
            if (!int.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out var value) || value <= 0)
            {
                return Clarify();
            }
            if (!absolute)
            {
                return new(AppearanceCommandOperation.Set, descriptor,
                    ReaderTextScaleAdjustment: string.Equals(operation, "increase", StringComparison.Ordinal) ? value : -value);
            }
            var scale = new AppearanceValue.Number(value);
            try { AppearanceOptionRegistry.Validate(descriptor.Option, scale); }
            catch (ArgumentOutOfRangeException) { return Clarify(); }
            return new(AppearanceCommandOperation.Set, descriptor, scale);
        }
        return Clarify();
    }

    private static AppearanceCommand Clarify() => new(AppearanceCommandOperation.Clarify, Error: Syntax);
}
