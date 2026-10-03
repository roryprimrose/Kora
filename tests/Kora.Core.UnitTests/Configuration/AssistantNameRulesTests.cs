using AwesomeAssertions;

using Kora.Core.Configuration;

namespace Kora.Core.UnitTests.Configuration;

public sealed class AssistantNameRulesTests
{
    [Theory]
    [InlineData(" Kora ", "Kora")]
    [InlineData("Nova Prime", "Nova Prime")]
    [InlineData("  Nova\tPrime  2 ", "Nova Prime 2")]
    [InlineData("O'Neil", "O'Neil")]
    [InlineData("Mary-Jane", "Mary-Jane")]
    [InlineData("ノヴァ", "ノヴァ")]
    public void Normalize_accepts_supported_names(string value, string expected)
    {
        AssistantNameRules.Normalize(value).Should().Be(expected);
    }

    [Theory]
    [InlineData("", "blank")]
    [InlineData("   ", "blank")]
    [InlineData("one two three four", "more than 3 words")]
    [InlineData("Nova!", "only letters")]
    [InlineData("---", "at least one letter or number")]
    public void Normalize_rejects_unsupported_names(string value, string expectedMessage)
    {
        var action = () => AssistantNameRules.Normalize(value);

        action.Should().Throw<ArgumentException>()
            .WithMessage($"*{expectedMessage}*");
    }

    [Fact]
    public void Normalize_rejects_null()
    {
        var action = () => AssistantNameRules.Normalize(null!);

        action.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Normalize_rejects_names_over_the_maximum_length()
    {
        var action = () => AssistantNameRules.Normalize(
            new string('a', AssistantNameRules.MaximumLength + 1));

        action.Should().Throw<ArgumentException>()
            .WithMessage($"*{AssistantNameRules.MaximumLength}*");
    }
}
