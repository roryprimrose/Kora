namespace Kora.Core.Context;

public sealed record LocalFileCommand(LocalFileOperation Operation)
{
    public static IEnumerable<string> FixedPhrases => ["preview file", "clear file preview", "search file", "inspect file"];

    public static LocalFileCommand? Parse(string normalized) => normalized switch
    {
        "preview file" => new(LocalFileOperation.Select),
        "clear file preview" => new(LocalFileOperation.Clear),
        "search file" or "inspect file" => new(LocalFileOperation.Inspect),
        _ when normalized.StartsWith("search file ", StringComparison.Ordinal)
            || normalized.StartsWith("inspect file ", StringComparison.Ordinal) => new(LocalFileOperation.Invalid),
        _ => null,
    };
}
