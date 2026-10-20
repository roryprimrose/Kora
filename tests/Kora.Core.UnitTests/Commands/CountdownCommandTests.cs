using AwesomeAssertions;
using Kora.Core.Commands;

namespace Kora.Core.UnitTests.Commands;

public sealed class CountdownCommandTests
{
    [Theory]
    [InlineData("timer start 1", CountdownOperation.Start, 1)]
    [InlineData("timer start 86400", CountdownOperation.Start, 86400)]
    [InlineData("timer start 00060", CountdownOperation.Start, 60)]
    [InlineData("  TIMER START 42  ", CountdownOperation.Start, 42)]
    [InlineData("Nova, timer status", CountdownOperation.Status, 0)]
    [InlineData("Nova timer cancel", CountdownOperation.Cancel, 0)]
    [InlineData("timer help", CountdownOperation.Help, 0)]
    public void ExactGrammarAcceptsOnlyBoundedOperations(string input, CountdownOperation operation, int seconds)
    {
        var command = CountdownCommand.Parse(input, "Nova")!;
        command.Operation.Should().Be(operation);
        command.Seconds.Should().Be(seconds);
        command.IsActivated.Should().Be(input.StartsWith("Nova", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("timer")]
    [InlineData("timer start")]
    [InlineData("timer start ")]
    [InlineData("timer start 0")]
    [InlineData("timer start 86401")]
    [InlineData("timer start 2147483648")]
    [InlineData("timer start 9999999999999999999999999")]
    [InlineData("timer start -1")]
    [InlineData("timer start +1")]
    [InlineData("timer start 1.0")]
    [InlineData("timer start 1e2")]
    [InlineData("timer start 1,000")]
    [InlineData("timer start \u0661")]
    [InlineData("timer start \uff11")]
    [InlineData("timer start 1 seconds")]
    [InlineData("timer  status")]
    [InlineData("timer\tstatus")]
    [InlineData("timer status\n")]
    [InlineData("timer status.")]
    [InlineData("timer status and approve")]
    [InlineData("timer start  2")]
    [InlineData("Nova, timer resume")]
    [InlineData("timer!")]
    public void ReservedMalformedCommandsNeverBecomeModelInput(string input)
    {
        CountdownCommand.Parse(input, "Nova")!.Operation.Should().Be(CountdownOperation.Invalid);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("start timer 10")]
    [InlineData("timers status")]
    [InlineData("Kora, timer status")]
    public void OtherInputIsNotACommand(string input) => CountdownCommand.Parse(input, "Nova").Should().BeNull();

    [Fact]
    public void LimitsAndDiscoveryShareTheDomainRules()
    {
        CountdownCommand.Parse("timer start " + new string('1', 256), "Nova")!.Operation.Should().Be(CountdownOperation.Invalid);
        CountdownCommand.Parse("timer status", "Timer")!.Operation.Should().Be(CountdownOperation.Status);
        CountdownCommand.Parse("Timer, timer status", "Timer")!.Operation.Should().Be(CountdownOperation.Status);
        CountdownCommand.Parse("timer status", "Timer")!.IsActivated.Should().BeFalse();
        CountdownCommand.Parse("Timer, timer status", "Timer")!.IsActivated.Should().BeTrue();
        CountdownCommand.Parse("Timer Status, timer start 1", "Timer Status")!.Seconds.Should().Be(1);
        CountdownCommand.DiscoveryPhrases.Should().HaveCount(4)
            .And.OnlyContain(phrase => CountdownCommand.Parse(phrase, "Kora")!.Operation != CountdownOperation.Invalid);
        CountdownCommand.IsValidSeconds(int.MinValue).Should().BeFalse();
        CountdownCommand.IsValidSeconds(int.MaxValue).Should().BeFalse();
        CountdownCommand.Syntax.Should().Contain("86400");
    }
}
