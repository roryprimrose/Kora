using System.Text.Json;
using System.Diagnostics;
using System.Collections.Concurrent;

using AwesomeAssertions;

using Kora.Application.Hosting;
using Kora.Application.Dependencies;
using Kora.Application.UnitTests.Agents;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Application.UnitTests.Dependencies;

public sealed partial class ModelTurnHostTests
{
    private const string HistoryAdvisory = """{"version":1,"advisory":"HISTORYREVIEW"}""";

    private static ConversationMessage HistoryAdvisorySource(Fixture f)
    {
        var source = Earlier(f, 2);
        source = source with { Body = " \r\n unchanged original question e\u0301? \t",
            AssistantResponse = source.AssistantResponse! with { Body = HistoryAdvisory } };
        f.HistoryMessages[source.Request.RequestId] = source;
        return source;
    }

    private static (ConversationMessage Earlier, ConversationMessage Advisory) TwoSessionHistory(AgentRunSetTests.Fixture f)
    {
        ConversationMessage Retained(long sequence, string body, string answer)
        {
            var request = new HostRequest(new(Guid.NewGuid()), f.Sessions[0].Authority.SessionId,
                new(Guid.NewGuid()), RequestOrigin.LocalUi);
            var provenance = new ModelTurnProvenance(new(Guid.NewGuid()), request, ModelProviderSelection.OllamaCandidate);
            var message = new ConversationMessage(request, f.Sessions[0].Authority.Generation, sequence, 3,
                f.Time.GetUtcNow(), body, ModelTurnOutcome.Succeeded, ModelTurnReason.ProviderOutcome)
            {
                Version = 2,
                Execution = provenance,
                ExecutionMetadataRevision = 0,
                AssistantResponse = new(provenance, answer),
            };
            message.Validate();
            f.Messages[request.RequestId] = message;
            return message;
        }
        return (Retained(1, "private earlier user message", "private earlier answer"),
            Retained(2, " \r\n unchanged original question e\u0301? \t", HistoryAdvisory));
    }

