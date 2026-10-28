using Kora.Core.Hosting;

namespace Kora.Core.Mcp.ResultFollowUp;

public sealed record McpResultProvenance(string Server, string Endpoint, string Account, string Tool,
    RequestOrigin Origin);
