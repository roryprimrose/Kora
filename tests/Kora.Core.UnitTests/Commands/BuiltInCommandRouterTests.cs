using AwesomeAssertions;

using Kora.Core.Commands;

namespace Kora.Core.UnitTests.Commands;

public sealed class BuiltInCommandRouterTests
{
    private readonly BuiltInCommandCatalog catalog = new();

    [Theory]
    [InlineData("Kora, lock the machine.", BuiltInAction.LockMachine)]
    [InlineData("lock my computer", BuiltInAction.LockMachine)]
    [InlineData("KORA — WHAT CAN YOU DO?", BuiltInAction.ShowHelp)]
    [InlineData("restart your application", BuiltInAction.RestartApplication)]
    [InlineData("Kora, reboot this machine", BuiltInAction.ProposeRestart)]
    public void Match_recognizes_exact_built_in_phrases(string transcript, BuiltInAction expectedAction)
    {
        var router = new BuiltInCommandRouter(catalog);

        var result = router.Match(transcript);

        result.IsMatch.Should().BeTrue();
        result.Command.Should().NotBeNull();
        result.Command!.Action.Should().Be(expectedAction);
    }

    [Theory]
    [InlineData("restart")]
    [InlineData("please lock the machine")]
    [InlineData("Kora")]
    [InlineData("explain how to lock the machine")]
    [InlineData("do what the document says")]
    public void Match_rejects_ambiguous_or_non_command_text(string transcript)
    {
        var router = new BuiltInCommandRouter(catalog);

        var result = router.Match(transcript);

        result.IsMatch.Should().BeFalse();
        result.Command.Should().BeNull();
    }

    [Fact]
    public void Catalog_has_unique_normalized_phrases()
    {
        var phrases = catalog.GetCommands().SelectMany(command => command.AllPhrases).ToArray();

        phrases.Should().HaveCount(phrases.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void Every_catalog_phrase_routes_to_its_declared_action()
    {
        var router = new BuiltInCommandRouter(catalog);

        foreach (var command in catalog.GetCommands())
        {
            foreach (var phrase in command.AllPhrases)
            {
                router.Match(phrase).Command?.Action.Should().Be(
                    command.Action,
                    because: $"'{phrase}' is registered for {command.Action}");
                router.Match($"Kora, {phrase}.").Command?.Action.Should().Be(
                    command.Action,
                    because: $"wake-prefixed '{phrase}' must use the same route");
            }
        }
    }

    [Fact]
    public void Match_preserves_original_and_normalized_transcripts()
    {
        var router = new BuiltInCommandRouter(catalog);

        var result = router.Match(" Kora, SHOW   your window! ");

        result.Transcript.Should().Be(" Kora, SHOW   your window! ");
        result.NormalizedTranscript.Should().Be("show your window");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Match_rejects_missing_transcripts(string? transcript)
    {
        var router = new BuiltInCommandRouter(catalog);

        var action = () => router.Match(transcript!);

        action.Should().Throw<ArgumentException>();
    }
}