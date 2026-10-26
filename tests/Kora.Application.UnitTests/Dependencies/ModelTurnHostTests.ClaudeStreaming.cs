using AwesomeAssertions;
using Kora.Application.Dependencies;
using Kora.Application.Hosting;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Providers;
using Kora.Core.Storage;

namespace Kora.Application.UnitTests.Dependencies;

public sealed partial class ModelTurnHostTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ClaudeStreamUsesCanonicalProvisionalOwnerWithFinalOnlySameRequestRetention(bool fail)
    {
        using var f = new Fixture();
        var runtime = new HostClaudeStream { Fail = fail };
        var host = f.ClaudeProfiles(runtime, new(f, f.Time));
        var service = f.ConversationService(host, execution: host);
        var updates = new List<ConversationAnswerPresentation>();
        service.ProvisionalAnswerChanged += (update, _) =>
        {
            f.Retained!.AssistantResponse.Should().BeNull();
            f.ExecutionCompletions.Should().Be(0);
            updates.Add(update);
            return ValueTask.CompletedTask;
        };
        var pending = service.SubmitClaudeAnswerStreamAsync(new(f.Session, null), "original Claude input", () => true, Token);
        await runtime.Entered.Task.WaitAsync(Token);
        try
        {
            updates.Should().ContainSingle().Which.Update.Answer.Should().Be("safe preview");
            updates[0].IsCurrent().Should().BeTrue();
            pending.IsCompleted.Should().BeFalse();
            f.Retained!.AssistantResponse.Should().BeNull();
        }
        finally { runtime.Release.TrySetResult(); }
        var receipt = await pending;
        receipt.Message.AssistantResponse?.Body.Should().Be(fail ? null : "safe preview complete");
        receipt.Message.Request.Should().Be(updates[0].Target.Request);
        updates.Should().OnlyContain(update => !update.IsCurrent());
        runtime.Buffered.Should().Be(0);
    }

    [Theory]
    [InlineData(RequestOrigin.ActivatedVoice, ModelToolMode.None)]
    [InlineData(RequestOrigin.LocalUi, ModelToolMode.ReadOnly)]
    public async Task StreamAdmissionCannotExpandToVoiceOrToolStreaming(RequestOrigin origin, ModelToolMode tools)
    {
        using var f = new Fixture(origin);
        using var root = f.Root();
        var host = f.ClaudeProfiles(new HostClaudeStream(), new(f, f.Time));
        var context = tools == ModelToolMode.None ? f.Context(toolMode: tools)
            : new(f.Request, "policy", "body", [], f.Time.Now, f.Time.Now.AddMinutes(1), tools,
                ClaudeObservationTools.Constrain([Kora.Core.Tools.ReadOnlyCapabilityCatalog.Version]));
        (await host.AdmitClaudeAsync(context, Token, streamAnswer: true)).Turn.Should().BeNull();
    }

    [Fact]
    public async Task ClaudeStreamRechecksCurrentAccountProfileAfterProviderCompletionBeforeFinalCommit()
    {
        using var f = new Fixture();
        var runtime = new HostClaudeStream();
        var host = f.ClaudeProfiles(runtime, new(f, f.Time));
        var service = f.ConversationService(host, execution: host);
        service.RunningConversationChanged += status =>
        {
            if (status.Phase == Kora.Core.Conversation.ConversationExecutionPhase.Completing)
            { f.ProfileCatalogue.Status = ProviderCatalogueStatus.Unavailable; }
        };
        var pending = service.SubmitClaudeAnswerStreamAsync(new(f.Session, null), "original", () => true, Token);
        await runtime.Entered.Task.WaitAsync(Token);
        runtime.Release.TrySetResult();
        var receipt = await pending;
        receipt.Message.Outcome.Should().Be(ModelTurnOutcome.DeniedEgress);
        receipt.Message.AssistantResponse.Should().BeNull();
        f.ExecutionCompletions.Should().Be(1);
    }

    private sealed class HostClaudeStream : IClaudeAnswerTurnRuntime
    {
        public bool IsQuiescent => Release.Task.IsCompleted || !Entered.Task.IsCompleted;
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool Fail { get; init; }
        internal int Buffered { get; private set; }
        public Task<ClaudeTurnReply> RunAsync(ModelTurnProvenance provenance, ProviderProfileTuple profile,
            ModelContextEnvelope context, Func<ClaudeTransmission, CancellationToken, ValueTask<bool>> admit,
            Func<CancellationToken, ValueTask<bool>> current, CancellationToken token)
        { Buffered++; throw new InvalidOperationException("No buffered fallback."); }
        public async Task<ClaudeTurnReply> RunWithAnswerAsync(ModelTurnProvenance provenance, ProviderProfileTuple profile,
            ModelContextEnvelope context, Func<ClaudeTransmission, CancellationToken, ValueTask<bool>> admit,
            Func<CancellationToken, ValueTask<bool>> current,
            Func<ModelAnswerUpdate, CancellationToken, ValueTask> publish, CancellationToken token)
        {
            await publish(new(provenance, "safe preview"), token);
            Entered.TrySetResult();
            await Release.Task;
            return Fail ? new(ModelTurnOutcome.Unknown) : new(ModelTurnOutcome.Succeeded, "safe preview complete");
        }
    }
}
