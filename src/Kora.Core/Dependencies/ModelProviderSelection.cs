using Kora.Core.Hosting;

namespace Kora.Core.Dependencies;

/// <summary>A host catalogue identity, never a provider-supplied destination or model name.</summary>
public sealed record ModelProviderSelection(
    ModelProviderIdentity Provider, HostId<ModelIdentity> Model, HostRevision Revision)
{
    public static ModelProviderSelection OllamaCandidate { get; } = new(
        ModelProviderIdentity.Ollama, new(new Guid("9e0b0865-82fc-49ad-97ae-adfdd596b74f")), new(1));

    public static ModelProviderSelection CopilotCandidate { get; } = new(
        ModelProviderIdentity.Copilot, new(new Guid("f953c342-944b-460e-a40a-2b4db3f51c98")), new(1));
}
