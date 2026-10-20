using System.Diagnostics;
using System.Text;
using AwesomeAssertions;
using Kora.Application.Dependencies;
using Kora.Core.Auditing;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Tools;

namespace Kora.Application.UnitTests.Dependencies;

public sealed partial class ModelTurnHostTests
{
    private const string VersionCall =
        """{"kind":"toolCalls","calls":[{"callId":"version_1","capabilityId":"application.get_version","input":{}}]}""";
    private const string FinalAnswer = """{"kind":"answer","answer":"The observed version is 1.0."}""";

    [Fact]
    public async Task Qualified_fake_model_receives_exact_real_host_version_and_catalogue_then_returns_genuine_answer()
    {
        using var fixture = new Fixture();
        using var root = fixture.Root();
        var transport = new ToolTransport(VersionCall,
            """{"kind":"toolCalls","calls":[{"callId":"catalogue_2","capabilityId":"capabilities.list","input":{}}]}""",
            FinalAnswer);
        await using var loop = CreateLoop(fixture, transport);
        var host = fixture.LoopHost(loop);
        var turn = (await host.AdmitAsync(ModelProviderSelection.OllamaCandidate, fixture.Context(), Token)).Turn!;
        var result = await host.RunAsync(turn, Token);
        result.Outcome.Should().Be(ModelTurnOutcome.Succeeded);
        result.Reason.Should().Be(ModelTurnReason.None);
        result.Response.Should().Be(new LocalModelResponse("The observed version is 1.0.", null));
        result.Provenance.Should().BeSameAs(turn.Provenance);
        transport.Requests.Should().HaveCount(3);
        transport.Requests[0].Messages.Should().BeEmpty();
        transport.Requests[0].Context.UserRequest.Should().Be("request-secret");
        transport.Requests[0].Context.Wire.Should().Equal(fixture.Context().Serialize());
        transport.Requests.Should().OnlyContain(request => ReferenceEquals(request.Provenance, turn.Provenance));
        var version = (ModelToolResultMessage)transport.Requests[1].Messages[1];
        version.Result.Should().Equal(CapabilityReplyWire.Serialize(new(CapabilityOutcome.Succeeded, "observed",
            Version: new("1.0", "Not observed by the current version provider."))));
        version.CallId.Should().Be("version_1");
        version.CapabilityId.Should().Be(ReadOnlyCapabilityCatalog.Version);
        transport.Requests[2].Messages.Should().HaveCount(4);
        var catalogue = (ModelToolResultMessage)transport.Requests[2].Messages[3];
        catalogue.Result.Should().Equal(CapabilityReplyWire.Serialize(new(CapabilityOutcome.Succeeded, "admitted-read-only",
            Capabilities: new(ReadOnlyCapabilityCatalog.Descriptors.ToArray(), 6, null))));
        (await host.RunAsync(turn, Token)).Reason.Should().Be(ModelTurnReason.TurnAlreadyUsed);
        transport.Requests.Should().HaveCount(3);
        transport.HostRequests.Should().OnlyContain(request => ReferenceEquals(request, fixture.Request));
        transport.TraceIds.Should().OnlyContain(trace => trace == root.Activity!.TraceId);
        fixture.Audits.Select(item => item.Outcome).Should().Equal(SecurityAuditOutcome.Requested, SecurityAuditOutcome.Succeeded,
            SecurityAuditOutcome.Requested, SecurityAuditOutcome.Succeeded);
        loop.IsQuiescent.Should().BeTrue();
        host.IsQuiescent.Should().BeTrue();
        HostActivity.Current.Should().BeSameAs(root);
    }

