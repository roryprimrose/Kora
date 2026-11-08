namespace Kora.Core.Presentation;

public static class MarkdownDisplayPreference
{
    public const MarkdownDisplayMode Default = MarkdownDisplayMode.Rendered;

    public static MarkdownDisplayMode? Parse(string? value) => value switch
    {
        null => null,
        "Rendered" => MarkdownDisplayMode.Rendered,
        "Source" => MarkdownDisplayMode.Source,
        _ => throw new InvalidDataException("The saved Markdown display preference is invalid; inspect saved state before repair."),
    };

    public static string Format(MarkdownDisplayMode value) => value switch
    {
        MarkdownDisplayMode.Rendered => "Rendered",
        MarkdownDisplayMode.Source => "Source",
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };
}
