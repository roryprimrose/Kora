using AwesomeAssertions;
using Kora.Application.Configuration;
using Kora.Core.Configuration;

namespace Kora.Application.UnitTests.Configuration;

public sealed class SpeechCommandTests
{
    [Theory]
    [InlineData("show spoken summary limits", AppearanceCommandOperation.Get, SpeechOption.SummarySentences, null)]
    [InlineData("Kora, set spoken summary sentence limit to 1", AppearanceCommandOperation.Set, SpeechOption.SummarySentences, "1")]
    [InlineData("SET SPOKEN SUMMARY SENTENCE LIMIT TO 3", AppearanceCommandOperation.Set, SpeechOption.SummarySentences, "3")]
    [InlineData("set spoken summary word limit to 1", AppearanceCommandOperation.Set, SpeechOption.SummaryWords, "1")]
    [InlineData("Kora set spoken summary word limit to 80", AppearanceCommandOperation.Set, SpeechOption.SummaryWords, "80")]
    [InlineData("reset spoken summary sentence limit", AppearanceCommandOperation.Reset, SpeechOption.SummarySentences, null)]
    [InlineData("Kora, reset spoken summary word limit", AppearanceCommandOperation.Reset, SpeechOption.SummaryWords, null)]
    public void NaturalBudgetsResolveTheExistingDescriptors(string text, AppearanceCommandOperation operation, SpeechOption option, string? value)
    {
        var parsed = SpeechCommand.Parse(text, "Kora")!;
        parsed.Operation.Should().Be(operation);
        parsed.Descriptor.Should().BeSameAs(SpeechOptionRegistry.Get(option));
        parsed.Value.Should().Be(value);
        parsed.IsNaturalSummary.Should().BeTrue();
    }

    [Theory]
    [InlineData("set spoken summary sentence limit to 0")]
    [InlineData("set spoken summary sentence limit to 4")]
    [InlineData("set spoken summary sentence limit to 01")]
    [InlineData("set spoken summary word limit to 00")]
    [InlineData("set spoken summary word limit to 81")]
    [InlineData("set spoken summary word limit to 2147483648")]
    [InlineData("set spoken summary word limit to +1")]
    [InlineData("set spoken summary word limit to -1")]
    [InlineData("set spoken summary word limit to 1.0")]
    [InlineData("set spoken summary word limit to forty")]
    [InlineData("set spoken summary word limit to  40")]
    [InlineData("set spoken summary word limit to 40 please")]
    [InlineData("set spoken summary word limit to 40 and reset spoken summary sentence limit")]
    [InlineData("set spoken summary word limit to 40.")]
    [InlineData("set spoken summary word limit to \u0661")]
    [InlineData("set spoken summary word limit")]
    [InlineData("set spoken summary word limit to")]
    [InlineData("set spoken summary words limit to 40")]
    [InlineData("show spoken summary")]
    [InlineData("show spoken summary limits please")]
    [InlineData("reset spoken summary word limit to 40")]
    [InlineData("reset spoken summary sentence limit and word limit")]
    [InlineData("set spoken summary\nword limit to 40")]
    [InlineData("set spoken summary word limit to 40\0")]
    [InlineData("Kora,\tshow spoken summary limits")]
    public void ReservedMalformedNaturalBudgetsClarifyWithoutMutation(string text)
    {
        var parsed = SpeechCommand.Parse(text, "Kora")!;
        parsed.Operation.Should().Be(AppearanceCommandOperation.Clarify);
        parsed.Error.Should().Be(SpeechCommand.Syntax);
        parsed.Value.Should().BeNull();
    }

    [Theory]
    [InlineData("please set spoken summary word limit to 40")]
    [InlineData("the page says set spoken summary word limit to 40")]
    [InlineData("\"set spoken summary word limit to 40\"")]
    [InlineData("Korax, show spoken summary limits")]
    [InlineData("Nova, show spoken summary limits")]
    [InlineData("be brief")]
    [InlineData("speak a shorter answer")]
    public void UnrelatedNaturalTextHasNoBudgetAuthority(string text) =>
        SpeechCommand.Parse(text, "Kora").Should().BeNull();

