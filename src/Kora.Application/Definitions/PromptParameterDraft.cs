using Kora.Core.Artifacts;

namespace Kora.Application.Definitions;

/// <summary>One private, ephemeral preparation of one exact source-qualified owned prompt revision.</summary>
public sealed class PromptParameterDraft : IDisposable
{
    private readonly ArtifactCatalogue catalogue;
    private readonly IReadOnlyList<ArtifactDefinition> candidates;
    private readonly long failureRevision;
    private readonly Func<bool> eligible;
    private readonly Lock gate = new();
    private readonly Dictionary<string, string> values = new(StringComparer.Ordinal);
    private long valueRevision;
    private bool disposed;
    private ExpandedPromptRequest? preview;
    private ExpandedPromptRequest? conversationAttempt;

    public PromptParameterDraft(ArtifactDefinition source, ArtifactCatalogue catalogue, Func<bool> eligible)
    {
        if (source.Kind != ArtifactKind.Prompt || !string.Equals(source.Source, "kora-profile", StringComparison.Ordinal)
            || source.OwnedInstructionName is null || source.OwnedInstructionRevision is null
            || !source.RequiresInstructionReview || source.PromptTemplate is null)
        { throw new InvalidDataException("Preparation requires an exact native v2 owned prompt revision."); }
        Source = source;
        this.catalogue = catalogue;
        this.eligible = eligible;
        candidates = catalogue.Candidates;
        failureRevision = catalogue.ReloadFailureRevision;
        foreach (var name in source.PromptTemplate.RequiredNames) { values.Add(name, string.Empty); }
    }

    public ArtifactDefinition Source { get; }
    public IReadOnlyList<string> RequiredNames => Source.PromptTemplate!.RequiredNames;
    private bool Current() => !disposed && eligible() && !catalogue.IsReloadUnavailable
        && failureRevision == catalogue.ReloadFailureRevision && ReferenceEquals(candidates, catalogue.Candidates)
        && catalogue.Candidates.Any(candidate => ReferenceEquals(candidate, Source) && candidate.IsUseEnabled);
    private bool PreviewCurrent() => Current() && preview is not null && preview.ValueRevision == valueRevision;
    public bool IsCurrent { get { lock (gate) { return Current(); } } }
    public bool CanUse { get { lock (gate) { return PreviewCurrent(); } } }
    public string? PreviewText { get { lock (gate) { return PreviewCurrent() ? preview!.UserRequest : null; } } }
    internal long InputRevision { get { lock (gate) { return valueRevision; } } }

    public void RetirePreview()
    {
        lock (gate) { preview = null; valueRevision++; }
    }

    public void SetValue(string name, string value)
    {
        lock (gate)
        {
            preview = null;
            valueRevision++;
            if (!Current()) { throw new InvalidOperationException("The prompt preparation has expired. Select the exact revision again."); }
            if (!values.ContainsKey(name)) { throw new InvalidDataException("That input is not declared by this prompt."); }
            values[name] = string.Empty;
            OwnedPromptTemplate.ValidateValue(value);
            values[name] = value;
        }
    }

    public async Task<ExpandedPromptRequest> Preview(
        Func<ArtifactDefinition, CancellationToken, Task<bool>> fresh, CancellationToken token)
    {
        long revision;
        lock (gate)
        {
            preview = null;
            revision = valueRevision;
            RequireCurrent(token);
        }
        if (!await fresh(Source, token).ConfigureAwait(false))
        { throw new InvalidOperationException("The original prompt source is stale or its reviewed use is unavailable."); }
        lock (gate)
        {
            RequireCurrent(token);
            if (revision != valueRevision) { throw new InvalidOperationException("Inputs changed during preview. Preview again."); }
            var request = Source.PromptTemplate!.Expand(values, token);
            preview = new(Source, Guid.NewGuid(), valueRevision, request);
            return preview;
        }
    }

    public async Task<ExpandedPromptRequest> CaptureUse(
        Func<ArtifactDefinition, CancellationToken, Task<bool>> fresh, CancellationToken token)
    {
        ExpandedPromptRequest captured;
        lock (gate)
        {
            RequireCurrent(token);
            captured = preview ?? throw new InvalidOperationException("Preview the complete expanded user request before Use.");
        }
        if (!await fresh(Source, token).ConfigureAwait(false))
        {
            lock (gate) { preview = null; }
            throw new InvalidOperationException("The original prompt source is stale or its reviewed use is unavailable.");
        }
        lock (gate)
        {
            RequireCurrent(token);
            if (!ReferenceEquals(preview, captured) || captured.ValueRevision != valueRevision)
            { throw new InvalidOperationException("The expanded preview changed before Use. Preview again."); }
            return captured;
        }
    }

    public bool IsUseCurrent(ExpandedPromptRequest request)
    { lock (gate) { return PreviewCurrent() && ReferenceEquals(preview, request); } }

    internal bool ClaimConversationUse(ExpandedPromptRequest request)
    {
        lock (gate)
        {
            if (ReferenceEquals(conversationAttempt, request)) { return false; }
            conversationAttempt = request;
            return true;
        }
    }

    private void RequireCurrent(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!Current())
        { preview = null; throw new InvalidOperationException("Prompt source, review, host, session, input or privacy changed. Select again."); }
    }

    public void Dispose()
    {
        lock (gate)
        {
            disposed = true;
            preview = null;
            conversationAttempt = null;
            values.Clear();
        }
    }
}
