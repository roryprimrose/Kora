using AwesomeAssertions;

using Kora.Application;
using Kora.Application.Diagnostics;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Windows.IntegrationTests.Storage;
using Kora.Windows.Storage;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionsConversationTests
{
    [Theory]
    [InlineData("current")]
    [InlineData("switch")]
    [InlineData("aba")]
    [InlineData("revision")]
    [InlineData("privacy")]
    [InlineData("selection-revision")]
    [InlineData("rename")]
    [InlineData("arrive-other")]
    [InlineData("generation")]
    [InlineData("close")]
    public async Task DeferredActualRunningEventCannotReattachToRetiredNativeSelection(string retirement)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            var original = f.Request.SessionId;
            f.Request = InteractionStorageFixture.NewRequest();
            await f.AdmitAsync(newSession: true);
            await WindowsSqliteConversationTests.FinishInitial(f);
            var dispatcher = new DeferredConversationDispatcher();
            var adapter = new GateableConversationAdapter();
            await using var composed = new Composition(f, adapter: adapter);
            var model = CreateDeferredPresentation(f, composed, dispatcher);
            await model.RefreshAsync();
            await model.SelectAsync(model.Sessions.Single(row => row.Authority.SessionId == original));
            var target = model.SelectedSessionRecord!;
            if (retirement is "arrive-other")
            { await model.SelectAsync(model.Sessions.Single(row => row.Authority.SessionId != original)); }
            var submission = Task.Run(() => composed.Conversation.SubmitAsync(target,
                "original independently admitted request", static () => true, f.Token));
            SessionsViewModel? reopened = null;
            try
            {
                var provenance = await adapter.Started.Task.WaitAsync(f.Token);
                dispatcher.Count.Should().BeGreaterThan(0, "the actual running event is held before native dispatch");
                model.CanCancelRunningConversation.Should().BeFalse();
                submission.IsCompleted.Should().BeFalse();
                if (retirement is "switch" or "aba")
                {
                    await model.SelectAsync(model.Sessions.Single(row => row.Authority.SessionId != original));
                    model.ComposerBody = "unrelated unsent input";
                    if (retirement is "aba")
                    {
                        await model.SelectAsync(model.Sessions.Single(row => row.Authority.SessionId == original));
                        model.SelectedSessionRecord.Should().Be(target, "the ABA restores the same exact source");
                    }
                }
                else if (retirement is "revision") { composed.Access.ControlRevision++; }
                else if (retirement is "privacy")
                {
                    composed.Access.CanInspect = false;
                    composed.Access.CanControl = false;
                    composed.Access.ControlRevision++;
                }
                else if (retirement is "selection-revision") { model.SelectTask(null); }
                else if (retirement is "rename")
                {
                    model.NameDraft = "renamed while native callback waited";
                    await model.RenameAsync();
                    model.SelectedSessionRecord!.Metadata.Should().NotBe(target.Metadata);
                }
                else if (retirement is "arrive-other") { await model.SelectAsync(target); }
                else if (retirement is "generation")
                {
                    adapter.Release.TrySetResult(new(ModelTurnOutcome.Succeeded, provenance.Selection,
                        new("original permitted final answer", null)));
                    await submission;
                    await model.ChangeLifecycleAsync(active: false);
                    await model.ChangeLifecycleAsync(active: true);
                    model.SelectedSessionRecord!.Authority.Generation.Should().NotBe(target.Authority.Generation);
                }
                else if (retirement is "close")
                {
                    model.Close();
                    reopened = CreateDeferredPresentation(f, composed, new DeferredConversationDispatcher());
                    await reopened.RefreshAsync();
                    await reopened.SelectAsync(reopened.Sessions.Single(row => row.Authority.SessionId == original));
                }

                dispatcher.Drain();
                if (retirement is "current")
                {
                    model.RunningConversationStatus.Should().Contain("Running")
                        .And.Contain(provenance.Request.TaskId.Value.ToString("D"));
                    model.CanCancelRunningConversation.Should().BeTrue();
                }
                else
                {
                    model.RunningConversationStatus.Should().NotContain(provenance.Request.TaskId.Value.ToString("D"));
                    model.CanCancelRunningConversation.Should().BeFalse();
                }
                if (reopened is not null)
                {
                    reopened.SelectedSessionRecord.Should().Be(target);
                    reopened.CanCancelRunningConversation.Should().BeFalse();
                    reopened.ConversationMessages.Should().BeEmpty();
                    reopened.ProvisionalAnswer.Should().BeEmpty();
                }
                if (retirement is "switch") { model.ComposerBody.Should().Be("unrelated unsent input"); }
                model.ConversationMessages.Should().BeEmpty("an execution notification is never retained conversation content");
                model.ProvisionalAnswer.Should().BeEmpty();
                f.Count("conversation_messages").Should().Be(1, "only the explicitly admitted original request exists");
                f.Count("conversation_drafts").Should().Be(0);
                composed.Audit.Events.Should().NotContain(value =>
                    string.Equals(value.ActionId, "conversation.cancel-request", StringComparison.Ordinal));
                adapter.Release.TrySetResult(new(ModelTurnOutcome.Unknown, provenance.Selection));
                if (retirement is "revision" or "rename" or "privacy")
                {
                    Func<Task> complete = async () => await submission;
                    await complete.Should().ThrowAsync<ConversationSubmissionUnconfirmedException>();
                }
                else { await submission; }
                if (retirement is "close")
                { dispatcher.Count.Should().Be(0, "the original presenter unsubscribed before subsequent physical completion"); }
                dispatcher.Drain();
                if (retirement is "close")
                {
                    model.RunningConversationStatus.Should().Contain("cleared");
                    model.CanCancelRunningConversation.Should().BeFalse();
                }
            }
            finally
            {
                adapter.Release.TrySetResult(new(ModelTurnOutcome.Unknown, ModelProviderSelection.OllamaCandidate));
                try
                {
                    try { await submission; }
                    catch (ConversationSubmissionUnconfirmedException) when (retirement is "revision" or "rename" or "privacy") { }
                }
                finally
                {
                    model.Close();
                    reopened?.Close();
                    dispatcher.Drain();
                }
            }
        });
    }

    [Fact]
    public async Task DeferredActualEventsAfterNativeStopAndDeleteCannotRestoreContentOrDisturbAnotherPresenter()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            var original = f.Request.SessionId;
            f.Request = InteractionStorageFixture.NewRequest();
            await f.AdmitAsync(newSession: true);
            await WindowsSqliteConversationTests.FinishInitial(f);
            var adapter = new GateableConversationAdapter();
            await using var composed = new Composition(f, adapter: adapter);
            var dispatcher = new DeferredConversationDispatcher();
            var otherDispatcher = new DeferredConversationDispatcher();
            var model = CreateDeferredPresentation(f, composed, dispatcher);
            var other = CreateDeferredPresentation(f, composed, otherDispatcher);
            var window = new SessionsWindow(model);
            window.Show();
            Task? submission = null;
            Task? deletion = null;
            try
            {
                await model.RefreshAsync();
                await model.SelectAsync(model.Sessions.Single(row => row.Authority.SessionId == original));
                await other.RefreshAsync();
                await other.SelectAsync(other.Sessions.Single(row => row.Authority.SessionId != original));
                var otherTarget = other.SelectedSessionRecord!;
                other.ComposerBody = "independent unsent input";
                var target = model.SelectedSessionRecord!;
                submission = Task.Run(() => composed.Conversation.SubmitAsync(target,
                    "private original request", static () => true, f.Token));
                var provenance = await adapter.Started.Task.WaitAsync(f.Token);
                dispatcher.Count.Should().BeGreaterThan(0);
                await model.ReviewStopAndDeleteAsync();
                model.CanConfirmStopAndDelete.Should().BeTrue();
                var cancellationAccepted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                void ObserveCancellation(ConversationExecutionStatus observed)
                {
                    if (observed.Target.Request == provenance.Request
                        && observed.Target.Generation == target.Authority.Generation
                        && observed.Phase == ConversationExecutionPhase.Cancelling)
                    { cancellationAccepted.TrySetResult(); }
                }
                composed.Conversation.RunningConversationChanged += ObserveCancellation;
                try
                {
                    deletion = model.ConfirmStopAndDeleteAsync();
                    await cancellationAccepted.Task.WaitAsync(f.Token);
                }
                finally { composed.Conversation.RunningConversationChanged -= ObserveCancellation; }
                deletion.IsCompleted.Should().BeFalse("deletion must retain the actual provider join");
                submission.IsCompleted.Should().BeFalse();
                (await f.Store.ReadMetadataAsync(original, f.Token)).Should().Be(target);
                adapter.Release.TrySetResult(new(ModelTurnOutcome.Succeeded, provenance.Selection,
                    new("forbidden late private answer", null)));
                await deletion;
                try { await submission; }
                catch (ConversationSubmissionUnconfirmedException) { }
                model.StopAndDeleteStatus.Should().Contain("logically deleted");
                (await f.Store.ReadHistoryAsync(original, null, 50, f.Token)).Disposed.Should().BeTrue();
                dispatcher.Drain();
                otherDispatcher.Drain();
                model.SelectedSessionRecord.Should().BeNull();
                model.ConversationMessages.Should().BeEmpty();
                model.ProvisionalAnswer.Should().BeEmpty();
                model.CanCancelRunningConversation.Should().BeFalse();
                model.RunningConversationStatus.Should().NotContain(provenance.Request.TaskId.Value.ToString("D"));
                other.SelectedSessionRecord.Should().Be(otherTarget);
                other.ComposerBody.Should().Be("independent unsent input");
                other.ConversationMessages.Should().BeEmpty();
                other.ProvisionalAnswer.Should().BeEmpty();
                other.CanCancelRunningConversation.Should().BeFalse();
                (await f.Store.ReadMetadataAsync(otherTarget.Authority.SessionId, f.Token)).Should().Be(otherTarget);
                f.Count("conversation_messages").Should().Be(0, "deleted input and late answer cannot be restored or resubmitted");
                f.Count("conversation_drafts").Should().Be(0);
                f.Count("host_questions").Should().Be(0);
                f.Count("scoped_grants").Should().Be(0);
            }
            finally
            {
                adapter.Release.TrySetResult(new(ModelTurnOutcome.Unknown, ModelProviderSelection.OllamaCandidate));
                try
                {
                    if (submission is not null)
                    {
                        try { await submission; }
                        catch (ConversationSubmissionUnconfirmedException) { }
                    }
                    if (deletion is not null) { await deletion; }
                }
                finally
                {
                    window.ClosePrivate();
                    other.Close();
                    dispatcher.Drain();
                    otherDispatcher.Drain();
                }
            }
        });
    }

    private static SessionsViewModel CreateDeferredPresentation(InteractionStorageFixture f,
        Composition composed, IUiDispatcher dispatcher)
    {
        var sink = new WindowsSqliteEvidenceSink(f.Paths);
        sink.Initialize();
        return new(composed.Workspace,
            new DurableEvidenceQuery(new WindowsSqliteEvidenceReader(sink), composed.Access, f.Time,
                NullLogger<DurableEvidenceQuery>.Instance), composed.Access, NullLogger<SessionsViewModel>.Instance,
            conversation: composed.Conversation, conversationPresentationDispatcher: dispatcher);
    }

    private sealed class DeferredConversationDispatcher : IUiDispatcher
    {
        private readonly Lock gate = new();
        private readonly Queue<Action> callbacks = new();
        internal int Count { get { lock (gate) { return callbacks.Count; } } }
        public Task InvokeAsync(Func<Task> action) => throw new NotSupportedException("This seam owns posted execution notifications only.");
        public void Post(Action action) { lock (gate) { callbacks.Enqueue(action); } }
        internal void Drain()
        {
            while (true)
            {
                Action callback;
                lock (gate)
                {
                    if (!callbacks.TryDequeue(out var next)) { return; }
                    callback = next;
                }
                callback();
            }
        }
    }
}
