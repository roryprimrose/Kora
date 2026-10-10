using System.Text;
using AwesomeAssertions;
using Kora.Core.Context;
using Kora.Core.Hosting;

namespace Kora.Core.UnitTests.Context;

public sealed class LocalFileRevisionRestoreTests
{
    [Theory]
    [InlineData("")]
    [InlineData("# café\r\n🙂 untrusted instructions\n")]
    public void RestorePreservesOriginalBomDigestIdsProjectionAndCitations(string text)
    {
        byte[] bytes = [0xef, 0xbb, 0xbf, .. Encoding.UTF8.GetBytes(text)];
        var original = Create(bytes);
        var restored = LocalFileRevision.Restore(original.Review, bytes, original.AdmittedAt, original.Reference);
        restored.Text.Should().Be(text);
        restored.Reference.Should().Be(original.Reference);
        restored.Review.Should().Be(original.Review);
        restored.AdmittedAt.Should().Be(original.AdmittedAt);
        restored.Digest.Should().NotBe(LocalFilePolicy.Digest(Encoding.UTF8.GetBytes(text)));
        var retrieval = new LocalFileLexicalRetrieval();
        var first = retrieval.Search(original, original.Reference, "café", DateTimeOffset.UnixEpoch, CancellationToken.None);
        var second = retrieval.Search(restored, restored.Reference, "café", DateTimeOffset.UnixEpoch, CancellationToken.None);
        second.Should().BeEquivalentTo(first);
    }

    [Theory]
    [InlineData("source")]
    [InlineData("revision")]
    [InlineData("item")]
    [InlineData("digest")]
    [InlineData("bom")]
    public void RestoreRejectsChangedLineageOrOriginalByteRepresentation(string invalid)
    {
        byte[] bytes = [0xef, 0xbb, 0xbf, .. Encoding.UTF8.GetBytes("same projection")];
        var original = Create(bytes);
        var reference = original.Reference;
        reference = invalid switch
        {
            "source" => reference with { SourceId = Guid.NewGuid() },
            "revision" => reference with { RevisionId = Guid.Empty },
            "item" => reference with { ItemId = Guid.Empty },
            "digest" => reference with { Digest = new string('a', 64) },
            _ => reference,
        };
        var restore = () => LocalFileRevision.Restore(original.Review,
            string.Equals(invalid, "bom", StringComparison.Ordinal) ? bytes.AsSpan(3) : bytes, original.AdmittedAt, reference);
        restore.Should().Throw<InvalidDataException>();
    }

    private static LocalFileRevision Create(byte[] bytes) =>
        new(new(Guid.NewGuid(), Guid.NewGuid(), HostRequest.Create(RequestOrigin.LocalUi),
            new(@"C:\Team\exact.md", "original-native-identity", bytes.Length, DateTimeOffset.UnixEpoch)),
            bytes, DateTimeOffset.UnixEpoch);
}
