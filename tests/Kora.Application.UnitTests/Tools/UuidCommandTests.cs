using AwesomeAssertions;
using Kora.Application.Tools;

namespace Kora.Application.UnitTests.Tools;

public sealed class UuidCommandTests
{
    [Theory]
    [InlineData("uuid generate", "Kora", UuidOperation.Generate)]
    [InlineData("Kora, uuid generate", "Kora", UuidOperation.Generate)]
    [InlineData("Nova uuid help", "Nova", UuidOperation.Help)]
    [InlineData("uuid generate", "uuid", UuidOperation.Generate)]
    [InlineData("uuid, uuid generate", "uuid", UuidOperation.Generate)]
    [InlineData("uuid help", "uuid help", UuidOperation.Help)]
    [InlineData("uuid inspect uuid generate", "uuid inspect", UuidOperation.Generate)]
    [InlineData("Kora: uuid help", "Kora", UuidOperation.Help)]
    [InlineData("uuid inspect 12345678-9abc-4def-8123-456789abcdef", "Kora", UuidOperation.Inspect)]
    public void ParsesExactCommandsAndCurrentNameCollisions(string text, string name, UuidOperation operation) =>
        UuidCommand.Parse(text, name)!.Operation.Should().Be(operation);

    [Theory]
    [InlineData("uuid")]
    [InlineData("UUID generate")]
    [InlineData("uuid generate ")]
    [InlineData(" uuid generate")]
    [InlineData("uuid\ngenerate")]
    [InlineData("uuid  generate")]
    [InlineData("uuid generate 2")]
    [InlineData("uuid help!")]
    [InlineData("uuid inspect")]
    [InlineData("uuid inspect 00000000-0000-0000-0000-000000000000")]
    [InlineData("uuid inspect 12345678-9abc-4def-8123-456789abcdeF")]
    [InlineData("uuid-generate")]
    [InlineData("Kora, uuid inspect nope")]
    [InlineData("Kora,\tuuid generate")]
    [InlineData("Kora\tuuid generate")]
    [InlineData("Kora\0uuid generate")]
    [InlineData("\0uuid generate")]
    [InlineData("Kora\0\0uuid generate")]
    [InlineData(" Kora, uuid generate")]
    [InlineData("uuid inspect 12345678-9abc-4def-8123-456789abcdef ")]
    public void ReservesMalformedFamily(string text) =>
        UuidCommand.Parse(text, "Kora")!.Operation.Should().Be(UuidOperation.Invalid);

    [Fact]
    public void BoundsInputAndLeavesUnrelatedCommandsAlone()
    {
        UuidCommand.Parse("uuid " + new string('x', 10000), "Kora")!.Operation.Should().Be(UuidOperation.Invalid);
        UuidCommand.Parse("Nova, uuid generate", "Kora").Should().BeNull();
        UuidCommand.Parse("choose blue", "Kora").Should().BeNull();
        UuidCommand.Parse("Kora", "Kora").Should().BeNull();
        UuidCommand.Parse("", "Kora").Should().BeNull();
        UuidCommand.FixedPhrases.Should().HaveCount(3);
        var invalid = () => UuidCommand.Parse(null!, "Kora");
        invalid.Should().Throw<ArgumentNullException>();
        UuidCommand.Parse("uuid help", "uuid")!.ActivationPrefixed.Should().BeFalse();
        UuidCommand.Parse("uuid, uuid help", "uuid")!.ActivationPrefixed.Should().BeTrue();
        UuidCommand.Parse("uuid inspect " + new string('a', 200), "Kora")!.Operation.Should().Be(UuidOperation.Invalid);
    }
}
