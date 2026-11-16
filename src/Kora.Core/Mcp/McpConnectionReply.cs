namespace Kora.Core.Mcp;

public sealed record McpConnectionReply<T>(T Value, int Utf8Bytes);
