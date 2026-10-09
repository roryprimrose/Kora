namespace Kora.Core.Context;

public sealed record LocalFileMetadata(string CanonicalPath, string FileIdentity, long ByteLength, DateTimeOffset LastWrite);
