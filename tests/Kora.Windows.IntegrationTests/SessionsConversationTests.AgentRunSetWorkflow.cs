using System.Collections.Concurrent;

using AwesomeAssertions;

using Kora.Application.Agents;
using Kora.Application.Dependencies;
using Kora.Application.Hosting;
using Kora.Core.Agents;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Providers;
using Kora.Core.Storage;
using Kora.Core.Tools;
using Kora.Windows.IntegrationTests.Storage;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionsConversationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task IndependentRunSetRetainsActualOverlappingLocalAnswersOnlyInTheirDistinctSessions(bool referenced)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteConversationTests.FinishInitial(f);
        var first = await f.Store.ReadMetadataAsync(f.Request.SessionId, f.Token);
        f.Request = InteractionStorageFixture.NewRequest();
        await f.AdmitAsync(newSession: true);
        await WindowsSqliteConversationTests.FinishInitial(f);
        var second = await f.Store.ReadMetadataAsync(f.Request.SessionId, f.Token);
        var adapter = new RunSetAdapter();
        await using var composed = new Composition(f, adapter: adapter);
        var readers = new List<NativeAgentReader>();
        var instructions = new AgentOwnedStore();
        var revisions = new Kora.Application.Definitions.InstructionRevisionCatalogue(f.Paths);
        var catalogue = EnabledAgentInstructions(instructions, revisions);
        var qualification = new RunSetQualification(f.Time.GetUtcNow(), adapter);
        var factory = new AgentRunSetFactory(composed.Turns, qualification, lane =>
        {
            var reader = referenced ? ReferencedAgentReader() : new NativeAgentReader();
            readers.Add(reader);
            var conversation = new SessionConversationService(f.Store, f.Store, composed.Workspace, composed.Access,
                lane, f.Time, NullLogger<SessionConversationService>.Instance, lane, composed.Audit);
            return new(reader, f.Store, composed.Access, conversation, lane, composed.Audit, f.Time,
                new(catalogue, instructions, revisions));
        });
        await using var runs = factory.Create();
        using var release = adapter.ReleaseOnExit();
        await runs.ReviewAsync(1, "one.agent.md", first, "first independent original", RequestOrigin.LocalUi, () => true, f.Token);
        await runs.ReviewAsync(2, "two.agent.md", second, "second independent original", RequestOrigin.LocalUi, () => true, f.Token);
        if (referenced)
        {
            instructions.Reads.Should().Be(0);
            await runs.ResolveAsync(1, RequestOrigin.LocalUi, f.Token);
            runs.Read(2)!.Review!.IsInvocationReady.Should().BeFalse();
            await runs.ResolveAsync(2, RequestOrigin.LocalUi, f.Token);
        }
        var one = runs.InvokeAsync(1, RequestOrigin.LocalUi, f.Token);
        await RequireRunSetStarted(one, adapter.Started(first).Task);
        var two = runs.InvokeAsync(2, RequestOrigin.LocalUi, f.Token);
        await RequireRunSetStarted(two, adapter.Started(second).Task);
        adapter.Active.Should().Be(2);
        readers.Should().OnlyContain(item => !item.Released);
        adapter.Finish(first, "first actual answer");
        await one;
        two.IsCompleted.Should().BeFalse();
        readers[0].Released.Should().BeTrue();
        readers[1].Released.Should().BeFalse();
        adapter.Finish(second, "second actual answer");
        await two;
        var firstMessage = (await f.Store.ReadConversationPageAsync(first.Authority.SessionId, null, 25, f.Token)).Messages.Single();
        var secondMessage = (await f.Store.ReadConversationPageAsync(second.Authority.SessionId, null, 25, f.Token)).Messages.Single();
        firstMessage.Body.Should().Be("first independent original");
        firstMessage.AssistantResponse!.Body.Should().Be("first actual answer");
        secondMessage.Body.Should().Be("second independent original");
        secondMessage.AssistantResponse!.Body.Should().Be("second actual answer");
        firstMessage.Execution!.Request.SessionId.Should().NotBe(secondMessage.Execution!.Request.SessionId);
        firstMessage.Execution.Request.TaskId.Should().NotBe(secondMessage.Execution.Request.TaskId);
        firstMessage.Execution.Turn.Should().NotBe(secondMessage.Execution.Turn);
        readers.Should().OnlyContain(item => item.Released);
        f.Count("host_questions").Should().Be(0);
        f.Count("scoped_grants").Should().Be(0);
    }

    private sealed class RunSetQualification(DateTimeOffset now, IModelTurnAdapter adapter) : ILocalAgentConcurrencyQualification
    {
        private readonly LocalAgentCapacity proof = new(ModelProviderSelection.OllamaCandidate, null, adapter,
            Guid.NewGuid(), now, now.AddMinutes(5), 2);
        public LocalAgentCapacity? Observe(ModelProviderSelection selection, ProviderProfileTuple? profile, IModelTurnAdapter adapter) => proof;
        public bool IsCurrent(LocalAgentCapacity capacity) => capacity == proof;
    }

    private static async Task RequireRunSetStarted(Task operation, Task started)
    {
        if (await Task.WhenAny(operation, started).WaitAsync(TimeSpan.FromSeconds(10)) == operation)
        {
            await operation;
            throw new InvalidOperationException("The invocation terminated before reaching its provider barrier.");
        }
    }

    private sealed class RunSetAdapter : IModelTurnAdapter
    {
        private sealed class Release(RunSetAdapter adapter) : IDisposable
        {
            public void Dispose() => adapter.FinishAll();
        }
        private readonly ConcurrentDictionary<HostId<SessionIdentity>, TaskCompletionSource> started = new();
        private readonly ConcurrentDictionary<HostId<SessionIdentity>, TaskCompletionSource<ModelProviderReply>> pending = new();
        private int active;
        internal int Active => Volatile.Read(ref active);
        public bool IsQuiescent => Active == 0;
        internal IDisposable ReleaseOnExit() => new Release(this);
        internal void FinishAll()
        {
            foreach (var completion in pending.Values)
            { completion.TrySetResult(new(ModelTurnOutcome.Unknown, ModelProviderSelection.OllamaCandidate)); }
        }
        internal TaskCompletionSource Started(SessionWorkspaceEntry entry) =>
            started.GetOrAdd(entry.Authority.SessionId, _ => new(TaskCreationOptions.RunContinuationsAsynchronously));
        internal void Finish(SessionWorkspaceEntry entry, string answer) =>
            pending[entry.Authority.SessionId].SetResult(new(ModelTurnOutcome.Succeeded, ModelProviderSelection.OllamaCandidate, new(answer, null)));
        public async Task<ModelProviderReply> RunAsync(ModelTurnProvenance provenance, ProviderProfileTuple? profile,
            ReadOnlyMemory<byte> context, Func<string, string, CancellationToken, ValueTask<CapabilityReply>> invokeTool, CancellationToken token)
        {
            var completion = pending.GetOrAdd(provenance.Request.SessionId, _ => new(TaskCreationOptions.RunContinuationsAsynchronously));
            Interlocked.Increment(ref active);
            started.GetOrAdd(provenance.Request.SessionId, _ => new(TaskCreationOptions.RunContinuationsAsynchronously)).TrySetResult();
            try { return await completion.Task; }
            finally { Interlocked.Decrement(ref active); }
        }
    }
}