    [Fact]
    public async Task Complete_multi_call_message_executes_allowed_members_serially_once_with_original_host_context()
    {
        using var fixture = new Fixture();
        using var root = fixture.Root();
        var versions = 0;
        fixture.OnVersion = () => versions++;
        var transport = new ToolTransport(
            """{"kind":"toolCalls","calls":[{"callId":"one","capabilityId":"application.get_version","input":{}},{"callId":"two","capabilityId":"capabilities.get","input":{"id":"application.get_version"}},{"callId":"three","capabilityId":"runtime.get_status","input":{"id":"local.inference"}}]}""",
            FinalAnswer);
        await using var loop = CreateLoop(fixture, transport);
        (await RunLoop(fixture, fixture.LoopHost(loop))).Outcome.Should().Be(ModelTurnOutcome.Succeeded);
        versions.Should().Be(1);
        var history = transport.Requests[1].Messages;
        history.Should().HaveCount(4);
        history.Skip(1).Cast<ModelToolResultMessage>().Select(result => result.CallId).Should().Equal("one", "two", "three");
        transport.Requests[1].Budget.InputUtf8Bytes.Should().BeLessThan(ModelToolProtocol.MaximumInputUtf8Bytes);
        transport.Requests[1].Budget.OutputUtf8Bytes.Should().BeLessThan(ModelToolProtocol.MaximumOutputUtf8Bytes);
    }

