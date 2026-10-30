using System.Collections.Immutable;

using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Providers;

namespace Kora.Core.Conversation;

/// <summary>Host adapter handoff evidence, not proof of provider delivery, processing or reasoning.</summary>
public sealed record SubmittedAnswerContext(int Version, ModelTurnProvenance Provenance,
    HostRevision Generation, ProviderProfileTuple Profile, DateTimeOffset SubmittedAt,
    int CanonicalUtf8Bytes, ImmutableArray<SubmittedHistorySource> Sources)
{
    /// <summary>Called by the host at adapter handoff, never from provider/source output.</summary>
    public static SubmittedAnswerContext? Capture(ModelTurnProvenance provenance, HostRevision generation,
        ProviderProfileTuple? profile, ModelContextEnvelope context, ReadOnlySpan<byte> canonical, DateTimeOffset submittedAt)
    {
        if (profile is null || provenance.Selection.Provider != ModelProviderIdentity.Ollama
            || context.ToolMode != ModelToolMode.None
            || !string.Equals(context.SystemPolicy, ConversationHistoryContext.Policy, StringComparison.Ordinal)
            || context.Evidence.IsEmpty || context.Evidence.Any(item => item.HistorySource is null))
        { return null; }
        if (context.Request != provenance.Request || !context.Serialize().AsSpan().SequenceEqual(canonical))
        { throw new InvalidDataException("The canonical admitted context changed before adapter handoff."); }
        var snapshot = new SubmittedAnswerContext(1, provenance, generation, profile, submittedAt, canonical.Length,
            [.. context.Evidence.Select(item => item.HistorySource!)]);
        snapshot.Validate();
        return snapshot;
    }

    public void Validate()
    {
        if (Version != 1 || Provenance?.Request is null || Provenance.Selection is null
            || !Provenance.Selection.IsValid || Provenance.Selection.Provider != ModelProviderIdentity.Ollama
            || Generation.Value <= 0 || SubmittedAt.Offset != TimeSpan.Zero
            || CanonicalUtf8Bytes is <= 0 or > ModelContextEnvelope.MaximumInputUtf8Bytes
            || Sources.IsDefaultOrEmpty || Sources.Length > ModelContextEnvelope.MaximumEvidenceItems
            || Sources.Any(source => source is null)
            || Sources.Select(source => (source.Request, source.Role)).Distinct().Count() != Sources.Length)
        { throw new InvalidDataException("The submitted local history context metadata is invalid or incomplete."); }
        Provenance.Request.RequestId.Validate();
        Provenance.Request.SessionId.Validate();
        Provenance.Request.TaskId.Validate();
        Provenance.Turn.Validate();
        ProviderProfileRules.ValidateTuple(Profile);
        if (Profile.Service != ProviderService.Local)
        { throw new InvalidDataException("Submitted history metadata supports only the original local profile."); }
        foreach (var source in Sources) { source.Validate(); }
        if (!Sources.SequenceEqual(Sources.OrderBy(source => source.Sequence).ThenBy(source => source.Role))
            || Sources.Any(source => source.Request == Provenance.Request.RequestId)
            || Sources.GroupBy(source => source.Sequence).Any(group => group.Select(source => source.Request).Distinct().Count() != 1))
        { throw new InvalidDataException("Submitted history metadata must retain exact distinct ordered source identities."); }
    }

    public bool Equals(SubmittedAnswerContext? other) => other is not null && Version == other.Version
        && Provenance == other.Provenance && Generation == other.Generation && Profile == other.Profile
        && SubmittedAt == other.SubmittedAt && CanonicalUtf8Bytes == other.CanonicalUtf8Bytes
        && Sources.SequenceEqual(other.Sources);

    public override int GetHashCode() => HashCode.Combine(Version, Provenance, Generation, Profile, SubmittedAt, CanonicalUtf8Bytes);
}
