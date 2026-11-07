using System.Collections.ObjectModel;

namespace Kora.Core.Context;

public sealed class LocalFileLexicalIndex
{
    public const string Version = "lexical-index-v1";

    private LocalFileLexicalIndex(string digest, IReadOnlyList<Chunk> chunks)
    {
        Digest = digest;
        Chunks = chunks;
    }

    public string Digest { get; }
    public string PolicyVersion => LocalFileRetrievalPolicy.Version;
    public IReadOnlyList<Chunk> Chunks { get; }

    public static LocalFileLexicalIndex Build(LocalFileRevision file, CancellationToken token,
        LocalFileLexicalIndex? previous = null)
    {
        ArgumentNullException.ThrowIfNull(file);
        token.ThrowIfCancellationRequested();
        if (previous is not null && string.Equals(previous.Digest, file.Reference.Digest, StringComparison.Ordinal)) { return previous; }
        return new(file.Reference.Digest, LocalFileLexicalRetrieval.IndexChunks(file.Text, token));
    }

    public static LocalFileLexicalIndex Restore(LocalFileRevision file, string version, string policyVersion,
        string digest, IReadOnlyList<Chunk> chunks, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(chunks);
        token.ThrowIfCancellationRequested();
        if (!string.Equals(version, Version, StringComparison.Ordinal)
            || !string.Equals(policyVersion, LocalFileRetrievalPolicy.Version, StringComparison.Ordinal)
            || !string.Equals(digest, file.Reference.Digest, StringComparison.Ordinal))
        { throw new InvalidDataException("The lexical index version, policy or original-byte digest disagrees with its source."); }
        var expected = Build(file, token);
        if (chunks.Count != expected.Chunks.Count || chunks.Where((chunk, index) =>
            chunk is null || chunk.Start != expected.Chunks[index].Start || chunk.Length != expected.Chunks[index].Length
            || !string.Equals(chunk.Heading, expected.Chunks[index].Heading, StringComparison.Ordinal)
            || !chunk.Frequencies.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .SequenceEqual(expected.Chunks[index].Frequencies.OrderBy(pair => pair.Key, StringComparer.Ordinal))).Any())
        { throw new InvalidDataException("The lexical index does not describe the exact canonical source chunks and tokens."); }
        return expected;
    }
    public sealed class Chunk
    {
        public Chunk(int start, int length, string? heading, IReadOnlyDictionary<string, int> frequencies)
        {
            ArgumentNullException.ThrowIfNull(frequencies);
            Start = start;
            Length = length;
            Heading = heading;
            Frequencies = new ReadOnlyDictionary<string, int>(frequencies.ToDictionary(pair => pair.Key, pair => pair.Value,
                StringComparer.Ordinal));
        }

        public int Start { get; }
        public int Length { get; }
        public string? Heading { get; }
        public IReadOnlyDictionary<string, int> Frequencies { get; }
    }
}
