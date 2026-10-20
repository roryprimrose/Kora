using AwesomeAssertions;
using Kora.Core.Context;

namespace Kora.Core.UnitTests.Context;

public sealed class PreparedLocalSearchCommandTests
{
    [Theory]
    [InlineData("prepare file search for ", false, false)]
    [InlineData("PREPARE FOLDER SEARCH FOR ", true, false)]
    [InlineData("Nova-Prime, prepare file search for ", false, true)]
    [InlineData("nova-prime prepare folder search for ", true, true)]
    public void RawFinalQuotedLiteralPreservesCasePunctuationUnicodeSpacesAndEscapedQuotes(string prefix, bool folder, bool activated)
    {
        const string query = "  MiXeD e\u0301界🙂 + \"verbatim\" -- C:\\test  ";
        var command = PreparedLocalSearchCommand.Parse(prefix + "\"" + query.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"", "Nova-Prime")!;
        command.IsValid.Should().BeTrue();
        command.IsFolder.Should().Be(folder);
        command.IsActivated.Should().Be(activated);
        command.Query.Should().Be(query);
        command.ToString().Should().NotContain(query);
    }

    [Theory]
    [InlineData("search file for \"private\"")]
    [InlineData("inspect folder")]
    [InlineData("prepared file search for \"query\"")]
    [InlineData("Kora, prepare file search for \"query\"")]
    [InlineData("Nova-PrimeX prepare file search for \"query\"")]
    [InlineData("Nova, prepare file search for \"query\"")]
    [InlineData("Nova-Prime")]
    [InlineData("prepare the shutdown")]
    [InlineData("prepare a computer reboot sometime")]
    [InlineData("hello")]
    [InlineData("")]
    public void UnrelatedAndOldOrPartialAssistantNamesAreNotAliases(string input) =>
        PreparedLocalSearchCommand.Parse(input, "Nova-Prime").Should().BeNull();

    [Theory]
    [InlineData("prepare")]
    [InlineData("prepare f")]
    [InlineData("prepare fo")]
    [InlineData("prepare file")]
    [InlineData("prepare file search")]
    [InlineData("prepare folder search for")]
    [InlineData("prepare files search for \"query\"")]
    [InlineData("prepare-file-search-for \"query\"")]
    [InlineData("prepare\nfile search for \"query\"")]
    [InlineData("prepare\tfile search for \"query\"")]
    [InlineData("prepare file search for query")]
    [InlineData("prepare file search for \"")]
    [InlineData("prepare file search for \"\"")]
    [InlineData("prepare file search for \"   \"")]
    [InlineData("prepare file search for \"🙂---\"")]
    [InlineData("prepare file search for \"query\" tail")]
    [InlineData("prepare file search for \"query\" \"second\"")]
    [InlineData("prepare file search for \"unclosed")]
    [InlineData("prepare file search for \"query\"\"")]
    [InlineData("prepare file search for \"que\0ry\"")]
    [InlineData("prepare file search for \"que\u202ery\"")]
    [InlineData("prepare file search for \"que\u2028ry\"")]
    public void ReservedMalformedInputIsExplicitAndDoesNotPublishPrivateData(string input)
    {
        var command = PreparedLocalSearchCommand.Parse(input, "Kora")!;
        command.IsValid.Should().BeFalse();
        command.Query.Should().BeNull();
    }

    [Fact]
    public void InvalidUnicodeIsRejectedBeforeAnyHandoff()
    {
        var input = "prepare file search for \"que" + new string('\ud800', 1) + "ry\"";
        PreparedLocalSearchCommand.Parse(input, "Kora")!.IsValid.Should().BeFalse();
    }

    [Fact]
    public void UsesAuthoritativeLexicalLimitsWithoutTruncationOrNormalization()
    {
        static bool Valid(string query) => PreparedLocalSearchCommand.Parse("prepare file search for \"" + query + "\"", "Kora")!.IsValid;
        Valid(string.Join(' ', Enumerable.Repeat(new string('a', 63), 4))).Should().BeTrue();
        Valid(string.Join(' ', Enumerable.Repeat(new string('a', 63), 4)) + "a").Should().BeTrue();
        Valid(string.Join(' ', Enumerable.Repeat(new string('a', 63), 4)) + "aa").Should().BeFalse();
        Valid(new string('a', 64)).Should().BeTrue();
        Valid(new string('a', 65)).Should().BeFalse();
        Valid(string.Join(' ', Enumerable.Range(1, 32))).Should().BeTrue();
        Valid(string.Join(' ', Enumerable.Range(1, 33))).Should().BeFalse();
        var bytes = string.Join(' ', Enumerable.Repeat(new string('\u754c', 56), 3));
        Valid(bytes + "abc de").Should().BeTrue();
        Valid(bytes + "abc def").Should().BeFalse();
        Valid(new string('a', 700)).Should().BeFalse();
        PreparedLocalSearchCommand.DiscoveryPhrases.Should().Equal("prepare file search", "prepare folder search");
    }
}
