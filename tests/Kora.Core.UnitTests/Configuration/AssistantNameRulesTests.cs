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

    [Theory]
    [InlineData(" Kora ", new[] { "Kora" })]
    [InlineData("KORA", new[] { "KORA" })]
    [InlineData("Nova", new[] { "Nova", "Kora" })]
    [InlineData("Kora Bot", new[] { "Kora Bot", "Kora" })]
    [InlineData("Ari", new[] { "Kora", "Ari" })]
    public void GetActivationNames_adds_only_the_fixed_default_name_longest_first(string configured, string[] expected)
    {
        AssistantNameRules.GetActivationNames(configured).Should().Equal(expected);
    }

    [Theory]
    [InlineData("Kora, open settings", "Nova", "Kora")]
    [InlineData("nova open settings", "Nova", "Nova")]
    [InlineData("Kora Bot, open settings", "Kora Bot", "Kora Bot")]
    [InlineData("Kora Botany", "Kora Bot", "Kora")]
    [InlineData("Kora", "Nova", "Kora")]
    [InlineData("Koral, open settings", "Nova", "Nova")]
    [InlineData("Aria, open settings", "Nova", "Nova")]
    [InlineData("Kora's settings", "Kora", "Kora")]
    public void ResolveActivationName_selects_the_accepted_leading_name_or_the_configured_name(
        string text, string configured, string expected)
    {
        AssistantNameRules.ResolveActivationName(text, configured).Should().Be(expected);
    }

    [Fact]
    public void Activation_helpers_reject_missing_values_and_invalid_names()
    {
        ((Action)(() => AssistantNameRules.ResolveActivationName(null!, "Nova"))).Should().Throw<ArgumentNullException>();
        ((Action)(() => AssistantNameRules.WithActivationNames(null!, "Nova"))).Should().Throw<ArgumentNullException>();
        ((Action)(() => AssistantNameRules.GetActivationNames("Nova!"))).Should().Throw<ArgumentException>();
    }

    [Fact]
    public void WithActivationNames_expands_bare_and_each_accepted_prefix()
    {
        AssistantNameRules.WithActivationNames(["open settings"], "Nova").Should()
            .Equal("open settings", "Nova open settings", "Kora open settings");
        AssistantNameRules.WithActivationNames(["open settings"], "Kora").Should()
            .Equal("open settings", "Kora open settings");
    }
}
