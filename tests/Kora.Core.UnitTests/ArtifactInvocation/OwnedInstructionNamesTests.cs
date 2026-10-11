using AwesomeAssertions;

using Kora.Core.Artifacts;

namespace Kora.Core.UnitTests.Artifacts;

public sealed class OwnedInstructionNamesTests
{
    [Theory]
    [InlineData("example")]
    [InlineData("code-9")]
    [InlineData("Legacy.Name 9")]
    [InlineData("规范")]
    public void BoundedReadNamesPreserveActualLegacyMemberIdentity(string name) =>
        OwnedInstructionNames.ReadName(name).Should().Be(name);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("ending.")]
    [InlineData("ending ")]
    [InlineData("bad\0name")]
    [InlineData("bad/name")]
    [InlineData("bad\\name")]
    [InlineData("bad:name")]
    [InlineData("bad*name")]
    [InlineData("bad?name")]
    [InlineData("bad\"name")]
    [InlineData("bad<name")]
    [InlineData("bad>name")]
    [InlineData("bad|name")]
    public void ReadNamesRejectPathsAliasesAndControlText(string? name) =>
        ((Action)(() => OwnedInstructionNames.ReadName(name!))).Should().Throw<InvalidDataException>();

    [Theory]
    [InlineData("-begin")]
    [InlineData("ending-")]
    [InlineData("Legacy")]
    [InlineData("legacy.name")]
    [InlineData("legacy name")]
    [InlineData("con")]
    [InlineData("prn")]
    [InlineData("aux")]
    [InlineData("nul")]
    [InlineData("com1")]
    [InlineData("lpt1")]
    public void ReadableOriginalNamesDoNotImplicitlyAuthorizeCanonicalWrites(string name) =>
        ((Action)(() => OwnedInstructionNames.WriteName(name))).Should().Throw<InvalidDataException>();

    [Fact]
    public void NativeNameBoundsAndCanonicalIdsAreAuthoritative()
    {
        OwnedInstructionNames.WriteName("code-19").Should().Be("code-19");
        OwnedInstructionNames.WriteName(new string('a', 64)).Should().HaveLength(64);
        ((Action)(() => OwnedInstructionNames.WriteName(new string('a', 65)))).Should().Throw<InvalidDataException>();
        ((Action)(() => OwnedInstructionNames.ReadName(new string('a', 129)))).Should().Throw<InvalidDataException>();
    }
}
