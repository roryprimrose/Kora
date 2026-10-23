using System.Security.Cryptography;
using System.Text.Json;
using System.Text;

namespace Kora.Core.Providers;

/// <summary>Release-authority attestation of exact-profile evidence, never an operator's qualification assertion.</summary>
public sealed class LocalModelQualificationEvidence
{
    public const string HostContract = "kora-ollama-native-v1";
    public const int MaximumBytes = 16384;
    private static readonly string[] RequiredGates =
    [
        "licence-identity", "quality-authority", "bounds", "cancellation-recovery",
        "resource-latency", "offline-egress", "integrated-host",
    ];

    private LocalModelQualificationEvidence(string digest, Guid profileId, long revision, string label,
        string runtimeVersion, string executableSha256, string model, string modelSha256,
        DateTimeOffset issuedAt, DateTimeOffset expiresAt)
    {
        Digest = digest;
        ProfileId = profileId;
        Revision = revision;
        Label = label;
        RuntimeVersion = runtimeVersion;
        ExecutableSha256 = executableSha256;
        Model = model;
        ModelSha256 = modelSha256;
        IssuedAt = issuedAt;
        ExpiresAt = expiresAt;
    }

    public string Digest { get; }
    public Guid ProfileId { get; }
    public long Revision { get; }
    public string Label { get; }
    public string RuntimeVersion { get; }
    public string ExecutableSha256 { get; }
    public string Model { get; }
    public string ModelSha256 { get; }
    public DateTimeOffset IssuedAt { get; }
    public DateTimeOffset ExpiresAt { get; }
    public bool IsCurrent(DateTimeOffset now) => IssuedAt <= now && now < ExpiresAt;

    /// <summary>
    /// Validates an exact release-pinned attestation and every required evidence record.
    /// The expected digest must come from the owning release authority, never the evidence or user input.
    /// </summary>
    public static LocalModelQualificationEvidence Read(ReadOnlyMemory<byte> bytes, string expectedDigest)
    {
        if (bytes.Length is 0 or > MaximumBytes || !IsHash(expectedDigest, 64)
            || !string.Equals(Convert.ToHexString(SHA256.HashData(bytes.Span)), expectedDigest, StringComparison.OrdinalIgnoreCase))
        { throw new InvalidDataException("Qualification evidence is not the exact release-authorized bytes."); }
        try
        {
            using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 8 });
            var root = document.RootElement;
            Fields(root, ["Format", "HostContract", "ProfileId", "Revision", "Label", "RuntimeVersion", "ExecutableSha256",
                "Model", "ModelSha256", "Reasoning", "Context", "IssuedAt", "ExpiresAt", "Proofs"]);
            if (root.GetProperty("Format").GetInt32() != 1
                || !string.Equals(Text(root, "HostContract"), HostContract, StringComparison.Ordinal)
                || !string.Equals(Text(root, "Reasoning"), "off", StringComparison.Ordinal)
                || !string.Equals(Text(root, "Context"), "bounded", StringComparison.Ordinal))
            { throw new InvalidDataException("Qualification does not cover the implemented host contract and exact options."); }
            var profile = root.GetProperty(nameof(ProfileId)).GetGuid();
            var revision = root.GetProperty(nameof(Revision)).GetInt64();
            var issued = root.GetProperty(nameof(IssuedAt)).GetDateTimeOffset();
            var expires = root.GetProperty(nameof(ExpiresAt)).GetDateTimeOffset();
            if (profile == Guid.Empty || revision <= 0 || expires <= issued)
            { throw new InvalidDataException("Qualification identity or validity is invalid."); }
            var executable = Text(root, "ExecutableSha256");
            var modelHash = Text(root, "ModelSha256");
            if (!IsHash(executable, 64) || !IsHash(modelHash, 64))
            { throw new InvalidDataException("Qualification requires exact runtime and model digests."); }
            var proofs = root.GetProperty("Proofs");
            if (proofs.ValueKind != JsonValueKind.Array || proofs.GetArrayLength() != RequiredGates.Length)
            { throw new InvalidDataException("Qualification must cover every required gate exactly once."); }
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var proof in proofs.EnumerateArray())
            {
                Fields(proof, ["Gate", "SourceRevision", "RunId", "ArtifactSha256", "Outcome"]);
                var gate = Text(proof, "Gate");
                if (!RequiredGates.Contains(gate, StringComparer.Ordinal) || !seen.Add(gate)
                    || !IsHash(Text(proof, "SourceRevision"), 40) || !IsHash(Text(proof, "ArtifactSha256"), 64)
                    || !string.Equals(Text(proof, "Outcome"), "passed", StringComparison.Ordinal))
                { throw new InvalidDataException("Qualification contains missing, duplicate, unproven or untraceable gates."); }
                ProviderProfileRules.ValidateId(Text(proof, "RunId"));
            }
            var model = Text(root, "Model");
            var runtime = Text(root, "RuntimeVersion");
            ProviderProfileRules.ValidateId(model);
            ProviderProfileRules.ValidateId(runtime);
            return new(expectedDigest.ToUpperInvariant(), profile, revision, Text(root, "Label"),
                runtime, executable, model, modelHash, issued, expires);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or FormatException)
        { throw new InvalidDataException("Qualification evidence is malformed.", exception); }
    }

    private static string Text(JsonElement value, string name)
    {
        var text = value.GetProperty(name).GetString();
        if (string.IsNullOrWhiteSpace(text) || Encoding.UTF8.GetByteCount(text) > 256 || text.Any(char.IsControl))
        { throw new InvalidDataException("Qualification fields require bounded inert text."); }
        return text;
    }

    internal static void Fields(JsonElement value, string[] expected)
    {
        if (value.ValueKind != JsonValueKind.Object) { throw new InvalidDataException("An evidence object is required."); }
        var names = value.EnumerateObject().Select(property => property.Name).ToArray();
        if (names.Length != expected.Length || names.Distinct(StringComparer.Ordinal).Count() != names.Length
            || names.Any(name => !expected.Contains(name, StringComparer.Ordinal)))
        { throw new InvalidDataException("Unknown, duplicate or missing evidence fields are not admitted."); }
    }

    public static bool IsHash(string? value, int length) => value is not null && value.Length == length && value.All(char.IsAsciiHexDigit);
}