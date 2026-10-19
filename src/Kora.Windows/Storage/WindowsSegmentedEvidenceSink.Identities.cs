using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Kora.Windows.Storage;

public sealed partial class WindowsSegmentedEvidenceSink
{
    private const int IdentityFilterWords = 512 * 1024;
    private readonly ulong[] identityFilter = new ulong[IdentityFilterWords];

    private void PopulateIdentityFilter(CancellationToken cancellationToken = default)
    {
        var preceding = new List<Segment>();
        foreach (var segment in segments)
        {
            segment.Sink.VisitAdmissionEvidenceIds(id =>
            {
                if (MayContainIdentity(id) && preceding.Any(previous =>
                    previous.Sink.ContainsEvidence(id, initialAdmission: true, cancellationToken)))
                {
                    throw new InvalidDataException("An evidence identity is ambiguous across registered segments.");
                }
                RememberIdentity(id);
            }, cancellationToken);
            preceding.Add(segment);
        }
    }

    private void RequireUniqueEvidence(Guid[] ids)
    {
        if (ids.Distinct().Count() != ids.Length)
        {
            throw new InvalidDataException("An evidence batch repeats an admitted identity.");
        }
        foreach (var id in ids)
        {
            if (MayContainIdentity(id) && segments.Any(segment => segment.Sink.ContainsEvidence(id, CancellationToken.None)))
            {
                throw new InvalidDataException("The evidence identity is already committed in a registered segment.");
            }
        }
    }

    private static void IdentityBits(Guid id, Span<int> bits)
    {
        Span<byte> bytes = stackalloc byte[16];
        id.TryWriteBytes(bytes);
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(bytes, hash);
        for (var index = 0; index < bits.Length; index++)
        {
            bits[index] = (int)(BinaryPrimitives.ReadUInt32LittleEndian(hash[(index * 4)..])
                % (IdentityFilterWords * 64u));
        }
    }

    private bool MayContainIdentity(Guid id)
    {
        Span<int> bits = stackalloc int[4];
        IdentityBits(id, bits);
        foreach (var bit in bits)
        {
            if ((identityFilter[bit / 64] & (1UL << (bit % 64))) == 0) { return false; }
        }
        return true;
    }

    private void RememberIdentity(Guid id)
    {
        Span<int> bits = stackalloc int[4];
        IdentityBits(id, bits);
        foreach (var bit in bits) { identityFilter[bit / 64] |= 1UL << (bit % 64); }
    }
}
