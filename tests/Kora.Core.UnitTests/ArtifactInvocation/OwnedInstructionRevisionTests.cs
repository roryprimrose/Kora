using AwesomeAssertions;

using Kora.Core.Artifacts;

namespace Kora.Core.UnitTests.ArtifactInvocation;

public sealed class OwnedInstructionRevisionTests
{
    [Fact]
    public void AcceptsOnlyCompleteOrdinalLowercaseNativeEncodingWithoutRepair()
    {
        ((Action)(() => OwnedInstructionRevision.Validate(null!))).Should().Throw<ArgumentNullException>();
        foreach (var character in "0123456789abcdef")
        {
            var revision = new string(character, 64) + "." + new string(character, 48) + "." + new string(character, 48) + "." + new string(character, 64);
            OwnedInstructionRevision.Validate(revision).Should().BeSameAs(revision);
            foreach (var invalid in new[] { "", " ", revision[..^1], revision + "a", " " + revision, revision + "\n", revision.Replace('.', '-') })
            {
                ((Action)(() => OwnedInstructionRevision.Validate(invalid))).Should().Throw<InvalidDataException>();
            }
            for (var index = 0; index < revision.Length; index++)
            {
                foreach (var replacement in "/:A`g \0\ud800")
                {
                    var invalid = revision[..index] + replacement + revision[(index + 1)..];
                    ((Action)(() => OwnedInstructionRevision.Validate(invalid))).Should().Throw<InvalidDataException>();
                }
            }
        }
    }
}
