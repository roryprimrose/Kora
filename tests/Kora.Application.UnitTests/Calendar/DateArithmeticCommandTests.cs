using AwesomeAssertions;
using Kora.Application.Calendar;

namespace Kora.Application.UnitTests.Calendar;

public sealed class DateArithmeticCommandTests
{
    [Theory]
    [InlineData("date help", "Kora", DateArithmeticOperation.Help)]
    [InlineData("DATE HELP", "Kora", DateArithmeticOperation.Help)]
    [InlineData("date help", "Date", DateArithmeticOperation.Help)]
    [InlineData("Date date help", "Date", DateArithmeticOperation.Help)]
    [InlineData("Date, date help", "Date", DateArithmeticOperation.Help)]
    [InlineData("Date Help, date help", "Date Help", DateArithmeticOperation.Help)]
    [InlineData("Date Help date help", "Date Help", DateArithmeticOperation.Help)]
    [InlineData("Date Add date help", "Date Add", DateArithmeticOperation.Help)]
    [InlineData("What Time, date help", "What Time", DateArithmeticOperation.Help)]
    [InlineData("Kora, date difference 2000-02-28 2000-03-01", "Kora", DateArithmeticOperation.Difference)]
    [InlineData("Kora date add 2000-02-29 -1", "Kora", DateArithmeticOperation.Add)]
    [InlineData("date add 2000-02-29 0", "Date", DateArithmeticOperation.Add)]
    public void Exact_commands_survive_activation_name_collisions(string text, string name, DateArithmeticOperation operation)
    {
        DateArithmeticCommand.Parse(text, name)!.Operation.Should().Be(operation);
    }

    [Theory]
    [InlineData("date")]
    [InlineData("date help!")]
    [InlineData("date.help")]
    [InlineData("date\tadd 2000-02-29 1")]
    [InlineData("date add 2000-02-29 +0")]
    [InlineData("date add 2000-02-29 +1")]
    [InlineData("date add 2000-02-29 -0")]
    [InlineData("date add 1900-02-29 1")]
    [InlineData("date add 2000-02-29 01")]
    [InlineData("date add 2000-02-29 3652059")]
    [InlineData("date add 2000-02-29 1 trailing")]
    [InlineData("date difference 2000-02-29 today")]
    [InlineData("date difference 2000-02-29 2000-03-01 inclusive")]
    [InlineData("date  help")]
    [InlineData("date Help ")]
    [InlineData(" date help")]
    [InlineData("date\u00a0help")]
    [InlineData("date\u0000help")]
    [InlineData("Kora,date help")]
    [InlineData("Kora: date help")]
    [InlineData("Kora; date help")]
    [InlineData("Kora,\tdate help")]
    [InlineData("Kora,  date help")]
    [InlineData(" Kora, date help")]
    [InlineData("Kora, date help\n")]
    [InlineData("date: add")]
    public void Reserved_malformed_inputs_fail_closed(string text)
    {
        var parsed = DateArithmeticCommand.Parse(text, "Kora");
        parsed!.Operation.Should().Be(DateArithmeticOperation.Invalid);
        parsed.Error.Should().NotBeNullOrEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("help")]
    [InlineData("update help")]
    [InlineData("dates help")]
    [InlineData("datebook help")]
    [InlineData("Kora help")]
    [InlineData("Kora, help")]
    [InlineData("Korat date help")]
    [InlineData("OldName, date help")]
    public void Unrelated_text_is_not_a_date_command(string text) =>
        DateArithmeticCommand.Parse(text, "Kora").Should().BeNull();

    [Fact]
    public void Input_length_is_bounded_and_null_is_explicit()
    {
        DateArithmeticCommand.Parse("date " + new string('x', 256), "Kora")!.Operation.Should().Be(DateArithmeticOperation.Invalid);
        var parse = () => DateArithmeticCommand.Parse(null!, "Kora");
        parse.Should().Throw<ArgumentNullException>();
    }
}
