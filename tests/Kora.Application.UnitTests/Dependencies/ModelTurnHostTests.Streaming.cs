using System.Text;
using System.Text.Json;

using AwesomeAssertions;

using Kora.Application.Dependencies;
using Kora.Application.Hosting;
using Kora.Core.Auditing;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Providers;
using Kora.Core.Storage;
using Kora.Core.Tools;
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

    [Theory]
    [InlineData("provider")]
    [InlineData("session")]
    [InlineData("privacy")]
    [InlineData("owner")]
    public async Task ObservedProviderRevocationRetiresOriginalProgressUntilPhysicalJoinAndCannotBeUndoneByRecovery(string fault)
    {
        using var f = new Fixture();
        var transport = new RevokedAnswerTransport();
        await using var adapter = CreateLoop(f, transport);
        var host = ConfigureAnswer(f, adapter);
        var service = f.ConversationService(host, execution: host);
        var updates = new List<ConversationAnswerPresentation>();
        var retirementNotifications = 0;
        service.RunningConversationChanged += status =>
        {
            if (status.Phase == ConversationExecutionPhase.Running && updates.Count != 0 && !updates[0].IsCurrent())
            { retirementNotifications++; }
        };
        service.ProvisionalAnswerChanged += (value, _) =>
        {
            updates.Add(value);
            return ValueTask.CompletedTask;
        };
        var submission = service.SubmitLocalAnswerAsync(new(f.Session, null), "original request", static () => true, Token);
        await transport.Entered.Task.WaitAsync(Token);
        var originalSession = f.Session;
        try
        {
            updates.Should().ContainSingle().Which.IsCurrent().Should().BeTrue();
            switch (fault)
            {
                case "provider": f.ProfileCatalogue.Status = ProviderCatalogueStatus.Unavailable; break;
                case "session": f.Session = f.Session with { Generation = new(2) }; break;
                case "privacy": f.CanControl = false; break;
                case "owner": f.Current = false; break;
            }
            transport.Probe.TrySetResult();
            await transport.Rejected.Task.WaitAsync(Token);
            retirementNotifications.Should().Be(1);
            host.IsQuiescent.Should().BeFalse("rejected output is not physical provider termination");
            submission.IsCompleted.Should().BeFalse();
            f.ExecutionCompletions.Should().Be(0);
            f.Retained!.AssistantResponse.Should().BeNull();
            updates[0].IsCurrent().Should().BeFalse("observed revocation retires previously published progress");
            f.ProfileCatalogue.Status = ProviderCatalogueStatus.Current;
            f.Session = originalSession;
            f.CanControl = true;
            f.Current = true;
            updates[0].IsCurrent().Should().BeFalse("recovery cannot renew this original turn's authority");
        }
        finally
        {
            f.ProfileCatalogue.Status = ProviderCatalogueStatus.Current;
            f.Session = originalSession;
            f.CanControl = true;
            f.Current = true;
            transport.Probe.TrySetResult();
            transport.Release.TrySetResult();
            await submission;
        }
        var receipt = await submission;
        receipt.Message.Outcome.Should().Be(ModelTurnOutcome.Denied);
        receipt.Message.AssistantResponse.Should().BeNull();
        updates.Should().ContainSingle();
        transport.LateRejected.Should().BeTrue();
        transport.BufferedCalls.Should().Be(0);
        host.IsQuiescent.Should().BeTrue();
        f.ExecutionCompletions.Should().Be(1);
        f.Audits.Last(value => string.Equals(value.ActionId, "model.turn", StringComparison.Ordinal))
            .Outcome.Should().Be(SecurityAuditOutcome.Denied);
        f.Messages.Should().NotContain(value => value.Contains("safe partial", StringComparison.Ordinal)
            || value.Contains("original request", StringComparison.Ordinal));

        var other = HostRequest.Create(RequestOrigin.LocalUi);
        f.Session = new(other.SessionId, new(1), true);
        f.Task = new(new(other, new(1), HostTaskState.IntentRecorded), new(1), "host", true, null);
        f.ExpectedAuditRequest = other;
        using var root = HostActivity.BeginRoot(other, HostActivityLayer.Application, HostOperation.Request);
        var admission = await host.AdmitAsync(ModelProviderSelection.OllamaCandidate,
            f.Context(other, toolMode: ModelToolMode.None), Token);
        var unrelated = await host.RunAsync(admission.Turn!, Token);
        unrelated.Outcome.Should().Be(ModelTurnOutcome.Succeeded);
        unrelated.Provenance!.Request.Should().BeSameAs(other);
        transport.BufferedCalls.Should().Be(1, "a separately admitted session is unaffected by the retired original turn");
    }

    [Fact]
    public async Task RevokedToolCallbackCannotRegainOriginalAuthorityAfterOwnerRecovery()
    {
        using var f = new Fixture();
        using var root = f.Root();
        var calls = 0;
        f.OnVersion = () => calls++;
        f.Adapter.OnTool = async invoke =>
        {
            f.Current = false;
            (await invoke(ReadOnlyCapabilityCatalog.Version, "{}", Token)).Outcome.Should().Be(CapabilityOutcome.Denied);
            f.Current = true;
            (await invoke(ReadOnlyCapabilityCatalog.Version, "{}", Token)).Outcome.Should().Be(CapabilityOutcome.Denied);
        };
        var result = await f.Run();
        result.Outcome.Should().Be(ModelTurnOutcome.Denied);
        result.Response.Should().BeNull();
        calls.Should().Be(0);
        f.Audits.Last().Outcome.Should().Be(SecurityAuditOutcome.Denied);
        f.Adapter.OnTool = null;
        (await f.Run()).Outcome.Should().Be(ModelTurnOutcome.Succeeded);
    }

    private sealed class RevokedAnswerTransport : IModelToolTransport, IModelAnswerTransport
    {
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Probe { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Rejected { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool LateRejected { get; private set; }
        internal int BufferedCalls { get; private set; }

        public Task<ModelToolTransportReply> CompleteAsync(ModelToolRequest request, CancellationToken cancellationToken)
        {
            BufferedCalls++;
            var bytes = JsonSerializer.SerializeToUtf8Bytes(new { kind = "answer", answer = "unrelated answer" });
            return Task.FromResult(new ModelToolTransportReply(ModelTurnOutcome.Succeeded, request.Provenance,
                ModelToolProtocol.ParseAssistant(bytes), new(ModelToolProtocol.SerializeRequest(request).Length, bytes.Length)));
        }

        public async Task<ModelToolTransportReply> CompleteWithAnswerAsync(ModelToolRequest request,
            Func<ModelAnswerUpdate, CancellationToken, ValueTask> publish, CancellationToken cancellationToken)
        {
            await publish(new(request.Provenance, "safe partial"), cancellationToken);
            Entered.TrySetResult();
            await Probe.Task;
            try { await publish(new(request.Provenance, "safe partial revoked"), CancellationToken.None); }
            catch (InvalidDataException) { Rejected.TrySetResult(); }
            await Release.Task;
            try { await publish(new(request.Provenance, "safe partial late"), CancellationToken.None); }
            catch (InvalidDataException) { LateRejected = true; }
            var bytes = JsonSerializer.SerializeToUtf8Bytes(new { kind = "answer", answer = "safe partial late" });
            return new(ModelTurnOutcome.Succeeded, request.Provenance, ModelToolProtocol.ParseAssistant(bytes),
                new(ModelToolProtocol.SerializeRequest(request).Length, bytes.Length));
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
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
