using AwesomeAssertions;

using Kora.Core.Commands;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Core.UnitTests.Commands;

public sealed class SessionRetentionCommandTests
{
    private const string Id = "12345678-1234-1234-1234-123456789012";

    [Theory]
    [InlineData("session retention status " + Id, SessionCommandOperation.RetentionStatus, false)]
    [InlineData("Nova, SESSION RETENTION REVIEW " + Id + " KEEP", SessionCommandOperation.RetentionReview, true)]
    [InlineData("session retention review " + Id + " ordinary", SessionCommandOperation.RetentionReview, false)]
    [InlineData("session retention confirm " + Id + " " + Id, SessionCommandOperation.RetentionConfirm, false)]
    [InlineData("session retention cancel " + Id + " " + Id, SessionCommandOperation.RetentionCancel, false)]
    public void ExactRetentionGrammarHasNoNameOrSelectionTarget(string input, SessionCommandOperation operation, bool keep)
    {
        var command = SessionCommand.Parse(input, "Nova")!;
        command.Operation.Should().Be(operation);
        command.SessionId.Should().Be(Guid.Parse(Id));
        command.KeepRetention.Should().Be(keep);
        command.IsRetentionCommand.Should().BeTrue();
        if (operation is SessionCommandOperation.RetentionConfirm or SessionCommandOperation.RetentionCancel)
        { command.RetentionReviewId.Should().Be(Guid.Parse(Id)); }
    }

    [Theory]
    [InlineData("")]
    [InlineData("help extra")]
    [InlineData("status")]
    [InlineData("status selected")]
    [InlineData("status 00000000-0000-0000-0000-000000000000")]
    [InlineData("status {12345678-1234-1234-1234-123456789012}")]
    [InlineData("status ABCDEF12-1234-1234-1234-123456789012")]
    [InlineData("status \u00a0" + Id)]
    [InlineData("status " + Id + " extra")]
    [InlineData("review " + Id)]
    [InlineData("review " + Id + " forever")]
    [InlineData("review " + Id + " ordinary extra")]
    [InlineData("confirm " + Id + " invalid")]
    [InlineData("confirm " + Id + " \u00a0" + Id)]
    [InlineData("cancel " + Id)]
    [InlineData("apply-now " + Id + " " + Id)]
    [InlineData("status\t" + Id)]
    public void InvalidReservedRetentionInputNeverFallsThrough(string arguments) =>
        SessionCommand.Parse("session retention " + arguments, "Kora")!.Operation.Should().Be(SessionCommandOperation.Invalid);

    [Fact]
    public void HelpBoundsAndRecordedStateComparisonAreExact()
    {
        SessionCommand.Parse("session retention help", "Kora")!.Operation.Should().Be(SessionCommandOperation.Help);
        SessionCommand.Parse("session retention " + new string('x', 1025), "Kora")!.Operation.Should().Be(SessionCommandOperation.Invalid);
        SessionCommand.Parse("session retention status " + Id + "\0", "Kora")!.Operation.Should().Be(SessionCommandOperation.Invalid);
        var observed = new SessionRetentionObservation(new(new(Guid.Parse(Id)), DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch.AddDays(1), DateTimeOffset.UnixEpoch.AddDays(30), false, false), new(1), 0, false);
        observed.MatchesRecordedState(observed with { ObservedAt = DateTimeOffset.UtcNow, WorkHoldObserved = true }).Should().BeTrue();
        observed.MatchesRecordedState(observed with { State = observed.State with { Perpetual = true } }).Should().BeFalse();
        observed.MatchesRecordedState(observed with { Generation = new HostRevision(2) }).Should().BeFalse();
        observed.MatchesRecordedState(observed with { ExemptionAuditSequence = 1 }).Should().BeFalse();
        observed.MatchesRecordedState(observed with { Removed = true }).Should().BeFalse();
        new SessionCommand(SessionCommandOperation.Help).IsRetentionCommand.Should().BeFalse();
        SessionCommand.RetentionSyntax.Should().Contain("native").And.Contain("Model retention control is unavailable");
    }
}
