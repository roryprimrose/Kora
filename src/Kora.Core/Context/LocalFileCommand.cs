namespace Kora.Core.Context;

public sealed record LocalFileCommand(LocalFileOperation Operation)
{
    public static IEnumerable<string> FixedPhrases => ["preview file", "clear file preview", "search file", "inspect file",
        "preview folder", "clear folder preview", "search folder", "inspect folder"];

    public static LocalFileCommand? Parse(string normalized) => normalized switch
    {
        "preview file" => new(LocalFileOperation.Select),
        "preview folder" => new(LocalFileOperation.SelectFolder),
        "clear file preview" or "clear folder preview" => new(LocalFileOperation.Clear),
        "search file" or "inspect file" or "search folder" or "inspect folder" => new(LocalFileOperation.Inspect),
        _ when normalized.StartsWith("search file ", StringComparison.Ordinal)
            || normalized.StartsWith("inspect file ", StringComparison.Ordinal)
            || normalized.StartsWith("search folder ", StringComparison.Ordinal)
            || normalized.StartsWith("inspect folder ", StringComparison.Ordinal)
            || normalized.StartsWith("preview folder ", StringComparison.Ordinal) => new(LocalFileOperation.Invalid),
        _ => null,
    };
}
