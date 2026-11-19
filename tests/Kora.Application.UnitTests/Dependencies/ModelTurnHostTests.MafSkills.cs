using System.Text.Json;
using AwesomeAssertions;
using Kora.Application.Dependencies;
using Kora.Application.Hosting;
using Kora.Application.Skills;
using Kora.Application.UnitTests.Skills;
using Kora.Core.Artifacts;
using Kora.Core.Auditing;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Providers;
using Microsoft.Extensions.Logging.Abstractions;
using static Kora.Application.UnitTests.Skills.StandaloneSkillAgentFrameworkTests;

namespace Kora.Application.UnitTests.Dependencies;

public sealed partial class ModelTurnHostTests
{
    private static ModelTurnHost MafSkillHost(Fixture f, IModelTurnAdapter adapter, bool qualified = true)
    {
        f.ProfileCatalogue.Status = ProviderCatalogueStatus.Current;
        f.ProfilePreferences.Value = new(ProviderService.Local, [Profile()]);
        return f.ConfiguredProfiles(production: !qualified, adapter: adapter);
    }

    [Theory]
    [InlineData("single", RequestOrigin.LocalUi)]
    [InlineData("single", RequestOrigin.ActivatedVoice)]
    [InlineData("references", RequestOrigin.LocalUi)]
    [InlineData("references", RequestOrigin.ActivatedVoice)]
    [InlineData("inputs", RequestOrigin.LocalUi)]
    public async Task OwnedReviewedSkillExecutesRealMafOnceAndRetainsOnlyTheGenuineOriginalAnswer(string kind, RequestOrigin origin)
    {
        using var f = new Fixture(origin);
        var transport = new StandaloneSkillTransport();
        await using var adapter = new BoundedModelTurnAdapter(transport, f.Time, NullLogger<BoundedModelTurnAdapter>.Instance);
        var host = MafSkillHost(f, adapter);
        await using var service = f.ConversationService(host, execution: host);
        await using var skill = new OwnedSkillHarness(f, origin, conversation: service);
        if (kind is "references") { skill.Store.Snapshot = MarkdownPackage(); }
        if (kind is "inputs") { Parameterize(skill); }
        using var review = await skill.Review();
        transport.Requests.Should().BeEmpty();
        if (kind is "references")
        {
            skill.Store.ReferenceReads.Should().Be(0);
            await skill.Workflow.ReadReferencesAsync(review, skill.Authority, skill.Eligible, Token);
            review.Text.Should().Contain("Exact reference").And.Contain("Review prose only.");
        }
        await skill.Workflow.EnableAsync(review, skill.Authority, skill.Eligible, Token);
        const string body = "  Original unchanged task e\u0301\r\n";
        const string literal = "  Exact ${input:literal} e\u0301  ";
        using var invocation = await skill.Workflow.PrepareAsync(review, new(f.Session, null), body, skill.Authority,
            skill.Eligible, Token, kind is "inputs" ? new Dictionary<string, string>(StringComparer.Ordinal) { ["topic"] = literal } : null);
        using var preview = JsonDocument.Parse(invocation.Preview);
        preview.RootElement.GetProperty("Constraints").GetProperty("MaximumRounds").GetInt32().Should().Be(1);
        preview.RootElement.GetProperty("Constraints").GetProperty("MaximumCalls").GetInt32().Should().Be(0);
        preview.RootElement.GetProperty("UserRequest").GetString().Should().Be(body);
        transport.Requests.Should().BeEmpty();
        f.RetainCalls.Should().Be(0);
        var receipt = await skill.Workflow.InvokeAsync(invocation, skill.Authority, skill.Eligible, Token);
        receipt.Message.Outcome.Should().Be(ModelTurnOutcome.Succeeded);
        receipt.Message.Body.Should().Be(body);
        receipt.Message.Request.SessionId.Should().Be(f.Session.SessionId);
        receipt.Message.Request.Origin.Should().Be(origin);
        receipt.Message.AssistantResponse!.Body.Should().Be(StandaloneSkillTransport.Answer);
        var request = transport.Requests.Should().ContainSingle().Subject;
        request.Context.UserRequest.Should().Be(body);
        request.Context.Evidence.Select(item => item.Content).Take(2).Should()
            .Equal(skill.Store.Snapshot!.Document.ManifestText, skill.Store.Snapshot.Document.Guidance);
        request.Context.Tools.Should().BeEmpty();
        request.Messages.Should().BeEmpty();
        request.ClarificationAllowed.Should().BeFalse();
        request.Context.Evidence.Should().OnlyContain(item => item.Disclosure == ModelEvidenceDisclosure.LocalOnly);
        if (kind is "references") { request.Context.Evidence[2].Content.Should().Contain("Exact reference"); }
        if (kind is "inputs")
        {
            using var inputs = JsonDocument.Parse(request.Context.Evidence[2].Content);
            inputs.RootElement.GetProperty("RequiredLiteralInputs").GetProperty("topic").GetString().Should().Be(literal);
        }
        transport.PackageCalls.Should().Be(1);
        transport.StreamingCalls.Should().Be(0);
        f.Adapter.Calls.Should().Be(0);
        f.ExecutionCompletions.Should().Be(1);
        skill.Store.Writes.Should().Be(0);
        await skill.Workflow.Invoking(value => value.InvokeAsync(invocation, skill.Authority, skill.Eligible, Token))
            .Should().ThrowAsync<InvalidOperationException>();
        transport.Requests.Should().ContainSingle();
    }

