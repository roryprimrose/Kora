using AwesomeAssertions;
using Kora.Core.Auditing;
using Kora.Core.Authorization;
using Kora.Core.Authorization.SessionPermission;
using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.Core.Mcp;
using Kora.Core.Storage;

namespace Kora.Core.UnitTests.Auditing;

public sealed class AutomaticApprovalAuditTests
{
    [Fact]
    public async Task UnsupportedAuditAndFinalWireBindingsNeverSilentlyAdmitAutomaticApproval()
    {
        IMcpConnection connection = new UnsupportedConnection();
        FluentActions.Invoking(() => connection.BindAutomaticDisclosureAuthorization(static (_, _) => ValueTask.CompletedTask))
            .Should().Throw<InvalidOperationException>();
        var request = HostRequest.Create(RequestOrigin.LocalUi);
        var policy = new SessionPermissionPolicy(request.SessionId, new(1), new(2), SessionPermissionLevel.ApproveAll);
        ISessionPermissionStore store = new UnauditedStore(policy);
        var details = new AutomaticApprovalAudit(request, policy, "local.control", HostOperationEffect.BoundedWrite,
            "control", new string('a', 64));
        await FluentActions.Awaiting(() => store.RecordAutomaticApprovalAsync(request, policy, details,
            static () => true, TestContext.Current.CancellationToken).AsTask()).Should().ThrowAsync<InvalidOperationException>();
    }

    [Theory]
    [InlineData("valid")]
    [InlineData("wrong-session")]
    [InlineData("review-all")]
    [InlineData("generation")]
    [InlineData("revision")]
    [InlineData("unknown-effect")]
    [InlineData("prohibited-effect")]
    [InlineData("undefined-effect")]
    [InlineData("proposal-without-revision")]
    [InlineData("revision-without-proposal")]
    [InlineData("proposal-revision")]
    [InlineData("destination")]
    [InlineData("account")]
    [InlineData("profile")]
    [InlineData("binding-action")]
    public void AuditRejectsIncompleteOrCrossSessionAutomaticAuthority(string scenario)
    {
        var request = new HostRequest(new(Guid.NewGuid()), new(Guid.NewGuid()), new(Guid.NewGuid()), RequestOrigin.LocalUi);
        var policy = new SessionPermissionPolicy(request.SessionId, new(1), new(2), SessionPermissionLevel.ApproveAll);
        if (scenario is "wrong-session") { policy = policy with { SessionId = new(Guid.NewGuid()) }; }
        if (scenario is "review-all") { policy = policy with { Level = SessionPermissionLevel.ReviewAll }; }
        if (scenario is "generation") { policy = policy with { Generation = default }; }
        if (scenario is "revision") { policy = policy with { Revision = default }; }
        var effect = scenario switch
        {
            "unknown-effect" => HostOperationEffect.Unknown,
            "prohibited-effect" => HostOperationEffect.Prohibited,
            "undefined-effect" => (HostOperationEffect)999,
            _ => HostOperationEffect.BoundedRead,
        };
        HostId<ProposalIdentity>? proposal = scenario is "revision-without-proposal" ? null : new(Guid.NewGuid());
        HostRevision? revision = scenario is "proposal-without-revision" ? null
            : scenario is "proposal-revision" ? default(HostRevision) : new(1);
        var digest = new string('a', 64);
        var binding = new ExactOperationBinding(scenario is "binding-action" ? "other.invoke" : "mcp.invoke",
            "test", "skill", digest, digest, digest, digest, digest, digest, digest, digest, digest, new(1));
        var create = () => new AutomaticApprovalAudit(request, policy, "mcp.invoke", effect,
            "mcp-final-request-disclosure", digest, proposal, revision, binding,
            destinationDigest: scenario is "destination" ? null : digest,
            accountDigest: scenario is "account" ? null : digest,
            profileDigest: scenario is "profile" ? null : digest, requestBodyDigest: digest);
        if (scenario is not "valid") { create.Should().Throw<InvalidDataException>(); }
        else
        {
            var audit = create();
            audit.Validate();
            var source = new SecurityAuditEvent(Guid.NewGuid(), SecurityAuditCategory.ProtectedOperation,
                "mcp.invoke", SecurityAuditOutcome.Requested, SecurityAuditInitiator.LocalUser,
                "mcp.remote", automaticApproval: audit);
            source.WithOutcome(SecurityAuditOutcome.Succeeded).AutomaticApproval.Should().BeSameAs(audit);
        }
    }

    [Fact]
    public void LocalAutomaticAuditNeedsNoDisclosureOrProposalAndRejectsMissingAuthority()
    {
        var request = HostRequest.Create(RequestOrigin.LocalUi);
        var policy = new SessionPermissionPolicy(request.SessionId, new(1), new(2), SessionPermissionLevel.ApproveAll);
        var digest = new string('a', 64);
        var audit = new AutomaticApprovalAudit(request, policy, "local.control", HostOperationEffect.BoundedWrite, "control", digest);
        audit.Validate();
        FluentActions.Invoking(() => new AutomaticApprovalAudit(null!, policy, "local.control",
            HostOperationEffect.BoundedWrite, "control", digest)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => new AutomaticApprovalAudit(request, null!, "local.control",
            HostOperationEffect.BoundedWrite, "control", digest)).Should().Throw<ArgumentNullException>();
    }

    private sealed class UnauditedStore(SessionPermissionPolicy policy) : ISessionPermissionStore
    {
        public ValueTask<SessionPermissionPolicy> ReadSessionPermissionAsync(HostId<SessionIdentity> session, CancellationToken token) =>
            ValueTask.FromResult(policy);
        public ValueTask<SessionPermissionPolicy> SetSessionPermissionAsync(HostRequest request, SessionPermissionPolicy expected,
            SessionPermissionLevel desired, Func<bool> admitted, CancellationToken token) => throw new NotSupportedException();
    }

    private sealed class UnsupportedConnection : IMcpConnection
    {
        public McpAccountReference Account => throw new NotSupportedException();
        public McpEndpoint Endpoint => throw new NotSupportedException();
        public IReadOnlyList<System.Net.IPAddress> RemoteAddresses => [];
        public Task<bool> IsCurrentAsync(CancellationToken token) => throw new NotSupportedException();
        public Task<McpConnectionReply<McpServerCapabilities>> InitializeAsync(CancellationToken token) => throw new NotSupportedException();
        public Task<McpConnectionReply<McpCapabilityPage>> ListAsync(McpCapabilityKind kind, string? cursor, CancellationToken token) => throw new NotSupportedException();
        public Task<McpConnectionReply<McpRemoteResult>> InvokeAsync(string name, McpToolSchema schema, string arguments, CancellationToken token) => throw new NotSupportedException();
        public Task<McpConnectionReply<McpTextDocument>> ReadResourceAsync(string uri, CancellationToken token) => throw new NotSupportedException();
        public Task<McpConnectionReply<McpTextDocument>> ReadPromptAsync(McpCapability prompt, string arguments, CancellationToken token) => throw new NotSupportedException();
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
