using AwesomeAssertions;
using Kora.Application.Dependencies;
using Kora.Core.Auditing;
using Kora.Core.Authorization.SessionPermission;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Providers;
using Kora.Core.Storage;

namespace Kora.Application.UnitTests.Dependencies;

public sealed partial class ModelTurnHostTests
{
    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 1)]
    [InlineData(false, 2)]
    [InlineData(true, 2)]
    public async Task AutomaticHostedPolicyReadCannotRenewRetiredHostOrFinalRequestAuthority(bool retireHost, int retireAtRead)
    {
        using var f = new Fixture();
        using var root = f.Root();
        var current = true;
        var reads = 0;
        var permissions = new AutomaticPermissionStore(SessionPermissionPolicy.Default(f.Session)
            with { Level = SessionPermissionLevel.ApproveAll })
        {
            AfterRead = () =>
            {
                if (++reads != retireAtRead) { return; }
                if (retireHost) { root.Complete(HostOperationOutcome.Cancelled); }
                else { current = false; }
            },
        };
        var reviews = new HostedTransmissionReviewService(f, f.Time, permissions);
        var authorization = reviews.PrepareTurn(f.Context(toolMode: ModelToolMode.None), Profile(ProviderService.Copilot));
        var transmission = new CopilotTransmission(new(new(Guid.NewGuid()), f.Request, ModelProviderSelection.CopilotCandidate),
            authorization.Profile, new("https://example.com/final"), new byte[] { 1 });
        (await reviews.ReviewAsync(authorization, transmission, _ => ValueTask.FromResult(current), Token)).Should().BeFalse();
        permissions.Audits.Should().HaveCount(retireAtRead - 1);
        f.Audits.Last().Outcome.Should().Be(SecurityAuditOutcome.Denied);
    }

    [Theory]
    [InlineData(ProviderService.Copilot, SessionPermissionLevel.ApproveAll)]
    [InlineData(ProviderService.Claude, SessionPermissionLevel.ApproveAll)]
    [InlineData(ProviderService.Copilot, SessionPermissionLevel.ReviewAll)]
    [InlineData(ProviderService.Claude, SessionPermissionLevel.ReviewAll)]
    [InlineData(ProviderService.Copilot, SessionPermissionLevel.ModelReview)]
    [InlineData(ProviderService.Claude, SessionPermissionLevel.ModelReview)]
    public async Task EveryHostedFinalRequestUsesCurrentPolicyAndExactBodyAudit(
        ProviderService service, SessionPermissionLevel level)
    {
        using var f = new Fixture();
        using var root = f.Root();
        var permissions = new AutomaticPermissionStore(SessionPermissionPolicy.Default(f.Session) with { Level = level });
        var reviews = new HostedTransmissionReviewService(f, f.Time, permissions);
        HostedTurnAuthorization authorization = service == ProviderService.Claude
            ? reviews.PrepareClaudeTurn(f.Context(toolMode: ModelToolMode.None), Profile(service))
            : reviews.PrepareTurn(f.Context(toolMode: ModelToolMode.None), Profile(service));
        for (var index = 1; index <= 2; index++)
        {
            var transmission = new CopilotTransmission(new(new(Guid.NewGuid()), f.Request,
                service == ProviderService.Claude ? ModelProviderSelection.OllamaCandidate with { Provider = ModelProviderIdentity.Claude }
                    : ModelProviderSelection.CopilotCandidate),
                authorization.Profile, new("https://example.com/final"), new byte[] { (byte)index });
            (await reviews.ReviewAsync(authorization, transmission, _ => ValueTask.FromResult(true), Token))
                .Should().BeTrue();
        }
        f.Audits.Where(item => item.Outcome != SecurityAuditOutcome.Requested).Select(item => item.ReasonCode)
            .Should().Equal(HostedTransmissionReviewService.AutomaticReason, HostedTransmissionReviewService.AutomaticReason);
        permissions.Audits.Count.Should().Be(level == SessionPermissionLevel.ApproveAll ? 2 : 0);
        if (level == SessionPermissionLevel.ApproveAll)
        {
            var first = permissions.Audits[0];
            first.Request.Should().Be(f.Request);
            first.Policy.Should().Be(permissions.Policy);
            first.PromptCategory.Should().Be("hosted-final-request-disclosure");
            first.DestinationDigest.Should().HaveLength(64);
            first.AccountDigest.Should().HaveLength(64);
            first.ProfileDigest.Should().HaveLength(64);
            first.RequestBodyDigest.Should().Be(first.ContentDigest);
            first.RequestBodyDigest.Should().NotBe(permissions.Audits[1].RequestBodyDigest);
            first.GrantsEvaluated.Should().BeFalse();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AutomaticHostedAuditFailureOrPolicyRetirementNeverAdmitsFinalBytes(bool retire)
    {
        using var f = new Fixture();
        using var root = f.Root();
        var permissions = new AutomaticPermissionStore(SessionPermissionPolicy.Default(f.Session)
            with { Level = SessionPermissionLevel.ApproveAll }) { FailAudit = !retire, RetireAfterAudit = retire };
        var reviews = new HostedTransmissionReviewService(f, f.Time, permissions);
        var authorization = reviews.PrepareTurn(f.Context(toolMode: ModelToolMode.None), Profile(ProviderService.Copilot));
        var transmission = new CopilotTransmission(new(new(Guid.NewGuid()), f.Request, ModelProviderSelection.CopilotCandidate),
            authorization.Profile, new("https://example.com/final"), new byte[] { 1 });
        var operation = () => reviews.ReviewAsync(authorization, transmission, _ => ValueTask.FromResult(true), Token).AsTask();
        if (retire) { (await operation()).Should().BeFalse(); }
        else
        {
            await operation.Should().ThrowAsync<IOException>();
            permissions.Audits.Should().BeEmpty();
        }
        f.Audits.Last().Outcome.Should().NotBe(SecurityAuditOutcome.Succeeded);
    }

    private sealed class AutomaticPermissionStore(SessionPermissionPolicy policy) : ISessionPermissionStore
    {
        public SessionPermissionPolicy Policy { get; private set; } = policy;
        public List<AutomaticApprovalAudit> Audits { get; } = [];
        public bool FailAudit { get; init; }
        public bool RetireAfterAudit { get; init; }
        public Action? AfterRead { get; init; }

        public ValueTask<SessionPermissionPolicy> ReadSessionPermissionAsync(HostId<SessionIdentity> session, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            session.Should().Be(Policy.SessionId);
            AfterRead?.Invoke();
            return ValueTask.FromResult(Policy);
        }

        public ValueTask RecordAutomaticApprovalAsync(HostRequest request, SessionPermissionPolicy expected,
            AutomaticApprovalAudit details, Func<bool> admitted, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            request.Should().Be(details.Request);
            expected.Should().Be(Policy);
            admitted().Should().BeTrue();
            if (FailAudit) { throw new IOException("Automatic approval audit unavailable."); }
            Audits.Add(details);
            if (RetireAfterAudit) { Policy = Policy with { Revision = new(Policy.Revision.Value + 1) }; }
            return ValueTask.CompletedTask;
        }

        public ValueTask<SessionPermissionPolicy> SetSessionPermissionAsync(HostRequest request,
            SessionPermissionPolicy expected, SessionPermissionLevel desired, Func<bool> admitted, CancellationToken token) =>
            throw new NotSupportedException();
    }
}
