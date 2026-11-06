using AwesomeAssertions;
using Kora.Application.Skills;
using Kora.Core.Artifacts;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.Application.UnitTests.Dependencies;

public sealed partial class ModelTurnHostTests
{
    [Theory]
    [InlineData("literal", "manifest.yaml", RequestOrigin.LocalUi)]
    [InlineData("literal", "instructions.md", RequestOrigin.ActivatedVoice)]
    [InlineData("tool", "manifest.yaml", RequestOrigin.ActivatedVoice)]
    [InlineData("tool", "instructions.md", RequestOrigin.LocalUi)]
    [InlineData("inputs", "manifest.yaml", RequestOrigin.LocalUi)]
    [InlineData("inputs", "instructions.md", RequestOrigin.ActivatedVoice)]
    public async Task OwnedSkillPrimaryExclusionRejectsDirectReview(string format, string primary, RequestOrigin origin)
    {
        using var f = new Fixture(origin);
        await using var skill = new OwnedSkillHarness(f, origin);
        if (format is "tool") { DeclareOwnedWebTool(skill); }
        if (format is "inputs") { Parameterize(skill); }
        var snapshot = skill.Store.Snapshot!;
        skill.UsePolicy.Disabled.Add(ArtifactUseIdentity.File(Path.Combine(skill.Store.Destination(snapshot.Document), primary)));

        await skill.Invoking(item => item.Review()).Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*excluded from Kora use*");

        f.Adapter.Calls.Should().Be(0);
        f.RetainCalls.Should().Be(0);
        skill.Store.Writes.Should().Be(0);
        skill.Store.Snapshot.Should().BeSameAs(snapshot);
        skill.Audit.Events.Should().BeEmpty();
    }

    [Theory]
    [InlineData("literal")]
    [InlineData("tool")]
    [InlineData("inputs")]
    public async Task OwnedSkillPrimaryExclusionPolicyAbsenceRejectsUse(string format)
    {
        using var f = new Fixture();
        await using var skill = new OwnedSkillHarness(f, hasUsePolicy: false);
        if (format is "tool") { DeclareOwnedWebTool(skill); }
        if (format is "inputs") { Parameterize(skill); }

        await skill.Invoking(item => item.Review()).Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*current host exclusion policy*");

        f.Adapter.Calls.Should().Be(0);
        f.RetainCalls.Should().Be(0);
        skill.Store.Writes.Should().Be(0);
        skill.Audit.Events.Should().BeEmpty();
    }

    [Theory]
    [InlineData("before-enable", "manifest.yaml")]
    [InlineData("before-enable", "instructions.md")]
    [InlineData("during-read", "manifest.yaml")]
    [InlineData("during-read", "instructions.md")]
    [InlineData("before-invoke", "manifest.yaml")]
    [InlineData("before-invoke", "instructions.md")]
    public async Task OwnedSkillPrimaryExclusionRetiresPendingUseAndRequiresFreshReview(string phase, string primary)
    {
        using var f = new Fixture();
        await using var skill = new OwnedSkillHarness(f);
        var snapshot = skill.Store.Snapshot!;
        var identity = ArtifactUseIdentity.File(Path.Combine(skill.Store.Destination(snapshot.Document), primary));
        using var review = await skill.Review();
        if (phase is "before-invoke")
        {
            await skill.Workflow.EnableAsync(review, skill.Authority, skill.Eligible, Token);
            using var invocation = await skill.Prepare(review);
            skill.UsePolicy.Disabled.Add(identity);
            await skill.Workflow.Invoking(item => item.InvokeAsync(invocation, skill.Authority, skill.Eligible, Token))
                .Should().ThrowAsync<InvalidOperationException>().WithMessage("*excluded from Kora use*");
            invocation.Body.Should().BeEmpty();
            invocation.Preview.Should().BeEmpty();
        }
        else
        {
            if (phase is "during-read")
            { skill.Store.DuringRead = () => { skill.UsePolicy.Disabled.Add(identity); return Task.CompletedTask; }; }
            else { skill.UsePolicy.Disabled.Add(identity); }
            await skill.Workflow.Invoking(item => item.EnableAsync(review, skill.Authority, skill.Eligible, Token))
                .Should().ThrowAsync<InvalidOperationException>().WithMessage("*excluded from Kora use*");
        }

        review.IsCurrent.Should().BeFalse();
        review.Text.Should().BeEmpty();
        f.Adapter.Calls.Should().Be(0);
        f.RetainCalls.Should().Be(0);
        skill.Store.Writes.Should().Be(0);
        skill.Store.Snapshot.Should().BeSameAs(snapshot);
        skill.Store.DuringRead = null;
        skill.UsePolicy.Disabled.Remove(identity);
        await skill.Workflow.Invoking(item => item.EnableAsync(review, skill.Authority, skill.Eligible, Token))
            .Should().ThrowAsync<InvalidOperationException>();
        f.Adapter.Calls.Should().Be(0);

        using var fresh = await skill.Review();
        await skill.Workflow.EnableAsync(fresh, skill.Authority, skill.Eligible, Token);
        using var prepared = await skill.Prepare(fresh);
        var receipt = await skill.Workflow.InvokeAsync(prepared, skill.Authority, skill.Eligible, Token);
        receipt.Message.Outcome.Should().Be(ModelTurnOutcome.Succeeded);
        receipt.Message.AssistantResponse!.Body.Should().Be("answer");
        f.Adapter.Calls.Should().Be(1);
        f.RetainCalls.Should().Be(1);
        skill.Store.Writes.Should().Be(0);
    }

    [Theory]
    [InlineData("manifest.yaml")]
    [InlineData("instructions.md")]
    public async Task OwnedSkillPrimaryExclusionStopsReviewedToolUseBeforePermissionOrNetwork(string primary)
    {
        using var f = new Fixture();
        await using var shared = new SharedSkillHarness(f);
        var transport = new ToolTransport(PageCall, PageAnswer);
        await using var loop = CreateLoop(f, transport);
        var network = new PageNetwork();
        var approval = new PageApprovalStore(f);
        var pipeline = shared.ToolWorkflow(adapter: loop, network: network, approval: approval);
        await using var skill = new OwnedSkillHarness(f, conversation: shared.Conversation);
        DeclareOwnedWebTool(skill);
        using var source = await skill.Review();
        using var review = await pipeline.ReviewOwnedAsync(skill.Workflow, source, new(f.Session, null),
            skill.Authority, skill.Eligible, Token);
        await pipeline.EnableAsync(review, skill.Eligible, RequestOrigin.LocalUi, Token);
        using var invocation = await pipeline.PrepareAsync(review, "Original page request.", ToolDestination,
            skill.Eligible, RequestOrigin.LocalUi, Token);
        skill.UsePolicy.Disabled.Add(ArtifactUseIdentity.File(Path.Combine(skill.Store.Destination(skill.Store.Snapshot!.Document), primary)));

        using var request = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Application, HostOperation.Request);
        await pipeline.Invoking(item => item.InvokeAsync(invocation, skill.Eligible, approval.Present, Token))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("*excluded from Kora use*");

        source.IsCurrent.Should().BeFalse();
        review.IsCurrent.Should().BeFalse();
        transport.Requests.Should().BeEmpty();
        network.Sends.Should().Be(0);
        approval.Presentations.Should().Be(0);
        f.RetainCalls.Should().Be(0);
        skill.Store.Writes.Should().Be(0);
    }
}
