namespace Kora.Core.Presentation;

public static class StaticHtmlProfile
{
    public const string Name = "static-html-v1";
    public const int MaximumUtf8Bytes = 64 * 1024;
    public const int MaximumSemanticUtf8Bytes = 64 * 1024;
    public const int MaximumBlocks = 256;
    public const int MaximumNodes = 2048;
    public const int MaximumDepth = 16;
    public const int MaximumAttributes = 0;
    public const int MaximumEntityReferences = 2048;
}
