using AwesomeAssertions;
using Kora.Application.Dependencies;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Providers;
using Kora.Core.Tools;

namespace Kora.Application.UnitTests.Dependencies;

public sealed partial class ModelTurnHostTests
{
    [Theory]
    [InlineData("unbound")]
    [InlineData("profile")]
    [InlineData("request")]
    [InlineData("bytes")]
    [InlineData("expired")]
    [InlineData("current")]
    public async Task Adapter_refuses_nonexact_preparation_before_entering_runtime(string fault)
    {
        using var f = new Fixture();
        using var root = f.Root();
        var reviews = new HostedTransmissionReviewService(f, f.Time);
        var runtime = new ProbeCopilotRuntime();
        var context = f.Context();
        var profile = Profile(ProviderService.Copilot);
        var authorization = reviews.PrepareTurn(context, profile);
        var prototype = new CopilotModelTurnAdapter(runtime, reviews);
        var adapter = fault is "unbound" ? prototype : prototype.Bind(authorization, _ => ValueTask.FromResult(fault is not "current"));
        if (fault is "expired") { f.Time.Now = context.ExpiresAt; }
        var provenance = new ModelTurnProvenance(new(Guid.NewGuid()),
            fault is "request" ? HostRequest.Create(RequestOrigin.LocalUi) : f.Request, ModelProviderSelection.CopilotCandidate);
        var result = await adapter.RunAsync(provenance, fault is "profile" ? profile with { Account = new("foreign") } : profile,
            fault is "bytes" ? new byte[] { 1 } : context.Serialize(),
            (_, _, _) => throw new InvalidOperationException("No tool should execute"), Token);
        result.Outcome.Should().Be(ModelTurnOutcome.DeniedEgress);
        result.Response.Should().BeNull();
        runtime.Calls.Should().Be(0);
    }

    [Theory]
    [InlineData("foreign-turn")]
    [InlineData("foreign-profile")]
    [InlineData("tool-current")]
    [InlineData("tool-expired")]
    [InlineData("after-tool")]
    [InlineData("after-answer")]
    [InlineData("expired-answer")]
    [InlineData("runtime-denied")]
    public async Task Adapter_revalidates_callbacks_and_never_returns_a_revoked_answer(string fault)
    {
        using var f = new Fixture();
        using var root = f.Root();
        var reviews = new HostedTransmissionReviewService(f, f.Time);
        var runtime = new ProbeCopilotRuntime();
        var context = f.Context();
        var profile = Profile(ProviderService.Copilot);
        var live = true;
        var adapter = new CopilotModelTurnAdapter(runtime, reviews).Bind(reviews.PrepareTurn(context, profile),
            _ => ValueTask.FromResult(live));
        var provenance = new ModelTurnProvenance(new(Guid.NewGuid()), f.Request, ModelProviderSelection.CopilotCandidate);
        var tools = 0;
        runtime.Run = async (invoke, admit) =>
        {
            if (fault.StartsWith("foreign", StringComparison.Ordinal))
            {
                var transmission = new CopilotTransmission(fault is "foreign-turn"
                    ? provenance with { Turn = new(Guid.NewGuid()) } : provenance,
                    fault is "foreign-profile" ? profile with { Account = new("foreign") } : profile,
                    new("https://api.githubcopilot.com/chat/completions"), new byte[] { 1 });
                (await admit(transmission, Token)).Should().BeFalse();
                return new(ModelTurnOutcome.DeniedEgress);
            }
            if (fault is "tool-current" or "tool-expired" or "after-tool")
            {
                if (fault is "tool-current") { live = false; }
                if (fault is "tool-expired") { f.Time.Now = context.ExpiresAt; }
                (await invoke(ReadOnlyCapabilityCatalog.Version, "{}", Token)).Outcome.Should().Be(CapabilityOutcome.Denied);
            }
            if (fault is "after-answer") { live = false; }
            if (fault is "expired-answer") { f.Time.Now = context.ExpiresAt; }
            return fault is "runtime-denied" ? new(ModelTurnOutcome.Denied) { TokenUsage = new(25, 5, 25) }
                : new(ModelTurnOutcome.Succeeded, "must not escape") { TokenUsage = new(25, 5, 25) };
        };
        var result = await adapter.RunAsync(provenance, profile, context.Serialize(), (_, _, _) =>
        {
            tools++;
            live = false;
            return ValueTask.FromResult(new CapabilityReply(CapabilityOutcome.Succeeded, "value"));
        }, Token);
        result.Outcome.Should().NotBe(ModelTurnOutcome.Succeeded);
        result.Response.Should().BeNull();
        result.TokenUsage.Should().Be(fault.StartsWith("foreign", StringComparison.Ordinal) ? null : new ModelTokenUsage(25, 5, 25));
        tools.Should().Be(fault is "after-tool" ? 1 : 0);
    }

