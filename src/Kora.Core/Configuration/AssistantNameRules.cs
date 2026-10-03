namespace Kora.Core.Configuration;

public static class AssistantNameRules
{
    public const string DefaultName = "Kora";

    public const int MaximumLength = 32;

    public const int MaximumWordCount = 3;

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
