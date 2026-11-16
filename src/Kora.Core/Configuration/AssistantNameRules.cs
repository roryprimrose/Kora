namespace Kora.Core.Configuration;

public static class AssistantNameRules
{
    public const string DefaultName = "Kora";

    public const int MaximumLength = 32;

    public const int MaximumWordCount = 3;

    /// <summary>
    /// Returns the names accepted as an activation/command prefix, longest first: the configured name and,
    /// when it differs, the fixed product name <see cref="DefaultName"/>. No other former name is retained.
    /// </summary>
    public static IReadOnlyList<string> GetActivationNames(string assistantName)
    {
        var name = Normalize(assistantName);
        if (string.Equals(name, DefaultName, StringComparison.OrdinalIgnoreCase))
        {
            return [name];
        }
        return name.Length >= DefaultName.Length ? [name, DefaultName] : [DefaultName, name];
    }

    /// <summary>
    /// Selects the accepted activation name that <paramref name="text"/> begins with at a word boundary,
    /// preferring the longest; otherwise returns the normalized configured name. Callers keep their own
    /// delimiter grammar after the returned name.
    /// </summary>
    public static string ResolveActivationName(string text, string assistantName)
    {
        ArgumentNullException.ThrowIfNull(text);
        var names = GetActivationNames(assistantName);
        foreach (var name in names)
        {
            if (text.StartsWith(name, StringComparison.OrdinalIgnoreCase)
                && (text.Length == name.Length || !char.IsLetterOrDigit(text[name.Length])))
            {
                return name;
            }
        }
        return Normalize(assistantName);
    }

    /// <summary>Expands recognition phrases with their bare form and each accepted activation-name prefix.</summary>
    public static IEnumerable<string> WithActivationNames(IEnumerable<string> phrases, string assistantName)
    {
        ArgumentNullException.ThrowIfNull(phrases);
        var names = GetActivationNames(assistantName);
        return phrases.SelectMany(phrase => names.Select(name => $"{name} {phrase}").Prepend(phrase));
    }

    public static string Normalize(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var normalized = string.Join(
            ' ',
            value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (normalized.Length == 0)
        {
            throw new ArgumentException("The assistant name cannot be blank.", nameof(value));
        }

        if (normalized.Length > MaximumLength)
        {
            throw new ArgumentException(
                $"The assistant name cannot exceed {MaximumLength} characters.",
                nameof(value));
        }

        if (normalized.Split(' ').Length > MaximumWordCount)
        {
            throw new ArgumentException(
                $"The assistant name cannot contain more than {MaximumWordCount} words.",
                nameof(value));
        }

        if (normalized.Any(character =>
                !char.IsLetterOrDigit(character)
                && character is not (' ' or '\'' or '-')))
        {
            throw new ArgumentException(
                "The assistant name can contain only letters, numbers, spaces, apostrophes, and hyphens.",
                nameof(value));
        }

        if (!normalized.Any(char.IsLetterOrDigit))
        {
            throw new ArgumentException(
                "The assistant name must contain at least one letter or number.",
                nameof(value));
        }

        return normalized;
    }
}
