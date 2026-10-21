namespace Kora.Core.Artifacts;

public sealed record SourceBufferSize(int Utf16CodeUnits, int? Utf8Bytes, int? UnicodeScalars);
