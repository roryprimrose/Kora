using AwesomeAssertions;

using Kora.Application.Hosting;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Application.UnitTests.Hosting;

public sealed partial class SessionWorkspaceServiceTests
{
    private static ConversationMessage RetainedAnswer(ConversationFixture fixture)
    {
        var original = Message(fixture, 1, "Original USER must never be spoken");
        var provenance = new ModelTurnProvenance(new(Guid.NewGuid()), original.Request, ModelProviderSelection.OllamaCandidate);
        return original with
        {
            Version = 2, Revision = 3, Execution = provenance, ExecutionMetadataRevision = 0,
            Outcome = ModelTurnOutcome.Succeeded, Reason = ModelTurnReason.ProviderOutcome,
            AssistantResponse = new(provenance, "Exact ASSISTANT answer.")
        };
    }

    [Theory]
    [InlineData(RequestOrigin.LocalUi, true)]
    [InlineData(RequestOrigin.ActivatedVoice, false)]
    public async Task AnswerReadRevalidatesExactRetainedAssistantWithoutChangingSourceOrLifecycle(RequestOrigin origin, bool active)
    {
        using var f = new ConversationFixture();
        f.Session = f.Session with { IsActive = active };
        var source = RetainedAnswer(f);
        if (!active) { f.Session = f.Session with { Generation = new(2) }; }
        f.Messages = [source];
        var playback = new AnswerPlayback();
        var workflow = new SessionAnswerReadWorkflow(f.Service, playback);
        var request = new HostRequest(f.Request.RequestId, f.Session.SessionId, f.Request.TaskId, origin);
        using var root = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request);
        var result = await workflow.ReadOnceAsync(new(f.Session, null), source, () => true, f.Token);
        result.Should().Be("fake playback completed");
        playback.Source.Should().Be(source);
        playback.Request.Should().Be(request);
        f.ExactReads.Should().Be(4);
        f.Messages.Should().Equal(source);
        f.TaskWrites.Should().BeEmpty();
        f.ControlCalls.Should().Be(0);
    }

    [Theory]
    [InlineData("user-only")]
    [InlineData("foreign")]
    [InlineData("provenance")]
    [InlineData("credential")]
    [InlineData("stale")]
    [InlineData("deleted")]
    [InlineData("late-source")]
    [InlineData("private")]
    [InlineData("channel")]
    [InlineData("cancel")]
    [InlineData("model")]
    [InlineData("invocation")]
    [InlineData("channel-mismatch")]
    [InlineData("activity-expired")]
    [InlineData("control")]
    public async Task AnswerReadNeverSubstitutesAnUnadmittedForeignStaleOrDeletedSource(string fault)
    {
        using var f = new ConversationFixture();
        var source = RetainedAnswer(f);
        var selected = source;
        if (fault is "user-only") { selected = Message(f, 1, "USER only"); }
        if (fault is "foreign") { selected = source with { Request = HostRequest.Create(RequestOrigin.LocalUi) }; }
        if (fault is "provenance")
        { selected = source with { AssistantResponse = source.AssistantResponse! with { Provenance = source.Execution! with { Turn = new(Guid.NewGuid()) } } }; }
        if (fault is "credential")
        { selected = source with { AssistantResponse = source.AssistantResponse! with { Body = "api_key=synthetic-only-value" } }; }
        f.Messages = fault is "deleted" ? [] : [fault is "stale" ? source with { Revision = 4 } : source];
        var admitted = fault is not "channel";
        using var cancellation = new CancellationTokenSource();
        HostActivity? originalActivity = null;
        if (fault is "cancel") { cancellation.Cancel(); }
        var playback = new AnswerPlayback
        {
            BeforeValidation = () =>
            {
                if (fault is "late-source") { f.Messages = [source with { Revision = 4 }]; }
                if (fault is "private") { f.CanInspect = false; }
                if (fault is "control") { f.CanControl = false; }
                if (fault is "activity-expired") { originalActivity!.Dispose(); }
            },
            ValidationRequest = fault is "channel-mismatch"
                ? new(f.Request.RequestId, f.Session.SessionId, f.Request.TaskId, RequestOrigin.ActivatedVoice) : null
        };
        var workflow = new SessionAnswerReadWorkflow(f.Service, playback);
        var request = new HostRequest(f.Request.RequestId, f.Session.SessionId, f.Request.TaskId,
            fault is "model" ? RequestOrigin.HostSystem : RequestOrigin.LocalUi,
            fault is "invocation" ? new(Guid.NewGuid()) : null);
        using var root = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request);
        originalActivity = root;
        await workflow.Invoking(value => value.ReadOnceAsync(new(f.Session, null), selected, () => admitted, cancellation.Token))
            .Should().ThrowAsync<Exception>();
        playback.Completed.Should().BeFalse();
        f.TaskWrites.Should().BeEmpty();
    }

    [Fact]
    public async Task AnswerReadWithoutHostActivityCannotAcquireSourceOrPlaybackAuthority()
    {
        using var f = new ConversationFixture();
        var source = RetainedAnswer(f);
        f.Messages = [source];
        var playback = new AnswerPlayback();
        var workflow = new SessionAnswerReadWorkflow(f.Service, playback);
        await workflow.Invoking(value => value.ReadOnceAsync(new(f.Session, null), source, () => true, f.Token))
            .Should().ThrowAsync<InvalidOperationException>();
        f.ExactReads.Should().Be(0);
        playback.Source.Should().BeNull();
    }

    private sealed class AnswerPlayback : ISessionAnswerPlayback
    {
        internal ConversationMessage? Source { get; private set; }
        internal HostRequest? Request { get; private set; }
        internal Action? BeforeValidation { get; init; }
        internal bool Completed { get; private set; }
        internal HostRequest? ValidationRequest { get; init; }
        public async Task<string> ReadAnswerOnceAsync(ConversationMessage source, Func<bool> admitted,
            Func<CancellationToken, Task> validateSource, CancellationToken token)
        {
            Source = source;
            Request = HostActivity.RequireCurrent().Request;
            BeforeValidation?.Invoke();
            using var changed = ValidationRequest is { } request
                ? HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request) : null;
            await validateSource(token);
            admitted().Should().BeTrue();
            Completed = true;
            return "fake playback completed";
        }
    }
}
