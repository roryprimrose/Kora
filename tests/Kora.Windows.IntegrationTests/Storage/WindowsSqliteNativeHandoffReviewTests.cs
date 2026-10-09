using AwesomeAssertions;
using Kora.Application;
using Kora.Application.Dependencies;
using Kora.Application.Interaction;
using Kora.Application.Tools;
using Kora.Core.Auditing;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.Core.Storage;
using Kora.Core.Tools;
using Kora.Tools.Runtime;
using Kora.Windows.IntegrationTests.Audio;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests.Storage;

[Collection(nameof(DurableStorageCompositionTestGroup))]
public sealed class WindowsSqliteNativeHandoffReviewTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExactNativeConsumerUsesPrivateSqliteQuestionIntentAuditTransactionWithoutSend(bool remove)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await AdmitCurrentTask(f);
        using var dependencies = new DependencyBootstrapper([], NullLogger<DependencyBootstrapper>.Instance);
        var adapter = new TestBoundaries();
        await using var host = CreateHost(f, dependencies, adapter);
        var workflow = new ModelProviderHandoffWorkflow(host, f.Questions, adapter,
            NullLogger<ModelProviderHandoffWorkflow>.Instance, new(f.Store, f.Time));
        var source = new ModelHandoffPresentation(workflow);
        ModelHandoffOffer offer = null!;
        await f.RunAsync(async () =>
        {
            var policy = new ModelProviderPolicy(f.Request.SessionId, new(1), ModelProviderMode.LocalFirst,
                ModelProviderSelection.OllamaCandidate, ModelProviderSelection.CopilotCandidate);
            (await host.SetPolicyAsync(policy, f.Token)).Outcome.Should().Be(ModelTurnOutcome.Succeeded);
            var context = new ModelContextEnvelope(f.Request, "exact local system", "exact private original input",
                [new(new(Guid.NewGuid()), f.Request, new(1), "private evidence", ModelEvidenceDisclosure.HostedEligible)],
                f.Time.Now, f.Time.Now.AddMinutes(2));
            offer = (await workflow.OfferAsync(policy, context, ModelHandoffReason.UserRequestedHosted, f.Token)).Offer!;
        });
        var sourceOffers = await source.ReadPending(RequestOrigin.LocalUi, static () => true, f.Token);
        sourceOffers.Should().ContainSingle().Which.Should().BeSameAs(offer);
        await using var review = source.Open(offer, RequestOrigin.LocalUi, static () => true)!;
        await review.Refresh(f.Token);
        review.Preview.Should().Contain("exact private original input").And.Contain("private evidence");
        if (remove)
        {
            await review.Remove(offer, [offer.Context.Evidence[0].Id], f.Token);
            review.HasReviewed.Should().BeFalse();
            await review.Refresh(f.Token);
            var rows = await f.Store.ReadQuestionsAsync(f.Request.SessionId, f.Token);
            rows.Single(row => row.Key.QuestionId == offer.Question.Key.QuestionId).Status.Should().Be(QuestionStatus.Cancelled);
            offer = review.Offer!;
            offer.Context.Evidence.Should().BeEmpty();
        }
        await review.Decide(offer, ModelHandoffDecision.Approve, RequestOrigin.LocalUi, f.Token);
        review.Outcome.Should().Be(ModelHandoffOutcome.Approved);
        (await f.Store.ReadQuestionsAsync(f.Request.SessionId, f.Token))
            .Single(row => row.Key.QuestionId == offer.Question.Key.QuestionId).Status.Should().Be(QuestionStatus.Answered);
        (await f.Store.ReadGrantsAsync(f.Token)).Should().BeEmpty();
        f.Count("security_audit_events").Should().BeGreaterThan(0);
        adapter.Calls.Should().Be(0);
        f.Reopen();
        (await new ModelHandoffPresentation(workflow).ReadPending(RequestOrigin.LocalUi, static () => true, f.Token)).Should().BeEmpty();
        (await f.Store.ReadQuestionsAsync(f.Request.SessionId, f.Token))
            .Single(row => row.Key.QuestionId == offer.Question.Key.QuestionId).Draft!.Choices.Should().Equal("approve");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PrivateTransactionAuditOrCommitFailureCannotPublishNativeApproval(bool beforeCommit)
    {
        using var f = new InteractionStorageFixture();
        var checkpoint = new InteractionTransactionCheckpoint();
        f.Reopen(checkpoint);
        await f.InitializeAsync();
        await AdmitCurrentTask(f);
        using var dependencies = new DependencyBootstrapper([], NullLogger<DependencyBootstrapper>.Instance);
        var boundaries = new TestBoundaries();
        await using var host = CreateHost(f, dependencies, boundaries);
        ModelHandoffOffer offer = null!;
        var workflow = new ModelProviderHandoffWorkflow(host, f.Questions, boundaries,
            NullLogger<ModelProviderHandoffWorkflow>.Instance, new(f.Store, f.Time));
        await f.RunAsync(async () =>
        {
            var policy = new ModelProviderPolicy(f.Request.SessionId, new(1), ModelProviderMode.LocalFirst,
                ModelProviderSelection.OllamaCandidate, ModelProviderSelection.CopilotCandidate);
            await host.SetPolicyAsync(policy, f.Token);
            offer = (await workflow.OfferAsync(policy,
                new(f.Request, "system", "user", [], f.Time.Now, f.Time.Now.AddMinutes(1)),
                ModelHandoffReason.UserRequestedHosted, f.Token)).Offer!;
        });
        var source = new ModelHandoffPresentation(workflow);
        var review = source.Open(offer, RequestOrigin.LocalUi, static () => true)!;
        await review.Refresh(f.Token);
        var auditCount = f.Count("security_audit_events");
        if (beforeCommit) { checkpoint.Commit = static (_, _) => throw new IOException("injected private commit failure"); }
        else { checkpoint.Audit = static (_, _) => throw new IOException("injected private audit failure"); }
        await Assert.ThrowsAsync<IOException>(() => review.Decide(offer, ModelHandoffDecision.Approve, RequestOrigin.LocalUi, f.Token));
        review.Outcome.Should().NotBe(ModelHandoffOutcome.Approved);
        review.Preview.Should().BeEmpty();
        await Assert.ThrowsAsync<IOException>(() => review.DisposeAsync().AsTask());
        f.Count("security_audit_events").Should().Be(auditCount);
        (await f.Store.ReadQuestionsAsync(f.Request.SessionId, f.Token))
            .Single(item => item.Key.QuestionId == offer.Question.Key.QuestionId).Status.Should().Be(QuestionStatus.Pending);
    }

    private static Task AdmitCurrentTask(InteractionStorageFixture f) => f.RunAsync(async () =>
    {
        await f.Store.PublishTrustedSnapshotAsync(f.Request, f.Policy, proposal: null, f.ObservationRevision, f.Token);
        var question = await f.Questions.CreateAsync(f.Request, LocalVersionWait.CreateSpec(),
            f.Time.Now.AddMinutes(5), f.Token);
        await f.Store.AdmitVersionWaitAsync(question.Question!.Key, f.Token);
    });

    private static ModelTurnHost CreateHost(InteractionStorageFixture f, DependencyBootstrapper dependencies, TestBoundaries boundaries)
    {
        var runtime = new RecordedRuntimeObservation(dependencies);
        var tools = new ReadOnlyCapabilityRegistry(boundaries, new(), new(), new(boundaries),
            new(dependencies), new(runtime), new(runtime), NullLogger<ReadOnlyCapabilityRegistry>.Instance);
        return new(f.Store, boundaries, boundaries, tools, boundaries,
            NullLogger<ModelTurnHost>.Instance, f.Time,
            [new(ModelProviderSelection.CopilotCandidate, ModelQualificationGate.DotNetFinalRequest
                | ModelQualificationGate.AllPathLifecycle | ModelQualificationGate.ExecutionAccount
                | ModelQualificationGate.IntegratedHost, f.Time.Now.AddMinutes(5), boundaries)]);
    }

    private sealed class TestBoundaries : ISessionWorkspaceAccess, ICapabilityHostAccess, ISecurityAuditLog,
        IApplicationInfo, IModelTurnAdapter
    {
        public bool CanControl => true;
        public bool CanInspect => true;
        public long ControlRevision => 0;
        public bool IsCurrentHost => true;
        public string Version => "1.0";
        internal int Calls { get; private set; }
        public void Write(SecurityAuditEvent auditEvent) { }
        public Task<ModelProviderReply> RunAsync(ModelTurnProvenance provenance, ReadOnlyMemory<byte> context,
            Func<string, string, CancellationToken, ValueTask<CapabilityReply>> invokeTool, CancellationToken cancellationToken)
        {
            Calls++;
            throw new InvalidOperationException("The review must never dispatch an adapter.");
        }
    }
}
