using AwesomeAssertions;
using Kora.Application.Dependencies;
using Kora.Application.Hosting;
using Kora.Core.Conversation;
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
    public async Task ExplicitClaudeStreamTurnPublishesThroughHostOwnerAndRevalidatesBeforeCommit(bool fail)
    {
        using var f = new Fixture();
        using var root = f.Root();
        var runtime = new HostClaudeStream { Fail = fail };
        var host = f.ClaudeProfiles(runtime, new(f, f.Time));
        var turn = (await host.AdmitClaudeAsync(f.Context(toolMode: ModelToolMode.None), Token, streamAnswer: true)).Turn!;
        turn.Should().NotBeNull();
        var updates = new List<string>();
        turn.ConversationTarget = new(f.Session, null);
        turn.PublishAnswer = (update, _) => { updates.Add(update.Answer); return ValueTask.CompletedTask; };
        runtime.Release.TrySetResult();
        var result = await host.RunAsync(turn, Token);
        updates.Should().Equal("safe preview");
        runtime.Streams.Should().Be(1);
        runtime.Buffered.Should().Be(0);
        if (fail)
        {
            result.Outcome.Should().Be(ModelTurnOutcome.Unknown);
            result.Response.Should().BeNull("a failed stream never promotes its provisional text");
            return;
        }
        result.Outcome.Should().Be(ModelTurnOutcome.Succeeded);
        result.Response!.Answer.Should().Be("safe preview complete");
        (await turn.ValidateAnswerCommit!(Token)).Should().Be(ModelTurnReason.None);
        f.ProfileCatalogue.Status = ProviderCatalogueStatus.Unavailable;
        (await turn.ValidateAnswerCommit(Token)).Should().NotBe(ModelTurnReason.None,
            "the current account/profile is rechecked after provider completion before final commit");
        runtime.Streams.Should().Be(1);
        runtime.Buffered.Should().Be(0);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task OrdinaryClaudeSubmitIsBufferedWithFinalOnlySameRequestRetentionEvenWhenTheRuntimeCanStream(bool fail, bool cancel)
    {
        using var f = new Fixture();
        var runtime = new HostClaudeStream { Fail = fail };
        var host = f.ClaudeProfiles(runtime, new(f, f.Time));
        await using var service = f.ConversationService(host, execution: host);
        var updates = 0;
        service.ProvisionalAnswerChanged += (_, _) => { updates++; return ValueTask.CompletedTask; };
        var target = new SessionWorkspaceEntry(f.Session, null);
        var pending = service.SubmitAsync(target, "original Claude input", () => true, Token);
        await runtime.Entered.Task.WaitAsync(Token);
        try
        {
            pending.IsCompleted.Should().BeFalse();
            f.Retained!.AssistantResponse.Should().BeNull();
            if (cancel)
            {
                var running = (await service.ReadRunningConversationAsync(target, () => true, Token))!;
                (await service.CancelRunningConversationAsync(running.Target, () => true, Token))
                    .Phase.Should().Be(ConversationExecutionPhase.Cancelling);
                pending.IsCompleted.Should().BeFalse("the runtime ignores cancellation until its physical release");
                host.IsQuiescent.Should().BeFalse();
                f.ExecutionCompletions.Should().Be(0);
                f.Retained.AssistantResponse.Should().BeNull();
            }
        }
        finally { runtime.Release.TrySetResult(); }
        var receipt = await pending;
        receipt.Message.AssistantResponse?.Body.Should().Be(fail || cancel ? null : "safe preview complete");
        receipt.Message.Outcome.Should().Be(cancel ? ModelTurnOutcome.Cancelled
            : fail ? ModelTurnOutcome.Unknown : ModelTurnOutcome.Succeeded);
        receipt.Message.Body.Should().Be("original Claude input");
        updates.Should().Be(0, "ordinary Claude answers are not provisional streams");
        f.ExecutionCompletions.Should().Be(1);
        host.IsQuiescent.Should().BeTrue();
        runtime.Streams.Should().Be(0);
        runtime.Buffered.Should().Be(1);
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
    public async Task OrdinaryClaudeRechecksCurrentAccountProfileAfterProviderCompletionBeforeFinalCommit()
    {
        using var f = new Fixture();
        var runtime = new HostClaudeStream();
        var host = f.ClaudeProfiles(runtime, new(f, f.Time));
        await using var service = f.ConversationService(host, execution: host);
        service.RunningConversationChanged += status =>
        {
            if (status.Phase == Kora.Core.Conversation.ConversationExecutionPhase.Completing)
            { f.ProfileCatalogue.Status = ProviderCatalogueStatus.Unavailable; }
        };
        var pending = service.SubmitAsync(new(f.Session, null), "original", () => true, Token);
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
        internal int Streams { get; private set; }
        public async Task<ClaudeTurnReply> RunAsync(ModelTurnProvenance provenance, ProviderProfileTuple profile,
            ModelContextEnvelope context, Func<ClaudeTransmission, CancellationToken, ValueTask<bool>> admit,
            Func<CancellationToken, ValueTask<bool>> current, CancellationToken token)
        {
            Buffered++;
            if (!await admit(new(provenance, profile, new("https://api.anthropic.com/v1/messages"), new byte[] { 123, 125 }, []), token))
            { return new(ModelTurnOutcome.DeniedEgress); }
            Entered.TrySetResult();
            await Release.Task;
            return Fail ? new(ModelTurnOutcome.Unknown) : new(ModelTurnOutcome.Succeeded, "safe preview complete");
        }
        public async Task<ClaudeTurnReply> RunWithAnswerAsync(ModelTurnProvenance provenance, ProviderProfileTuple profile,
            ModelContextEnvelope context, Func<ClaudeTransmission, CancellationToken, ValueTask<bool>> admit,
            Func<CancellationToken, ValueTask<bool>> current,
            Func<ModelAnswerUpdate, CancellationToken, ValueTask> publish, CancellationToken token)
        {
            Streams++;
            await publish(new(provenance, "safe preview"), token);
            Entered.TrySetResult();
            await Release.Task;
            return Fail ? new(ModelTurnOutcome.Unknown) : new(ModelTurnOutcome.Succeeded, "safe preview complete");
        }
    }
}
