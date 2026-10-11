namespace Kora.Core.Context;

public sealed record LocalFileHeadingContext(int Start, int Length, int Line, int Column, string Label);
