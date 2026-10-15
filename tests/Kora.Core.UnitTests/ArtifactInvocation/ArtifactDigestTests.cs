using AwesomeAssertions;

using Kora.Core.Artifacts;

namespace Kora.Core.UnitTests.Artifacts;

public sealed class ArtifactDigestTests
{
    [Theory]
    [InlineData("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef")]
    [InlineData("ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff")]
    [InlineData("0000000000000000000000000000000000000000000000000000000000000000")]
    public void ValidDigestPreservesExactValueAndConstructorContract(string value)
    {
        ArtifactDigest.Validate(value).Should().BeSameAs(value);
        Definition(value).Digest.Should().BeSameAs(value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    [InlineData("\n")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("gaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData(" aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa ")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\n")]
    [InlineData("éaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public void InvalidDigestAndConstructorKeepSameExceptionSemanticsWithoutRepair(string? value)
    {
        var validation = ((Action)(() => ArtifactDigest.Validate(value!))).Should().Throw<ArgumentException>().Which;
        var construction = ((Action)(() => Definition(value!))).Should().Throw<ArgumentException>().Which;
        validation.ParamName.Should().Be("value");
        construction.GetType().Should().Be(validation.GetType());
        construction.ParamName.Should().Be(validation.ParamName);
        construction.Message.Should().Be(validation.Message);
    }

    private static ArtifactDefinition Definition(string digest) =>
        new("owned.example", ArtifactKind.Instruction, "Example", "Description", "example",
            ["example"], "kora-profile", "1", digest, "Private content");
}
