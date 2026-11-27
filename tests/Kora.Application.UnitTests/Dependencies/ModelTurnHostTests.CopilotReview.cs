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
    [InlineData("current-throws")]
    [InlineData("unrelated-cancel")]
    [InlineData("revoked-after")]
    [InlineData("current-after")]
    public async Task Final_admission_failure_has_a_typed_terminal_audit_and_never_admits_bytes(string fault)
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
        ValueTask<bool> Current(CancellationToken _)
        {
            checks++;
            if (fault is "current-throws") { throw new IOException("synthetic current-host failure"); }
            if (fault is "unrelated-cancel") { throw new OperationCanceledException("synthetic unrelated cancellation"); }
            if (fault is "revoked-after") { root.Complete(HostOperationOutcome.Failed); }
            return ValueTask.FromResult(checks != (fault is "current" ? 1 : fault is "current-after" ? 2 : -1));
        }
        if (fault is "cancel") { await cancellation.CancelAsync(); }
        var run = () => reviews.ReviewAsync(authorization, transmission, Current, cancellation.Token).AsTask();
        if (fault is "cancel" or "unrelated-cancel") { await run.Should().ThrowAsync<OperationCanceledException>(); }
        else if (fault is "current-throws") { await run.Should().ThrowAsync<IOException>(); }
        else { (await run()).Should().BeFalse(); }
        f.Audits.Last().Outcome.Should().Be(fault is "cancel" ? SecurityAuditOutcome.Cancelled
            : fault is "current-throws" or "unrelated-cancel" ? SecurityAuditOutcome.Unknown : SecurityAuditOutcome.Denied);
        f.Audits.Last().ReasonCode.Should().BeNull();
    }
}