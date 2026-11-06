using System.Security.Cryptography;
using System.Text;
using Kora.Core.Skills;

namespace Kora.Application.Mcp;

/// <summary>Host-created immutable binding for the separately admitted reviewed skill consumer.</summary>
internal sealed record McpSkillBinding(DeclarativeMcpToolReference Reference, string SkillIdentity,
    string SkillRevision, string OriginalRequest, string Arguments, string Guidance, DateTimeOffset ExpiresAt)
{
    internal McpSkillReview Review => new(SkillIdentity, SkillRevision, OriginalRequest,
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Arguments))));
}
