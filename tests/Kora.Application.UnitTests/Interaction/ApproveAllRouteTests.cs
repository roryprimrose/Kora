using AwesomeAssertions;
using Kora.Core.Authorization.SessionPermission;
using Kora.Core.Interaction;
using Kora.Core.Network;

namespace Kora.Application.UnitTests.Interaction;

[Collection("Host tracing")]
public sealed class ApproveAllRouteTests
{
    [Theory]
    [InlineData("present")]
    [InlineData("tool")]
    [InlineData("consume")]
    [InlineData("observe")]
    public async Task ApproveAllDoesNotEvaluateConsumeOrRetireExistingGrants(string route)
    {
        using var f = new InteractionFixture();
        await f.GrantAsync("session");
        f.Rebind();
        f.ChangeProposal(binding: InteractionFixture.Binding(0));
        f.Store.Change(snapshot => snapshot with
        {
            Permission = SessionPermissionPolicy.Default(snapshot.Session) with { Level = SessionPermissionLevel.ApproveAll },
        });
        var grants = f.Store.Snapshot.Grants;
        var token = TestContext.Current.CancellationToken;
        var decision = await f.RunAsync(() => route switch
        {
            "present" => f.Authorization.PresentAsync(f.Request, token),
            "tool" => f.Authorization.RequestGrantAsync(f.Request, new("Approve?", QuestionKind.SingleChoice,
                [new("once", "Once")], sourceId: "bundled-fixed-script"), token),
            "consume" => f.Authorization.ConsumeAsync(f.Request, new(Guid.NewGuid()), new(99), token),
            _ => f.Authorization.ObserveContentAsync(f.Request, token),
        });
        f.Store.Snapshot.Grants.Should().Equal(grants);
        if (string.Equals(route, "observe", StringComparison.Ordinal))
        {
            decision.Reason.Should().Be("grants-not-evaluated");
            f.Store.Snapshot.Admissions.Should().BeEmpty();
            return;
        }
        decision.Outcome.Should().Be(HostInteractionOutcome.Consumed);
        decision.Grant.Should().BeNull();
        var audit = f.Store.Commits.Last().Audit.AutomaticApproval!;
        audit.Request.Should().Be(f.Request);
        audit.Policy.Should().Be(f.Store.Snapshot.EffectivePermission);
        audit.Binding.Should().Be(f.Store.Snapshot.Proposal!.Binding);
        audit.ProposalId.Should().Be(f.Store.Snapshot.Proposal.ProposalId);
        audit.ProposalRevision.Should().Be(f.Store.Snapshot.Proposal.Revision);
        audit.PromptCategory.Should().Be(string.Equals(route, "tool", StringComparison.Ordinal) ? "bundled-fixed-script" : "host-operation");
        audit.GrantsEvaluated.Should().BeFalse();
        audit.Basis.Should().Be("approve-all");
        audit.RequestBodyDigest.Should().BeNull("local operations have no outgoing disclosure body");
        audit.ContentDigest.Should().Be(f.Store.Snapshot.Proposal.Binding.TrackedContentDigest);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task ExactWebAddressDisclosuresSkipPromptsButAuditFailureBlocksAdmission(bool failAudit, bool directRequest)
    {
        using var f = new InteractionFixture();
        var address = new Uri("https://example.com/page");
        var binding = f.Store.Snapshot.Proposal!.Binding;
        f.ChangeProposal(binding: new(WebPageAccessBinding.ActionId, binding.SourcePartition, binding.SkillId,
            binding.DefinitionDigest, binding.DeclaredResourceDigest, binding.TrackedContentDigest,
            binding.ImplementationDigest, binding.InvocationDigest, binding.ResourceDigest, binding.IdentityDigest,
            WebPageAccessBinding.DestinationDigest(address), binding.TransformationDigest, binding.PolicyRevision));
        f.Store.Change(snapshot => snapshot with
        {
            Permission = SessionPermissionPolicy.Default(snapshot.Session) with { Level = SessionPermissionLevel.ApproveAll },
        });
        f.Store.FailAudit = failAudit;
        var operation = () => f.RunAsync(() => directRequest
            ? f.Authorization.RequestWebPageAccessAsync(f.Request, address, TestContext.Current.CancellationToken)
            : f.Authorization.PresentWebPageOnceAsync(f.Request, address, TestContext.Current.CancellationToken));
        if (failAudit)
        {
            await operation.Should().ThrowAsync<IOException>();
            f.Store.Snapshot.Admissions.Should().BeEmpty();
            f.Store.Commits.Should().BeEmpty();
        }
        else
        {
            (await operation()).Outcome.Should().Be(HostInteractionOutcome.Consumed);
            f.Store.Commits.Single().Audit.AutomaticApproval!.DestinationDigest
                .Should().Be(WebPageAccessBinding.DestinationDigest(address));
        }
        f.Store.Snapshot.Questions.Should().BeEmpty();
        f.Store.Snapshot.Grants.Should().BeEmpty();
    }
}
