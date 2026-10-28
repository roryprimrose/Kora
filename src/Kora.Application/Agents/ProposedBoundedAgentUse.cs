using Kora.Core.Artifacts;
using Kora.Core.Customizations;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Application.Agents;

/// <summary>Ephemeral source-bound recommendation. The existing native workflow still owns all execution admission.</summary>
public sealed class ProposedBoundedAgentUse
{
    private readonly ICustomizationMetadataStore sources;
    private readonly CustomizationMetadataObservation observation;
    private readonly Func<bool> current;
    private int consumed;

    internal ProposedBoundedAgentUse(ArtifactDefinition definition, CustomizationMetadataObservation observation,
        ICustomizationMetadataStore sources, SessionWorkspaceEntry target, string input, RequestOrigin origin,
        Func<bool> current)
    {
        Definition = definition;
        this.observation = observation;
        this.sources = sources;
        Target = target;
        Input = input;
        Origin = origin;
        this.current = current;
    }

    public ArtifactDefinition Definition { get; }
    public SessionWorkspaceEntry Target { get; }
    public string Input { get; }
    public RequestOrigin Origin { get; }
    public bool IsCurrent() => current();

    public async Task ConsumeForReviewAsync(CancellationToken token)
    {
        if (Interlocked.CompareExchange(ref consumed, 1, 0) != 0)
        { throw new InvalidOperationException("This proposal already opened its one review. Make a fresh original request."); }
        await ValidateAsync(token).ConfigureAwait(false);
    }

    public async Task ValidateAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!IsCurrent())
        { throw new InvalidOperationException("The original proposal, source, input, privacy or session admission expired."); }
        var observed = await sources.ReadMetadata(observation.Source, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        if (observed != observation || !IsCurrent())
        { throw new InvalidOperationException("The proposed exact source revision changed. No invocation is authorized."); }
    }
}
