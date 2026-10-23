using System.Security.Cryptography;
using System.Text;

using Kora.Core.Context;

namespace Kora.Core.Artifacts;

public sealed class ProfileInstructionSnapshot
{
    public const string ReaderVersion = "kora-instruction-v1";
    public const int MaximumFreshnessChecks = 5;

    private ProfileInstructionSnapshot(Guid sourceId, LocalFileMetadata metadata, ArtifactFileDocument document)
    {
        SourceId = sourceId;
        Metadata = metadata;
        Document = document;
        Identity = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{sourceId:D}\n{metadata.CanonicalPath}\n{metadata.FileIdentity}\n{ReaderVersion}\n{document.Definition.Digest}")));
    }

    public Guid SourceId { get; }
    public LocalFileMetadata Metadata { get; }
    public ArtifactFileDocument Document { get; }
    public string Identity { get; }

    public static ProfileInstructionSnapshot Parse(Guid sourceId, LocalFileMetadata metadata, ReadOnlySpan<byte> bytes)
    {
        LocalFilePolicy.ValidatePath(metadata.CanonicalPath);
        const string extension = ".instructions.md";
        if (sourceId == Guid.Empty || string.IsNullOrWhiteSpace(metadata.FileIdentity)
            || metadata.ByteLength != bytes.Length
            || !metadata.CanonicalPath.EndsWith(extension, StringComparison.Ordinal))
        { throw new InvalidDataException("The exact instruction lease, canonical filename or byte length is invalid."); }
        var name = metadata.CanonicalPath[(metadata.CanonicalPath.LastIndexOf('\\') + 1)..^extension.Length];
        var document = ArtifactFileDocument.Parse(bytes, ArtifactKind.Instruction, "profile-instruction", name);
        document.RequireProfileInstructionSource();
        return new(sourceId, metadata, document);
    }
}
