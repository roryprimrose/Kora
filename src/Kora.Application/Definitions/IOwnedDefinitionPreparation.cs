using Kora.Core.Artifacts;

namespace Kora.Application.Definitions;

/// <summary>A private native lifetime and synchronous acknowledgement, without source access.</summary>
public interface IOwnedDefinitionPreparation
{
    Func<bool> CaptureLifetime();
    bool TryPrepare(ArtifactKind kind, string ownedId, Func<bool> admitted);
}
