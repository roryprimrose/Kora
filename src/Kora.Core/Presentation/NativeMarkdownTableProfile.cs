namespace Kora.Core.Presentation;

public static class NativeMarkdownTableProfile
{
    public const string Name = "native-pipe-tables-v2";
    public const int MaximumRowsPerTable = 32;
    public const int MaximumColumns = 8;
    public const int MaximumCellsPerDocument = 128;
    public const int MaximumCellUtf8Bytes = 2048;
}