    [Theory]
    [InlineData(false, RequestOrigin.LocalUi)]
    [InlineData(true, RequestOrigin.LocalUi)]
    [InlineData(false, RequestOrigin.ActivatedVoice)]
    [InlineData(true, RequestOrigin.ActivatedVoice)]
    public async Task SharedReviewedSkillExecutesRealMafWithOnlyTheConsentedReferenceBundle(bool references, RequestOrigin origin)
    {
        using var f = new Fixture(origin);
        var transport = new StandaloneSkillTransport();
        await using var adapter = new BoundedModelTurnAdapter(transport, f.Time, NullLogger<BoundedModelTurnAdapter>.Instance);
        var host = MafSkillHost(f, adapter);
        await using var service = f.ConversationService(host, execution: host);
        await using var skill = new SharedSkillHarness(f);
        if (references) { ConfigureMafReference(skill); }
        await skill.Register();
        var workflow = skill.Workflow(conversation: service);
        using var review = await MafSharedReview(skill, workflow, references, origin);
        review.Text.Should().Contain(skill.Reader.Text);
        if (references) { review.Text.Should().Contain(skill.Reader.Reference!); }
        await workflow.EnableAsync(review, skill.Eligible, origin, Token);
        const string body = "  Original unchanged shared task e\u0301\r\n";
        using var invocation = await workflow.PrepareAsync(review, new(f.Session, null), body, skill.Eligible, origin, Token);
        review.Text.Should().Contain("\"MaximumCalls\":0").And.Contain("\"MaximumRounds\":1");
        var completeReview = review.Text;
        transport.Requests.Should().BeEmpty();
        f.RetainCalls.Should().Be(0);
        using var root = f.Root();
        var receipt = await workflow.InvokeAsync(invocation, skill.Eligible, Token);
        receipt.Message.Outcome.Should().Be(ModelTurnOutcome.Succeeded);
        receipt.Message.Body.Should().Be(body);
        receipt.Message.Request.SessionId.Should().Be(f.Session.SessionId);
        receipt.Message.Request.Origin.Should().Be(origin);
        receipt.Message.AssistantResponse!.Body.Should().Be(StandaloneSkillTransport.Answer);
        var request = transport.Requests.Should().ContainSingle().Subject;
        request.Context.UserRequest.Should().Be(body);
        request.Context.Evidence[0].Content.Should().Be(skill.Reader.Text);
        if (references) { request.Context.Evidence[1].Content.Should().Contain(skill.Reader.Reference!); }
        request.Context.Evidence.Should().OnlyContain(item => item.Disclosure == ModelEvidenceDisclosure.LocalOnly);
        request.Context.Tools.Should().BeEmpty();
        request.ClarificationAllowed.Should().BeFalse();
        completeReview.Should().EndWith(JsonSerializer.Serialize(request.Context.Constraints));
        transport.PackageCalls.Should().Be(1);
        transport.StreamingCalls.Should().Be(0);
        f.Adapter.Calls.Should().Be(0);
        f.ExecutionCompletions.Should().Be(1);
        await workflow.Invoking(value => value.InvokeAsync(invocation, skill.Eligible, Token))
            .Should().ThrowAsync<InvalidOperationException>();
        transport.Requests.Should().ContainSingle();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RealMafSkillRemainsUnavailableWithoutRuntimeQualificationAndNeverFallsBack(bool shared)
    {
        using var f = new Fixture();
        var transport = new StandaloneSkillTransport();
        await using var adapter = new BoundedModelTurnAdapter(transport, f.Time, NullLogger<BoundedModelTurnAdapter>.Instance);
        var host = MafSkillHost(f, adapter, qualified: false);
        await using var service = f.ConversationService(host, execution: host);
        using var root = f.Root();
        if (shared)
        {
            await using var skill = new SharedSkillHarness(f);
            await skill.Register();
            var workflow = skill.Workflow(qualified: false, conversation: service);
            using var review = await MafSharedReview(skill, workflow, false, RequestOrigin.LocalUi);
            await workflow.EnableAsync(review, skill.Eligible, RequestOrigin.LocalUi, Token);
            using var invocation = await workflow.PrepareAsync(review, new(f.Session, null), "Original", skill.Eligible, RequestOrigin.LocalUi, Token);
            var receipt = await workflow.InvokeAsync(invocation, skill.Eligible, Token);
            receipt.Message.Outcome.Should().Be(ModelTurnOutcome.Unavailable);
            receipt.Message.AssistantResponse.Should().BeNull();
        }
        else
        {
            await using var skill = new OwnedSkillHarness(f, qualified: false, conversation: service);
            using var review = await skill.Review();
            await skill.Workflow.EnableAsync(review, skill.Authority, skill.Eligible, Token);
            using var invocation = await skill.Prepare(review);
            var receipt = await skill.Workflow.InvokeAsync(invocation, skill.Authority, skill.Eligible, Token);
            receipt.Message.Outcome.Should().Be(ModelTurnOutcome.Unavailable);
            receipt.Message.AssistantResponse.Should().BeNull();
        }
        transport.Requests.Should().BeEmpty();
        f.Adapter.Calls.Should().Be(0);
    }

    [Theory]
    [InlineData(false, "source")]
    [InlineData(true, "source")]
    [InlineData(false, "reference")]
    [InlineData(true, "reference")]
    [InlineData(false, "exclusion")]
    [InlineData(true, "exclusion")]
    [InlineData(false, "host")]
    [InlineData(true, "host")]
    [InlineData(false, "privacy")]
    [InlineData(true, "privacy")]
    [InlineData(false, "target")]
    [InlineData(true, "target")]
    [InlineData(false, "expiry")]
    [InlineData(true, "expiry")]
    [InlineData(false, "owner")]
    [InlineData(true, "owner")]
    public async Task OwnedMafWithdrawalBeforeOrAfterModelNeverPublishesOrReplays(bool afterModel, string fault)
    {
        using var f = new Fixture();
        var transport = new StandaloneSkillTransport();
        await using var adapter = new BoundedModelTurnAdapter(transport, f.Time, NullLogger<BoundedModelTurnAdapter>.Instance);
        var host = MafSkillHost(f, adapter);
        await using var service = f.ConversationService(host, execution: host);
        await using var skill = new OwnedSkillHarness(f, conversation: service);
        skill.Store.Snapshot = MarkdownPackage();
        using var review = await skill.Review();
        await skill.Workflow.ReadReferencesAsync(review, skill.Authority, skill.Eligible, Token);
        await skill.Workflow.EnableAsync(review, skill.Authority, skill.Eligible, Token);
        using var invocation = await skill.Prepare(review);
        var auditCount = skill.Audit.Events.Count;
        void Withdraw()
        {
            if (fault is "source") { ChangeOwnedSkill("instructions-bytes", skill, f, review); }
            if (fault is "reference") { skill.Store.Snapshot = MarkdownPackage("Changed reference"); }
            if (fault is "exclusion")
            {
                skill.UsePolicy.Disabled.Add(ArtifactUseIdentity.File(Path.Combine(
                    skill.Store.Destination(skill.Store.Snapshot!.Document), "manifest.yaml")));
                skill.UsePolicy.Revision++;
            }
            if (fault is "host") { f.ControlRevision++; }
            if (fault is "privacy") { f.CanControl = false; }
            if (fault is "target") { f.Session = f.Session with { Generation = new(2) }; }
            if (fault is "expiry") { f.Time.Now = review.ExpiresAt; }
            if (fault is "owner") { skill.Open = false; }
        }
        if (afterModel) { transport.DuringModel = () => { Withdraw(); return Task.CompletedTask; }; }
        else { Withdraw(); }
        var invoking = () => skill.Workflow.InvokeAsync(invocation, skill.Authority, skill.Eligible, Token);
        if (afterModel) { await invoking.Should().ThrowAsync<ConversationSubmissionUnconfirmedException>(); }
        else if (fault is "source" or "reference") { await invoking.Should().ThrowAsync<InvalidDataException>(); }
        else { await invoking.Should().ThrowAsync<InvalidOperationException>(); }
        transport.Requests.Should().HaveCount(afterModel ? 1 : 0);
        transport.PackageCalls.Should().Be(afterModel ? 1 : 0);
        f.ExecutionCompletions.Should().Be(0);
        skill.Store.Writes.Should().Be(0);
        review.Text.Should().BeEmpty();
        await invoking.Should().ThrowAsync<InvalidOperationException>();
        transport.Requests.Should().HaveCount(afterModel ? 1 : 0);
        if (afterModel) { skill.Audit.Events.Last().Outcome.Should().Be(SecurityAuditOutcome.Failed); }
        else { skill.Audit.Events.Skip(auditCount).Should().NotContain(item => item.ActionId == "skills.owned-revision.invoke"); }
    }

    [Theory]
    [InlineData(false, "source")]
    [InlineData(true, "source")]
    [InlineData(false, "reference")]
    [InlineData(true, "reference")]
    [InlineData(false, "registration")]
    [InlineData(true, "registration")]
    [InlineData(false, "host")]
    [InlineData(true, "host")]
    [InlineData(false, "privacy")]
    [InlineData(true, "privacy")]
    [InlineData(false, "target")]
    [InlineData(true, "target")]
    [InlineData(false, "expiry")]
    [InlineData(true, "expiry")]
    [InlineData(false, "owner")]
    [InlineData(true, "owner")]
    public async Task SharedMafWithdrawalBeforeOrAfterModelNeverPublishesOrReplays(bool afterModel, string fault)
    {
        using var f = new Fixture();
        using var root = f.Root();
        var transport = new StandaloneSkillTransport();
        await using var adapter = new BoundedModelTurnAdapter(transport, f.Time, NullLogger<BoundedModelTurnAdapter>.Instance);
        var host = MafSkillHost(f, adapter);
        await using var service = f.ConversationService(host, execution: host);
        await using var skill = new SharedSkillHarness(f);
        ConfigureMafReference(skill);
        await skill.Register();
        var workflow = skill.Workflow(conversation: service);
        using var review = await MafSharedReview(skill, workflow, true, RequestOrigin.LocalUi);
        await workflow.EnableAsync(review, skill.Eligible, RequestOrigin.LocalUi, Token);
        using var invocation = await workflow.PrepareAsync(review, new(f.Session, null), "original",
            skill.Eligible, RequestOrigin.LocalUi, Token);
        var auditCount = skill.Audit.Events.Count;
        void Withdraw()
        {
            if (fault is "source") { skill.Reader.Text += "\nChanged"; }
            if (fault is "reference") { skill.Reader.Reference += "\nChanged"; }
            if (fault is "registration") { skill.Preferences.Save([]); }
            if (fault is "host") { f.ControlRevision++; }
            if (fault is "privacy") { f.CanControl = false; }
            if (fault is "target") { f.Session = f.Session with { Generation = new(2) }; }
            if (fault is "expiry") { f.Time.Now = review.ExpiresAt; }
            if (fault is "owner") { skill.Open = false; }
        }
        if (afterModel) { transport.DuringModel = () => { Withdraw(); return Task.CompletedTask; }; }
        else { Withdraw(); }
        var invoking = () => workflow.InvokeAsync(invocation, skill.Eligible, Token);
        if (afterModel) { await invoking.Should().ThrowAsync<ConversationSubmissionUnconfirmedException>(); }
        else { await invoking.Should().ThrowAsync<InvalidOperationException>(); }
        transport.Requests.Should().HaveCount(afterModel ? 1 : 0);
        transport.PackageCalls.Should().Be(afterModel ? 1 : 0);
        f.ExecutionCompletions.Should().Be(0);
        review.Text.Should().BeEmpty();
        await invoking.Should().ThrowAsync<InvalidOperationException>();
        transport.Requests.Should().HaveCount(afterModel ? 1 : 0);
        if (afterModel) { skill.Audit.Events.Last().Outcome.Should().Be(SecurityAuditOutcome.Failed); }
        else { skill.Audit.Events.Skip(auditCount).Should().NotContain(item => item.ActionId == "skills.revision.invoke"); }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task RealMafForbiddenToolOrClarificationRetainsOnlyTruthfulTerminal(bool shared, bool question)
    {
        using var f = new Fixture();
        using var root = f.Root();
        var transport = new StandaloneSkillTransport
        {
            Message = question ? """{"kind":"question","prompt":"Choose","options":["A","B"]}"""
                : """{"kind":"toolCalls","calls":[{"callId":"one","capabilityId":"application.get_version","input":{}}]}""",
        };
        var effects = 0;
        f.OnVersion = () => effects++;
        await using var adapter = new BoundedModelTurnAdapter(transport, f.Time, NullLogger<BoundedModelTurnAdapter>.Instance);
        var host = MafSkillHost(f, adapter);
        await using var service = f.ConversationService(host, execution: host);
        if (shared)
        {
            await using var skill = new SharedSkillHarness(f);
            await skill.Register();
            var workflow = skill.Workflow(conversation: service);
            using var review = await MafSharedReview(skill, workflow, false, RequestOrigin.LocalUi);
            await workflow.EnableAsync(review, skill.Eligible, RequestOrigin.LocalUi, Token);
            using var invocation = await workflow.PrepareAsync(review, new(f.Session, null), "original",
                skill.Eligible, RequestOrigin.LocalUi, Token);
            var receipt = await workflow.InvokeAsync(invocation, skill.Eligible, Token);
            receipt.Message.Outcome.Should().NotBe(ModelTurnOutcome.Succeeded);
            receipt.Message.AssistantResponse.Should().BeNull();
        }
        else
        {
            await using var skill = new OwnedSkillHarness(f, conversation: service);
            using var review = await skill.Review();
            await skill.Workflow.EnableAsync(review, skill.Authority, skill.Eligible, Token);
            using var invocation = await skill.Prepare(review);
            var receipt = await skill.Workflow.InvokeAsync(invocation, skill.Authority, skill.Eligible, Token);
            receipt.Message.Outcome.Should().NotBe(ModelTurnOutcome.Succeeded);
            receipt.Message.AssistantResponse.Should().BeNull();
            skill.Store.Writes.Should().Be(0);
        }
        effects.Should().Be(0);
        transport.PackageCalls.Should().Be(1);
        transport.Requests.Should().ContainSingle();
        f.Adapter.Calls.Should().Be(0);
        var retained = await service.ReadPageAsync(f.Session.SessionId, null, 25, Token);
        retained.Messages.Should().ContainSingle().Which.AssistantResponse.Should().BeNull();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task RealMafCancellationKeepsOwnershipUntilIgnoringTransportPhysicallyJoins(bool shared, bool references)
    {
        using var f = new Fixture();
        using var root = f.Root();
        var transport = new StandaloneSkillTransport();
        await using var adapter = new BoundedModelTurnAdapter(transport, f.Time, NullLogger<BoundedModelTurnAdapter>.Instance);
        var host = MafSkillHost(f, adapter);
        await using var service = f.ConversationService(host, execution: host);
        async Task AssertJoin(Func<Task<ConversationSubmissionReceipt>> invoke)
        {
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var joined = false;
            transport.DuringModel = async () =>
            {
                using var observed = transport.ModelToken.Register(() => cancelled.TrySetResult());
                entered.TrySetResult();
                await release.Task;
                joined = true;
            };
            var submitting = invoke();
            try
            {
                await entered.Task.WaitAsync(Token);
                var running = await service.ReadRunningConversationAsync(new(f.Session, null), static () => true, Token);
                running.Should().NotBeNull();
                await service.CancelRunningConversationAsync(running!.Target, static () => true, Token);
                await cancelled.Task.WaitAsync(Token);
                submitting.IsCompleted.Should().BeFalse();
                host.IsQuiescent.Should().BeFalse();
                f.ExecutionCompletions.Should().Be(0);
                joined.Should().BeFalse();
            }
            finally { release.TrySetResult(); }
            var receipt = await submitting;
            joined.Should().BeTrue();
            receipt.Message.Outcome.Should().Be(ModelTurnOutcome.Cancelled);
            receipt.Message.AssistantResponse.Should().BeNull();
            receipt.Message.Body.Should().Be("original");
            host.IsQuiescent.Should().BeTrue();
            f.ExecutionCompletions.Should().Be(1);
            transport.Requests.Should().ContainSingle();
            transport.PackageCalls.Should().Be(1);
            transport.StreamingCalls.Should().Be(0);
        }
        if (shared)
        {
            await using var skill = new SharedSkillHarness(f);
            if (references) { ConfigureMafReference(skill); }
            await skill.Register();
            var workflow = skill.Workflow(conversation: service);
            using var review = await MafSharedReview(skill, workflow, references, RequestOrigin.LocalUi);
            await workflow.EnableAsync(review, skill.Eligible, RequestOrigin.LocalUi, Token);
            using var invocation = await workflow.PrepareAsync(review, new(f.Session, null), "original",
                skill.Eligible, RequestOrigin.LocalUi, Token);
            await AssertJoin(() => workflow.InvokeAsync(invocation, skill.Eligible, Token));
        }
        else
        {
            await using var skill = new OwnedSkillHarness(f, conversation: service);
            if (references) { skill.Store.Snapshot = MarkdownPackage(); }
            using var review = await skill.Review();
            if (references) { await skill.Workflow.ReadReferencesAsync(review, skill.Authority, skill.Eligible, Token); }
            await skill.Workflow.EnableAsync(review, skill.Authority, skill.Eligible, Token);
            using var invocation = await skill.Prepare(review);
            await AssertJoin(() => skill.Workflow.InvokeAsync(invocation, skill.Authority, skill.Eligible, Token));
        }
    }

    private static void ConfigureMafReference(SharedSkillHarness skill)
    {
        skill.Reader.Text = "---\nname: concise-review\nversion: 1.0.0\ndescription: Review prose\n"
            + "format: skill-md-instructions-v2\nreferences: notes.md\n---\nReview [notes](notes.md).";
        skill.Reader.Reference = "Exact consented shared reference e\u0301";
    }

    private static async Task<SharedSkillUseReview> MafSharedReview(SharedSkillHarness skill,
        SharedSkillInvocationWorkflow workflow, bool references, RequestOrigin origin)
    {
        if (!references) { return await workflow.ReviewAsync(skill.Reader.Source, skill.Reader.Snapshot(), skill.Eligible, origin, Token); }
        using var declaration = await workflow.ReviewReferenceDeclarationAsync(skill.Reader.Source, skill.Reader.Snapshot(),
            skill.Eligible, origin, Token);
        return await workflow.ConsentToReferencesAsync(declaration, skill.Eligible, origin, Token);
    }
}