    private sealed class HistoryRegistrations : IModelProviderRegistrationSource
    {
        internal ConcurrentDictionary<HostId<SessionIdentity>, AgentRunSetTests.BarrierAdapter> Transports { get; } = new();
        public Task<ModelProviderRegistration?> ResolveAsync(ModelProviderSelection selection,
            Kora.Core.Providers.ProviderProfileTuple profile, CancellationToken cancellationToken) =>
            Task.FromResult<ModelProviderRegistration?>(new(selection,
                ModelQualificationGate.LocalCandidateSelection | ModelQualificationGate.IntegratedHost,
                DateTimeOffset.UnixEpoch.AddMinutes(5),
                Transports.GetOrAdd(HostActivity.RequireCurrent().Request.SessionId, _ => new())));
        internal AgentRunSetTests.BarrierAdapter Transport(int index, AgentRunSetTests.Fixture f) =>
            Transports[f.Sessions[index].Authority.SessionId];
        internal Task Started(int index, AgentRunSetTests.Fixture f) =>
            Transport(index, f).Started(f.Sessions[index]).Task.WaitAsync(TimeSpan.FromSeconds(5), Token);
        internal void FinishAll(AgentRunSetTests.Fixture f)
        {
            foreach (var session in f.Sessions)
            {
                if (Transports.TryGetValue(session.Authority.SessionId, out var transport) && transport.Active > 0)
                { transport.Finish(session, "test cleanup"); }
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AdvisoryTwoSessionCompletionReturnsExactAnswerOrUnavailableWithoutJoiningIndependentSession(bool singleCapacity)
    {
        var source = new RuntimeResourceAllocatorTests.RuntimeSource { Current = !singleCapacity };
        var registrations = new HistoryRegistrations();
        source.Evidence = source.Evidence with { OwnsIndependentTransport = adapter => registrations.Transports.Values.Contains(adapter) };
        await using var f = new AgentRunSetTests.Fixture(runtimeCapacity: source, registrationSource: registrations);
        using var cleanup = source;
        var (earlier, advisory) = TwoSessionHistory(f);
        using var binding = await f.Conversation.BindModelHistoryContextAsync(f.Sessions[0], advisory, static () => true, Token);
        using var review = await f.Conversation.ReviewModelHistoryContextAsync(binding,
            [new(earlier, ConversationHistoryRole.Assistant)], static () => true, Token);
        review.Text.Should().Contain(earlier.AssistantResponse!.Body).And.Contain(advisory.Body)
            .And.NotContain(earlier.Body).And.NotContain(HistoryAdvisory);
        var independent = f.Conversation.SubmitAsync(f.Sessions[1], "independent original question", static () => true, Token);
        await registrations.Started(1, f);
        var addressed = f.Conversation.SubmitHistoryAsync(review, static () => true, Token);
        try
        {
            if (!singleCapacity)
            {
                await registrations.Started(0, f);
                registrations.Transport(0, f).Finish(f.Sessions[0], "exact reviewed-history answer");
            }
            var receipt = await addressed.WaitAsync(TimeSpan.FromSeconds(5), Token);
            receipt.Message.Body.Should().Be(advisory.Body);
            receipt.Message.Request.SessionId.Should().Be(advisory.Request.SessionId);
            receipt.Message.Outcome.Should().Be(singleCapacity ? ModelTurnOutcome.Unavailable : ModelTurnOutcome.Succeeded);
            if (singleCapacity)
            {
                receipt.Message.Reason.Should().Be(ModelTurnReason.ProviderBusy);
                receipt.Message.Execution.Should().BeNull();
                receipt.Message.AssistantResponse.Should().BeNull();
                registrations.Transport(0, f).Started(f.Sessions[0]).Task.IsCompleted.Should().BeFalse();
            }
            else { receipt.Message.AssistantResponse!.Body.Should().Be("exact reviewed-history answer"); }
            (await f.ReadConversationMessageAsync(receipt.Message.Request.SessionId, receipt.Message.Request.RequestId, Token))
                .Should().Be(receipt.Message);
            (await f.ReadTaskAsync(receipt.Task.Request.SessionId, receipt.Task.Request.TaskId, Token))!.Task.Should().Be(receipt.Task);
            receipt.Task.IsTerminal.Should().BeTrue();
            independent.IsCompleted.Should().BeFalse("the completed operation must not join an unrelated session");
            registrations.Transport(1, f).Cancelled(f.Sessions[1]).Task.IsCompleted.Should().BeFalse();
            f.Host.IsQuiescent.Should().BeFalse();
        }
        finally
        {
            registrations.FinishAll(f);
            await Task.WhenAll(addressed, independent).WaitAsync(Token);
        }
    }

    [Fact]
    public async Task AdvisoryTwoSessionCancellationJoinsIgnoringOwnTransportButNotIndependentSessionOrLateAnswer()
    {
        var source = new RuntimeResourceAllocatorTests.RuntimeSource();
        var registrations = new HistoryRegistrations();
        source.Evidence = source.Evidence with { OwnsIndependentTransport = adapter => registrations.Transports.Values.Contains(adapter) };
        await using var f = new AgentRunSetTests.Fixture(runtimeCapacity: source, registrationSource: registrations);
        using var cleanup = source;
        var (earlier, advisory) = TwoSessionHistory(f);
        using var binding = await f.Conversation.BindModelHistoryContextAsync(f.Sessions[0], advisory, static () => true, Token);
        using var review = await f.Conversation.ReviewModelHistoryContextAsync(binding,
            [new(earlier, ConversationHistoryRole.Assistant)], static () => true, Token);
        var independent = f.Conversation.SubmitAsync(f.Sessions[1], "independent original question", static () => true, Token);
        await registrations.Started(1, f);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var addressed = f.Conversation.SubmitHistoryAsync(review, static () => true, cancellation.Token);
        try
        {
            await registrations.Started(0, f);
            await cancellation.CancelAsync();
            await registrations.Transport(0, f).Cancelled(f.Sessions[0]).Task.WaitAsync(Token);
            addressed.IsCompleted.Should().BeFalse("cancellation cannot abandon its own ignoring physical work");
            registrations.Transport(0, f).IsQuiescent.Should().BeFalse();
            registrations.Transport(0, f).Finish(f.Sessions[0], "forbidden late private answer");
            await ((Func<Task>)(() => addressed.WaitAsync(TimeSpan.FromSeconds(5), Token)))
                .Should().ThrowAsync<ConversationSubmissionUnconfirmedException>();
            registrations.Transport(0, f).IsQuiescent.Should().BeTrue();
            independent.IsCompleted.Should().BeFalse();
            registrations.Transport(1, f).Cancelled(f.Sessions[1]).Task.IsCompleted.Should().BeFalse();
            var retained = f.Messages.Values.Single(message => message.Request.SessionId == advisory.Request.SessionId
                && message.Request != earlier.Request && message.Request != advisory.Request);
            retained.AssistantResponse.Should().BeNull();
            retained.Outcome.Should().NotBe(ModelTurnOutcome.Succeeded);
            retained.Revision.Should().Be(2, "no late answer may be committed");
            f.Host.IsQuiescent.Should().BeFalse();
        }
        finally
        {
            registrations.FinishAll(f);
            await ((Func<Task>)(() => addressed)).Should().ThrowAsync<ConversationSubmissionUnconfirmedException>();
            await independent.WaitAsync(Token);
        }
    }

    [Fact]
    public async Task OrdinaryProductionAdapterPolicyCanReturnInertAdvisoryWithoutAnyHistoryRead()
    {
        using var f = new Fixture();
        _ = QualifiedHistoryHost(f);
        var transport = new ToolTransport(JsonSerializer.Serialize(new { kind = "answer", answer = HistoryAdvisory }));
        await using var adapter = CreateLoop(f, transport);
        var host = f.ConfiguredProfiles(adapter: adapter);
        await using var service = f.ConversationService(host, execution: host);
        var result = await service.SubmitAsync(new(f.Session, null), "original question", static () => true, Token);
        result.Message.AssistantResponse!.Body.Should().Be(HistoryAdvisory);
        transport.Requests.Should().ContainSingle();
        transport.Requests[0].Context.SystemPolicy.Should().Contain(ModelHistoryContextProposal.ModelPolicy);
        transport.Requests[0].Context.UserRequest.Should().Be("original question");
        f.HistoryReads.Should().Be(0);
        f.RetainCalls.Should().Be(1);
    }

    [Theory]
    [InlineData(RequestOrigin.LocalUi)]
    [InlineData(RequestOrigin.ActivatedVoice)]
    public async Task AdvisoryPinsOriginalQuestionAndChannelThenUserSubsetReviewConfirmsOneRealLocalAnswer(RequestOrigin origin)
    {
        using var f = new Fixture();
        var earlier = Earlier(f);
        var source = HistoryAdvisorySource(f);
        var originalRequest = new HostRequest(source.Request.RequestId, source.Request.SessionId, source.Request.TaskId, origin);
        var provenance = source.Execution! with { Request = originalRequest };
        source = source with { Request = originalRequest, Execution = provenance,
            AssistantResponse = new(provenance, HistoryAdvisory) };
        f.HistoryMessages[source.Request.RequestId] = source;
        var host = QualifiedHistoryHost(f);
        await using var service = f.ConversationService(host, execution: host);
        using var binding = await service.BindModelHistoryContextAsync(new(f.Session, null), source, static () => true, Token);
        binding.OriginalQuestion.Should().Be(source.Body);
        binding.Origin.Should().Be(origin);
        f.HistoryReads.Should().Be(1, "binding reads only the advisory source, never additional history");
        f.Adapter.Calls.Should().Be(0);
        f.RetainCalls.Should().Be(0);
        using var review = await service.ReviewModelHistoryContextAsync(binding,
            [new(earlier, ConversationHistoryRole.Assistant)], static () => true, Token);
        review.Text.Should().Contain(earlier.AssistantResponse!.Body).And.Contain(source.Body)
            .And.Contain("UNCHANGED ORIGINAL USER REQUEST - LOCAL ONLY, NO TOOLS")
            .And.NotContain(earlier.Body).And.NotContain(HistoryAdvisory);
        f.Adapter.Calls.Should().Be(0);
        f.RetainCalls.Should().Be(0);
        var result = await service.SubmitHistoryAsync(review, static () => true, Token);
        result.Message.Body.Should().Be(source.Body);
        result.Message.Request.Origin.Should().Be(origin);
        result.Message.Request.SessionId.Should().Be(source.Request.SessionId);
        result.Message.Generation.Should().Be(source.Generation);
        result.Message.AssistantResponse!.Body.Should().Be("answer");
        result.Message.Should().Be(f.Retained);
        f.Adapter.Calls.Should().Be(1);
        f.RetainCalls.Should().Be(1);
        f.ExecutionCompletions.Should().Be(1);
        using var wire = JsonDocument.Parse(f.Adapter.Wire!);
        var root = wire.RootElement;
        root.GetProperty("UserRequest").GetString().Should().Be(source.Body);
        root.GetProperty("ToolMode").GetInt32().Should().Be((int)ModelToolMode.None);
        root.GetProperty("Tools").GetArrayLength().Should().Be(0);
        root.GetProperty("Evidence").GetArrayLength().Should().Be(1);
        root.GetProperty("Evidence")[0].GetProperty("Disclosure").GetInt32().Should().Be((int)ModelEvidenceDisclosure.LocalOnly);
        var disclosed = root.GetProperty("Evidence")[0].GetProperty("Content").GetString()!;
        disclosed.Should().Contain(earlier.AssistantResponse.Body).And.NotContain(earlier.Body).And.NotContain(HistoryAdvisory);
        await service.Invoking(value => value.SubmitHistoryAsync(review, static () => true, Token))
            .Should().ThrowAsync<InvalidOperationException>();
        await service.Invoking(value => value.ReviewModelHistoryContextAsync(binding,
            [new(earlier, ConversationHistoryRole.User)], static () => true, Token)).Should().ThrowAsync<InvalidOperationException>();
        f.Messages.Should().NotContain(text => text.Contains(source.Body, StringComparison.Ordinal)
            || text.Contains(earlier.Body, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("foreign")]
    [InlineData("invocation")]
    [InlineData("generation")]
    [InlineData("malformed")]
    [InlineData("changed")]
    [InlineData("missing")]
    [InlineData("control")]
    [InlineData("privacy")]
    [InlineData("admission")]
    [InlineData("cancel")]
    public async Task BindingRejectsUntrustedOrRevokedSourceBeforeReadingAnyAdditionalHistory(string fault)
    {
        using var f = new Fixture();
        var source = HistoryAdvisorySource(f);
        var host = QualifiedHistoryHost(f);
        await using var service = f.ConversationService(host, execution: host);
        var target = new SessionWorkspaceEntry(f.Session, null);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        if (fault is "invocation")
        {
            var request = new HostRequest(source.Request.RequestId, source.Request.SessionId, source.Request.TaskId,
                source.Request.Origin, new(Guid.NewGuid()));
            var provenance = source.Execution! with { Request = request };
            source = source with { Request = request, Execution = provenance, AssistantResponse = new(provenance, HistoryAdvisory) };
        }
        if (fault is "foreign") { target = target with { Authority = target.Authority with { SessionId = new(Guid.NewGuid()) } }; }
        if (fault is "generation") { target = target with { Authority = target.Authority with { Generation = new(2) } }; }
        if (fault is "malformed") { source = source with { AssistantResponse = source.AssistantResponse! with { Body = "{}" } }; }
        if (fault is "changed") { f.HistoryMessages[source.Request.RequestId] = source with { Revision = 4 }; }
        if (fault is "missing") { f.HistoryMessages.Clear(); }
        f.BeforeHistoryRead = () =>
        {
            if (fault is "control") { f.ControlRevision++; }
            if (fault is "privacy") { f.Inspect = false; }
            if (fault is "cancel") { cancellation.Cancel(); }
        };
        await service.Invoking(value => value.BindModelHistoryContextAsync(target, source,
            () => fault is not "admission", cancellation.Token)).Should().ThrowAsync<Exception>();
        f.HistoryReads.Should().BeLessThanOrEqualTo(1);
        f.RetainCalls.Should().Be(0);
        f.Adapter.Calls.Should().Be(0);
    }

    [Theory]
    [InlineData("source")]
    [InlineData("selected")]
    [InlineData("delete")]
    [InlineData("generation")]
    [InlineData("metadata")]
    [InlineData("private")]
    [InlineData("control")]
    [InlineData("input")]
    [InlineData("lifetime")]
    [InlineData("expiry")]
    [InlineData("clock")]
    [InlineData("disposed")]
    [InlineData("cancel")]
    public async Task ChangedPinsCannotRetainOrDispatchAndCannotRenewTheSource(string fault)
    {
        using var f = new Fixture();
        var earlier = Earlier(f);
        var source = HistoryAdvisorySource(f);
        var host = QualifiedHistoryHost(f);
        await using var service = f.ConversationService(host, execution: host);
        var live = true;
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(Token);
        using var binding = await service.BindModelHistoryContextAsync(new(f.Session, null), source, () => live, lifetime.Token);
        using var review = await service.ReviewModelHistoryContextAsync(binding,
            [new(earlier, ConversationHistoryRole.User)], static () => true, Token);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        switch (fault)
        {
            case "source": f.HistoryMessages[source.Request.RequestId] = source with { Body = "replacement original question" }; break;
            case "selected": f.HistoryMessages[earlier.Request.RequestId] = earlier with { Revision = 4 }; break;
            case "delete": f.HistoryMessages.Clear(); break;
            case "generation": f.Session = f.Session with { Generation = new(2) }; break;
            case "metadata": f.Metadata = new(f.Session.SessionId, new(1), new("changed name")); break;
            case "private": f.Inspect = false; break;
            case "control": f.ControlRevision++; break;
            case "input": live = false; break;
            case "lifetime": lifetime.Cancel(); break;
            case "expiry": f.Time.Now = f.Time.Now.AddSeconds(60); break;
            case "clock": f.Time.Now = f.Time.Now.AddTicks(-1); break;
            case "disposed": binding.Dispose(); break;
            case "cancel": cancellation.Cancel(); break;
        }
        await service.Invoking(value => value.SubmitHistoryAsync(review, static () => true, cancellation.Token))
            .Should().ThrowAsync<Exception>();
        f.RetainCalls.Should().Be(0);
        f.Adapter.Calls.Should().Be(0);
        if (fault is "private" or "control" or "input" or "lifetime" or "expiry" or "clock" or "disposed")
        { binding.OriginalQuestion.Should().BeEmpty(); }
        review.Text.Should().BeEmpty();
    }

    [Fact]
    public async Task SelectionMustPrecedeSourceAndSourceOwnerAndOneUseCannotBeSubstituted()
    {
        using var f = new Fixture();
        var earlier = Earlier(f);
        var source = HistoryAdvisorySource(f);
        var later = Earlier(f, 3);
        var host = QualifiedHistoryHost(f);
        await using var service = f.ConversationService(host, execution: host);
        await using var other = f.ConversationService(host, execution: host);
        using var binding = await service.BindModelHistoryContextAsync(new(f.Session, null), source, static () => true, Token);
        await other.Invoking(value => value.ReviewModelHistoryContextAsync(binding,
            [new(earlier, ConversationHistoryRole.User)], static () => true, Token)).Should().ThrowAsync<InvalidOperationException>();
        foreach (var invalid in new[] { source, later })
        {
            await service.Invoking(value => value.ReviewModelHistoryContextAsync(binding,
                [new(invalid, ConversationHistoryRole.User)], static () => true, Token)).Should().ThrowAsync<InvalidDataException>();
        }
        using var first = await service.ReviewModelHistoryContextAsync(binding,
            [new(earlier, ConversationHistoryRole.User)], static () => true, Token);
        using var second = await service.ReviewModelHistoryContextAsync(binding,
            [new(earlier, ConversationHistoryRole.Assistant)], static () => true, Token);
        await service.SubmitHistoryAsync(first, static () => true, Token);
        await service.Invoking(value => value.SubmitHistoryAsync(second, static () => true, Token))
            .Should().ThrowAsync<InvalidOperationException>();
        f.Adapter.Calls.Should().Be(1);
        await service.Invoking(value => value.ReviewModelHistoryContextAsync(null!,
            [new(earlier, ConversationHistoryRole.User)], static () => true, Token)).Should().ThrowAsync<ArgumentNullException>();
    }

    [Theory]
    [InlineData("advisory")]
    [InlineData("selected")]
    [InlineData("private")]
    [InlineData("metadata")]
    public async Task SourceChangeDuringActualInferenceSuppressesLateAnswerBeforeTerminalCommit(string fault)
    {
        using var f = new Fixture();
        var earlier = Earlier(f);
        var source = HistoryAdvisorySource(f);
        var host = QualifiedHistoryHost(f);
        await using var service = f.ConversationService(host, execution: host);
        using var binding = await service.BindModelHistoryContextAsync(new(f.Session, null), source, static () => true, Token);
        using var review = await service.ReviewModelHistoryContextAsync(binding,
            [new(earlier, ConversationHistoryRole.User)], static () => true, Token);
        f.Adapter.OnRun = () =>
        {
            if (fault is "advisory") { f.HistoryMessages[source.Request.RequestId] = source with { Revision = 4 }; }
            if (fault is "selected") { f.HistoryMessages[earlier.Request.RequestId] = earlier with { Revision = 4 }; }
            if (fault is "private") { f.CanControl = false; }
            if (fault is "metadata") { f.Session = f.Session with { Generation = new(2) }; }
        };
        await service.Invoking(value => value.SubmitHistoryAsync(review, static () => true, Token))
            .Should().ThrowAsync<ConversationSubmissionUnconfirmedException>();
        f.Adapter.Calls.Should().Be(1);
        f.ExecutionCompletions.Should().Be(0);
        f.Retained!.AssistantResponse.Should().BeNull();
        host.IsQuiescent.Should().BeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancelledAdvisorySubmissionJoinsIgnoringAdapterAndRejectsLateSuccessOrFault(bool fault)
    {
        using var f = new Fixture();
        var earlier = Earlier(f);
        var source = HistoryAdvisorySource(f);
        var host = QualifiedHistoryHost(f);
        await using var service = f.ConversationService(host, execution: host);
        using var binding = await service.BindModelHistoryContextAsync(new(f.Session, null), source, static () => true, Token);
        using var review = await service.ReviewModelHistoryContextAsync(binding,
            [new(earlier, ConversationHistoryRole.User)], static () => true, Token);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var pending = new TaskCompletionSource<ModelProviderReply>(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Adapter.Pending = pending.Task;
        f.Adapter.OnRun = () => entered.SetResult();
        var running = service.SubmitHistoryAsync(review, static () => true, cancellation.Token);
        try
        {
            await entered.Task.WaitAsync(Token);
            await cancellation.CancelAsync();
            running.IsCompleted.Should().BeFalse("physical ignoring work must remain owned");
            host.IsQuiescent.Should().BeFalse();
            if (fault) { pending.SetException(new IOException("private ignored late failure")); }
            else { pending.SetResult(new(ModelTurnOutcome.Succeeded, ModelProviderSelection.OllamaCandidate, new("late private answer", null))); }
            await ((Func<Task>)(() => running)).Should().ThrowAsync<ConversationSubmissionUnconfirmedException>();
            host.IsQuiescent.Should().BeTrue();
            f.ExecutionCompletions.Should().Be(0);
            f.Retained!.AssistantResponse.Should().BeNull();
        }
        finally
        {
            pending.TrySetResult(new(ModelTurnOutcome.Unknown, ModelProviderSelection.OllamaCandidate));
            await host.WaitForQuiescenceAsync(Token);
        }
    }

    [Fact]
    public async Task AdvisoryCannotEnableUnqualifiedRuntimeOrRelabelNonOriginalAuthority()
    {
        using var f = new Fixture();
        var earlier = Earlier(f);
        var source = HistoryAdvisorySource(f);
        var host = f.ConfiguredProfiles(production: true);
        await using var service = f.ConversationService(host);
        using (var hostile = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.HostSystem),
            HostActivityLayer.Application, HostOperation.Request))
        {
            await service.Invoking(value => value.BindModelHistoryContextAsync(new(f.Session, null), source,
                static () => true, Token)).Should().ThrowAsync<InvalidOperationException>();
        }
        using var binding = await service.BindModelHistoryContextAsync(new(f.Session, null), source, static () => true, Token);
        using var review = await service.ReviewModelHistoryContextAsync(binding,
            [new(earlier, ConversationHistoryRole.User)], static () => true, Token);
        var receipt = await service.SubmitHistoryAsync(review, static () => true, Token);
        receipt.Message.Outcome.Should().Be(ModelTurnOutcome.Unavailable);
        receipt.Message.AssistantResponse.Should().BeNull();
        f.Adapter.Calls.Should().Be(0);
    }

    [Fact]
    public async Task AdvisoryBindingRechecksAdmissionAfterCompletedStorageAndDisposedSourcesCannotBeReviewed()
    {
        using var f = new Fixture();
        var earlier = Earlier(f);
        var source = HistoryAdvisorySource(f);
        var host = QualifiedHistoryHost(f);
        await using var service = f.ConversationService(host, execution: host);
        var live = true;
        using (var stopped = new ActivityListener
        {
            ShouldListenTo = activitySource => string.Equals(activitySource.Name, "Kora.Application", StringComparison.Ordinal),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activity =>
            {
                if (string.Equals(activity.OperationName, "storage.commit", StringComparison.Ordinal)) { live = false; }
            },
        })
        {
            ActivitySource.AddActivityListener(stopped);
            await service.Invoking(value => value.BindModelHistoryContextAsync(new(f.Session, null), source, () => live, Token))
                .Should().ThrowAsync<OperationCanceledException>();
        }
        using var binding = await service.BindModelHistoryContextAsync(new(f.Session, null), source, static () => true, Token);
        binding.Dispose();
        await service.Invoking(value => value.ReviewModelHistoryContextAsync(binding,
            [new(earlier, ConversationHistoryRole.User)], static () => true, Token)).Should().ThrowAsync<InvalidOperationException>();
        f.RetainCalls.Should().Be(0);
        f.Adapter.Calls.Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ObservedRevocationOrExpiryCannotReopenAnExistingAdvisoryConsent(bool expiry)
    {
        using var f = new Fixture();
        var source = HistoryAdvisorySource(f);
        var host = QualifiedHistoryHost(f);
        await using var service = f.ConversationService(host, execution: host);
        var live = true;
        using var binding = await service.BindModelHistoryContextAsync(new(f.Session, null), source, () => live, Token);
        if (expiry) { f.Time.Now = f.Time.Now.AddSeconds(60); }
        else { live = false; }
        binding.IsCurrent.Should().BeFalse();
        f.Time.Now = DateTimeOffset.UnixEpoch;
        live = true;
        binding.IsCurrent.Should().BeFalse();
        binding.OriginalQuestion.Should().BeEmpty();
        f.RetainCalls.Should().Be(0);
    }
}
