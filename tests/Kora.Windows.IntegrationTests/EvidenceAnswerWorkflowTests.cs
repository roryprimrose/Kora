using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using Kora.Application.Dependencies;
using Kora.Application.Diagnostics;
using Kora.Application.Diagnostics.EvidenceAnswers;
using Kora.Application.Hosting;
using Kora.Application.Tools;
using Kora.Core.Auditing;
using Kora.Core.Context.EvidenceAnswers;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Providers;
using Kora.Core.Storage;
using Kora.Core.Tools;
using Kora.EvidenceAnswers;
using Kora.Windows.IntegrationTests.Storage;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

[Collection("Host tracing")]
public sealed class EvidenceAnswerWorkflowTests
{
    [Theory]
    [InlineData("success", EvidenceAnswerOutcome.Succeeded)]
    [InlineData("unsupported", EvidenceAnswerOutcome.Unsupported)]
    [InlineData("foreign-citation", EvidenceAnswerOutcome.InvalidResponse)]
    [InlineData("unqualified", EvidenceAnswerOutcome.Unavailable)]
    [InlineData("source-unavailable", EvidenceAnswerOutcome.Unavailable)]
    [InlineData("source-denied", EvidenceAnswerOutcome.Denied)]
    public async Task RealHostCreatesOnlyFreshReviewedLocalConversationAndRetainsOnlyValidatedFinal(
        string mode, EvidenceAnswerOutcome expected)
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.Tasks.InitializeAsync(fixture.Token);
        await fixture.Store.InitializeAsync(fixture.Token);
        var controls = new Controls { Mode = mode };
        var adapter = new Adapter { Mode = mode };
        using var dependencies = new DependencyBootstrapper([], NullLogger<DependencyBootstrapper>.Instance);
        var runtimes = new Kora.Tools.Runtime.RecordedRuntimeObservation(dependencies);
        var registry = new ReadOnlyCapabilityRegistry(controls, new(), new(), new(controls), new(dependencies),
            new(runtimes), new(runtimes), NullLogger<ReadOnlyCapabilityRegistry>.Instance);
        var gates = mode is "unqualified" ? ModelQualificationGate.None
            : ModelQualificationGate.LocalCandidateSelection | ModelQualificationGate.IntegratedHost;
        await using var host = new ModelTurnHost(fixture.Store, controls, controls, registry, controls,
            NullLogger<ModelTurnHost>.Instance, fixture.Time,
            [new(ModelProviderSelection.OllamaCandidate, gates, fixture.Time.GetUtcNow().AddMinutes(1), adapter)]);
        var sessions = new SessionWorkspaceService(fixture.Store, new(fixture.Tasks), controls,
            NullLogger<SessionWorkspaceService>.Instance);
        await using var conversations = new SessionConversationService(fixture.Store, fixture.Store, sessions,
            controls, new Admission(host), fixture.Time, NullLogger<SessionConversationService>.Instance, host);
        var action = new EvidenceAnswerWorkflow(controls, new EvidenceAnswerConversation(sessions, conversations, host),
            controls, controls, fixture.Time, NullLogger<EvidenceAnswerWorkflow>.Instance);
        var now = fixture.Time.GetUtcNow();
        var sourceHost = HostRequest.Create(RequestOrigin.HostSystem);
        var record = new EvidenceRecord(new(EvidenceSource.Log, new(Guid.NewGuid())), now, now.AddHours(1),
            EvidenceSegmentStatus.Present, sourceHost, null, null, null, "Information", "Example", 42,
            "private-diagnostic-only-marker-40915717", new Dictionary<string, EvidenceValue>(StringComparer.Ordinal),
            null, null, []);
        var serialized = Encoding.UTF8.GetString(DurableEvidenceQuery.Serialize(
            new(EvidencePageStatus.Available, [record], null, [], EvidencePage.StorageDisclosure)));
        var review = new EvidenceAnswerReview(serialized, "What was recorded?", HostRequest.Create(RequestOrigin.LocalUi), now);
        adapter.Citation = record.Reference.Citation;
        using var root = HostActivity.BeginRoot(review.Original, HostActivityLayer.Desktop, HostOperation.Request);
        fixture.Count("work_sessions").Should().Be(0);
        adapter.Calls.Should().Be(0);
        var result = await action.AnswerAsync(review, () => true, fixture.Token);
        result.Outcome.Should().Be(expected, "{0} with receipt reason {1}", mode, result.Receipt?.Message.Reason);
        var refused = mode is "source-unavailable" or "source-denied";
        adapter.Calls.Should().Be(refused || mode is "unqualified" ? 0 : 1);
        fixture.Count("work_sessions").Should().Be(refused ? 0 : 1);
        fixture.Count("conversation_messages").Should().Be(refused ? 0 : 1);
        fixture.Count("scoped_grants").Should().Be(0);
        fixture.Count("host_questions").Should().Be(0);
        host.IsQuiescent.Should().BeTrue();
        if (adapter.Wire is { } wire)
        {
            using var json = JsonDocument.Parse(wire);
            json.RootElement.GetProperty("UserRequest").GetString().Should().Be(review.Question);
            json.RootElement.GetProperty("Evidence")[0].GetProperty("Content").GetString().Should().Be(serialized);
            json.RootElement.GetProperty("Evidence").GetArrayLength().Should().Be(1);
            json.RootElement.GetProperty("Tools").GetArrayLength().Should().Be(0);
        }
        if (result.Receipt is { } receipt)
        {
            receipt.Message.Request.SessionId.Should().Be(review.Destination.SessionId).And.NotBe(sourceHost.SessionId);
            receipt.Message.Request.Origin.Should().Be(RequestOrigin.LocalUi);
            receipt.Message.Generation.Value.Should().BeGreaterThan(0);
            fixture.Reopen();
            await fixture.Store.InitializeAsync(fixture.Token);
            var actual = await fixture.Store.ReadConversationMessageAsync(receipt.Message.Request.SessionId,
                receipt.Message.Request.RequestId, fixture.Token);
            actual.Should().Be(receipt.Message);
            actual.Body.Should().Be(review.Question);
            if (expected is EvidenceAnswerOutcome.Succeeded)
            {
                actual.AssistantResponse!.Body.Should().Contain(EvidenceAnswerResponse.Disclaimer)
                    .And.Contain("Recorded fact (model claim)").And.Contain("Inference (not a recorded fact)")
                    .And.Contain(record.Reference.Citation);
            }
            else if (expected is EvidenceAnswerOutcome.Unsupported)
            { actual.AssistantResponse!.Body.Should().Be(EvidenceAnswerResponse.Unsupported); }
            else { actual.AssistantResponse.Should().BeNull(); }
        }
        (await action.AnswerAsync(review, () => true, fixture.Token)).Outcome.Should().Be(EvidenceAnswerOutcome.Denied);
        foreach (var file in Directory.EnumerateFiles(fixture.Paths.LocalRoot, "*", SearchOption.AllDirectories))
        {
            Encoding.UTF8.GetString(await File.ReadAllBytesAsync(file, fixture.Token))
                .Should().NotContain("private-diagnostic-only-marker-40915717");
        }
        string.Join(' ', controls.Audits.Select(item => item.ActionId + item.TargetId + item.ReasonCode))
            .Should().NotContain(review.Question).And.NotContain("private-diagnostic");
    }

    private sealed class Admission(ModelTurnHost host) : IModelTurnAdmission
    {
        public bool IsQuiescent => host.IsQuiescent;
        public Task<ModelTurnAdmission> AdmitAsync(ModelTurnChoice choice, ModelContextEnvelope context,
            ModelHandoffReview? review, CancellationToken token)
        {
            choice.Should().Be(ModelTurnChoice.Local);
            context.ToolMode.Should().Be(ModelToolMode.None);
            return host.AdmitAsync(ModelProviderSelection.OllamaCandidate, context, token);
        }
    }

    private sealed class Controls : ISessionWorkspaceAccess, ICapabilityHostAccess, ISecurityAuditLog,
        IApplicationInfo, IEvidenceAnswerSourceUsePolicy
    {
        internal string Mode { get; init; } = "success";
        public bool CanInspect => true;
        public bool CanControl => true;
        public long ControlRevision => 1;
        public bool IsCurrentHost => true;
        public string Version => "1.0";
        internal List<SecurityAuditEvent> Audits { get; } = [];
        public void Write(SecurityAuditEvent auditEvent) => Audits.Add(auditEvent);
        public Task<EvidenceSourceUseDecision> AdmitAsync(EvidenceAnswerReview review, CancellationToken token) =>
            Task.FromResult(Mode is "source-unavailable" ? EvidenceSourceUseDecision.Unavailable
                : Mode is "source-denied" ? EvidenceSourceUseDecision.Denied : EvidenceSourceUseDecision.Allowed);
    }

    private sealed class Adapter : IModelTurnAdapter
    {
        internal string Mode { get; init; } = "success";
        internal string Citation { get; set; } = string.Empty;
        internal int Calls { get; private set; }
        internal byte[]? Wire { get; private set; }
        public Task<ModelProviderReply> RunAsync(ModelTurnProvenance provenance, ProviderProfileTuple? profile,
            ReadOnlyMemory<byte> context, Func<string, string, CancellationToken, ValueTask<CapabilityReply>> invokeTool,
            CancellationToken cancellationToken)
        {
            Calls++;
            Wire = context.ToArray();
            var answer = Mode is "unsupported" ? "{\"unsupported\":true,\"claims\":[]}" : JsonSerializer.Serialize(new
            {
                unsupported = false,
                claims = new[]
                {
                    new { kind = "fact", text = "A diagnostic is recorded.", citations = new[] { Mode is "foreign-citation" ? "foreign" : Citation } },
                    new { kind = "inference", text = "The event might explain the observation.", citations = new[] { Citation } },
                },
            });
            return Task.FromResult(new ModelProviderReply(ModelTurnOutcome.Succeeded, provenance.Selection, new(answer, null)));
        }
    }
}
