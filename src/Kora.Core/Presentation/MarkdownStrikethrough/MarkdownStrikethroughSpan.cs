namespace Kora.Core.Presentation.MarkdownStrikethrough;

/// <summary>End-exclusive UTF-16 ranges in the exact source and labelled native section text.</summary>
public sealed record MarkdownStrikethroughSpan
{
    public MarkdownStrikethroughSpan(int sourceStart, int sourceLength, int textStart, int textLength)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(sourceStart);
        ArgumentOutOfRangeException.ThrowIfLessThan(sourceLength, 4);
        ArgumentOutOfRangeException.ThrowIfNegative(textStart);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(textLength);
        SourceStart = sourceStart;
        SourceEnd = checked(sourceStart + sourceLength);
        TextStart = textStart;
        TextEnd = checked(textStart + textLength);
    }

    public int SourceStart { get; }
    public int SourceEnd { get; }
    public int TextStart { get; }
    public int TextEnd { get; }
}
