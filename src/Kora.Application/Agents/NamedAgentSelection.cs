using Kora.Core.Artifacts;

namespace Kora.Application.Agents;

public sealed record NamedAgentSelection(ArtifactDefinition Definition, string Path, Func<bool> IsCurrent)
{
    public void ValidateSource(ArtifactFileDocument document)
    {
        if (!IsCurrent() || !string.Equals(document.Definition.Name, Definition.Name, StringComparison.Ordinal)
            || !string.Equals(document.Definition.Digest, Definition.Digest, StringComparison.Ordinal))
        { throw new InvalidOperationException("The selected agent source is no longer the exact registered revision. Start a fresh command."); }
    }
}
