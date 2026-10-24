using System.Buffers.Binary;
using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;

namespace Kora.Core.Skills;

/// <summary>Complete immutable content, with a domain-separated length-framed aggregate revision.</summary>
public sealed class SharedSkillReferenceBundle
{
    public sealed class ReferenceContent
    {
        private readonly byte[] bytes;

        internal ReferenceContent(string path, byte[] content)
        {
            RelativePath = path;
            bytes = content.ToArray();
            if (bytes.Length is 0 or > SharedSkillReferenceDeclaration.MaximumReferenceBytes)
            { throw new InvalidDataException("A declared reference exceeds its complete byte bound."); }
            try { Text = new UTF8Encoding(false, true).GetString(bytes); }
            catch (DecoderFallbackException exception)
            { throw new InvalidDataException("A declared reference is not strict UTF-8.", exception); }
            var reasons = new List<string>();
            SharedSkillSnapshot.ValidateInstructionText(Text, null, reasons);
            if (reasons.Count != 0)
            { throw new InvalidDataException("References must be inert Markdown without nested references or executable content."); }
        }

        public string RelativePath { get; }
        public string Text { get; }
        public ReadOnlySpan<byte> Bytes => bytes;
    }

    private SharedSkillReferenceBundle(ReadOnlyCollection<ReferenceContent> references, string digest)
    { References = references; RevisionDigest = digest; }

    public ReadOnlyCollection<ReferenceContent> References { get; }
    public string RevisionDigest { get; }

    public static SharedSkillReferenceBundle Create(SharedSkillSnapshot primary, IReadOnlyDictionary<string, byte[]> contents)
    {
        ArgumentNullException.ThrowIfNull(primary);
        ArgumentNullException.ThrowIfNull(contents);
        var declaration = primary.ReferenceDeclaration;
        if (declaration is null || !primary.IsReferenceCandidate || contents.Count != declaration.Paths.Count
            || !contents.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(declaration.Paths))
        { throw new InvalidDataException("The complete exact declared set is required, without extra or omitted references."); }
        var references = declaration.Paths.Select(path => new ReferenceContent(path, contents[path])).ToList().AsReadOnly();
        if (primary.Bytes.Length + references.Sum(item => item.Bytes.Length) > SharedSkillReferenceDeclaration.MaximumBundleBytes)
        { throw new InvalidDataException("The complete primary and references exceed the bundle byte bound."); }
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, Encoding.UTF8.GetBytes("Kora.shared-skill.markdown-bundle.v2"));
        Append(hash, Encoding.UTF8.GetBytes(primary.RelativeFile));
        Append(hash, primary.Bytes);
        foreach (var reference in references)
        {
            Append(hash, Encoding.UTF8.GetBytes(reference.RelativePath));
            Append(hash, reference.Bytes);
        }
        return new(references, Convert.ToHexStringLower(hash.GetHashAndReset()));
    }

    private static void Append(IncrementalHash hash, ReadOnlySpan<byte> value)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, value.Length);
        hash.AppendData(length);
        hash.AppendData(value);
    }
}
