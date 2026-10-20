using AwesomeAssertions;
using Kora.Core.Context;

namespace Kora.Core.UnitTests.Context;

public sealed class ClipboardFindCommandTests
{
    private const string Id = "12345678-abcd-4321-abcd-123456789abc";

    [Theory]
    [InlineData("clipboard find ", false)]
    [InlineData("CLIPBOARD FIND ", false)]
    [InlineData("Nova-Prime, clipboard find ", true)]
    [InlineData("nova-prime clipboard find ", true)]
    public void PreservesRawLiteralWithoutLexicalNormalizationOrQuoteDecoding(string prefix, bool activated)
    {
        const string literal = "  MiXeD e\u0301界🙂 .*? \"\" C:\\no-action  ";
        var command = ClipboardFindCommand.Parse(prefix + Id + " " + literal, "Nova-Prime")!;
        command.IsValid.Should().BeTrue();
        command.IsActivated.Should().Be(activated);
        command.SnapshotId.Should().Be(Guid.Parse(Id));
        command.Query.Should().Be(literal);
        command.ToString().Should().Be(nameof(ClipboardFindCommand));
    }

    [Theory]
    [InlineData("")]
    [InlineData("preview clipboard")]
    [InlineData("explain clipboard")]
    [InlineData("clipboards find")]
    [InlineData("Kora, clipboard find")]
    [InlineData("Nova-PrimeX clipboard find")]
    [InlineData("Nova-Prime")]
    [InlineData("Nova, clipboard find")]
    public void UnrelatedAndOldOrPartialAssistantNamesAreNotAliases(string text) =>
        ClipboardFindCommand.Parse(text, "Nova-Prime").Should().BeNull();

    [Theory]
    [InlineData("clipboard")]
    [InlineData("clipboard find")]
    [InlineData("clipboard find ")]
    [InlineData("clipboard find invalid PRIVATE")]
    [InlineData("clipboard-find " + Id + " PRIVATE")]
    [InlineData("clipboard\tfind " + Id + " PRIVATE")]
    [InlineData("clipboard find " + Id)]
    [InlineData("clipboard find " + Id + "\tPRIVATE")]
    [InlineData("clipboard find " + Id + "PRIVATE")]
    [InlineData("clipboard find {12345678-abcd-4321-abcd-123456789abc} PRIVATE")]
    [InlineData("clipboard find 12345678ABCD4321ABCD123456789ABC PRIVATE")]
    [InlineData("clipboard find 12345678-ABCD-4321-abcd-123456789abc PRIVATE")]
    [InlineData("clipboard find 12345678-abcd-4321-abcd-123456789abz PRIVATE")]
    [InlineData("clipboard find 00000000-0000-0000-0000-000000000000 PRIVATE")]
    [InlineData("clipboard find " + Id + " ")]
    [InlineData("clipboard find " + Id + " PRIVATE\n")]
    [InlineData("clipboard find " + Id + " PRIVATE\u202e")]
    [InlineData("clipboard find " + Id + " PRIVATE\u2028")]
    [InlineData("clipboard find " + Id + " PRIVATE\u2029")]
    public void MalformedReservedRoutesAreContentFreeRefusals(string text)
    {
        var command = ClipboardFindCommand.Parse(text, "Kora")!;
        command.IsValid.Should().BeFalse();
        command.Query.Should().BeNull();
        command.SnapshotId.Should().BeEmpty();
        command.ToString().Should().NotContain("PRIVATE");
    }

    [Fact]
    public void UsesExactSharedLiteralLimitsIncludingPunctuationWhitespaceAndUnicode()
    {
        static bool Valid(string literal) => ClipboardFindCommand.Parse("clipboard find " + Id + " " + literal, "Kora")!.IsValid;
        Valid("   ").Should().BeTrue();
        Valid("🙂.*?").Should().BeTrue();
        Valid(new string('a', 256)).Should().BeTrue();
        Valid(new string('a', 257)).Should().BeFalse();
        Valid(new string('a', 700)).Should().BeFalse();
        Valid(new string('界', 170) + "ab").Should().BeTrue();
        Valid(new string('界', 170) + "abc").Should().BeFalse();
        Valid(string.Concat(Enumerable.Repeat("🙂", 128))).Should().BeTrue();
        Valid("a" + new string('\ud800', 1)).Should().BeFalse();
        Valid("a" + new string('\udc00', 1)).Should().BeFalse();
        ClipboardFindCommand.Syntax.Should().Contain("clipboard find <snapshot-id> <literal-query>");
    }

    [Theory]
    [InlineData("Clipboard")]
    [InlineData("Clipboard Find")]
    public void DirectRouteIsNotMistakenForAnActivationNameThatOverlapsTheNamespace(string name)
    {
        var direct = "clipboard find " + Id + " PRIVATE";
        ClipboardFindCommand.Parse(direct, name)!.IsActivated.Should().BeFalse();
        ClipboardFindCommand.Parse(direct, name)!.IsValid.Should().BeTrue();
        var activated = ClipboardFindCommand.Parse(name + ", " + direct, name)!;
        activated.IsActivated.Should().BeTrue();
        activated.IsValid.Should().BeTrue();
    }
}
