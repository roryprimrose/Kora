using System.Text;
using System.Text.Json;

using Kora.Core.Dependencies;
using Kora.Core.Tools;

namespace Kora.Core.Agents;

/// <summary>Untrusted selection data, never invocation consent or host authority.</summary>
public sealed record ModelAgentUseProposal(string Reference)
{
    public const string ModelPolicy = "For this explicitly requested agent recommendation only, return a final answer containing "
        + "exactly one JSON object with exactly one string field agentRef, selecting one enabled Agent ref actually returned "
        + "by customizations.list/get. Example: {\"agentRef\":\"opaque returned ref\"}. No paths, input, instructions, approval, "
        + "destination or other fields. This is only a proposal; the original user must separately review and confirm any invocation.";

    public static ModelAgentUseProposal Parse(string answer)
    {
        using var document = ModelToolProtocol.ReadJson(Encoding.UTF8.GetBytes(answer), 128);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != 1
            || !root.TryGetProperty("agentRef", out var reference) || reference.ValueKind != JsonValueKind.String
            || !CustomizationDiscoveryTools.IsReference(reference.GetString()!))
        { throw new InvalidDataException("An agent recommendation requires exactly one bounded returned agentRef; it grants no authority."); }
        return new(reference.GetString()!);
    }
}
