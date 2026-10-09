using AwesomeAssertions;

using Kora.Core.Interaction;

namespace Kora.Core.UnitTests.Interaction;

public sealed class LocalEventCommandTests
{
    private const string Id = "b85065b9-4de3-4eaa-a46e-bdb88a0fd70a";
    [Theory]
    [InlineData("status", LocalEventOperation.Status)]
    [InlineData("review", LocalEventOperation.Review)]
    [InlineData("dismiss", LocalEventOperation.Dismiss)]
    [InlineData("defer", LocalEventOperation.Defer)]
    public void Typed_and_current_name_activated_commands_resolve_only_exact_ids_and_revisions(string verb, LocalEventOperation operation)
    {
        var text = "event " + verb + " " + Id + " 42";
        var expected = new LocalEventCommand(operation, Guid.Parse(Id), 42);
        LocalEventCommand.Parse(text, "Kora").Should().Be(expected);
        LocalEventCommand.Parse("Aster, " + text, "Aster").Should().Be(expected);
        LocalEventCommand.Parse("Aster\t" + text, "Aster").Should().Be(expected);
        LocalEventCommand.Parse("Kora, " + text, "Aster").Should().BeNull();
        LocalEventCommand.FixedPhrases.Should().Contain("event " + verb);
        LocalEventCommand.Syntax.Should().Contain("exact-event-id");
    }
    [Theory]
    [InlineData("event status")]
    [InlineData("event review friendly-name 1")]
    [InlineData("event review b85065b94de34eaaa46ebdb88a0fd70a 1")]
    [InlineData("event status 00000000-0000-0000-0000-000000000000 1")]
    [InlineData("event status " + Id + " 0")]
    [InlineData("event status " + Id + " -1")]
    [InlineData("event status " + Id + " 01")]
    [InlineData("event status " + Id + " +1")]
    [InlineData("event status " + Id + " 9223372036854775808")]
    [InlineData("event open " + Id + " 1")]
    [InlineData("events status " + Id + " 1")]
    [InlineData("event status " + Id + " 1 appended")]
    public void Reserved_invalid_input_is_not_model_text_or_ambient_authority(string input) =>
        LocalEventCommand.Parse(input, "Kora")!.Operation.Should().Be(LocalEventOperation.Invalid);

    [Fact]
    public void Long_reserved_input_is_rejected_and_unrelated_text_is_not_an_event()
    {
        LocalEventCommand.Parse("event " + new string('a', 256), "Kora")!.Operation.Should().Be(LocalEventOperation.Invalid);
        LocalEventCommand.Parse("ordinary user text", "Kora").Should().BeNull();
        LocalEventCommand.Parse(" Kora ", "Kora").Should().BeNull();
        LocalEventCommand.Parse("KoraX event status " + Id + " 1", "Kora").Should().BeNull();
    }
}
