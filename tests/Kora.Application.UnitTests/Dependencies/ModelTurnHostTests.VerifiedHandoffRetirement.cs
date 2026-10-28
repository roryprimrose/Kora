using AwesomeAssertions;

using Kora.Core.Dependencies;

namespace Kora.Application.UnitTests.Dependencies;

public sealed partial class ModelTurnHostTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SettledPermanentTurnRetirementCannotIssueOrReviveLocalOutcomeProof(bool alreadyIssued)
    {
        using var f = new Fixture();
        using var activity = f.Root();
        var policy = await Configure(f);
        var context = f.Context();
        var turn = (await f.PolicyHost.AdmitAsync(policy, ModelTurnChoice.Local, context, null, Token)).Turn!;
        await f.PolicyHost.ReserveConversationResourcesAsync(turn, static () => true, Token);
        f.Adapter.Reply = new(ModelTurnOutcome.Unavailable, policy.Local);
        (await f.PolicyHost.RunAsync(turn, Token)).Outcome.Should().Be(ModelTurnOutcome.Unavailable);
        await f.PolicyHost.JoinConversationResourcesAsync(turn, Token);
        var observation = alreadyIssued ? f.PolicyHost.ObserveJoinedLocalUnavailable(turn) : null;
        if (alreadyIssued) { observation!.IsCurrent.Should().BeTrue(); }
        turn.Retire(ModelTurnReason.HostAdmissionClosed);
        f.PolicyHost.ObserveJoinedLocalUnavailable(turn).Should().BeNull();
        if (alreadyIssued) { observation!.IsCurrent.Should().BeFalse(); }
        f.Adapter.Calls.Should().Be(1);
        f.PolicyHost.IsQuiescent.Should().BeTrue();
    }
}
