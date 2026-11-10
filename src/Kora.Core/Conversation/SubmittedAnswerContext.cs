using System.Collections.Immutable;
using System.Text.Json.Serialization;

using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Memory;
using Kora.Core.Providers;
using Kora.Core.Conversation.ReviewedContext;

namespace Kora.Core.Conversation;

/// <summary>Host adapter handoff evidence, not proof of provider delivery, processing or reasoning.</summary>
public sealed record SubmittedAnswerContext(int Version, ModelTurnProvenance Provenance,
    HostRevision Generation, ProviderProfileTuple Profile, DateTimeOffset SubmittedAt,
    int CanonicalUtf8Bytes, ImmutableArray<SubmittedHistorySource> Sources)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public ImmutableArray<SubmittedMemorySource> MemorySources { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public ImmutableArray<SubmittedProfileMemorySource> ProfileMemorySources { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SubmittedInstructionSource? InstructionSource { get; init; }
    [JsonIgnore]
    public int SourceCount => Sources.Length + (MemorySources.IsDefault ? 0 : MemorySources.Length)
        + (ProfileMemorySources.IsDefault ? 0 : ProfileMemorySources.Length) + (InstructionSource is null ? 0 : 1);

    /// <summary>Called by the host at adapter handoff, never from provider/source output.</summary>
    public static SubmittedAnswerContext? Capture(ModelTurnProvenance provenance, HostRevision generation,
        ProviderProfileTuple? profile, ModelContextEnvelope context, ReadOnlySpan<byte> canonical, DateTimeOffset submittedAt)
    {
        var history = string.Equals(context.SystemPolicy, ConversationHistoryContext.Policy, StringComparison.Ordinal)
            && context.Evidence.All(item => item.HistorySource is not null);
        var memory = string.Equals(context.SystemPolicy, SessionMemoryContextPolicy.Policy, StringComparison.Ordinal)
            && context.Evidence.All(item => item.MemorySource is not null);
        var profileMemory = string.Equals(context.SystemPolicy, SessionMemoryContextPolicy.Policy, StringComparison.Ordinal)
            && context.Evidence.All(item => item.ProfileMemorySource is not null);
        var mixed = string.Equals(context.SystemPolicy, MixedConversationContext.Policy, StringComparison.Ordinal)
            && context.Evidence.Any(item => item.InstructionSource is not null);
        if (profile is null || provenance.Selection.Provider != ModelProviderIdentity.Ollama
            || context.ToolMode != ModelToolMode.None || context.Evidence.IsEmpty || !history && !memory && !profileMemory && !mixed)
        { return null; }
        if (context.Request != provenance.Request || !context.Serialize().AsSpan().SequenceEqual(canonical))
        { throw new InvalidDataException("The canonical admitted context changed before adapter handoff."); }
        if (mixed) { ValidateMixedEvidence(context); }
        var snapshot = new SubmittedAnswerContext(mixed ? 4 : history ? 1 : memory ? 2 : 3,
            provenance, generation, profile, submittedAt, canonical.Length,
            history || mixed ? [.. context.Evidence.Where(item => item.HistorySource is not null).Select(item => item.HistorySource!)] : [])
        {
            MemorySources = memory || mixed
                ? [.. context.Evidence.Where(item => item.MemorySource is not null).Select(item => item.MemorySource!)] : default,
            ProfileMemorySources = profileMemory ? [.. context.Evidence.Select(item => item.ProfileMemorySource!)] : default,
            InstructionSource = mixed ? context.Evidence.Single(item => item.InstructionSource is not null).InstructionSource : null,
        };
        snapshot.Validate();
        return snapshot;
    }

    public void Validate()
    {
        if (Version is not (1 or 2 or 3 or 4) || Provenance?.Request is null || Provenance.Selection is null
            || !Provenance.Selection.IsValid || Provenance.Selection.Provider != ModelProviderIdentity.Ollama
            || Generation.Value <= 0 || SubmittedAt.Offset != TimeSpan.Zero
            || CanonicalUtf8Bytes is <= 0 or > ModelContextEnvelope.MaximumInputUtf8Bytes
            || Sources.IsDefault || (Version == 1
                ? Sources.IsEmpty || !MemorySources.IsDefaultOrEmpty || !ProfileMemorySources.IsDefaultOrEmpty
                : Version == 2 ? !Sources.IsEmpty || MemorySources.IsDefaultOrEmpty || !ProfileMemorySources.IsDefaultOrEmpty
                : Version == 3 ? !Sources.IsEmpty || !MemorySources.IsDefaultOrEmpty || ProfileMemorySources.IsDefaultOrEmpty
                : Sources.IsEmpty || MemorySources.IsDefaultOrEmpty || !ProfileMemorySources.IsDefaultOrEmpty)
            || (Version == 4) != (InstructionSource is not null)
            || SourceCount > ModelContextEnvelope.MaximumEvidenceItems
            || Sources.Any(source => source is null)
            || Sources.Select(source => (source.Request, source.Role)).Distinct().Count() != Sources.Length)
        { throw new InvalidDataException("The submitted local history context metadata is invalid or incomplete."); }
        Provenance.Request.RequestId.Validate();
        Provenance.Request.SessionId.Validate();
        Provenance.Request.TaskId.Validate();
        Provenance.Turn.Validate();
        ProviderProfileRules.ValidateTuple(Profile);
        InstructionSource?.Validate();
        if (Profile.Service != ProviderService.Local)
        { throw new InvalidDataException("Submitted history metadata supports only the original local profile."); }
        foreach (var source in Sources) { source.Validate(); }
        if (!MemorySources.IsDefault)
        {
            foreach (var source in MemorySources)
            {
                if (source is null) { throw new InvalidDataException("A submitted memory reference is missing."); }
                source.Validate();
                if (source.Session != Provenance.Request.SessionId)
                { throw new InvalidDataException("Submitted memory references must belong to this exact answer's session."); }
            }
            if (MemorySources.Select(source => source.Id).Distinct().Count() != MemorySources.Length)
            { throw new InvalidDataException("Submitted memory references must retain distinct ordered identities."); }
        }
        if (!ProfileMemorySources.IsDefault)
        {
            foreach (var source in ProfileMemorySources)
            {
                if (source is null) { throw new InvalidDataException("A submitted profile memory reference is missing."); }
                source.Validate();
            }
            if (ProfileMemorySources.Select(source => source.Id).Distinct().Count() != ProfileMemorySources.Length
                || ProfileMemorySources.Select(source => source.Profile).Distinct().Count() > 1)
            { throw new InvalidDataException("Submitted profile memory references must retain distinct ordered identities in one profile."); }
        }
        if (!Sources.SequenceEqual(Sources.OrderBy(source => source.Sequence).ThenBy(source => source.Role))
            || Sources.Any(source => source.Request == Provenance.Request.RequestId)
            || Sources.GroupBy(source => source.Sequence).Any(group => group.Select(source => source.Request).Distinct().Count() != 1))
        { throw new InvalidDataException("Submitted history metadata must retain exact distinct ordered source identities."); }
    }

    private static void ValidateMixedEvidence(ModelContextEnvelope context)
    {
        var histories = context.Evidence.TakeWhile(item => item.HistorySource is not null).Count();
        if (histories == 0 || histories >= context.Evidence.Length - 1)
        { throw new InvalidDataException("Mixed submission requires complete history, one instruction and Session memories."); }
        for (var index = 0; index < context.Evidence.Length; index++)
        {
            var item = context.Evidence[index];
            if (item.Disclosure != ModelEvidenceDisclosure.LocalOnly || item.ProfileMemorySource is not null
                || (index < histories
                    ? item.InstructionSource is not null || item.MemorySource is not null
                    : index == histories
                        ? item.InstructionSource is null || item.MemorySource is not null
                            || item.InstructionSource.Reference != item.Id.Value || item.Revision.Value != 1
                        : item.MemorySource is null || item.HistorySource is not null || item.InstructionSource is not null))
            { throw new InvalidDataException("Mixed submission must preserve every exact ordered typed source without substitution."); }
        }
    }

    public bool Equals(SubmittedAnswerContext? other) => other is not null && Version == other.Version
        && Provenance == other.Provenance && Generation == other.Generation && Profile == other.Profile
        && SubmittedAt == other.SubmittedAt && CanonicalUtf8Bytes == other.CanonicalUtf8Bytes
        && Sources.SequenceEqual(other.Sources) && MemorySources.AsSpan().SequenceEqual(other.MemorySources.AsSpan())
        && ProfileMemorySources.AsSpan().SequenceEqual(other.ProfileMemorySources.AsSpan())
        && InstructionSource == other.InstructionSource;

    public override int GetHashCode() => HashCode.Combine(Version, Provenance, Generation, Profile, SubmittedAt, CanonicalUtf8Bytes);
}