    [Theory]
    [InlineData("valid")]
    [InlineData("foreign-turn")]
    [InlineData("revoked")]
    [InlineData("continuation")]
    [InlineData("tools")]
    public async Task Copilot_adapter_streams_only_exact_current_tool_free_answers(string fault)
    {
        using var f = new Fixture();
        using var root = f.Root();
        var context = f.Context(toolMode: fault is "tools" ? ModelToolMode.ReadOnly : ModelToolMode.None);
        var reviews = new HostedTransmissionReviewService(f, f.Time);
        var runtime = new ProbeCopilotRuntime();
        var profile = Profile(ProviderService.Copilot);
        var live = true;
        var adapter = new CopilotModelTurnAdapter(runtime, reviews).Bind(reviews.PrepareTurn(context, profile),
            _ => ValueTask.FromResult(live));
        var provenance = new ModelTurnProvenance(new(Guid.NewGuid()), f.Request, ModelProviderSelection.CopilotCandidate);
        var answers = new List<string>();
        runtime.Stream = async publish =>
        {
            if (fault is "revoked") { live = false; }
            await publish(new(fault is "foreign-turn" ? provenance with { Turn = new(Guid.NewGuid()) } : provenance,
                "safe preview"), Token);
            return new(ModelTurnOutcome.Succeeded, "safe preview complete");
        };
        Task<ModelProviderReply> Run() => adapter.RunWithAnswerAsync(provenance, profile, context.Serialize(),
            (_, _, _) => throw new InvalidOperationException("No tool is admitted"),
            _ => ValueTask.FromResult(fault is "continuation" ? ModelTurnReason.HostAdmissionClosed : ModelTurnReason.None),
            (update, _) =>
            {
                answers.Add(update.Answer);
                return ValueTask.CompletedTask;
            }, Token);
        if (fault is "valid" or "tools")
        {
            var reply = await Run();
            reply.Outcome.Should().Be(fault is "valid" ? ModelTurnOutcome.Succeeded : ModelTurnOutcome.Denied);
            answers.Should().Equal(fault is "valid" ? ["safe preview"] : []);
        }
        else
        {
            await Assert.ThrowsAsync<InvalidDataException>(Run);
            answers.Should().BeEmpty();
        }
    }

    private sealed class ProbeCopilotRuntime : ICopilotAnswerTurnRuntime
    {
        public bool IsQuiescent { get; set; } = true;
        internal int Calls { get; private set; }
        internal Func<Func<string, string, CancellationToken, ValueTask<CapabilityReply>>,
            Func<CopilotTransmission, CancellationToken, ValueTask<bool>>, Task<CopilotTurnReply>>? Run { get; set; }
        internal Func<Func<ModelAnswerUpdate, CancellationToken, ValueTask>, Task<CopilotTurnReply>>? Stream { get; set; }
        public Task<CopilotTurnReply> RunWithAnswerAsync(ModelTurnProvenance provenance, ProviderProfileTuple profile,
            ReadOnlyMemory<byte> context, Func<string, string, CancellationToken, ValueTask<CapabilityReply>> invokeTool,
            Func<CopilotTransmission, CancellationToken, ValueTask<bool>> admitTransmission,
            Func<ModelAnswerUpdate, CancellationToken, ValueTask> publish, CancellationToken cancellationToken)
        {
            Calls++;
            return Stream!(publish);
        }
        public Task<CopilotTurnReply> RunAsync(ModelTurnProvenance provenance, ProviderProfileTuple profile,
            ReadOnlyMemory<byte> context, Func<string, string, CancellationToken, ValueTask<CapabilityReply>> invokeTool,
            Func<CopilotTransmission, CancellationToken, ValueTask<bool>> admitTransmission, CancellationToken cancellationToken)
        {
            Calls++;
            return Run is null ? Task.FromResult(new CopilotTurnReply(ModelTurnOutcome.Denied))
                : Run(invokeTool, admitTransmission);
        }
    }
}
