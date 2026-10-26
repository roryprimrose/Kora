using System.Text;
using System.Text.Json;

using AwesomeAssertions;

using Kora.Application.Dependencies;
using Kora.Application.Hosting;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Providers;
using Kora.Core.Storage;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Application.UnitTests.Dependencies;

public sealed partial class ModelTurnHostTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task LocalAnswerPublishesVolatileExactTurnAndRetainsOnlyValidatedWholeTerminal(bool fail, bool noViewer)
    {
        using var f = new Fixture();
        var transport = new AnswerTransport { Fail = fail };
        await using var adapter = CreateLoop(f, transport);
        var host = ConfigureAnswer(f, adapter);
        var service = f.ConversationService(host, execution: host);
        var updates = new List<ConversationAnswerPresentation>();
        if (!noViewer)
        {
            service.ProvisionalAnswerChanged += (value, _) =>
            {
                updates.Add(value);
                value.IsCurrent().Should().BeTrue();
                f.Retained!.AssistantResponse.Should().BeNull();
                f.ExecutionCompletions.Should().Be(0);
                return ValueTask.CompletedTask;
            };
        }
        var submission = service.SubmitLocalAnswerAsync(new(f.Session, null), "original local question", static () => true, Token);
        await transport.Entered.Task.WaitAsync(Token);
        try
        {
            submission.IsCompleted.Should().BeFalse();
            f.Retained!.AssistantResponse.Should().BeNull();
            f.ExecutionCompletions.Should().Be(0);
            if (!noViewer)
            {
                updates.Should().ContainSingle().Which.Update.Answer.Should().Be("safe partial");
                updates[0].Target.Request.Should().Be(f.Retained.Request);
                updates[0].Target.Generation.Should().Be(f.Session.Generation);
                updates[0].Update.Provenance.Should().Be(f.Retained.Execution);
            }
        }
        finally { transport.Release.TrySetResult(); }
        var receipt = await submission;
        receipt.Message.Body.Should().Be("original local question");
        receipt.Message.Outcome.Should().Be(fail ? ModelTurnOutcome.Denied : ModelTurnOutcome.Succeeded);
        if (fail) { receipt.Message.AssistantResponse.Should().BeNull(); }
        else { receipt.Message.AssistantResponse!.Body.Should().Be("safe partial complete"); }
        updates.Should().OnlyContain(value => !value.IsCurrent());
        f.ExecutionCompletions.Should().Be(1);
        f.Messages.Should().NotContain(value => value.Contains("safe partial", StringComparison.Ordinal)
            || value.Contains("original local question", StringComparison.Ordinal));
        transport.BufferedCalls.Should().Be(0);
    }

    [Fact]
    public async Task LocalAnswerStopSuppressesNoncooperativeLatePublicationAndJoinsBeforeDurableTerminal()
    {
        using var f = new Fixture();
        var transport = new AnswerTransport();
        await using var adapter = CreateLoop(f, transport);
        var host = ConfigureAnswer(f, adapter);
        var service = f.ConversationService(host, execution: host);
        var updates = new List<ConversationAnswerPresentation>();
        service.ProvisionalAnswerChanged += (value, _) =>
        {
            updates.Add(value);
            return ValueTask.CompletedTask;
        };
        var target = new SessionWorkspaceEntry(f.Session, null);
        var submission = service.SubmitLocalAnswerAsync(target, "stop this answer", static () => true, Token);
        await transport.Entered.Task.WaitAsync(Token);
        var running = (await service.ReadRunningConversationAsync(target, static () => true, Token))!;
        await service.CancelRunningConversationAsync(running.Target, static () => true, Token);
        updates.Single().IsCurrent().Should().BeFalse();
        submission.IsCompleted.Should().BeFalse();
        f.ExecutionCompletions.Should().Be(0);
        host.IsQuiescent.Should().BeFalse();
        transport.Release.TrySetResult();
        var receipt = await submission;
        receipt.Message.Outcome.Should().Be(ModelTurnOutcome.Cancelled);
        receipt.Message.AssistantResponse.Should().BeNull();
        updates.Should().ContainSingle();
        host.IsQuiescent.Should().BeTrue();
        transport.LatePublicationRejected.Should().BeTrue();
    }

    [Theory]
    [InlineData("source")]
    [InlineData("privacy")]
    [InlineData("control")]
    [InlineData("owner")]
    [InlineData("generation")]
    [InlineData("metadata")]
    [InlineData("expiry")]
    public async Task LocalAnswerRevalidatesSourcePrivacyOwnerGenerationAndMetadataBeforeEveryPublication(string fault)
    {
        using var f = new Fixture();
        var transport = new AnswerTransport();
        await using var adapter = CreateLoop(f, transport);
        var host = ConfigureAnswer(f, adapter);
        var service = f.ConversationService(host, execution: host);
        var current = true;
        var updates = new List<ConversationAnswerPresentation>();
        service.ProvisionalAnswerChanged += (value, _) =>
        {
            updates.Add(value);
            return ValueTask.CompletedTask;
        };
        var submission = service.SubmitLocalAnswerAsync(new(f.Session, null), "exact request", () => current, Token);
        await transport.Entered.Task.WaitAsync(Token);
        switch (fault)
        {
            case "source": current = false; break;
            case "privacy": f.CanControl = false; break;
            case "control": f.ControlRevision++; break;
            case "owner": f.Current = false; break;
            case "generation": f.Session = f.Session with { Generation = new(2) }; break;
            case "metadata": f.Metadata = new(f.Session.SessionId, new(1), new("changed")); break;
            case "expiry": f.Time.Now += TimeSpan.FromMinutes(1); break;
        }
        transport.Release.TrySetResult();
        if (fault is "owner" or "expiry")
        {
            var receipt = await submission;
            receipt.Message.Outcome.Should().Be(ModelTurnOutcome.Denied);
        }
        else { await submission.Invoking(async pending => await pending).Should().ThrowAsync<Exception>(); }
        f.Retained!.AssistantResponse.Should().BeNull();
        updates.Should().ContainSingle();
        host.IsQuiescent.Should().BeTrue();
    }

    [Theory]
    [InlineData("foreign")]
    [InlineData("replacement")]
    [InlineData("oversize")]
    public async Task HostRejectsForeignReplacingOrOversizedProvisionalSnapshotsBeforePublication(string fault)
    {
        using var f = new Fixture();
        var transport = new AnswerTransport { Fault = fault };
        await using var adapter = CreateLoop(f, transport);
        var host = ConfigureAnswer(f, adapter);
        var service = f.ConversationService(host, execution: host);
        var updates = new List<string>();
        service.ProvisionalAnswerChanged += (value, _) =>
        {
            updates.Add(value.Update.Answer);
            return ValueTask.CompletedTask;
        };
        var submission = service.SubmitLocalAnswerAsync(new(f.Session, null), "request", static () => true, Token);
        await transport.Entered.Task.WaitAsync(Token);
        transport.Release.TrySetResult();
        var receipt = await submission;
        receipt.Message.AssistantResponse.Should().BeNull();
        receipt.Message.Outcome.Should().NotBe(ModelTurnOutcome.Succeeded);
        updates.Should().Equal("safe partial");
    }

    private static ModelTurnHost ConfigureAnswer(Fixture f, IModelTurnAdapter adapter)
    {
        f.ProfileCatalogue.Status = ProviderCatalogueStatus.Current;
        f.ProfilePreferences.Value = new(ProviderService.Local, [Profile()]);
        return f.ConfiguredProfiles(adapter: adapter);
    }

    private sealed class AnswerTransport : IModelToolTransport, IModelAnswerTransport
    {
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool Fail { get; init; }
        internal string? Fault { get; init; }
        internal int BufferedCalls { get; private set; }
        internal bool LatePublicationRejected { get; private set; }

        public Task<ModelToolTransportReply> CompleteAsync(ModelToolRequest request, CancellationToken cancellationToken)
        {
            BufferedCalls++;
            return Task.FromResult(new ModelToolTransportReply(ModelTurnOutcome.Denied, request.Provenance));
        }

        public async Task<ModelToolTransportReply> CompleteWithAnswerAsync(ModelToolRequest request,
            Func<ModelAnswerUpdate, CancellationToken, ValueTask> publish, CancellationToken cancellationToken)
        {
            request.Context.ToolMode.Should().Be(ModelToolMode.None);
            await publish(new(request.Provenance, "safe partial"), cancellationToken);
            Entered.TrySetResult();
            await Release.Task;
            if (Fail) { return new(ModelTurnOutcome.Denied, request.Provenance); }
            var provenance = Fault is "foreign" ? request.Provenance with { Turn = new(Guid.NewGuid()) } : request.Provenance;
            var answer = Fault switch
            {
                "replacement" => "replacement text",
                "oversize" => new string('x', ModelToolProtocol.MaximumOutputUtf8Bytes + 1),
                _ => "safe partial complete",
            };
            try { await publish(new(provenance, answer), CancellationToken.None); }
            catch (OperationCanceledException) { LatePublicationRejected = true; throw; }
            var bytes = JsonSerializer.SerializeToUtf8Bytes(new { kind = "answer", answer });
            return new(ModelTurnOutcome.Succeeded, request.Provenance, ModelToolProtocol.ParseAssistant(bytes),
                new(ModelToolProtocol.SerializeRequest(request).Length, bytes.Length));
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
