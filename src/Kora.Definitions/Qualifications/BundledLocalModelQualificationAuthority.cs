using Kora.Core.Providers;

namespace Kora.Definitions.Qualifications;

/// <summary>Only release-reviewed, digest-pinned bundled attestations are trusted. No candidate is accepted in this build.</summary>
public sealed class BundledLocalModelQualificationAuthority : ILocalModelQualificationAuthority
{
    private readonly IReadOnlyDictionary<string, string> pins;
    private readonly Func<string, Stream?> open;

    public BundledLocalModelQualificationAuthority()
        : this(new Dictionary<string, string>(StringComparer.Ordinal),
            typeof(BundledLocalModelQualificationAuthority).Assembly.GetManifestResourceStream)
    { }

    internal BundledLocalModelQualificationAuthority(IReadOnlyDictionary<string, string> pins, Func<string, Stream?> open)
    {
        // Adding an attestation here is an enablement decision, not the outcome of user consent or local tests.
        this.pins = new Dictionary<string, string>(pins, StringComparer.Ordinal);
        this.open = open;
    }

    public async Task<IReadOnlyList<LocalModelQualificationEvidence>> Read(CancellationToken cancellationToken)
    {
        var result = new List<LocalModelQualificationEvidence>();
        foreach (var pin in pins)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await using var stream = open(pin.Key)
                ?? throw new InvalidDataException("A release-pinned qualification attestation is missing from the build.");
            var bytes = new byte[LocalModelQualificationEvidence.MaximumBytes + 1];
            var count = 0;
            while (count < bytes.Length)
            {
                var read = await stream.ReadAsync(bytes.AsMemory(count), cancellationToken).ConfigureAwait(false);
                if (read == 0) { break; }
                count += read;
            }
            result.Add(LocalModelQualificationEvidence.Read(bytes.AsMemory(0, count), pin.Value));
        }
        cancellationToken.ThrowIfCancellationRequested();
        if (result.Select(item => item.ProfileId).Distinct().Count() != result.Count)
        { throw new InvalidDataException("Release qualification profile identities must be unique."); }
        return result.AsReadOnly();
    }
}