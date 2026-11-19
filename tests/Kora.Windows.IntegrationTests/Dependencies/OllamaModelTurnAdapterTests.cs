using System.Diagnostics;
using System.Text;
using AwesomeAssertions;
using Kora.Application.Dependencies;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Providers;
using Kora.Core.Skills;
using Kora.Core.Tools;
using Kora.Windows.Dependencies;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Agents.AI;

namespace Kora.Windows.IntegrationTests.Dependencies;

[Collection("Host tracing")]
public sealed class OllamaModelTurnAdapterTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("unguarded")]
    [InlineData("missing-profile")]
    [InlineData("missing-context")]
    [InlineData("closed-host")]
    [InlineData("process-exited")]
    [InlineData("revoked")]
    public async Task MissingExactAuthorityCannotCreateATransportOrUseLegacyReasoner(string state)
    {
        using var f = new WindowsOllamaRuntimeResolverTests.Fixture();
        using var listener = Listen();
        var provenance = new ModelTurnProvenance(new(Guid.NewGuid()), HostRequest.Create(RequestOrigin.LocalUi), f.Selection);
        using var root = state is "missing-context" ? null
            : HostActivity.BeginRoot(provenance.Request, HostActivityLayer.Application, HostOperation.Request);
        var adapter = new OllamaModelTurnAdapter(f.Resolver, f.Value!, f.Time, NullLoggerFactory.Instance,
            _ => throw new InvalidOperationException("No transport may be created."));
        if (state is "process-exited") { f.ProcessCurrent = false; }
        if (state is "revoked") { await f.Revoked.CancelAsync(); }
        var profile = state is "missing-profile" ? null : f.Profile;
        var wire = new ModelContextEnvelope(provenance.Request, "Host policy.", "Request.", [], f.Time.Now,
            f.Time.Now.AddSeconds(15)).Serialize();
        var result = state is "unguarded"
            ? await adapter.RunAsync(provenance, profile, wire, Invoke, Token)
            : await adapter.RunAsync(provenance, profile, wire, Invoke,
                _ => ValueTask.FromResult(state is "closed-host" ? ModelTurnReason.HostAdmissionClosed : ModelTurnReason.None), Token);
        result.Outcome.Should().NotBe(ModelTurnOutcome.Succeeded);
        result.Response.Should().BeNull();
    }

    [Fact]
    public async Task TransportConstructionFailureRevokesTheIssuedBindingAndSurfacesTheFailure()
    {
        using var f = new WindowsOllamaRuntimeResolverTests.Fixture();
        using var listener = Listen();
        var provenance = new ModelTurnProvenance(new(Guid.NewGuid()), HostRequest.Create(RequestOrigin.LocalUi), f.Selection);
        using var root = HostActivity.BeginRoot(provenance.Request, HostActivityLayer.Application, HostOperation.Request);
        WindowsOllamaToolEndpointBinding? captured = null;
        var adapter = new OllamaModelTurnAdapter(f.Resolver, f.Value!, f.Time, NullLoggerFactory.Instance, binding =>
        {
            captured = binding;
            throw new IOException("Construction failed.");
        });
        var action = () => adapter.RunAsync(provenance, f.Profile, ReadOnlyMemory<byte>.Empty, Invoke,
            _ => ValueTask.FromResult(ModelTurnReason.None), Token);
        await action.Should().ThrowAsync<IOException>();
        captured.Should().NotBeNull();
        captured!.Revoked.IsCancellationRequested.Should().BeTrue();
        captured.IsAdmitted().Should().BeFalse();
    }

    [Fact]
    public async Task RegistrationFactoryCannotTurnMissingQualificationIntoBlanketGates()
    {
        using var f = new WindowsOllamaRuntimeResolverTests.Fixture();
        f.Value = null;
        var factory = new OllamaModelProviderFactory(f.Resolver, f.Time, NullLoggerFactory.Instance);
        (await factory.ResolveAsync(f.Selection, f.Profile, Token)).Should().BeNull();
        f.Observations.Should().Be(0);
    }

    [Theory]
    [InlineData("none")]
    [InlineData("host")]
    [InlineData("qualification")]
    public async Task ReviewedToolFreeSkillUsesRealMafAndRevokesItsWindowsBindingWithoutStreaming(string withdrawal)
    {
        using var f = new WindowsOllamaRuntimeResolverTests.Fixture();
        using var listener = Listen();
        var context = OwnedSkillGuidanceContext.Create(
            OwnedDeclarativeSkillDocument.Create("example", "1.0.0", "Example", "Description", "Literal"),
            HostRequest.Create(RequestOrigin.LocalUi), "Original unchanged task", f.Time.Now, f.Time.Now.AddSeconds(5));
        var provenance = new ModelTurnProvenance(new(Guid.NewGuid()), context.Request, f.Selection);
        using var root = HostActivity.BeginRoot(context.Request, HostActivityLayer.Application, HostOperation.Request);
        var admitted = true;
        var transport = new ToolFreeSkillTransport
        {
            DuringModel = async () =>
            {
                if (withdrawal is "host") { admitted = false; }
                if (withdrawal is "qualification") { await f.Revoked.CancelAsync(); }
            },
        };
        WindowsOllamaToolEndpointBinding? captured = null;
        var adapter = new OllamaModelTurnAdapter(f.Resolver, f.Value!, f.Time, NullLoggerFactory.Instance, binding =>
        { captured = binding; return transport; });
        var updates = 0;
        var result = await adapter.RunWithAnswerAsync(provenance, f.Profile, context.Serialize(), Invoke,
            _ => ValueTask.FromResult(admitted ? ModelTurnReason.None : ModelTurnReason.HostAdmissionClosed),
            (_, _) => { updates++; return ValueTask.CompletedTask; }, Token);
        if (withdrawal is "none")
        {
            result.Outcome.Should().Be(ModelTurnOutcome.Succeeded);
            result.Response!.Answer.Should().Be(ToolFreeSkillTransport.Answer);
        }
        else
        {
            result.Outcome.Should().NotBe(ModelTurnOutcome.Succeeded);
            result.Response.Should().BeNull();
        }
        var request = transport.Requests.Should().ContainSingle().Subject;
        request.Profile.Should().Be(f.Profile);
        request.Context.UserRequest.Should().Be(context.UserRequest);
        request.Context.Evidence[1].Content.Should().Be("Literal");
        request.Context.Constraints!.MaximumCalls.Should().Be(0);
        request.Context.Constraints.MaximumRounds.Should().Be(1);
        request.Context.Tools.Should().BeEmpty();
        transport.PackageCalls.Should().Be(1);
        transport.AgentFrameworkBridgeCalls.Should().Be(1);
        transport.StreamingCalls.Should().Be(0);
        updates.Should().Be(0);
        transport.Disposed.Should().BeTrue();
        captured.Should().NotBeNull();
        captured!.Revoked.IsCancellationRequested.Should().BeTrue();
        adapter.IsQuiescent.Should().BeTrue();
    }

    internal sealed class ToolFreeSkillTransport : IModelToolTransport, IModelAnswerTransport
    {
        internal const string Answer = "Genuine final local skill answer.";
        internal List<ModelToolRequest> Requests { get; } = [];
        internal int PackageCalls { get; private set; }
        internal int AgentFrameworkBridgeCalls { get; private set; }
        internal int StreamingCalls { get; private set; }
        internal bool Disposed { get; private set; }
        internal Func<Task>? DuringModel { get; set; }

        public async Task<ModelToolTransportReply> CompleteAsync(ModelToolRequest request, CancellationToken cancellationToken)
        {
            var frames = new StackTrace().GetFrames();
            if (frames.Any(frame => frame.GetMethod()?.DeclaringType?.Assembly == typeof(ChatClientAgent).Assembly))
            { PackageCalls++; }
            if (frames.Any(frame => frame.GetMethod()?.DeclaringType is { } type
                && (type == typeof(AgentFrameworkModelChatClient) || type.DeclaringType == typeof(AgentFrameworkModelChatClient))))
            { AgentFrameworkBridgeCalls++; }
            Requests.Add(request);
            if (DuringModel is { } during) { await during(); }
            var message = ModelToolProtocol.ParseAssistant(
                Encoding.UTF8.GetBytes("""{"kind":"answer","answer":"Genuine final local skill answer."}"""), clarificationAllowed: false);
            return new(ModelTurnOutcome.Succeeded, request.Provenance, message,
                new(ModelToolProtocol.SerializeRequest(request).Length, ModelToolProtocol.SerializeMessage(message).Length));
        }

        public Task<ModelToolTransportReply> CompleteWithAnswerAsync(ModelToolRequest request,
            Func<ModelAnswerUpdate, CancellationToken, ValueTask> publish, CancellationToken cancellationToken)
        {
            StreamingCalls++;
            return CompleteAsync(request, cancellationToken);
        }

        public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
    }

    private static ValueTask<CapabilityReply> Invoke(string id, string input, CancellationToken token) =>
        throw new InvalidOperationException("No tool is admitted by this test.");

    private static ActivityListener Listen()
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
        };
        ActivitySource.AddActivityListener(listener);
        return listener;
    }
}
