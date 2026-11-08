using AwesomeAssertions;
using Kora.Application.Configuration;
using Kora.Core.Presentation;

namespace Kora.Application.UnitTests.Configuration;

public sealed class MarkdownDisplayCommandTests
{
    [Theory]
    [InlineData("get responses.markdown-display", MarkdownDisplayCommand.OperationType.Inspect, null)]
    [InlineData("status responses.markdown-display", MarkdownDisplayCommand.OperationType.Inspect, null)]
    [InlineData("set responses.markdown-display to Rendered", MarkdownDisplayCommand.OperationType.Review, MarkdownDisplayMode.Rendered)]
    [InlineData("set responses.markdown-display to Source", MarkdownDisplayCommand.OperationType.Review, MarkdownDisplayMode.Source)]
    [InlineData("reset responses.markdown-display", MarkdownDisplayCommand.OperationType.Reset, null)]
    [InlineData("review markdown display change", MarkdownDisplayCommand.OperationType.Review, null)]
    [InlineData("confirm markdown display change", MarkdownDisplayCommand.OperationType.Confirm, null)]
    [InlineData("cancel markdown display change", MarkdownDisplayCommand.OperationType.Cancel, null)]
    public void WholeOriginalAndActivatedGrammar(string phrase, MarkdownDisplayCommand.OperationType operation, MarkdownDisplayMode? value)
    {
        MarkdownDisplayCommand.FixedPhrases.Should().Contain(phrase);
        foreach (var input in new[] { phrase, phrase.ToUpperInvariant(), "Ada, " + phrase, "Ada " + phrase, "  " + phrase + "  " })
        { MarkdownDisplayCommand.Parse(input, "Ada").Should().Be(new MarkdownDisplayCommand(operation, value)); }
    }

    [Theory]
    [InlineData("set responses.markdown-display to Source and open all results")]
    [InlineData("set responses.markdown-display to HTML")]
    [InlineData("confirm markdown display changes")]
    [InlineData("cancel markdown display change now")]
    [InlineData("review markdown display something")]
    [InlineData("get responses.markdown-display\n")]
    [InlineData("Old, set responses.markdown-display to Source")]
    public void RelatedMalformedInputIsConsumedNotSentToModel(string input) =>
        MarkdownDisplayCommand.Parse(input, "Ada")!.Operation.Should().Be(MarkdownDisplayCommand.OperationType.Invalid);

    [Theory]
    [InlineData("tell me about Markdown")]
    [InlineData("Adaline, help")]
    [InlineData("Ada")]
    public void UnrelatedInputIsNotASetting(string input) => MarkdownDisplayCommand.Parse(input, "Ada").Should().BeNull();

    [Fact]
    public void OversizedInputIsInvalid() =>
        MarkdownDisplayCommand.Parse(new string(' ', 17000) + "get responses.markdown-display", "Ada")!.Operation
            .Should().Be(MarkdownDisplayCommand.OperationType.Invalid);
}
