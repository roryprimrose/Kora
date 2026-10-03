namespace Kora.Core.Diagnostics;

public sealed record ApplicationLogFile(
    string FileName,
    DateOnly Date,
    long Length,
    DateTimeOffset LastWriteTime);
