using AwesomeAssertions;
using Kora.Core.Maintenance;

namespace Kora.Core.UnitTests.Maintenance;

public sealed class MaintenanceCommandParserTests
{
    [Theory]
    [InlineData("maintenance status", MaintenanceCommand.Status)]
    [InlineData("maintenance review", MaintenanceCommand.Review)]
    [InlineData("maintenance snooze", MaintenanceCommand.Snooze)]
    [InlineData("  Kora, maintenance status  ", MaintenanceCommand.Status)]
    [InlineData("Kora maintenance review", MaintenanceCommand.Review)]
    [InlineData("KORA\tmaintenance snooze", MaintenanceCommand.Snooze)]
    [InlineData("maintenance status?", MaintenanceCommand.Invalid)]
    [InlineData("maintenance status now", MaintenanceCommand.Invalid)]
    [InlineData("maintenance review https://example.com", MaintenanceCommand.Invalid)]
    [InlineData("maintenance snooze security approval", MaintenanceCommand.Invalid)]
    [InlineData("maintenance check", MaintenanceCommand.Invalid)]
    [InlineData("maintenance open", MaintenanceCommand.Invalid)]
    [InlineData("maintenancelookalike", MaintenanceCommand.Invalid)]
    public void Complete_exact_grammar_has_no_network_or_external_target(string text, MaintenanceCommand expected) =>
        MaintenanceCommandParser.Parse(text, "Kora").Should().Be(expected);

    [Theory]
    [InlineData("Kora")]
    [InlineData("Korax maintenance status")]
    [InlineData("please maintenance status")]
    [InlineData("answer maintenance status")]
    [InlineData("open maintenance")]
    public void Unrelated_text_is_not_a_maintenance_command(string text) =>
        MaintenanceCommandParser.Parse(text, "Kora").Should().BeNull();

    [Fact]
    public void Original_complete_input_and_output_are_bounded_without_truncation()
    {
        var atLimit = "maintenance status".PadLeft(MaintenanceCommandParser.MaximumLength);
        MaintenanceCommandParser.Parse(atLimit, "Kora").Should().Be(MaintenanceCommand.Status);
        MaintenanceCommandParser.Parse(" " + atLimit, "Kora").Should().Be(MaintenanceCommand.Invalid);
        MaintenanceCommandParser.Parse("Kora, " + atLimit, "Kora").Should().Be(MaintenanceCommand.Invalid);
        MaintenanceCommandParser.Parse("Echo, maintenance review", "Echo").Should().Be(MaintenanceCommand.Review);
        MaintenanceCommandParser.FixedPhrases.Should().HaveCount(3);
        MaintenanceCommandParser.Syntax.Should().Contain("no check");
        var output = new string('x', MaintenanceCommandParser.MaximumOutputLength);
        MaintenanceCommandParser.BoundOutput(output).Should().Be(output);
        var oversized = () => MaintenanceCommandParser.BoundOutput(output + "x");
        oversized.Should().Throw<InvalidDataException>();
    }
}
