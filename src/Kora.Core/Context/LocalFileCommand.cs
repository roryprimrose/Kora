namespace Kora.Core.Context;

public sealed record LocalFileCommand(LocalFileOperation Operation)
{
    public static IEnumerable<string> FixedPhrases => ["preview file", "clear file preview"];

    public static LocalFileCommand? Parse(string normalized) => normalized switch
    {
        "preview file" => new(LocalFileOperation.Select),
        "clear file preview" => new(LocalFileOperation.Clear),
        _ => null,
    };
}
