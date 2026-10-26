using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;

using AwesomeAssertions;

using Kora.Application.Dependencies;
using Kora.Application.UnitTests.Agents;
using Kora.Core.Auditing;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Providers;
using Kora.Core.Storage;
using Kora.Core.Tools;

namespace Kora.Application.UnitTests.Dependencies;

[Collection("Host tracing")]
public sealed partial class RuntimeResourceAllocatorTests
{
    internal static ProviderProfileTuple Profile { get; } =
        new(ProviderService.Local, new("account"), new(1), new("exact-model"), new("managed-effort"), new("managed-window"));
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task OrdinaryDistinctSessionsConsumeTwoOwnedTransportsAndThirdWaitsFairlyWithoutDispatchOrContextMixing()
    {
        var source = new RuntimeSource();
        await using var f = new AgentRunSetTests.Fixture(runtimeCapacity: source, registrationSource: source);
        using var cleanup = source;
        var service = f.Conversation;
        var one = service.SubmitAsync(f.Sessions[0], "private original one", () => true, Token);
        await source.Started(0, f);
        var two = service.SubmitAsync(f.Sessions[1], "private original two", () => true, Token,
            origin: RequestOrigin.ActivatedVoice);
        await source.Started(1, f);
        var three = service.SubmitAsync(f.Sessions[2], "private original three", () => true, Token);
        var waiting = await service.ReadRunningConversationAsync(f.Sessions[2], () => true, Token);
        waiting!.Phase.Should().Be(ConversationExecutionPhase.Waiting);
        source.Transport(2, f).Started.Task.IsCompleted.Should().BeFalse();
        f.Messages.Values.Single(item => item.Request.SessionId == f.Sessions[2].Authority.SessionId)
            .Execution.Should().BeNull("resource admission precedes durable execution intent");
        source.Transport(0, f).Finish("answer only one");
        var first = await one;
        first.Message.AssistantResponse!.Body.Should().Be("answer only one");
        await source.Started(2, f);
        two.IsCompleted.Should().BeFalse("A's terminal commit does not wait for B's unrelated physical runtime");
        source.Transport(1, f).Finish("answer only two");
        source.Transport(2, f).Finish("answer only three");
        var second = await two;
        var third = await three;
        second.Message.AssistantResponse!.Body.Should().Be("answer only two");
        third.Message.AssistantResponse!.Body.Should().Be("answer only three");
        second.Message.Request.Origin.Should().Be(RequestOrigin.ActivatedVoice);
        source.Transports.Values.Should().OnlyContain(item => item.ToolReply!.Outcome == CapabilityOutcome.Succeeded);
        source.Transports.Values.Select(item => item.Input).Should().BeEquivalentTo(
            "private original one", "private original two", "private original three");
        source.Transports.Values.Select(item => item.Provenance!.Request.SessionId).Should().OnlyHaveUniqueItems();
        f.Audits.Where(item => item.ActionId.StartsWith("runtime.resource.", StringComparison.Ordinal))
            .Should().HaveCount(12);
        f.Audits.Should().OnlyContain(item => !JsonSerializer.Serialize(item).Contains("private original", StringComparison.Ordinal)
            && !JsonSerializer.Serialize(item).Contains("answer only", StringComparison.Ordinal));
        f.Host.IsQuiescent.Should().BeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnknownOrSingleCapacityRemainsSerializedAndNeverClonesTransport(bool missingQualification)
    {
        var source = new RuntimeSource { Current = false };
        await using var f = new AgentRunSetTests.Fixture(runtimeCapacity: missingQualification ? null : source,
            registrationSource: source);
        using var cleanup = source;
        var one = f.Conversation.SubmitAsync(f.Sessions[0], "one", () => true, Token);
        await source.Started(0, f);
        var two = await f.Conversation.SubmitAsync(f.Sessions[1], "two", () => true, Token);
        two.Message.Outcome.Should().Be(ModelTurnOutcome.Unavailable);
        two.Message.Reason.Should().Be(ModelTurnReason.ProviderBusy);
        source.Transport(1, f).Started.Task.IsCompleted.Should().BeFalse();
        source.Transport(0, f).Finish("one");
        await one;
    }

    [Fact]
    public async Task CancellationJoinsOnlyAddressedRuntimeWhileOtherOrdinarySessionContinues()
    {
        var source = new RuntimeSource();
        await using var f = new AgentRunSetTests.Fixture(runtimeCapacity: source, registrationSource: source);
        using var cleanup = source;
        var one = f.Conversation.SubmitAsync(f.Sessions[0], "one", () => true, Token);
        await source.Started(0, f);
        var two = f.Conversation.SubmitAsync(f.Sessions[1], "two", () => true, Token);
        await source.Started(1, f);
        var observed = (await f.Conversation.ReadRunningConversationAsync(f.Sessions[0], () => true, Token))!;
        await f.Conversation.CancelRunningConversationAsync(observed.Target, () => true, Token);
        await source.Transport(0, f).Cancelled.Task;
        one.IsCompleted.Should().BeFalse();
        source.Transport(1, f).Cancelled.Task.IsCompleted.Should().BeFalse();
        source.Transport(0, f).Finish("forbidden late one");
        var cancelled = await one;
        cancelled.Message.Outcome.Should().Be(ModelTurnOutcome.Cancelled);
        cancelled.Message.AssistantResponse.Should().BeNull();
        two.IsCompleted.Should().BeFalse();
        source.Transport(1, f).Finish("independent two");
        (await two).Message.AssistantResponse!.Body.Should().Be("independent two");
    }

    [Fact]
    public async Task WaitingCancellationNeverDispatchesAndSameSessionCannotEnterTwice()
    {
        var source = new RuntimeSource();
        await using var f = new AgentRunSetTests.Fixture(runtimeCapacity: source, registrationSource: source);
        using var cleanup = source;
        var one = f.Conversation.SubmitAsync(f.Sessions[0], "one", () => true, Token);
        await source.Started(0, f);
        var two = f.Conversation.SubmitAsync(f.Sessions[1], "two", () => true, Token);
        await source.Started(1, f);
        await f.Conversation.Invoking(service => service.SubmitAsync(f.Sessions[0], "duplicate", () => true, Token))
            .Should().ThrowAsync<InvalidOperationException>();
        var three = f.Conversation.SubmitAsync(f.Sessions[2], "three", () => true, Token);
        var observed = (await f.Conversation.ReadRunningConversationAsync(f.Sessions[2], () => true, Token))!;
        await f.Conversation.CancelRunningConversationAsync(observed.Target, () => true, Token);
        var cancelled = await three;
        cancelled.Task.State.Should().Be(HostTaskState.Cancelled);
        source.Transport(2, f).Started.Task.IsCompleted.Should().BeFalse();
        source.Transport(0, f).Finish("one");
        source.Transport(1, f).Finish("two");
        await Task.WhenAll(one, two);
    }

    [Theory]
    [InlineData("account")]
    [InlineData("catalogue")]
    [InlineData("selection")]
    [InlineData("generation")]
    [InlineData("expired")]
    [InlineData("future")]
    [InlineData("unknown-resource")]
    [InlineData("duplicate-resource")]
    [InlineData("no-resource")]
    [InlineData("unowned-transport")]
    [InlineData("uncancellable")]
    public async Task InvalidExactCapacityEvidenceCannotDispatchEvenFirstOrdinaryRequest(string fault)
    {
        var source = new RuntimeSource();
        source.Evidence = fault switch
        {
            "account" => source.Evidence with { Profile = Profile with { Account = new("foreign") } },
            "catalogue" => source.Evidence with { Profile = Profile with { CatalogueRevision = new(2) } },
            "selection" => source.Evidence with { Selection = ModelProviderSelection.CopilotCandidate },
            "generation" => source.Evidence with { RuntimeGeneration = Guid.Empty },
            "expired" => source.Evidence with { ExpiresAt = DateTimeOffset.UnixEpoch },
            "future" => source.Evidence with { ObservedAt = DateTimeOffset.UnixEpoch.AddMinutes(1) },
            "unknown-resource" => source.Evidence with { Resources = [new(new(Guid.Empty), false)] },
            "duplicate-resource" => source.Evidence with { Resources = [source.Evidence.Resources[0], source.Evidence.Resources[0]] },
            "no-resource" => source.Evidence with { Resources = [] },
            "uncancellable" => source.Evidence with { Revoked = CancellationToken.None },
            _ => source.Evidence with { OwnsIndependentTransport = _ => false },
        };
        await using var f = new AgentRunSetTests.Fixture(runtimeCapacity: source, registrationSource: source);
        using var cleanup = source;
        var receipt = await f.Conversation.SubmitAsync(f.Sessions[0], "not dispatched", () => true, Token);
        receipt.Message.Outcome.Should().Be(ModelTurnOutcome.Unavailable);
        source.Transport(0, f).Started.Task.IsCompleted.Should().BeFalse();
        receipt.Message.Execution.Should().BeNull();
    }

    [Theory]
    [InlineData("revoked")]
    [InlineData("generation")]
    [InlineData("privacy")]
    [InlineData("selection")]
    [InlineData("session")]
    public async Task AuthorityDriftDuringOwnedExecutionSuppressesAnswerAndNewDispatch(string fault)
    {
        var source = new RuntimeSource();
        await using var f = new AgentRunSetTests.Fixture(runtimeCapacity: source, registrationSource: source);
        using var cleanup = source;
        var live = true;
        var one = f.Conversation.SubmitAsync(f.Sessions[0], "one", () => live, Token);
        await source.Started(0, f);
        if (fault is "revoked") { source.Current = false; }
        if (fault is "generation") { source.Evidence = source.Evidence with { RuntimeGeneration = Guid.NewGuid() }; }
        if (fault is "privacy") { f.CanControl = false; }
        if (fault is "selection") { live = false; }
        if (fault is "session") { f.Sessions[0] = f.Sessions[0] with { Authority = f.Sessions[0].Authority with { Generation = new(2) } }; }
        source.Transport(0, f).Finish("forbidden");
        if (fault is "privacy" or "selection" or "session")
        { await one.Invoking(value => value).Should().ThrowAsync<Exception>(); }
        else { (await one).Message.AssistantResponse.Should().BeNull(); }
    }

    [Theory]
    [InlineData("runtime-generation")]
    [InlineData("exclusive-resource")]
    [InlineData("same-transport")]
    public async Task SharedPhysicalResourcesAndSingleFlightTransportsCannotPretendIndependentCapacity(string conflict)
    {
        var source = new RuntimeSource { ReuseTransport = conflict is "same-transport" };
        if (conflict is "exclusive-resource")
        { source.Evidence = source.Evidence with { Resources = [source.Evidence.Resources[0] with { Exclusive = true }] }; }
        await using var f = new AgentRunSetTests.Fixture(runtimeCapacity: source, registrationSource: source);
        using var cleanup = source;
        var one = f.Conversation.SubmitAsync(f.Sessions[0], "one", () => true, Token);
        await source.Started(0, f);
        if (conflict is "runtime-generation")
        { source.Evidence = source.Evidence with { RuntimeGeneration = Guid.NewGuid() }; }
        var two = f.Conversation.SubmitAsync(f.Sessions[1], "two", () => true, Token);
        var waiting = (await f.Conversation.ReadRunningConversationAsync(f.Sessions[1], () => true, Token))!;
        waiting.Phase.Should().Be(ConversationExecutionPhase.Waiting);
        source.Transports.Values.Distinct().Count().Should().Be(conflict is "same-transport" ? 1 : 2);
        f.Messages.Values.Single(item => item.Request.SessionId == f.Sessions[1].Authority.SessionId).Execution.Should().BeNull();
        await f.Conversation.CancelRunningConversationAsync(waiting.Target, () => true, Token);
        (await two).Message.Outcome.Should().Be(ModelTurnOutcome.Cancelled);
        source.Transport(0, f).Finish("one");
        await one;
    }

    [Fact]
    public async Task FailedPhysicalJoinQuarantinesAffectedResourceAndNeverRetainsSuccessOrReissuesIt()
    {
        var source = new RuntimeSource();
        var f = new AgentRunSetTests.Fixture(runtimeCapacity: source, registrationSource: source);
        using var cleanup = source;
        var one = f.Conversation.SubmitAsync(f.Sessions[0], "one", () => true, Token);
        await source.Started(0, f);
        source.Transport(0, f).JoinFailed = true;
        source.Transport(0, f).Finish("not a joined answer");
        await one.Invoking(value => value).Should().ThrowAsync<ConversationSubmissionUnconfirmedException>();
        f.Messages.Values.Should().OnlyContain(item => item.AssistantResponse == null);
        var refused = await f.Conversation.SubmitAsync(f.Sessions[1], "two", () => true, Token);
        refused.Message.Outcome.Should().Be(ModelTurnOutcome.Unavailable);
        source.Transport(1, f).Started.Task.IsCompleted.Should().BeFalse();
        f.Audits.Should().Contain(item => string.Equals(item.ActionId, "runtime.resource.quarantine", StringComparison.Ordinal)
            && item.Outcome == SecurityAuditOutcome.Unknown);
        await f.Invoking(value => value.DisposeAsync().AsTask()).Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task RuntimeRevocationSignalsBothOwnedTransportsButRetainsEachLeaseUntilItsOwnPhysicalJoin()
    {
        var source = new RuntimeSource();
        await using var f = new AgentRunSetTests.Fixture(runtimeCapacity: source, registrationSource: source);
        using var cleanup = source;
        var one = f.Conversation.SubmitAsync(f.Sessions[0], "one", () => true, Token);
        await source.Started(0, f);
        var two = f.Conversation.SubmitAsync(f.Sessions[1], "two", () => true, Token);
        await source.Started(1, f);
        var three = f.Conversation.SubmitAsync(f.Sessions[2], "three", () => true, Token);
        await source.Revocation.CancelAsync();
        await source.Transport(0, f).Cancelled.Task;
        await source.Transport(1, f).Cancelled.Task;
        one.IsCompleted.Should().BeFalse();
        two.IsCompleted.Should().BeFalse();
        (await three).Message.Outcome.Should().Be(ModelTurnOutcome.Unavailable);
        source.Transport(0, f).Finish("suppressed one");
        (await one).Message.AssistantResponse.Should().BeNull();
        two.IsCompleted.Should().BeFalse();
        source.Transport(1, f).Finish("suppressed two");
        (await two).Message.AssistantResponse.Should().BeNull();
    }

    internal sealed class RuntimeSource : IRuntimeCapacityQualification, IModelProviderRegistrationSource, IDisposable
    {
        internal bool Current { get; set; } = true;
        internal bool ReuseTransport { get; set; }
        internal Action? OnObserve { get; set; }
        internal ConcurrentDictionary<HostId<SessionIdentity>, OwnedTransport> Transports { get; } = new();
        internal RuntimeCapacityEvidence Evidence { get; set; }
        internal CancellationTokenSource Revocation { get; } = new();
        internal RuntimeSource()
        {
            Evidence = new(ModelProviderSelection.OllamaCandidate, Profile, Guid.NewGuid(), DateTimeOffset.UnixEpoch,
                DateTimeOffset.UnixEpoch.AddMinutes(5), [new(new(Guid.NewGuid()), false)],
                adapter => Transports.Values.Contains(adapter), Revocation.Token);
        }
        public RuntimeCapacityEvidence? Observe(ModelProviderSelection selection, ProviderProfileTuple profile)
        {
            OnObserve?.Invoke();
            return Current ? Evidence : null;
        }
        public Task<ModelProviderRegistration?> ResolveAsync(ModelProviderSelection selection, ProviderProfileTuple profile,
            CancellationToken cancellationToken)
        {
            var adapter = Transports.GetOrAdd(HostActivity.RequireCurrent().Request.SessionId,
                _ => ReuseTransport && !Transports.IsEmpty ? Transports.Values.First() : new());
            return Task.FromResult<ModelProviderRegistration?>(new(selection,
                ModelQualificationGate.LocalCandidateSelection | ModelQualificationGate.IntegratedHost,
                DateTimeOffset.UnixEpoch.AddMinutes(5), adapter));
        }
        internal OwnedTransport Transport(int index, AgentRunSetTests.Fixture f) =>
            Transports[f.Sessions[index].Authority.SessionId];
        internal Task Started(int index, AgentRunSetTests.Fixture f) =>
            Transport(index, f).Started.Task.WaitAsync(TimeSpan.FromSeconds(5), Token);
        public void Dispose()
        {
            foreach (var transport in Transports.Values) { transport.Finish("test cleanup"); }
            Revocation.Dispose();
        }
    }

    internal sealed class OwnedTransport : IModelTurnAdapter
    {
        internal TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Cancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<ModelProviderReply> reply = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private bool active;
        internal bool JoinFailed { get; set; }
        internal string? Input { get; private set; }
        internal ModelTurnProvenance? Provenance { get; private set; }
        internal CapabilityReply? ToolReply { get; private set; }
        public bool IsQuiescent => !active && !JoinFailed;
        internal void Finish(string answer) => reply.TrySetResult(new(ModelTurnOutcome.Succeeded,
            ModelProviderSelection.OllamaCandidate, new(answer, null)));
        public async Task<ModelProviderReply> RunAsync(ModelTurnProvenance provenance, ProviderProfileTuple? profile,
            ReadOnlyMemory<byte> context, Func<string, string, CancellationToken, ValueTask<CapabilityReply>> invokeTool,
            CancellationToken cancellationToken)
        {
            active = true;
            Provenance = provenance;
            using var json = JsonDocument.Parse(context);
            Input = json.RootElement.GetProperty("UserRequest").GetString();
            ToolReply = await invokeTool(ReadOnlyCapabilityCatalog.Version, "{}", cancellationToken);
            using var registration = cancellationToken.Register(() => Cancelled.TrySetResult());
            Started.TrySetResult();
            try { return await reply.Task; }
            finally { active = false; }
        }
    }
}