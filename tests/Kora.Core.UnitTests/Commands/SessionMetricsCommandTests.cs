using AwesomeAssertions;
using Kora.Core.Commands;

namespace Kora.Core.UnitTests.Commands;

public sealed class SessionMetricsCommandTests
{
    [Theory]
    [InlineData("show session metrics")]
    [InlineData("show token usage")]
    [InlineData("how many tokens have I used")]
    [InlineData("how many tokens have I used in this session?")]
    [InlineData("what is my context usage")]
    [InlineData("session metrics")]
    [InlineData("Kora, SHOW SESSION METRICS")]
    public void BindsCurrentSessionOnlyAtHostInputCapture(string input)
    {
        var command = SessionCommand.Parse(input, "Kora");
        command!.Operation.Should().Be(SessionCommandOperation.Metrics);
        command.SessionId.Should().BeNull();
    }

    [Theory]
    [InlineData("a95f949a-dfb8-4647-8e02-79b5e5cab3be", true)]
    [InlineData("A95F949A-DFB8-4647-8E02-79B5E5CAB3BE", true)]
    [InlineData("active", false)]
    [InlineData("a95f949a-dfb8-4647-8e02-79b5e5cab3be extra", false)]
    [InlineData("a95f949a-dfb8-4647-8e02-79b5e5cab3be\n", false)]
    public void RequiresExactIdentityWithoutNameOrRecencyGuess(string target, bool valid)
    {
        var command = SessionCommand.Parse("session metrics " + target, "Kora")!;
        command.Operation.Should().Be(valid ? SessionCommandOperation.Metrics : SessionCommandOperation.Invalid);
        if (valid) { command.SessionId.Should().Be(Guid.Parse(target)); }
        else { command.Error.Should().Be(SessionCommand.MetricsSyntax); }
    }

    [Fact]
    public void UnrelatedInputIsNotMetrics()
    {
        SessionCommand.Parse("explain something else", "Kora").Should().BeNull();
        SessionCommand.Parse("session metrics " + new string('x', 1024), "Kora")!.Operation.Should().Be(SessionCommandOperation.Invalid);
        SessionCommand.Parse("show session metrics\n", "Kora")!.Operation.Should().Be(SessionCommandOperation.Invalid);
    }
}