    [Theory]
    [InlineData("""{"kind":"action","action":"LockMachine"}""")]
    [InlineData("""{"kind":"grantChange","approved":true}""")]
    [InlineData("""{"kind":"question","prompt":"Choose"}""")]
    [InlineData("""{"kind":"answer","answer":"fake","calls":[]}""")]
    [InlineData("""{"kind":"toolCalls","calls":[{"callId":"one","capabilityId":"application.get_version","input":{}},{"callId":"two","capabilityId":"computer.lock","input":{}}]}""")]
    [InlineData("""{"kind":"toolCalls","calls":[{"callId":"one","capabilityId":"application.get_version","input":{}},{"callId":"two","capabilityId":"application.get_version","input":{"sessionId":"foreign"}}]}""")]
    [InlineData("""{"kind":"toolCalls","calls":[{"callId":"one","capabilityId":"application.get_version","input":{}},{"callId":"one","capabilityId":"application.get_version","input":{}}]}""")]
    [InlineData("""{"kind":"toolCalls","calls":[{"callId":"one","capabilityId":"application.get_version","input":{"path":"C:\\secret"}}]}""")]
    [InlineData("""{"kind":"answer","answer":"\uD800"}""")]
    [InlineData("""{"kind":"answer","answer":"\u0000"}""")]
    public async Task Entire_hostile_message_is_rejected_before_any_valid_prefix_call(string message)
    {
        using var fixture = new Fixture();
        using var root = fixture.Root();
        var versions = 0;
        fixture.OnVersion = () => versions++;
        var transport = new ToolTransport(message);
        await using var loop = CreateLoop(fixture, transport);
        var result = await RunLoop(fixture, fixture.LoopHost(loop));
        result.Outcome.Should().Be(ModelTurnOutcome.Unknown);
        result.Reason.Should().Be(ModelTurnReason.InvalidProviderResponse);
        result.Response.Should().BeNull();
        versions.Should().Be(0);
        transport.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task Replayed_provider_call_id_cannot_execute_twice_or_trigger_automatic_repair()
    {
        using var fixture = new Fixture();
        using var root = fixture.Root();
        var versions = 0;
        fixture.OnVersion = () => versions++;
        var transport = new ToolTransport(VersionCall, VersionCall, FinalAnswer);
        await using var loop = CreateLoop(fixture, transport);
        var result = await RunLoop(fixture, fixture.LoopHost(loop));
        result.Reason.Should().Be(ModelTurnReason.InvalidProviderResponse);
        result.Response.Should().BeNull();
        versions.Should().Be(1);
        transport.Requests.Should().HaveCount(2);
    }

    [Theory]
    [InlineData("turn")]
    [InlineData("request")]
    [InlineData("provider")]
    [InlineData("model")]
    [InlineData("revision")]
    [InlineData("outcome")]
    [InlineData("missing-message")]
    [InlineData("failure-with-message")]
    [InlineData("missing-usage")]
    [InlineData("zero-input")]
    [InlineData("negative-output")]
    [InlineData("understated-input")]
    [InlineData("overstated-input")]
    [InlineData("overstated-output")]
    [InlineData("null")]
    public async Task Corrupt_transport_provenance_outcomes_and_byte_counters_never_dispatch_tools(string fault)
    {
        using var fixture = new Fixture();
        using var root = fixture.Root();
        var versions = 0;
        fixture.OnVersion = () => versions++;
        var transport = new ToolTransport(VersionCall)
        {
            ChangeReply = (request, reply) => fault switch
            {
                "turn" => reply with { Provenance = request.Provenance with { Turn = new(Guid.NewGuid()) } },
                "request" => reply with { Provenance = request.Provenance with { Request = HostRequest.Create(RequestOrigin.LocalUi) } },
                "provider" => reply with { Provenance = request.Provenance with { Selection = request.Provenance.Selection with { Provider = ModelProviderIdentity.Copilot } } },
                "model" => reply with { Provenance = request.Provenance with { Selection = request.Provenance.Selection with { Model = new(Guid.NewGuid()) } } },
                "revision" => reply with { Provenance = request.Provenance with { Selection = request.Provenance.Selection with { Revision = new(2) } } },
                "outcome" => reply with { Outcome = (ModelTurnOutcome)999 },
                "missing-message" => reply with { Message = null },
                "failure-with-message" => reply with { Outcome = ModelTurnOutcome.Unavailable },
                "missing-usage" => reply with { Usage = null },
                "zero-input" => reply with { Usage = reply.Usage! with { InputUtf8Bytes = 0 } },
                "negative-output" => reply with { Usage = reply.Usage! with { OutputUtf8Bytes = -1 } },
                "understated-input" => reply with { Usage = reply.Usage! with { InputUtf8Bytes = reply.Usage.InputUtf8Bytes - 1 } },
                "overstated-input" => reply with { Usage = reply.Usage! with { InputUtf8Bytes = request.Budget.InputUtf8Bytes + 1 } },
                "overstated-output" => reply with { Usage = reply.Usage! with { OutputUtf8Bytes = request.Budget.OutputUtf8Bytes + 1 } },
                _ => null!,
            },
        };
        await using var loop = CreateLoop(fixture, transport);
        var result = await RunLoop(fixture, fixture.LoopHost(loop));
        result.Reason.Should().Be(ModelTurnReason.InvalidProviderResponse);
        result.Response.Should().BeNull();
        versions.Should().Be(0);
    }

    [Theory]
    [InlineData(ModelTurnOutcome.Unavailable)]
    [InlineData(ModelTurnOutcome.AuthenticationRequired)]
    [InlineData(ModelTurnOutcome.QuotaExceeded)]
    [InlineData(ModelTurnOutcome.TimedOut)]
    [InlineData(ModelTurnOutcome.Cancelled)]
    [InlineData(ModelTurnOutcome.Denied)]
    [InlineData(ModelTurnOutcome.DeniedEgress)]
    [InlineData(ModelTurnOutcome.Unknown)]
    public async Task Transport_unavailability_or_failure_is_terminal_without_fallback_or_echo(ModelTurnOutcome outcome)
    {
        using var fixture = new Fixture();
        using var root = fixture.Root();
        var transport = new ToolTransport(VersionCall)
        { ChangeReply = (request, _) => new(outcome, request.Provenance) };
        await using var loop = CreateLoop(fixture, transport);
        var result = await RunLoop(fixture, fixture.LoopHost(loop));
        result.Outcome.Should().Be(outcome);
        result.Response.Should().BeNull();
        transport.Requests.Should().ContainSingle();
    }

    [Theory]
    [InlineData("privacy", ModelTurnReason.HostAdmissionClosed)]
    [InlineData("owner", ModelTurnReason.HostAdmissionClosed)]
    [InlineData("generation", ModelTurnReason.SessionNotAdmitted)]
    [InlineData("task", ModelTurnReason.SessionNotAdmitted)]
    [InlineData("expiry", ModelTurnReason.ContextExpired)]
    [InlineData("qualification", ModelTurnReason.QualificationPending)]
    public async Task Host_authority_is_revalidated_before_each_continuation_and_each_tool(string fault, ModelTurnReason expected)
    {
        using var fixture = new Fixture();
        using var root = fixture.Root();
        var versions = 0;
        fixture.OnVersion = () => versions++;
        var transport = new ToolTransport(VersionCall, FinalAnswer)
        {
            AfterRequest = _ =>
            {
                switch (fault)
                {
                    case "privacy": fixture.CanControl = false; break;
                    case "owner": fixture.Current = false; break;
                    case "generation": fixture.Session = fixture.Session with { Generation = new(2) }; break;
                    case "task": fixture.Task = fixture.Task! with { CurrentSource = false }; break;
                    case "expiry": fixture.Time.Now += TimeSpan.FromMinutes(1); break;
                    case "qualification": fixture.Time.Now += TimeSpan.FromSeconds(30); break;
                }
            },
        };
        await using var loop = CreateLoop(fixture, transport);
        var result = await RunLoop(fixture, fixture.LoopHost(loop));
        result.Reason.Should().Be(expected);
        result.Response.Should().BeNull();
        versions.Should().Be(0);
        transport.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task Authority_withdrawal_during_real_tool_prevents_result_egress_and_further_members()
    {
        using var fixture = new Fixture();
        using var root = fixture.Root();
        var versions = 0;
        fixture.OnVersion = () => { versions++; fixture.ControlRevision++; };
        var transport = new ToolTransport(
            """{"kind":"toolCalls","calls":[{"callId":"one","capabilityId":"application.get_version","input":{}},{"callId":"two","capabilityId":"application.get_version","input":{}}]}""",
            FinalAnswer);
        await using var loop = CreateLoop(fixture, transport);
        var result = await RunLoop(fixture, fixture.LoopHost(loop));
        result.Reason.Should().Be(ModelTurnReason.HostAdmissionClosed);
        result.Response.Should().BeNull();
        versions.Should().Be(1);
        transport.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task Tool_failure_is_truthful_terminal_data_not_a_provider_retry_or_a_fake_answer()
    {
        using var fixture = new Fixture();
        using var root = fixture.Root();
        var transport = new ToolTransport(
            """{"kind":"toolCalls","calls":[{"callId":"page_1","capabilityId":"runtime.list","input":{"offset":1}}]}""",
            FinalAnswer);
        await using var loop = CreateLoop(fixture, transport);
        var result = await RunLoop(fixture, fixture.LoopHost(loop));
        result.Outcome.Should().Be(ModelTurnOutcome.Denied);
        result.Reason.Should().Be(ModelTurnReason.ToolResultRejected);
        result.Response.Should().BeNull();
        transport.Requests.Should().ContainSingle();
    }

    [Theory]
    [InlineData("input")]
    [InlineData("output")]
    [InlineData("calls")]
    public async Task Cumulative_full_byte_and_call_exhaustion_stop_without_an_extra_attempt(string budget)
    {
        using var fixture = new Fixture();
        using var root = fixture.Root();
        var versions = 0;
        fixture.OnVersion = () => versions++;
        var transport = new ToolTransport(Enumerable.Range(1, 7).Select(index =>
            VersionCall.Replace("version_1", "version_" + index, StringComparison.Ordinal)).ToArray());
        if (budget is not "calls")
        {
            transport.ChangeReply = (request, reply) => reply with
            {
                Usage = budget is "input"
                    ? reply.Usage! with { InputUtf8Bytes = request.Budget.InputUtf8Bytes }
                    : reply.Usage! with { OutputUtf8Bytes = request.Budget.OutputUtf8Bytes },
            };
        }
        await using var loop = CreateLoop(fixture, transport);
        var result = await RunLoop(fixture, fixture.LoopHost(loop));
        result.Reason.Should().Be(ModelTurnReason.ToolLoopBudgetExceeded);
        result.Response.Should().BeNull();
        transport.Requests.Should().HaveCount(budget is "calls" ? 7 : 1);
        versions.Should().Be(budget is "calls" ? 6 : 1);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Caller_cancel_or_deadline_keeps_noncooperative_transport_owned_until_actual_termination(bool deadline, bool fault)
    {
        using var fixture = new Fixture();
        using var root = fixture.Root();
        using var cancellation = new CancellationTokenSource();
        var pending = new TaskCompletionSource<ModelToolTransportReply>(TaskCreationOptions.RunContinuationsAsynchronously);
        var transport = new ToolTransport(VersionCall) { Pending = pending.Task };
        await using var loop = CreateLoop(fixture, transport);
        var host = fixture.LoopHost(loop);
        var run = RunLoop(fixture, host, cancellation.Token);
        transport.Requests.Should().ContainSingle();
        if (deadline) { fixture.Time.FireDeadline(); }
        else { cancellation.Cancel(); }
        var result = await run;
        result.Outcome.Should().Be(deadline ? ModelTurnOutcome.TimedOut : ModelTurnOutcome.Cancelled);
        result.Response.Should().BeNull();
        host.IsQuiescent.Should().BeFalse();
        loop.IsQuiescent.Should().BeFalse();
        (await RunLoop(fixture, host)).Reason.Should().Be(ModelTurnReason.ProviderBusy);
        var hostClose = host.DisposeAsync();
        var loopClose = loop.DisposeAsync();
        hostClose.IsCompleted.Should().BeFalse();
        loopClose.IsCompleted.Should().BeFalse();
        transport.Disposals.Should().Be(0);
        var request = transport.Requests[0];
        if (fault) { pending.SetException(new IOException("private late transport payload")); }
        else { pending.SetResult(ToolTransport.Reply(request, VersionCall)); }
        await hostClose;
        await loopClose;
        host.IsQuiescent.Should().BeTrue();
        loop.IsQuiescent.Should().BeTrue();
        transport.Disposals.Should().Be(1);
        transport.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task Full_elapsed_deadline_and_cancellation_during_a_real_tool_suppress_the_next_send()
    {
        using var fixture = new Fixture();
        using var root = fixture.Root();
        var elapsedTransport = new ToolTransport(VersionCall, FinalAnswer)
        { AfterRequest = _ => fixture.Time.Now += TimeSpan.FromSeconds(15) };
        await using var elapsedLoop = CreateLoop(fixture, elapsedTransport);
        var elapsed = await RunLoop(fixture, fixture.LoopHost(elapsedLoop));
        elapsed.Outcome.Should().Be(ModelTurnOutcome.TimedOut);
        elapsed.Reason.Should().Be(ModelTurnReason.DeadlineExceeded);
        elapsedTransport.Requests.Should().ContainSingle();
        using var cancellation = new CancellationTokenSource();
        fixture.OnVersion = cancellation.Cancel;
        var cancelledTransport = new ToolTransport(VersionCall, FinalAnswer);
        await using var cancelledLoop = CreateLoop(fixture, cancelledTransport);
        var cancelled = await RunLoop(fixture, fixture.LoopHost(cancelledLoop), cancellation.Token);
        cancelled.Outcome.Should().Be(ModelTurnOutcome.Cancelled);
        cancelled.Response.Should().BeNull();
        cancelledTransport.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task Failed_audit_and_transport_fault_remain_explicit_and_never_release_running_work_as_success()
    {
        using var fixture = new Fixture();
        using var root = fixture.Root();
        var transport = new ToolTransport(VersionCall, FinalAnswer);
        await using var loop = CreateLoop(fixture, transport);
        var host = fixture.LoopHost(loop);
        fixture.AuditFailure = true;
        var audit = () => RunLoop(fixture, host);
        await audit.Should().ThrowAsync<IOException>();
        transport.Requests.Should().BeEmpty();
        fixture.AuditFailure = false;
        transport.AfterRequest = _ => throw new IOException("private payload must not be logged");
        var fault = () => RunLoop(fixture, host);
        await fault.Should().ThrowAsync<IOException>();
        fixture.Audits.Last().Outcome.Should().Be(SecurityAuditOutcome.Unknown);
        host.IsQuiescent.Should().BeTrue();
        loop.IsQuiescent.Should().BeTrue();
        fixture.Messages.Should().NotContain(message => message.Contains("private payload", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Bare_adapter_without_host_continuation_guard_or_foreign_host_context_is_closed()
    {
        using var fixture = new Fixture();
        var transport = new ToolTransport(FinalAnswer);
        await using var loop = CreateLoop(fixture, transport);
        var provenance = new ModelTurnProvenance(new(Guid.NewGuid()), fixture.Request, ModelProviderSelection.OllamaCandidate);
        static ValueTask<CapabilityReply> Forbidden(string _, string __, CancellationToken ___) =>
            throw new InvalidOperationException("No tool is admitted.");
        (await loop.RunAsync(provenance, null, fixture.Context().Serialize(), Forbidden, Token))
            .Reason.Should().Be(ModelTurnReason.HostContextRequired);
        (await loop.RunAsync(provenance, null, fixture.Context().Serialize(), Forbidden,
            _ => ValueTask.FromResult(ModelTurnReason.None), Token)).Reason.Should().Be(ModelTurnReason.HostContextRequired);
        using var foreign = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi),
            HostActivityLayer.Application, HostOperation.Request);
        (await loop.RunAsync(provenance, null, fixture.Context().Serialize(), Forbidden,
            _ => ValueTask.FromResult(ModelTurnReason.None), Token)).Reason.Should().Be(ModelTurnReason.HostContextRequired);
        await loop.DisposeAsync();
        (await loop.RunAsync(provenance, null, fixture.Context().Serialize(), Forbidden,
            _ => ValueTask.FromResult(ModelTurnReason.None), Token)).Reason.Should().Be(ModelTurnReason.HostAdmissionClosed);
        transport.Requests.Should().BeEmpty();
    }

    private static BoundedModelTurnAdapter CreateLoop(Fixture fixture, ToolTransport transport) =>
        new(transport, fixture.Time, fixture);

    private static async Task<ModelTurnResult> RunLoop(Fixture fixture, ModelTurnHost host, CancellationToken? token = null)
    {
        var turn = (await host.AdmitAsync(ModelProviderSelection.OllamaCandidate, fixture.Context(), Token)).Turn!;
        return await host.RunAsync(turn, token ?? Token);
    }

    private sealed partial class Fixture : Microsoft.Extensions.Logging.ILogger<BoundedModelTurnAdapter>
    {
        internal ModelTurnHost LoopHost(IModelTurnAdapter adapter)
        {
            var modelHost = new ModelTurnHost(this, this, this, registry, this, this, Time,
                [new(ModelProviderSelection.OllamaCandidate, Gates(ModelProviderSelection.OllamaCandidate),
                    Time.Now.AddSeconds(30), adapter)]);
            ownedHosts.Add(modelHost);
            return modelHost;
        }
    }

    private sealed class ToolTransport(params string[] script) : IModelToolTransport
    {
        internal List<ModelToolRequest> Requests { get; } = [];
        internal List<HostRequest> HostRequests { get; } = [];
        internal List<ActivityTraceId> TraceIds { get; } = [];
        internal Action<ModelToolRequest>? AfterRequest { get; set; }
        internal Func<ModelToolRequest, ModelToolTransportReply, ModelToolTransportReply>? ChangeReply { get; set; }
        internal Task<ModelToolTransportReply>? Pending { get; set; }
        internal int Disposals { get; private set; }

        public async Task<ModelToolTransportReply> CompleteAsync(ModelToolRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            HostRequests.Add(HostActivity.RequireCurrent().Request);
            TraceIds.Add(Activity.Current!.TraceId);
            AfterRequest?.Invoke(request);
            if (Pending is { } pending) { return await pending; }
            if (Requests.Count > script.Length) { throw new InvalidOperationException("An unplanned provider attempt occurred."); }
            var reply = Reply(request, script[Requests.Count - 1]);
            return ChangeReply is { } change ? change(request, reply) : reply;
        }

        internal static ModelToolTransportReply Reply(ModelToolRequest request, string wire)
        {
            var message = ModelToolProtocol.ParseAssistant(Encoding.UTF8.GetBytes(wire));
            return new(ModelTurnOutcome.Succeeded, request.Provenance, message,
                new(ModelToolProtocol.SerializeRequest(request).Length,
                    Math.Max(Encoding.UTF8.GetByteCount(wire), ModelToolProtocol.SerializeMessage(message).Length)));
        }

        public ValueTask DisposeAsync()
        {
            Disposals++;
            return ValueTask.CompletedTask;
        }
    }
}
