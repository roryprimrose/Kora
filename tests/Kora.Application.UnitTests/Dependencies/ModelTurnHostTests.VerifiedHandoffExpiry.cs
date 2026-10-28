using AwesomeAssertions;

using Kora.Application.Dependencies;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.Application.UnitTests.Dependencies;

public sealed partial class ModelTurnHostTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SameSerializedBytesCannotRenewTheCanonicalContextBeforeFirstSuccessorAdmission(bool changeExpiry)
    {
        using var f = new Fixture();
        var (host, workflow, operation, runtime, presented) = f.VerifiedOperation();
        var running = operation.RunAsync(new(f.Session, null), "original", static () => true, Token);
        await presented.Task.WaitAsync(Token);
        var source = new ModelHandoffPresentation(workflow);
        var offer = (await source.ReadPending(RequestOrigin.LocalUi, static () => true, Token)).Single();
        var rejected = false;
        f.AfterHandoffCommitAsync = async committed =>
        {
            var original = offer.Context;
            var counterfeit = new ModelContextEnvelope(committed.Successor.Message.Request, original.SystemPolicy,
                original.UserRequest, [], changeExpiry ? original.CreatedAt : original.CreatedAt.AddTicks(-1),
                changeExpiry ? original.ExpiresAt.AddMinutes(1) : original.ExpiresAt, original.ToolMode);
            counterfeit.Serialize().Should().Equal(original.Serialize(), "timestamps are not outbound content bytes");
            using var activity = HostActivity.BeginRoot(counterfeit.Request, HostActivityLayer.Application, HostOperation.Request);
            var denied = await host.AdmitVerifiedHandoffAsync(offer, committed, counterfeit, Token);
            denied.Result.Reason.Should().Be(ModelTurnReason.HandoffReviewStale);
            denied.Turn.Should().BeNull();
            runtime.Calls.Should().Be(0);
            rejected = true;
        };
        f.HandoffReviews.Offered += exact =>
        {
            using var presentation = exact.Presentation();
            f.HandoffReviews.Confirm(exact, true, RequestOrigin.LocalUi).Should().BeTrue();
        };
        await using var review = source.Open(offer, RequestOrigin.LocalUi, static () => true)!;
        await review.Refresh(Token);
        f.Time.ExpectedDeadline = HostedTurnAuthorization.MaximumDuration;
        await review.Decide(offer, ModelHandoffDecision.Approve, RequestOrigin.LocalUi, Token);
        var completed = await running;
        rejected.Should().BeTrue();
        completed.Message.Outcome.Should().Be(ModelTurnOutcome.Succeeded,
            "rejecting altered wrappers must not consume the valid original one-use capability");
        completed.Message.Handoff!.ExpiresAt.Should().Be(offer.Context.ExpiresAt);
        runtime.Transmissions.Should().Be(1);
        host.IsQuiescent.Should().BeTrue();
    }
}