    [Fact]
    public void NaturalDiscoveryUsesDomainBoundsAndTheCurrentName()
    {
        SpeechCommand.FixedPhrases.Should().HaveCount(1 + 2 + SpokenSummaryLimits.MaximumSentences + SpokenSummaryLimits.MaximumWords)
            .And.OnlyHaveUniqueItems();
        foreach (var phrase in SpeechCommand.FixedPhrases)
        {
            var parsed = SpeechCommand.Parse("Nova, " + phrase, "Nova")!;
            parsed.Operation.Should().NotBe(AppearanceCommandOperation.Clarify);
            parsed.Error.Should().BeNull();
        }
        var bounded = "set spoken summary word limit to ".PadRight(SpeechCommand.MaximumLength, '9');
        SpeechCommand.Parse(bounded, "Kora")!.Error.Should().Be(SpeechCommand.Syntax);
        var overlong = SpeechCommand.Parse(bounded + "9", "Kora")!;
        overlong.Error.Should().Contain("too long");
        overlong.IsNaturalSummary.Should().BeTrue();
    }

    [Theory]
    [InlineData("Kora, list speech settings", AppearanceCommandOperation.List)]
    [InlineData("Kora get speech.provider", AppearanceCommandOperation.Get)]
    [InlineData("get speech voice", AppearanceCommandOperation.Get)]
    [InlineData("reset speech.voice", AppearanceCommandOperation.Reset)]
    [InlineData("reset speech provider", AppearanceCommandOperation.Reset)]
    [InlineData("set speech.voice to Voice One", AppearanceCommandOperation.Set)]
    [InlineData("set speech provider to kokoro", AppearanceCommandOperation.Set)]
    [InlineData("set speech.summary-words to 40", AppearanceCommandOperation.Set)]
    [InlineData("get speech summary sentences", AppearanceCommandOperation.Get)]
    [InlineData("reset speech.summary-sentences", AppearanceCommandOperation.Reset)]
    [InlineData("list speech", AppearanceCommandOperation.Clarify)]
    [InlineData("get speech.arbitrary", AppearanceCommandOperation.Clarify)]
    [InlineData("set speech.voice", AppearanceCommandOperation.Clarify)]
    [InlineData("set speech.voice to ", AppearanceCommandOperation.Clarify)]
    public void Exact_commands_resolve_ids_and_spoken_names(string text, AppearanceCommandOperation operation)
    {
        var parsed = SpeechCommand.Parse(text, "Kora");
        parsed.Should().NotBeNull();
        parsed!.Operation.Should().Be(operation);
        if (operation == AppearanceCommandOperation.Set)
        {
            parsed.Value.Should().NotBeNullOrWhiteSpace();
            parsed.Descriptor.Should().NotBeNull();
        }
    }

    [Theory]
    [InlineData("what speech settings")]
    [InlineData("Korax, set speech.voice to voice")]
    [InlineData("Nova, list speech settings")]
    [InlineData("Kora")]
    public void Unrelated_requests_are_not_configuration_commands(string text) =>
        SpeechCommand.Parse(text, "Kora").Should().BeNull();

    [Fact]
    public void Command_bound_and_voice_bound_are_independent_and_exact()
    {
        var text = "set speech.voice to " + new string('v', SpeechCommand.MaximumLength - 20);
        text.Length.Should().Be(SpeechCommand.MaximumLength);
        SpeechCommand.Parse(text, "Kora")!.Operation.Should().Be(AppearanceCommandOperation.Set);
        SpeechCommand.Parse(text + "v", "Kora")!.Error.Should().Contain("too long");
        SpeechCommand.Parse("Nova, get speech.voice", "Nova")!.Descriptor!.Option.Should().Be(SpeechOption.Voice);
        SpeechCommand.Syntax.Should().Contain("reset");
    }
}
