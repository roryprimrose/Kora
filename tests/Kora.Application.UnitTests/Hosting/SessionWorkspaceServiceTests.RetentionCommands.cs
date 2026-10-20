using AwesomeAssertions;

using Kora.Application.Hosting;
using Kora.Core.Commands;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Application.UnitTests.Hosting;

public sealed partial class SessionWorkspaceServiceTests
{
    [Theory]
    [InlineData(RequestOrigin.LocalUi, RequestOrigin.LocalUi, true)]
    [InlineData(RequestOrigin.ActivatedVoice, RequestOrigin.LocalUi, false)]
    [InlineData(RequestOrigin.ActivatedVoice, RequestOrigin.ActivatedVoice, true)]
    public async Task RetentionCommandsObserveThenOpenExactNativeReviewWithoutWritingOrRelabelling(
        RequestOrigin original, RequestOrigin confirmation, bool keep)
    {
        using var fixture = new Fixture();
        var time = new RetentionCommandTime();
        var service = new SessionRetentionCommandService(fixture.Service, fixture, time);
        var id = fixture.Session.SessionId.Value;
        var status = await service.ExecuteAsync(new(SessionCommandOperation.RetentionStatus, id), original,
            () => true, NeverOpenRetention, fixture.Token);
        status.Retention.Should().Be(fixture.Retention);
        status.Outcome.Should().Be("observed");
        var result = await service.ExecuteAsync(new(SessionCommandOperation.RetentionReview, id) { KeepRetention = keep },
            original, () => true, NeverOpenRetention, fixture.Token);
        var review = result.RetentionReview!;
        review.Observation.Should().Be(fixture.Retention);
        review.Origin.Should().Be(original);
        review.Keep.Should().Be(keep);
        review.ExpiresAt.Should().Be(time.Now.AddSeconds(60));
        time.Now = time.Now.AddSeconds(59);
        var opened = 0;
        var confirm = new SessionCommand(SessionCommandOperation.RetentionConfirm, id) { RetentionReviewId = review.Id };
        var native = await service.ExecuteAsync(confirm, confirmation, () => true, (exact, admitted, token) =>
        {
            exact.Should().Be(review);
            admitted().Should().BeTrue();
            token.Should().Be(fixture.Token);
            HostActivity.Current!.Request.Origin.Should().Be(original);
            HostActivity.Current.Request.SessionId.Should().Be(fixture.Session.SessionId);
            HostActivity.Current.Activity!.Links.Should().HaveCount(2);
            opened++;
            return Task.CompletedTask;
        }, fixture.Token);
        native.Outcome.Should().Be("native-review");
        native.RetentionReview.Should().Be(review);
        fixture.TaskWrites.Should().BeEmpty();
        fixture.PassiveInspectionCalls.Should().Be(0);
        var replay = () => service.ExecuteAsync(confirm, confirmation, () => true, NeverOpenRetention, fixture.Token);
        await replay.Should().ThrowAsync<InvalidOperationException>();
        opened.Should().Be(1);
    }

