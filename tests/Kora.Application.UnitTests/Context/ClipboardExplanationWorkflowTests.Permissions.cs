using System.Collections.Immutable;

using AwesomeAssertions;

using Kora.Core.Authorization;
using Kora.Core.Authorization.SessionPermission;
using Kora.Core.Context;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Interaction;

namespace Kora.Application.UnitTests.Context;

public sealed partial class ClipboardExplanationWorkflowTests
{
    [Theory]
    [InlineData("once", 2)]
    [InlineData("session", 1)]
    [InlineData("perpetual", 1)]
    public async Task ProductionPermissionDeliversActualLocalAnswersAndHonoursGrantScope(string scope, int questions)
    {
        await using var fixture = new Fixture(productionSourcePolicy: true);
        using var root = fixture.Root();
        fixture.GrantChoice = scope;
        fixture.Reader.Text = "-----BEGIN PRIVATE KEY-----\nsynthetic test material\n-----END PRIVATE KEY-----";
        for (var index = 0; index < 2; index++)
        {
            (await fixture.Action.ExecuteAsync(null, () => true, fixture.Token)).Outcome
                .Should().Be(ClipboardExplanationOutcome.ReviewRequired);
            fixture.Adapter.Calls.Should().Be(index);
            var source = fixture.Broker.Current!;
            var result = await fixture.Action.ExecuteAsync(source.SnapshotId, () => true, fixture.Token);
            result.Outcome.Should().Be(ClipboardExplanationOutcome.Succeeded);
            fixture.Workflow.CurrentExplanation.Should().Be("Genuine synthetic local explanation.");
            fixture.Tasks.Last().State.Should().Be(HostTaskState.Succeeded);
            fixture.Adapter.Provenance!.Selection.Provider.Should().Be(ModelProviderIdentity.Ollama);
            ModelToolProtocol.ReadContext(fixture.Adapter.Wire!, fixture.Adapter.Provenance).ToolMode.Should().Be(ModelToolMode.None);
            fixture.Broker.Clear();
        }
        fixture.PermissionQuestions.Should().Be(questions);
        fixture.Interactions.Interaction!.Grants.Should().OnlyContain(grant =>
            grant.ApprovedProposal.Binding.Target != null
            && grant.ApprovedProposal.Binding.Target.Kind == PermissionGrantArtifactKind.Tool
            && grant.ApprovedProposal.Binding.Target.Scope == Kora.Core.Customizations.CustomizationScope.BuiltIn);
        fixture.Reader.Calls.Should().Be(2);
        fixture.Messages.Should().NotContain(text => text.Contains("synthetic test material", StringComparison.Ordinal));
        fixture.Audits.Should().NotContain(item => item.TargetId.Contains("synthetic test material", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DeniedGrantCannotConstructContextOrDispatchEvenForOrdinaryReviewedText()
    {
        await using var fixture = new Fixture(productionSourcePolicy: true);
        using var root = fixture.Root();
        fixture.GrantChoice = null;
        await fixture.Action.ExecuteAsync(null, () => true, fixture.Token);
        var result = await fixture.Action.ExecuteAsync(fixture.Broker.Current!.SnapshotId, () => true, fixture.Token);
        result.Outcome.Should().Be(ClipboardExplanationOutcome.SourceUseDenied);
        fixture.Adapter.Calls.Should().Be(0);
        fixture.Adapter.Wire.Should().BeNull();
        fixture.Workflow.CurrentExplanation.Should().BeNull();
        fixture.Interactions.Interaction!.Grants.Should().BeEmpty();
        fixture.Tasks.Last().State.Should().Be(HostTaskState.Failed);
    }

    [Theory]
    [InlineData("source")]
    [InlineData("grant")]
    public async Task PermissionCannotSurviveRevocationBeforeDispatch(string change)
    {
        await using var fixture = new Fixture(productionSourcePolicy: true);
        using var root = fixture.Root();
        await fixture.Action.ExecuteAsync(null, () => true, fixture.Token);
        fixture.BeforePermissionResult = () =>
        {
            if (change is "source") { fixture.Broker.Clear(); }
            else { RevokePermission(fixture); }
        };
        var result = await fixture.Action.ExecuteAsync(fixture.Broker.Current!.SnapshotId, () => true, fixture.Token);
        result.Outcome.Should().Be(change is "source" ? ClipboardExplanationOutcome.Cancelled : ClipboardExplanationOutcome.SourceUseDenied);
        fixture.Adapter.Calls.Should().Be(0);
        fixture.Workflow.CurrentExplanation.Should().BeNull();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RevokedPermissionAtFinalStoreReadOrAfterAdapterCompletionCannotPublish(bool afterAdapter)
    {
        await using var fixture = new Fixture(productionSourcePolicy: true);
        using var root = fixture.Root();
        await fixture.Action.ExecuteAsync(null, () => true, fixture.Token);
        var id = fixture.Broker.Current!.SnapshotId;
        if (afterAdapter) { fixture.Adapter.Pending = new(TaskCreationOptions.RunContinuationsAsynchronously); }
        else
        {
            fixture.BeforePermissionResult = () => fixture.BeforeMetadata = () =>
            {
                fixture.BeforeMetadata = null;
                RevokePermission(fixture);
            };
        }
        var run = fixture.Action.ExecuteAsync(id, () => true, fixture.Token);
        if (afterAdapter)
        {
            await fixture.Adapter.Started.Task.WaitAsync(fixture.Token);
            RevokePermission(fixture);
            fixture.Adapter.Pending!.SetResult(fixture.Adapter.Reply());
        }
        var operation = () => run;
        await operation.Should().ThrowAsync<InvalidOperationException>();
        fixture.Adapter.Calls.Should().Be(afterAdapter ? 1 : 0);
        fixture.Workflow.CurrentExplanation.Should().BeNull();
        await fixture.Workflow.WaitForQuiescenceAsync();
        fixture.Workflow.IsQuiescent.Should().BeTrue();
    }

    [Theory]
    [InlineData("outcome")]
    [InlineData("proposal")]
    [InlineData("basis")]
    [InlineData("grant-id")]
    [InlineData("grant-revision")]
    [InlineData("binding")]
    [InlineData("channel")]
    [InlineData("no-grant")]
    [InlineData("no-admission")]
    [InlineData("null-decision")]
    [InlineData("missing-grant-id")]
    [InlineData("missing-grant-revision")]
    public async Task AClaimedPermissionReceiptCannotReplaceTheActualExactGrant(string fault)
    {
        await using var fixture = new Fixture(productionSourcePolicy: true);
        using var root = fixture.Root();
        await fixture.Action.ExecuteAsync(null, () => true, fixture.Token);
        fixture.ChangePermissionResult = decision =>
        {
            var receipt = decision.Admission!;
            return fault switch
            {
                "outcome" => decision with { Outcome = HostInteractionOutcome.Approved },
                "proposal" => decision with { Admission = receipt with
                    { Proposal = new(receipt.Proposal.Request, new(Guid.NewGuid()), new(1), receipt.Proposal.Binding,
                        receipt.Proposal.Effect, receipt.Proposal.ExpiresAt) } },
                "basis" => decision with { Admission = receipt with { Basis = SessionPermissionBasis.ApproveAll } },
                "grant-id" => decision with { Admission = receipt with { GrantId = new(Guid.NewGuid()) } },
                "grant-revision" => decision with { Admission = receipt with { GrantRevision = new(99) } },
                "binding" => decision with { Grant = decision.Grant! with
                    { ApprovedProposal = new(receipt.Proposal.Request, new(Guid.NewGuid()), new(1),
                        DifferentBinding(), receipt.Proposal.Effect, receipt.Proposal.ExpiresAt) } },
                "channel" => decision with { Grant = decision.Grant! with { CreatorChannel = RequestOrigin.ActivatedVoice } },
                "no-grant" => decision with { Grant = null },
                "null-decision" => null!,
                "missing-grant-id" => decision with { Admission = receipt with { GrantId = null } },
                "missing-grant-revision" => decision with { Admission = receipt with { GrantRevision = null } },
                _ => decision with { Admission = null },
            };
        };
        (await fixture.Action.ExecuteAsync(fixture.Broker.Current!.SnapshotId, () => true, fixture.Token)).Outcome
            .Should().Be(ClipboardExplanationOutcome.SourceUseDenied);
        fixture.Adapter.Calls.Should().Be(0);
    }

    [Theory]
    [InlineData("current")]
    [InlineData("origin")]
    [InlineData("future")]
    [InlineData("expiry")]
    [InlineData("session")]
    [InlineData("foreign-session")]
    [InlineData("after-read")]
    public async Task PermissionPolicyRequiresCurrentOriginalSourceAndSession(string change)
    {
        await using var fixture = new Fixture(productionSourcePolicy: true);
        using var root = fixture.Root();
        await fixture.Action.ExecuteAsync(null, () => true, fixture.Token);
        var snapshot = fixture.Broker.Current!;
        var eligible = change is not "current";
        if (change is "future") { fixture.Time.Now = snapshot.CapturedAt.AddSeconds(-1); }
        if (change is "expiry") { fixture.Time.Now = snapshot.CapturedAt + ClipboardExplanationContext.Lifetime; }
        fixture.Interactions.Authority = new(root.Request.SessionId, new(1), change is not "session");
        if (change is "foreign-session") { fixture.Interactions.Authority = new(new(Guid.NewGuid()), new(1), true); }
        if (change is "after-read") { fixture.BeforeMetadata = () => eligible = false; }
        using var system = change is "origin" ? fixture.Root(RequestOrigin.HostSystem) : null;
        (await fixture.GrantedPolicy.EvaluateAsync(snapshot, () => eligible, fixture.Token)).Should().Be(ClipboardSourceUseOutcome.Denied);
        fixture.PermissionQuestions.Should().Be(0);
        fixture.Adapter.Calls.Should().Be(0);
    }

    [Fact]
    public async Task MissingOrUnrelatedSourcePermissionCannotBeValidated()
    {
        await using var fixture = new Fixture(productionSourcePolicy: true);
        using var root = fixture.Root();
        await fixture.Action.ExecuteAsync(null, () => true, fixture.Token);
        var source = fixture.Broker.Current!;
        var validate = () => fixture.GrantedPolicy.ValidateAsync(source, fixture.Token);
        await validate.Should().ThrowAsync<InvalidOperationException>();
        (await fixture.Action.ExecuteAsync(source.SnapshotId, () => true, fixture.Token)).Outcome
            .Should().Be(ClipboardExplanationOutcome.Succeeded);
        await validate.Should().ThrowAsync<InvalidOperationException>();
        fixture.GrantedPolicy.BindPresenter((_, _, _, _) => throw new IOException("synthetic permission failure"));
        var evaluate = () => fixture.Action.ExecuteAsync(source.SnapshotId, () => true, fixture.Token);
        await evaluate.Should().ThrowAsync<IOException>();
        fixture.Workflow.CurrentExplanation.Should().BeNull();
    }

    private static void RevokePermission(Fixture fixture)
    {
        var snapshot = fixture.Interactions.Interaction!;
        fixture.Interactions.Interaction = snapshot with
        {
            Grants = snapshot.Grants.Select(item => item with { Status = OperationGrantStatus.Revoked }).ToImmutableArray(),
        };
    }

    private static ExactOperationBinding DifferentBinding()
    {
        var binding = Kora.Application.Context.ClipboardPermissionSourceUsePolicy.CreateBinding();
        return new("other.action", binding.SourcePartition, binding.SkillId, binding.DefinitionDigest,
            binding.DeclaredResourceDigest, binding.TrackedContentDigest, binding.ImplementationDigest,
            binding.InvocationDigest, binding.ResourceDigest, binding.IdentityDigest, binding.DestinationDigest,
            binding.TransformationDigest, binding.PolicyRevision);
    }
}
