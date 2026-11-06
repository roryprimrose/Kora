using System.Text;
using System.Text.Json;

using Kora.Core.Dependencies;
using Kora.Core.Tools;

namespace Kora.Core.Skills;

/// <summary>One untrusted served metadata reference, never source-read, enablement or invocation authority.</summary>
public sealed record ModelSkillUseProposal(string Reference)
{
    public const string ModelPolicy = "For this explicitly requested skill selection only, return exactly one JSON object "
        + "with exactly one string field skillRef, selecting one enabled Skill ref actually returned by customizations.list/get. "
        + "Example: {\"skillRef\":\"opaque returned ref\"}. No paths, commands, task, input, grants or other fields. "
        + "The unchanged original task is frozen by the host. Selection does not read, enable or invoke the skill. "
        + "Separate original-user source-read/revision review, enablement and invocation confirmation remain mandatory.";

    public static ModelSkillUseProposal Parse(string answer)
    {
        using var document = ModelToolProtocol.ReadJson(Encoding.UTF8.GetBytes(answer), 128);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != 1
            || !root.TryGetProperty("skillRef", out var reference) || reference.ValueKind != JsonValueKind.String
            || !CustomizationDiscoveryTools.IsReference(reference.GetString()!))
        { throw new InvalidDataException("A skill selection requires exactly one actually served opaque skillRef; it grants no authority."); }
        return new(reference.GetString()!);
    }
}