    [Theory]
    [InlineData("expired")]
    [InlineData("source")]
    [InlineData("private")]
    [InlineData("control")]
    [InlineData("revision")]
    [InlineData("retired")]
    [InlineData("wrong-review")]
    [InlineData("missing-review-id")]
    [InlineData("wrong-session")]
    [InlineData("state")]
    [InlineData("generation")]
    [InlineData("aba")]
    [InlineData("removed")]
    [InlineData("late-source")]
    public async Task RetentionHandoffHoldsOnExpiredChangedOrUncertainAuthority(string failure)
    {
        using var fixture = new Fixture();
        var time = new RetentionCommandTime();
        var service = new SessionRetentionCommandService(fixture.Service, fixture, time);
        var source = true;
        var id = fixture.Session.SessionId.Value;
        var review = (await service.ExecuteAsync(new(SessionCommandOperation.RetentionReview, id),
            RequestOrigin.ActivatedVoice, () => source, NeverOpenRetention, fixture.Token)).RetentionReview!;
        if (failure is "expired") { time.Now = review.ExpiresAt; }
        if (failure is "source") { source = false; }
        if (failure is "private") { fixture.CanInspect = false; }
        if (failure is "control") { fixture.CanControl = false; }
        if (failure is "revision") { fixture.AdvanceRevision(); }
        if (failure is "retired") { service.Retire(); }
        if (failure is "late-source") { fixture.RetentionReading = () => source = false; }
        fixture.ChangeRetention(observed => failure switch
        {
            "state" => observed with { State = observed.State with { ArchiveDue = observed.State.ArchiveDue.AddHours(1) } },
            "generation" => observed with { Generation = new(2) },
            "aba" => observed with { ExemptionAuditSequence = 2 },
            "removed" => observed with { Removed = true },
            _ => observed,
        });
        var confirm = new SessionCommand(SessionCommandOperation.RetentionConfirm,
            failure is "wrong-session" ? Guid.NewGuid() : id)
        { RetentionReviewId = failure is "missing-review-id" ? null : failure is "wrong-review" ? Guid.NewGuid() : review.Id };
        var action = () => service.ExecuteAsync(confirm, RequestOrigin.LocalUi, () => true, NeverOpenRetention, fixture.Token);
        await action.Should().ThrowAsync<InvalidOperationException>();
        fixture.TaskWrites.Should().BeEmpty();
        fixture.RetentionReading = null;
        fixture.CanInspect = true;
        fixture.CanControl = true;
        source = true;
        await action.Should().ThrowAsync<InvalidOperationException>("every attempted confirmation consumes its review");
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("unknown")]
    [InlineData("cancelled-open")]
    [InlineData("late-open")]
    [InlineData("unsupported")]
    public async Task RetentionCancelAndUnknownHandoffNeverRetryOrClaimHoldCommit(string outcome)
    {
        using var fixture = new Fixture();
        var service = new SessionRetentionCommandService(fixture.Service, fixture, new RetentionCommandTime());
        var id = fixture.Session.SessionId.Value;
        var review = (await service.ExecuteAsync(new(SessionCommandOperation.RetentionReview, id),
            RequestOrigin.LocalUi, () => true, NeverOpenRetention, fixture.Token)).RetentionReview!;
        var command = new SessionCommand(outcome is "cancel" ? SessionCommandOperation.RetentionCancel
            : outcome is "unsupported" ? SessionCommandOperation.Help : SessionCommandOperation.RetentionConfirm, id)
        { RetentionReviewId = review.Id };
        var admitted = true;
        Task Open(SessionRetentionReview exact, Func<bool> live, CancellationToken token)
        {
            if (outcome is "unknown") { throw new IOException("Handoff outcome unknown."); }
            if (outcome is "cancelled-open") { throw new OperationCanceledException(token); }
            admitted = false;
            return Task.CompletedTask;
        }
        var action = () => service.ExecuteAsync(command, RequestOrigin.LocalUi, () => admitted, Open, fixture.Token);
        if (outcome is "cancel") { (await action()).Outcome.Should().Be("cancelled"); }
        else { await action.Should().ThrowAsync<Exception>(); }
        admitted = true;
        await action.Should().ThrowAsync<InvalidOperationException>();
        fixture.TaskWrites.Should().BeEmpty();
    }

    [Theory]
    [InlineData("source")]
    [InlineData("late-read")]
    [InlineData("late-stage")]
    [InlineData("purged")]
    [InlineData("removed")]
    [InlineData("control")]
    [InlineData("cancelled")]
    [InlineData("missing-id")]
    [InlineData("system")]
    [InlineData("model")]
    [InlineData("missing-review")]
    public async Task RetentionReviewRefusesMissingAdmissionAndUnusableSubjects(string failure)
    {
        using var fixture = new Fixture();
        var service = new SessionRetentionCommandService(fixture.Service, fixture, new RetentionCommandTime());
        var admitted = true;
        var reads = 0;
        if (failure is "late-read") { fixture.RetentionReading = () => admitted = false; }
        fixture.ChangeRetention(observed => failure switch
        {
            "purged" => observed with { State = observed.State with { Purged = true } },
            "removed" => observed with { Removed = true },
            _ => observed,
        });
        if (failure is "control") { fixture.CanControl = false; }
        using var cancellation = new CancellationTokenSource();
        if (failure is "cancelled") { cancellation.Cancel(); }
        using var root = HostActivity.BeginRoot(new(new(Guid.NewGuid()),
            fixture.Session.SessionId, new(Guid.NewGuid()), RequestOrigin.LocalUi,
            failure is "model" ? new(Guid.NewGuid()) : null),
            HostActivityLayer.Application, HostOperation.Request);
        bool Live()
        {
            if (failure is "late-stage" && ++reads == 3) { return false; }
            return admitted && failure is not "source";
        }
        var action = () => service.ExecuteAsync(new(failure is "missing-review" ? SessionCommandOperation.RetentionConfirm
            : SessionCommandOperation.RetentionReview, failure is "missing-id" ? null : fixture.Session.SessionId.Value),
            failure is "system" ? RequestOrigin.HostSystem : RequestOrigin.LocalUi, Live, NeverOpenRetention,
            cancellation.Token);
        await action.Should().ThrowAsync<Exception>();
        fixture.TaskWrites.Should().BeEmpty();
    }

    private static Task NeverOpenRetention(SessionRetentionReview review, Func<bool> admitted, CancellationToken token) =>
        throw new InvalidOperationException("No native review should open.");

    private sealed class RetentionCommandTime : TimeProvider
    {
        internal DateTimeOffset Now { get; set; } = DateTimeOffset.UnixEpoch;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private partial class Fixture
    {
        internal Action? RetentionReading { get; set; }
        internal void ChangeRetention(Func<SessionRetentionObservation, SessionRetentionObservation> change) =>
            savedRetention = change(Retention);
    }
}
