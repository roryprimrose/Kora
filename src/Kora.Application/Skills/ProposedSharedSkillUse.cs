using Kora.Core.Customizations;
using Kora.Core.Hosting;
using Kora.Core.Skills;
using Kora.Core.Storage;

namespace Kora.Application.Skills;

/// <summary>Ephemeral served selection. The canonical shared workflow owns all later review and execution.</summary>
public sealed class ProposedSharedSkillUse : IDisposable
{
    private readonly CustomizationMetadataObservation observation;
    private readonly ICustomizationMetadataStore metadata;
    private readonly Func<bool> current;
    private int consumed;
    private volatile bool joined;
    private volatile bool retired;

    internal ProposedSharedSkillUse(CustomizationMetadataObservation observation, SharedSkillFileBinding binding,
        ICustomizationMetadataStore metadata, SessionWorkspaceEntry target, string originalRequest, RequestOrigin origin,
        Func<bool> current)
    {
        this.observation = observation;
        Binding = binding;
        this.metadata = metadata;
        Target = target;
        OriginalRequest = originalRequest;
        Origin = origin;
        this.current = current;
    }

    internal SharedSkillFileBinding Binding { get; }
    public SessionWorkspaceEntry Target { get; }
    public string OriginalRequest { get; }
    public RequestOrigin Origin { get; }
    public bool IsCurrent
    {
        get
        {
            if (!joined || retired) { return false; }
            if (current()) { return true; }
            Dispose();
            return false;
        }
    }
    public string Description => $"{observation.Name}\nOriginal task: {OriginalRequest}\n"
        + $"Original channel: {Origin}\nExact Active target: {Target.Authority.SessionId.Value:D}\n"
        + "LocalOnly; one single-file instruction-only skill; ZERO tools. Complete serialized input at most 32768 UTF-8 bytes; "
        + $"output at most {Kora.Core.Dependencies.ModelContextEnvelope.MaximumOutputUtf8Bytes} UTF-8 bytes and 15-second model deadline. "
        + "Separate source-read review, volatile enablement and invocation confirmation required.";

    internal void SelectionJoined() => joined = true;

    internal async Task ConsumeForReadAsync(CancellationToken token)
    {
        if (Interlocked.CompareExchange(ref consumed, 1, 0) != 0)
        { throw new InvalidOperationException("This selected skill already opened its one source-read review."); }
        await ValidateAsync(token).ConfigureAwait(false);
    }

    internal async Task ValidateAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!IsCurrent) { throw new InvalidOperationException("The selected skill or unchanged original task admission expired."); }
        var observed = await metadata.ReadMetadata(observation.Source, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        if (observed != observation || !IsCurrent)
        { Dispose(); throw new InvalidOperationException("The selected exact source identity or native revision changed."); }
    }

    internal void RequireSnapshot(SharedSkillSnapshot snapshot)
    {
        if (!IsCurrent || !string.Equals(snapshot.RevisionDigest, observation.Digest, StringComparison.Ordinal))
        { throw new InvalidOperationException("The registered skill bytes are not the selected metadata revision."); }
    }

    public void Dispose() => retired = true;
}
