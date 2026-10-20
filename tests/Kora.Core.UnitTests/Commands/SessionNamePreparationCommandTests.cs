using AwesomeAssertions;

using Kora.Core.Commands;
using Kora.Core.Hosting;

namespace Kora.Core.UnitTests.Commands;

public sealed class SessionNamePreparationCommandTests
{
    private const string Id = "12345678-1234-1234-abcd-123456789012";
    private const string Prefix = "session name prepare " + Id + " ";

    [Theory]
    [InlineData("Hello  world")]
    [InlineData("Café 東京 😀")]
    [InlineData("a \"quoted\" name")]
    [InlineData("same name as another session")]
    public void ExactNamesUseAuthoritativeQuoteDecoderWithoutChangingLiteralBytes(string name)
    {
        var literal = "\"" + name.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
        foreach (var activation in new[] { "", "Kora, ", "  Kora " })
        {
            var parsed = SessionNamePreparationCommand.Parse(activation + Prefix + literal, "Kora")!;
            parsed.IsValid.Should().BeTrue();
            parsed.IsHelp.Should().BeFalse();
            parsed.SessionId.Should().Be(Guid.Parse(Id));
            parsed.Name!.Value.Should().Be(name);
            parsed.ToString().Should().NotContain(name);
        }
    }

    [Fact]
    public void MaximumNameAndEscapedQuoteBoundsAreExactlySessionNameBounds()
    {
        foreach (var name in new[] { new string('x', SessionName.MaximumScalars),
            string.Concat(Enumerable.Repeat("😀", SessionName.MaximumScalars)), new string('"', SessionName.MaximumScalars) })
        {
            var parsed = SessionNamePreparationCommand.Parse(Prefix + "\"" + name.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"", "Kora")!;
            parsed.Name!.Value.Should().Be(name);
        }
        SessionNamePreparationCommand.Parse(Prefix + "\"" + new string('x', SessionName.MaximumScalars + 1) + "\"", "Kora")!
            .IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData("\"\"")]
    [InlineData("\" \"")]
    [InlineData("\" leading\"")]
    [InlineData("\"trailing \"")]
    [InlineData("\"a\u200bb\"")]
    [InlineData("\"a\nb\"")]
    [InlineData("\"a\u2028b\"")]
    [InlineData("\"Cafe\u0301\"")]
    [InlineData("unquoted")]
    [InlineData("\"unterminated")]
    [InlineData("\"name\" extra")]
    [InlineData("\"name\" \"two\"")]
    public void InvalidNamesAndMalformedQuotesStayReserved(string literal)
    {
        var parsed = SessionNamePreparationCommand.Parse(Prefix + literal, "Kora")!;
        parsed.IsValid.Should().BeFalse();
        parsed.Name.Should().BeNull();
    }

    [Theory]
    [InlineData("session name")]
    [InlineData("session name prepare")]
    [InlineData("session name confirm anything")]
    [InlineData("session name rename selected \"x\"")]
    [InlineData("session name prepare selected \"x\"")]
    [InlineData("session name prepare 00000000-0000-0000-0000-000000000000 \"x\"")]
    [InlineData("session name prepare 12345678-1234-1234-ABCD-123456789012 \"x\"")]
    [InlineData("session name prepare {12345678-1234-1234-abcd-123456789012} \"x\"")]
    [InlineData("session name prepare 1234567812341234abcd123456789012 \"x\"")]
    [InlineData("session name prepare 12345678-1234-1234-abcd-123456789012")]
    [InlineData("session\tname help")]
    public void ReservedRouteCannotFallThroughOrRepairIdentity(string input) =>
        SessionNamePreparationCommand.Parse(input, "Kora")!.IsValid.Should().BeFalse();

    [Theory]
    [InlineData("")]
    [InlineData("session")]
    [InlineData("session rename")]
    [InlineData("session namespace")]
    [InlineData("please session name prepare")]
    [InlineData("not a command")]
    [InlineData("Kora")]
    [InlineData("Koran, session name help")]
    [InlineData("Kora: session name help")]
    [InlineData("Kora, unrelated")]
    public void UnrelatedRoutesAreNotCaptured(string input) =>
        SessionNamePreparationCommand.Parse(input, "Kora").Should().BeNull();

    [Fact]
    public void HelpAndOversizedOrBrokenUnicodeRequestsNeverExposeNames()
    {
        var help = SessionNamePreparationCommand.Parse("Kora, session name help", "Kora")!;
        help.IsValid.Should().BeTrue();
        help.IsHelp.Should().BeTrue();
        help.Name.Should().BeNull();
        SessionNamePreparationCommand.Parse("Session, " + Prefix + "\"Exact literal\"", "Session")!.Name!.Value.Should().Be("Exact literal");
        SessionNamePreparationCommand.Parse(Prefix + "\"Exact literal\"", "Session")!.Name!.Value.Should().Be("Exact literal");
        SessionNamePreparationCommand.Parse(Prefix + "\"" + new string('x', 1025) + "\"", "Kora")!.IsValid.Should().BeFalse();
        SessionNamePreparationCommand.Parse(Prefix + "\"\ud800\"", "Kora")!.IsValid.Should().BeFalse();
        SessionNamePreparationCommand.Syntax.Should().NotContain(Id);
    }
}
