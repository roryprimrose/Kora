using AwesomeAssertions;

using Kora.Core.Commands;
using Kora.Core.Hosting;

namespace Kora.Core.UnitTests.Commands;

public sealed class SessionDiagnosticsPlanCommandTests
{
    private const string Id = "12345678-1234-1234-abcd-123456789012";
    private const string Head = "session diagnostics plan ";

    [Fact]
    public void EveryNonemptySubsetHasOneExactCanonicalOrderAndNoInferredProfiles()
    {
        string[] tokens = ["version", "capabilities", "readiness", "runtimes"];
        SessionQueueReadProfile[] profiles = [SessionQueueReadProfile.Version, SessionQueueReadProfile.Capabilities,
            SessionQueueReadProfile.Readiness, SessionQueueReadProfile.Runtimes];
        for (var mask = 1; mask < 16; mask++)
        {
            var selected = Enumerable.Range(0, 4).Where(index => (mask & (1 << index)) != 0).ToArray();
            foreach (var prefix in new[] { "", "Nova, ", "nova " })
            {
                var command = SessionDiagnosticsPlanCommand.Parse(prefix + Head + Id + " "
                    + string.Join(',', selected.Select(index => tokens[index])), "Nova")!;
                command.Error.Should().BeNull();
                command.SessionId.Should().Be(Guid.Parse(Id));
                command.Profiles.Should().Equal(selected.Select(index => profiles[index]));
            }
            if (selected.Length > 1)
            {
                SessionDiagnosticsPlanCommand.Parse(Head + Id + " "
                    + string.Join(',', selected.Reverse().Select(index => tokens[index])), "Nova")!.Error.Should().NotBeNull();
            }
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("session")]
    [InlineData("session status exact")]
    [InlineData("session diagnosticsX plan")]
    [InlineData("other diagnostics plan")]
    [InlineData("Kora, session diagnostics plan help")]
    [InlineData("NovaX, session diagnostics plan help")]
    [InlineData("Nova:session diagnostics plan help")]
    [InlineData("Nova")]
    public void UnrelatedOrOldNameInputIsNotClaimed(string input) =>
        SessionDiagnosticsPlanCommand.Parse(input, "Nova").Should().BeNull();

    [Theory]
    [InlineData("session diagnostics")]
    [InlineData("session diagnostics plan")]
    [InlineData("session diagnostics confirm")]
    [InlineData(Head + "help extra")]
    [InlineData(Head + "selected version")]
    [InlineData(Head + "current version")]
    [InlineData(Head + "\"a name\" version")]
    [InlineData(Head + "00000000-0000-0000-0000-000000000000 version")]
    [InlineData(Head + "1234567812341234abcd123456789012 version")]
    [InlineData(Head + "{12345678-1234-1234-abcd-123456789012} version")]
    [InlineData(Head + "12345678-1234-1234-ABCD-123456789012 version")]
    [InlineData(Head + Id)]
    [InlineData(Head + Id + " ,")]
    [InlineData(Head + Id + " ,version")]
    [InlineData(Head + Id + " version,")]
    [InlineData(Head + Id + " version,,runtimes")]
    [InlineData(Head + Id + " version,version")]
    [InlineData(Head + Id + " runtimes,version")]
    [InlineData(Head + Id + " Version")]
    [InlineData(Head + Id + " all")]
    [InlineData(Head + Id + " capability")]
    [InlineData(Head + Id + " runtime-status")]
    [InlineData(Head + Id + " version,unknown")]
    [InlineData(Head + Id + " version, readiness")]
    [InlineData(Head + Id + " version confirm")]
    [InlineData(Head + Id + " version\n")]
    [InlineData("session\tdiagnostics plan " + Id + " version")]
    [InlineData("session diagnostics nope " + Id + " version")]
    public void MalformedReservedInputNeverCreatesAPlanOrConfirmation(string input)
    {
        var result = SessionDiagnosticsPlanCommand.Parse(input, "Nova")!;
        result.Error.Should().Contain(SessionDiagnosticsPlanCommand.Syntax);
        result.SessionId.Should().BeEmpty();
        result.Profiles.Should().BeEmpty();
    }

    [Theory]
    [InlineData("session diagnostics plan help")]
    [InlineData(" NOVA, SESSION DIAGNOSTICS PLAN HELP ")]
    public void HelpIsExplicitAndInert(string input) =>
        SessionDiagnosticsPlanCommand.Parse(input, "Nova")!.IsHelp.Should().BeTrue();

    [Fact]
    public void ExactUtf8InputLimitIsEnforcedWithoutTruncation()
    {
        var input = (Head + Id + " version").PadRight(SessionCommand.MaximumInputBytes);
        SessionDiagnosticsPlanCommand.Parse(input, "Nova")!.Error.Should().BeNull();
        SessionDiagnosticsPlanCommand.Parse(input + " ", "Nova")!.Error.Should().NotBeNull();
        SessionDiagnosticsPlanCommand.Parse(Head + Id + " " + new string('\u00e9', 512), "Nova")!.Error.Should().NotBeNull();
    }
}
