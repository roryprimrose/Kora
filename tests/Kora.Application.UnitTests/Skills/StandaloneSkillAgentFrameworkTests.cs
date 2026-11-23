using System.Diagnostics;
using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using Kora.Application.Agents;
using Kora.Application.Dependencies;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Skills;
using Kora.Core.Tools;
using Microsoft.Agents.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Application.UnitTests.Skills;

[Collection("Host tracing")]
public sealed class StandaloneSkillAgentFrameworkTests : IDisposable
{
    private readonly ActivityListener listener = new()
    {
        ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
        Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
    };

    public StandaloneSkillAgentFrameworkTests() => ActivitySource.AddActivityListener(listener);
    public void Dispose() => listener.Dispose();
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(RequestOrigin.LocalUi)]
    [InlineData(RequestOrigin.ActivatedVoice)]
    public async Task AgentGuidanceKeepsExactSourcesSeparateFromStandaloneExecutionBudgets(RequestOrigin origin)
    {
        await using var f = new SharedSkillAgentGuidanceLeaseTests.Fixture();
        await f.Register();
        var snapshot = f.Reader.Snapshot();
        var resolver = new AgentSkillReferenceResolver(f.Discovery, f.Workflow);
        using var guidance = resolver.Review(SharedSkillAgentGuidanceLeaseTests.Profile(snapshot.SourceQualifiedIdentity));
        await guidance.DeclareAsync(f.Reader.Source, snapshot, f.Eligible, origin, Token);
        f.Reader.Reads.Should().Be(0);
        await guidance.ConsentAsync(f.Eligible, origin, Token);
        var capturedReads = f.Reader.Reads;
        var source = $"PRIMARY {snapshot.RelativeFile}\n{f.Reader.Primary}\n\nREFERENCE notes.md\n{f.Reader.Reference}";
        guidance.CompleteText.Should().Be(source);
        guidance.Snapshot.Bytes.ToArray().Should().Equal(snapshot.Bytes.ToArray());
        guidance.Snapshot.ReferenceBundle!.References.Single().Text.Should().Be(f.Reader.Reference);
        f.Reader.Reads.Should().Be(capturedReads);
        await guidance.EnableAsync(f.Eligible, origin, Token);
        await guidance.AcquireAsync(f.Eligible, origin, Token);
        guidance.Lease!.IsCurrent.Should().BeTrue();
        using var standalone = await f.Capture(origin);
        var standaloneReads = f.Reader.Reads;
        standalone.Text.Should().Be($"{source}\n\nHOST EXECUTION CONSTRAINTS (not skill source)\n"
            + JsonSerializer.Serialize(ToolFreeSkillConstraints.Instance));
        f.Reader.Reads.Should().Be(standaloneReads);
        f.Open = false;
        guidance.CompleteText.Should().BeEmpty();
        standalone.Text.Should().BeEmpty();
        standalone.SourceText.Should().BeEmpty();
        f.Reader.Reads.Should().Be(standaloneReads);
        f.Audit.Events.Should().NotContain(item => item.ActionId == "skills.revision.invoke");
    }

    [Theory]
    [InlineData("application.get_version")]
    [InlineData("kora.ask_user")]
    [InlineData("shell.run")]
    public async Task ZeroToolSkillRejectsEveryCallbackWithoutInvokingOrContinuing(string tool)
    {
        var clock = new SkillClock();
        var transport = new StandaloneSkillTransport
        {
            Message = """{"kind":"toolCalls","calls":[{"callId":"one","capabilityId":"TOOL","input":{}}]}"""
                .Replace("TOOL", tool, StringComparison.Ordinal),
        };
        await using var adapter = new BoundedModelTurnAdapter(transport, clock, NullLogger<BoundedModelTurnAdapter>.Instance);
        var context = Context(clock);
        var provenance = new ModelTurnProvenance(new(Guid.NewGuid()), context.Request, ModelProviderSelection.OllamaCandidate);
        using var root = HostActivity.BeginRoot(context.Request, HostActivityLayer.Application, HostOperation.Request);
        var callbacks = 0;
        var result = await adapter.RunAsync(provenance, null, context.Serialize(), (_, _, _) =>
        {
            callbacks++;
            throw new InvalidOperationException("No callback may execute.");
        }, _ => ValueTask.FromResult(ModelTurnReason.None), Token);
        result.Outcome.Should().NotBe(ModelTurnOutcome.Succeeded);
        result.Response.Should().BeNull();
        callbacks.Should().Be(0);
        transport.Requests.Should().ContainSingle();
        transport.PackageCalls.Should().Be(1);
    }

    [Theory]
    [InlineData("question")]
    [InlineData("output")]
    [InlineData("input-usage")]
    [InlineData("output-usage")]
    [InlineData("deadline")]
    public async Task InvalidOrExhaustedPackageReplyNeverBecomesAnAnswer(string fault)
    {
        var clock = new SkillClock();
        var transport = new StandaloneSkillTransport();
        if (fault is "question")
        { transport.Message = """{"kind":"question","prompt":"Choose","options":["A","B"]}"""; }
        if (fault is "output")
        { transport.Message = JsonSerializer.Serialize(new { kind = "answer", answer = new string('x', 4097) }); }
        transport.Transform = (request, reply) =>
        {
            if (fault is "deadline") { clock.Now += TimeSpan.FromSeconds(15); }
            return fault switch
            {
                "input-usage" => reply with { Usage = reply.Usage! with { InputUtf8Bytes = request.Budget.InputUtf8Bytes + 1 } },
                "output-usage" => reply with { Usage = reply.Usage! with { OutputUtf8Bytes = request.Budget.OutputUtf8Bytes + 1 } },
                _ => reply,
            };
        };
        await using var adapter = new BoundedModelTurnAdapter(transport, clock, NullLogger<BoundedModelTurnAdapter>.Instance);
        var context = Context(clock);
        var provenance = new ModelTurnProvenance(new(Guid.NewGuid()), context.Request, ModelProviderSelection.OllamaCandidate);
        using var root = HostActivity.BeginRoot(context.Request, HostActivityLayer.Application, HostOperation.Request);
        var result = await adapter.RunAsync(provenance, null, context.Serialize(), NoTool,
            _ => ValueTask.FromResult(ModelTurnReason.None), Token);
        result.Outcome.Should().NotBe(ModelTurnOutcome.Succeeded);
        result.Response.Should().BeNull();
        transport.Requests.Should().ContainSingle();
        transport.PackageCalls.Should().Be(1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MaintainedExecutionBuffersSelectedSkillAndStreamsOrdinaryToolFreeRoute(bool skill)
    {
        var clock = new SkillClock();
        var transport = new StandaloneSkillTransport();
        await using var adapter = new BoundedModelTurnAdapter(transport, clock, NullLogger<BoundedModelTurnAdapter>.Instance);
        var selected = Context(clock);
        var context = skill ? selected : new ModelContextEnvelope(selected.Request, "Ordinary authoring policy", "Original",
            [], clock.Now, clock.Now.AddSeconds(15), ModelToolMode.None);
        var provenance = new ModelTurnProvenance(new(Guid.NewGuid()), context.Request, ModelProviderSelection.OllamaCandidate);
        using var root = HostActivity.BeginRoot(context.Request, HostActivityLayer.Application, HostOperation.Request);
        var updates = new List<ModelAnswerUpdate>();
        var result = await adapter.RunWithAnswerAsync(provenance, null, context.Serialize(), NoTool,
            _ => ValueTask.FromResult(ModelTurnReason.None), (update, _) => { updates.Add(update); return ValueTask.CompletedTask; }, Token);
        result.Outcome.Should().Be(ModelTurnOutcome.Succeeded);
        result.Response!.Answer.Should().Be(StandaloneSkillTransport.Answer);
        transport.PackageCalls.Should().Be(1);
        transport.StreamingCalls.Should().Be(skill ? 0 : 1);
        updates.Should().Equal(skill ? [] : [new ModelAnswerUpdate(provenance, StandaloneSkillTransport.Answer)]);
    }

    private static ModelContextEnvelope Context(SkillClock clock) => OwnedSkillGuidanceContext.Create(
        OwnedDeclarativeSkillDocument.Create("example", "1.0.0", "Example", "Description", "Literal"),
        HostRequest.Create(RequestOrigin.LocalUi), "Original", clock.Now, clock.Now.AddSeconds(15));

    private static ValueTask<CapabilityReply> NoTool(string id, string input, CancellationToken token) =>
        throw new InvalidOperationException("No tool is authorized.");

    private sealed class SkillClock : TimeProvider
    {
        internal DateTimeOffset Now { get; set; } = DateTimeOffset.UnixEpoch;
        public override DateTimeOffset GetUtcNow() => Now;
    }
    internal sealed class StandaloneSkillTransport : IModelToolTransport, IModelAnswerTransport
    {
        internal const string Answer = "Exact final local skill answer.";
        internal string Message { get; set; } = """{"kind":"answer","answer":"Exact final local skill answer."}""";
        internal List<ModelToolRequest> Requests { get; } = [];
        internal int PackageCalls { get; private set; }
        internal int StreamingCalls { get; private set; }
        internal CancellationToken ModelToken { get; private set; }
        internal Func<Task>? DuringModel { get; set; }
        internal Func<ModelToolRequest, ModelToolTransportReply, ModelToolTransportReply>? Transform { get; set; }

        public async Task<ModelToolTransportReply> CompleteAsync(ModelToolRequest request, CancellationToken cancellationToken)
        {
            // Observe the executing package, not merely the selector's constraint values.
            if (new StackTrace().GetFrames().Any(frame => frame.GetMethod()?.DeclaringType?.Assembly == typeof(ChatClientAgent).Assembly))
            { PackageCalls++; }
            Requests.Add(request);
            ModelToken = cancellationToken;
            if (DuringModel is { } during) { await during(); }
            ModelAssistantMessage message;
            try { message = ModelToolProtocol.ParseAssistant(Encoding.UTF8.GetBytes(Message), clarificationAllowed: true); }
            catch (InvalidDataException) { return new(ModelTurnOutcome.Unknown, request.Provenance, null); }
            var reply = new ModelToolTransportReply(ModelTurnOutcome.Succeeded, request.Provenance, message,
                new(ModelToolProtocol.SerializeRequest(request).Length, ModelToolProtocol.SerializeMessage(message).Length));
            return Transform?.Invoke(request, reply) ?? reply;
        }

        public Task<ModelToolTransportReply> CompleteWithAnswerAsync(ModelToolRequest request,
            Func<ModelAnswerUpdate, CancellationToken, ValueTask> publish, CancellationToken cancellationToken)
        {
            StreamingCalls++;
            return CompleteAsync(request, cancellationToken);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
