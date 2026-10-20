using AwesomeAssertions;
using Kora.Core.Commands;
using Kora.Core.Timekeeping;

namespace Kora.Core.UnitTests.Commands;

public sealed class StopwatchCommandTests
{
    [Theory]
    [InlineData("stopwatch start", "Kora", StopwatchOperation.Start)]
    [InlineData(" Kora, stopwatch status ", "Kora", StopwatchOperation.Status)]
    [InlineData("Nova stopwatch stop", "Nova", StopwatchOperation.Stop)]
    [InlineData("stopwatch reset", "Stopwatch", StopwatchOperation.Reset)]
    [InlineData("Stopwatch, stopwatch help", "Stopwatch", StopwatchOperation.Help)]
    [InlineData("StopwatchStatus: stopwatch status", "StopwatchStatus", StopwatchOperation.Status)]
    [InlineData("STOPWATCH START", "Stopwatch Start", StopwatchOperation.Start)]
    [InlineData("Stopwatch Start stopwatch stop", "Stopwatch Start", StopwatchOperation.Stop)]
    public void ExactGrammarPreservesReservedFamilyAndCurrentName(string input, string name, StopwatchOperation operation)
    {
        StopwatchCommand.Parse(input, name)!.Operation.Should().Be(operation);
        new BuiltInCommandCatalog().GetCommands(name).Should().NotBeEmpty();
    }

    [Theory]
    [InlineData("stopwatch")]
    [InlineData("stopwatch status extra")]
    [InlineData("stopwatch start!")]
    [InlineData("stopwatch  stop")]
    [InlineData("stopwatch\nreset")]
    [InlineData("stopwatch\0help")]
    [InlineData("Kora, stopwatch laps")]
    [InlineData("stopwatch-status")]
    [InlineData("stopwatch start and stop speaking")]
    public void MalformedReservedFamilyCannotFallThrough(string input) =>
        StopwatchCommand.Parse(input, "Kora")!.Operation.Should().Be(StopwatchOperation.Invalid);

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("stop")]
    [InlineData("stop speaking")]
    [InlineData("cancel task")]
    [InlineData("cancel shutdown")]
    [InlineData("timer start")]
    [InlineData("Nova, stopwatch start")]
    public void OtherFamiliesRemainUnmatched(string input) => StopwatchCommand.Parse(input, "Kora").Should().BeNull();

    [Theory]
    [InlineData("Stopwatch, stop speaking", "Stopwatch")]
    [InlineData("Stopwatch: cancel task", "Stopwatch")]
    [InlineData("StopwatchStatus stop speaking", "StopwatchStatus")]
    [InlineData("Stopwatch Status, cancel shutdown", "Stopwatch Status")]
    [InlineData("Kora, stop speaking", "Kora")]
    public void ExplicitActivationForOtherFamiliesIsNotCapturedByAnAssistantNameCollision(string input, string name) =>
        StopwatchCommand.Parse(input, name).Should().BeNull();

    [Theory]
    [InlineData("Stopwatch")]
    [InlineData("Stopwatch Status")]
    public void BareMalformedFamilyStillWinsOverACollidingName(string name) =>
        StopwatchCommand.Parse("stopwatch status extra", name)!.Operation.Should().Be(StopwatchOperation.Invalid);

    [Fact]
    public void DomainDecisionsCoverEveryStateAndOperation()
    {
        foreach (var state in Enum.GetValues<StopwatchState>())
        {
            StopwatchRules.Decide(state, StopwatchOperation.Status).Should().Be(StopwatchOutcome.Observed);
            StopwatchRules.Decide(state, StopwatchOperation.Reset).Should().Be(StopwatchOutcome.Reset);
            StopwatchRules.Decide(state, StopwatchOperation.Help).Should().Be(StopwatchOutcome.Help);
            StopwatchRules.Decide(state, StopwatchOperation.Invalid).Should().Be(StopwatchOutcome.Invalid);
        }
        StopwatchRules.Decide(StopwatchState.Idle, StopwatchOperation.Start).Should().Be(StopwatchOutcome.Started);
        StopwatchRules.Decide(StopwatchState.Running, StopwatchOperation.Start).Should().Be(StopwatchOutcome.AlreadyRunning);
        StopwatchRules.Decide(StopwatchState.Stopped, StopwatchOperation.Start).Should().Be(StopwatchOutcome.ResetRequired);
        StopwatchRules.Decide(StopwatchState.Idle, StopwatchOperation.Stop).Should().Be(StopwatchOutcome.NotRunning);
        StopwatchRules.Decide(StopwatchState.Running, StopwatchOperation.Stop).Should().Be(StopwatchOutcome.Stopped);
        StopwatchRules.Decide(StopwatchState.Stopped, StopwatchOperation.Stop).Should().Be(StopwatchOutcome.AlreadyStopped);
        StopwatchCommand.FixedPhrases.Should().HaveCount(5);
    }
}
