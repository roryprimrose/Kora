using AwesomeAssertions;
using Kora.Core.Providers;

namespace Kora.Core.UnitTests.Providers;

public sealed class ProviderProfilePreferencesTests
{
    private static ProviderProfileTuple Tuple(ProviderService service) => new(service, new("account"), new(1), new("model"), new("effort"), new("window"));

    [Fact]
    public void ExactRoundtripServiceSwitchAndResetPreserveOtherServicesWithoutDefaults()
    {
        var tuples = new[] { Tuple(ProviderService.Local), Tuple(ProviderService.Claude) };
        var preferences = new ProviderProfilePreferences(ProviderService.Claude, tuples);
        tuples[0] = Tuple(ProviderService.Copilot);
        preferences.Tuples[0].Service.Should().Be(ProviderService.Local);
        var parsed = ProviderProfilePreferences.Parse(preferences.Serialize());
        parsed.Serialize().Should().Be(preferences.Serialize());
        parsed.For(ProviderService.Copilot).Should().BeNull();
        parsed.Select(ProviderService.Codex).Tuples.Should().Equal(preferences.Tuples);
        var replaced = parsed.Select(Tuple(ProviderService.Claude) with { Context = new("different") });
        replaced.Service.Should().Be(ProviderService.Claude);
        replaced.For(ProviderService.Claude)!.Context.Value.Should().Be("different");
        replaced.For(ProviderService.Local).Should().Be(preferences.For(ProviderService.Local));
        replaced.Reset(ProviderService.Claude).Tuples.Should().ContainSingle().Which.Should().Be(preferences.For(ProviderService.Local));
        new ProviderProfilePreferences(ProviderService.Unset, []).Serialize().Should().Be("{\"Schema\":1,\"Service\":0,\"Tuples\":[]}");
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"Schema\":2,\"Service\":0,\"Tuples\":[]}")]
    [InlineData("{\"Schema\":1,\"Service\":99,\"Tuples\":[]}")]
    [InlineData("{\"Schema\":1,\"Service\":0,\"Tuples\":null}")]
    [InlineData("{\"Schema\":1,\"Service\":0,\"Tuples\":[null]}")]
    [InlineData("{\"Schema\":1,\"Schema\":1,\"Service\":0,\"Tuples\":[]}")]
    [InlineData("{\"Schema\":1,\"Service\":0,\"Tuples\":[],\"Unknown\":true}")]
    [InlineData("{\"Schema\":1,\"Service\":0,\"Tuples\":[{}]}")]
    [InlineData("{\"Schema\":1,\"Service\":1,\"Tuples\":[{\"Service\":1,\"Account\":{\"Value\":\"account\"},\"CatalogueRevision\":{\"Value\":0},\"Model\":{\"Value\":\"model\"},\"Reasoning\":{\"Value\":\"effort\"},\"Context\":{\"Value\":\"window\"}}]}")]
    [InlineData("{not-json}")]
    [InlineData(" {\"Schema\":1,\"Service\":0,\"Tuples\":[]}")]
    public void UnknownMalformedNoncanonicalOrIncompleteSavedStateNeverBecomesDefaults(string text)
    {
        FluentActions.Invoking(() => ProviderProfilePreferences.Parse(text)).Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void UnknownDuplicateAndOversizedPreferencesAreRejected()
    {
        FluentActions.Invoking(() => new ProviderProfilePreferences((ProviderService)99, [])).Should().Throw<InvalidDataException>();
        FluentActions.Invoking(() => new ProviderProfilePreferences(ProviderService.Local, [Tuple(ProviderService.Local), Tuple(ProviderService.Local)])).Should().Throw<InvalidDataException>();
        FluentActions.Invoking(() => new ProviderProfilePreferences(ProviderService.Local, Enumerable.Repeat(Tuple(ProviderService.Local), 5).ToArray())).Should().Throw<InvalidDataException>();
        FluentActions.Invoking(() => ProviderProfilePreferences.Parse(new string(' ', ProviderProfilePreferences.MaximumUtf8Bytes + 1))).Should().Throw<InvalidDataException>();
        FluentActions.Invoking(() => new ProviderProfilePreferences(ProviderService.Local, [Tuple(ProviderService.Unset)])).Should().Throw<InvalidDataException>();
    }
}
