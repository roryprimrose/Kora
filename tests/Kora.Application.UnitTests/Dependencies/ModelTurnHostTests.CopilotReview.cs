using AwesomeAssertions;
using Kora.Application.Dependencies;
using Kora.Core.Auditing;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Providers;

namespace Kora.Application.UnitTests.Dependencies;

public sealed partial class ModelTurnHostTests
{
    [Theory]
    [InlineData("stopped")]
    [InlineData("completed")]
    [InlineData("foreign")]
    [InlineData("system")]
    [InlineData("future")]
    [InlineData("expired")]
    [InlineData("service")]
    [InlineData("revision")]
    public void Preparation_rejects_stale_foreign_or_nonuser_authority(string fault)
    {
        using var f = new Fixture(fault is "system" ? RequestOrigin.HostSystem : RequestOrigin.LocalUi);
        using var root = f.Root();
        var reviews = new HostedTransmissionReviewService(f, f.Time);
        var context = fault is "foreign" ? f.Context(HostRequest.Create(RequestOrigin.LocalUi)) : f.Context();
        var profile = Profile(ProviderService.Copilot);
        if (fault is "stopped") { root.Activity!.Stop(); }
        if (fault is "completed") { root.Complete(HostOperationOutcome.Completed); }
        if (fault is "future") { f.Time.Now = DateTimeOffset.UnixEpoch.AddMinutes(-1); }
        if (fault is "expired") { f.Time.Now = DateTimeOffset.UnixEpoch.AddMinutes(1); }
        if (fault is "service") { profile = profile with { Service = ProviderService.Local }; }
        if (fault is "revision") { profile = profile with { CatalogueRevision = default }; }
        var prepare = () => reviews.PrepareTurn(context, profile);
        prepare.Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData("owner")]
    [InlineData("profile")]
    [InlineData("request")]
    [InlineData("current")]
    [InlineData("cancel")]
    [InlineData("presenter")]
    [InlineData("cancel-presenter")]
    [InlineData("revoked-after")]
    [InlineData("current-after")]
    [InlineData("decline")]
    public async Task Final_review_failure_has_a_typed_terminal_audit_and_never_admits_bytes(string fault)
    {
        using var f = new Fixture();
        using var root = f.Root();
        var reviews = new HostedTransmissionReviewService(f, f.Time);
        var authorization = reviews.PrepareTurn(f.Context(), Profile(ProviderService.Copilot));
        if (fault is "owner") { authorization = new(new(f, f.Time), root, f.Context(), authorization.Profile); }
        var transmission = new CopilotTransmission(
            new(new(Guid.NewGuid()), fault is "request" ? HostRequest.Create(RequestOrigin.LocalUi) : f.Request,
                ModelProviderSelection.CopilotCandidate),
            fault is "profile" ? authorization.Profile with { Account = new("foreign") } : authorization.Profile,
            new("https://api.githubcopilot.com/chat/completions"), new byte[] { 1 });
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var checks = 0;
        reviews.Offered += review =>
        {
            review.IsPending.Should().BeTrue();
            if (fault is "presenter") { throw new IOException("synthetic presenter failure"); }
            if (fault is "cancel-presenter") { throw new OperationCanceledException("synthetic unrelated cancellation"); }
            reviews.Confirm(review, fault is not "decline", RequestOrigin.ActivatedVoice).Should().BeTrue();
            if (fault is "revoked-after") { root.Complete(HostOperationOutcome.Failed); }
        };
        CopilotTransmissionReview? retired = null;
        reviews.Retired += review => retired = review;
        if (fault is "cancel") { await cancellation.CancelAsync(); }
        var run = () => reviews.ReviewAsync(authorization, transmission,
            _ => ValueTask.FromResult(++checks != (fault is "current" ? 1 : fault is "current-after" ? 2 : -1)),
            cancellation.Token).AsTask();
        if (fault is "cancel" or "cancel-presenter") { await run.Should().ThrowAsync<OperationCanceledException>(); }
        else if (fault is "presenter") { await run.Should().ThrowAsync<IOException>(); }
        else { (await run()).Should().BeFalse(); }
        if (retired is not null)
        {
            retired.IsPending.Should().BeFalse();
            reviews.Confirm(retired, true, RequestOrigin.LocalUi).Should().BeFalse();
        }
        f.Audits.Last().Outcome.Should().Be(fault is "cancel" ? SecurityAuditOutcome.Cancelled
            : fault is "presenter" or "cancel-presenter" ? SecurityAuditOutcome.Unknown : SecurityAuditOutcome.Denied);
    }

    [Fact]
    public async Task Concurrent_review_is_denied_without_retiring_the_original_and_cancellation_retires_exact_review()
    {
        using var f = new Fixture();
        using var root = f.Root();
        var reviews = new HostedTransmissionReviewService(f, f.Time);
        var authorization = reviews.PrepareTurn(f.Context(), Profile(ProviderService.Copilot));
        var transmission = new CopilotTransmission(new(new(Guid.NewGuid()), f.Request, ModelProviderSelection.CopilotCandidate),
            authorization.Profile, new("https://api.githubcopilot.com/chat/completions"), new byte[] { 1 });
        CopilotTransmissionReview? first = null;
        reviews.Offered += review => first = review;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var pending = reviews.ReviewAsync(authorization, transmission, _ => ValueTask.FromResult(true), cancellation.Token).AsTask();
        (await reviews.ReviewAsync(authorization, transmission, _ => ValueTask.FromResult(true), Token)).Should().BeFalse();
        first!.IsPending.Should().BeTrue();
        using (var foreign = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Application, HostOperation.Request))
        { reviews.Confirm(first, true, RequestOrigin.LocalUi).Should().BeFalse(); }
        root.Complete(HostOperationOutcome.Completed);
        reviews.Confirm(first, true, RequestOrigin.LocalUi).Should().BeFalse();
        root.Complete(HostOperationOutcome.Unknown);
        root.Activity!.Stop();
        reviews.Confirm(first, true, RequestOrigin.LocalUi).Should().BeFalse();
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        first.IsPending.Should().BeFalse();
        f.Audits.Last().Outcome.Should().Be(SecurityAuditOutcome.Cancelled);
    }
}
