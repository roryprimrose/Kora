using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using Kora.Core.Hosting;
using Kora.Core.Tools;

namespace Kora.Core.Dependencies;

/// <summary>Immutable, session-bound data; none of its text carries approval, destination or memory authority.</summary>
public sealed class ModelContextEnvelope
{
    public const int MaximumInputUtf8Bytes = 32768;
    public const int MaximumEvidenceItems = 16;
    public static int MaximumOutputUtf8Bytes => ReadOnlyCapabilityCatalog.Limits.MaximumOutputUtf8Bytes;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public ModelContextEnvelope(HostRequest request, string systemPolicy, string userRequest,
        IEnumerable<ModelContextEvidence> evidence, DateTimeOffset createdAt, DateTimeOffset expiresAt)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(systemPolicy);
        ArgumentNullException.ThrowIfNull(userRequest);
        ArgumentNullException.ThrowIfNull(evidence);
        if (expiresAt <= createdAt)
        {
            throw new InvalidDataException("A context envelope requires a finite positive validity interval.");
        }
        Request = request;
        SystemPolicy = systemPolicy;
        UserRequest = userRequest;
        CreatedAt = createdAt;
        ExpiresAt = expiresAt;
        Evidence = evidence.Take(MaximumEvidenceItems + 1).ToImmutableArray();
        if (Evidence.Length > MaximumEvidenceItems)
        {
            throw new InvalidDataException("The context evidence count exceeds the host limit.");
        }
        ValidateText(systemPolicy);
        ValidateText(userRequest);
        var ids = new HashSet<HostId<EvidenceIdentity>>();
        foreach (var item in Evidence)
        {
            if (item is null) { throw new InvalidDataException("A context evidence item is missing."); }
            item.Id.Validate();
            if (!ids.Add(item.Id) || item.Request != request || item.Revision.Value <= 0
                || item.Disclosure is not (ModelEvidenceDisclosure.LocalOnly or ModelEvidenceDisclosure.HostedEligible))
            {
                throw new InvalidDataException("Evidence must have unique, current, exact-request host provenance.");
            }
            ValidateText(item.Content);
        }
    }

    public HostRequest Request { get; }
    public string SystemPolicy { get; }
    public string UserRequest { get; }
    public ImmutableArray<ModelContextEvidence> Evidence { get; }
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset ExpiresAt { get; }

    public byte[] Serialize() => JsonSerializer.SerializeToUtf8Bytes(new
    {
        SystemPolicy,
        UserRequest,
        Evidence,
        Tools = ReadOnlyCapabilityCatalog.Descriptors,
        MaximumOutputUtf8Bytes,
    });

    private static void ValidateText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (StrictUtf8.GetByteCount(text) > MaximumInputUtf8Bytes)
        {
            throw new InvalidDataException("Context text exceeds the complete-envelope limit.");
        }
    }
}
