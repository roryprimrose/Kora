using AwesomeAssertions;
using Kora.Application.Skills;

namespace Kora.Application.UnitTests.Skills;

public sealed class NamedSkillCommandTests
{
    [Theory]
    [InlineData("", null)]
    [InlineData("ordinary text", null)]
    [InlineData("use skillful prose", null)]
    [InlineData("skills", null)]
    [InlineData("use skill", "Invalid")]
    [InlineData("use skill\nLock", "Invalid")]
    [InlineData("skill", "Invalid")]
    [InlineData("skill anything", "Invalid")]
    [InlineData("skill close", "Close")]
    [InlineData("skill close extra", "Invalid")]
    [InlineData("skill choice 0", "Invalid")]
    [InlineData("skill choice 01", "Invalid")]
    [InlineData("skill choice -1", "Invalid")]
    [InlineData("skill choice 161", "Invalid")]
    [InlineData("skill choice 2147483648", "Invalid")]
    [InlineData("skill choice 1 ", "Invalid")]
    [InlineData("skill choice 1", "Choice")]
    [InlineData("Nova, skill choice 160", "Choice")]
    [InlineData("use skill Lock", "Review")]
    [InlineData("Nova, USE SKILL Daily review", "Review")]
    public void OnlyReservedOriginalUserOperationsAreParsed(string text, string? action) =>
        NamedSkillCommand.Parse(text, "Nova")?.Action.Should().Be(action);

    [Theory]
    [InlineData("  Daily review  ")]
    [InlineData("e\u0301")]
    [InlineData("\u00e9")]
    [InlineData("owned skill invoke")]
    public void ReadableNameRemainsLiteralWithoutNestedParsing(string name)
    {
        var command = NamedSkillCommand.Parse("Nova, use skill " + name, "Nova")!;
        command.Action.Should().Be("Review");
        command.Name.Should().Be(name);
        NamedSkillCommand.Parse("skill choice 12", "Nova")!.Choice.Should().Be(12);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(128, true)]
    [InlineData(129, false)]
    public void ExistingNameBoundIsNotTruncated(int length, bool valid) =>
        NamedSkillCommand.Parse("use skill " + new string('x', length), "Nova")!.Action.Should().Be(valid ? "Review" : "Invalid");

    [Theory]
    [InlineData(" ")]
    [InlineData("x\u0000")]
    [InlineData("x\u202e")]
    [InlineData("x\u2028")]
    public void InvalidUnicodeOrControlNamesNeverEscapeTheReservedNamespace(string name) =>
        NamedSkillCommand.Parse("use skill " + name, "Nova")!.Action.Should().Be("Invalid");

    [Fact]
    public void UnpairedSurrogateIsRejectedWithoutAttributeSerializationReplacement() =>
        NamedSkillCommand.Parse("use skill " + new string((char)0xD800, 1), "Nova")!.Action.Should().Be("Invalid");
}
