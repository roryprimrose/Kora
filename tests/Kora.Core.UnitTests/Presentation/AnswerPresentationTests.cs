using AwesomeAssertions;

using Kora.Core.Presentation;

namespace Kora.Core.UnitTests.Presentation;

public sealed class AnswerPresentationTests
{
    [Fact]
    public void ExplicitBriefPreservesTheCompleteOriginalAnswerAndNormalizesOnlyTheProjection()
    {
        const string body = "Brief response:\r\nA concise summary.\r\n\r\nDetailed response:\r\n# Full answer\r\n\r\n```mermaid\r\ngraph TD\r\n A-->B\r\n```";
        var result = AnswerPresentation.From(body);
        result.Brief.Should().Be("A concise summary.");
        result.HasDetails.Should().BeTrue();
        body.Should().Contain("\r\n```mermaid\r\n");
        AnswerPresentation.ModelPolicy.Should().Contain("Kora owns");
        AnswerPresentation.Offer.Should().Contain("detailed response");
    }

    [Theory]
    [InlineData("short answer", false)]
    [InlineData("one\n\ntwo\n\nthree", false)]
    [InlineData("one\n\ntwo\n\nthree\n\nfour", true)]
    [InlineData("```mermaid\ngraph TD\n A-->B\n```", true)]
    [InlineData("| A | B |\n|---|---|\n| 1 | 2 |", true)]
    [InlineData("Brief response:\nmissing separator", false)]
    [InlineData("Brief response:\n\n\nDetailed response:\nfull", false)]
    [InlineData("Brief response:\nsummary\n\nDetailed response:\n ", false)]
    public void ShortAnswersRemainCompleteAndComplexAnswersNeverGetAnInventedSummary(string body, bool complex)
    {
        var result = AnswerPresentation.From(body);
        result.HasDetails.Should().Be(complex);
        result.Brief.Should().Be(complex ? AnswerPresentation.Available : body);
    }

    [Theory]
    [InlineData(120, false)]
    [InlineData(121, true)]
    public void WordThresholdIsMeasuredExactly(int words, bool complex) =>
        AnswerPresentation.From(string.Join(' ', Enumerable.Repeat("word", words))).HasDetails.Should().Be(complex);

    [Theory]
    [InlineData(80, true)]
    [InlineData(81, false)]
    public void ExplicitBriefHasAnExactWordBudget(int words, bool accepted)
    {
        var brief = string.Join(' ', Enumerable.Repeat("word", words));
        var body = "Brief response:\n" + brief + "\n\nDetailed response:\ncomplete";
        AnswerPresentation.From(body).HasDetails.Should().Be(accepted);
    }

    [Theory]
    [InlineData(1024, true)]
    [InlineData(1025, false)]
    public void ExplicitBriefHasAnExactCharacterBudget(int characters, bool accepted) =>
        AnswerPresentation.From("Brief response:\n" + new string('x', characters)
            + "\n\nDetailed response:\ncomplete").HasDetails.Should().Be(accepted);

    [Fact]
    public void NullIsRejectedRatherThanAnEmptySuccess() =>
        ((Action)(() => AnswerPresentation.From(null!))).Should().Throw<ArgumentNullException>();
}
