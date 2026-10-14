using System.Text;

using AwesomeAssertions;

using Kora.Core.Artifacts;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Application.UnitTests.Definitions;

public sealed partial class InstructionEditorTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ComparisonAtExactByteBoundIsCompleteAndOneMoreByteIsRejected(bool bom)
    {
        await using var f = new Fixture();
        const string header = "---\nkora-format: 1\nname: example\ndescription: Test\nattribution: Original\n---\n";
        var text = header + new string('a', 65536 - Encoding.UTF8.GetByteCount(header) - (bom ? 3 : 0));
        var original = new OwnedInstructionSnapshot("example", "aaa", "bbb",
            ArtifactFileDocument.Parse(ArtifactFileDocument.Encode(text, bom), ArtifactKind.Instruction, "kora-profile", "example"));
        var complete = await f.Actions.CompareDraft("example", original, text, NullLogger.Instance, f.Eligible, CancellationToken.None);
        complete.RawByteCount.Should().Be(65536);
        complete.Draft.Should().Be(text);
        complete.ExactBytes.Should().BeTrue();
        complete.Validation.ProposedByteCount.Should().Be(65536);
        var over = () => f.Actions.CompareDraft("example", original, text + "x", NullLogger.Instance, f.Eligible, CancellationToken.None);
        await over.Should().ThrowAsync<InvalidDataException>();
        f.Files.Reads.Should().Be(0); f.Files.Writes.Should().Be(0);
    }
}
