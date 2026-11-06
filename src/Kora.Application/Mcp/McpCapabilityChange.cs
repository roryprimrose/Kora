using Kora.Core.Mcp;

namespace Kora.Application.Mcp;

public sealed record McpCapabilityChange(bool ServerIncluded, McpCapabilitySelection Selection);