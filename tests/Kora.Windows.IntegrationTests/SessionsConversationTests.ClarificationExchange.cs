using System.Collections.Immutable;

using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

using AwesomeAssertions;

using Kora.Core.Authorization;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.Core.Storage;
using Kora.Windows.IntegrationTests.Storage;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionsConversationTests
{
    [Theory]
    [InlineData("primary")]
    [InlineData("sessions")]
    [InlineData("compact")]
    public async Task ClarificationNativeControlsReadOnlyExactOriginalTurnWithoutReplayWritesOrComposerChanges(string surface)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            var first = await RetainClarifiedTurn(f, "first accepted answer");
            await RetainClarifiedTurn(f, "other accepted answer");
            await using var composed = new Composition(f);
            var model = composed.Model;
            Window window = surface switch
            {
                "sessions" => new SessionsWindow(model),
                "compact" => new CompactSessionWindow(model, new(model)),
                _ => new Window { Content = new SessionInteractionView { DataContext = model }, Width = 1000, Height = 800 },
            };
            window.Show();
            try
            {
                await model.RefreshAsync();
                await model.SelectAsync(model.Sessions.Single());
                await model.ReadConversationAsync();
                model.ComposerBody = "unrelated unsent input";
                window.UpdateLayout();
                var audits = WindowsSqliteConversationTests.AuditText(f);
                var history = f.Count("session_history");
                var tasks = f.Count("host_task_events");
                var questions = f.Count("host_questions");
                var retention = await f.Store.ReadRetentionAsync(first.SessionId, f.Token);
                var control = window.GetVisualDescendants().OfType<ClarificationExchangeView>().Single(item =>
                    item.DataContext is ConversationMessage message && message.Request == first);
                control.GetVisualDescendants().OfType<Button>().Single(button => button.Name is "ShowClarificationExchange")
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await control.Operation;
                window.UpdateLayout();
                var owner = model.CurrentClarificationExchange!;
                owner.Should().NotBeNull();
                string.Join("\n", owner.Records).Should().Contain("first accepted answer").And.Contain("same question")
                    .And.Contain("ActivatedVoice").And.NotContain("other accepted answer");
                control.GetVisualDescendants().OfType<Button>().Where(button => button.IsVisible)
                    .Should().OnlyContain(button => button.Name == "ShowClarificationExchange" || button.Name == "HideClarificationExchange");
                model.ComposerBody.Should().Be("unrelated unsent input");
                f.Count("host_task_events").Should().Be(tasks);
                f.Count("host_questions").Should().Be(questions);
                f.Count("session_history").Should().Be(history);
                f.Count("scoped_grants").Should().Be(0);
                WindowsSqliteConversationTests.AuditText(f).Should().Be(audits);
                (await f.Store.ReadRetentionAsync(first.SessionId, f.Token)).Should().Be(retention);
                control.GetVisualDescendants().OfType<Button>().Single(button => button.Name is "HideClarificationExchange")
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                model.CurrentClarificationExchange.Should().BeNull();
                owner.Records.Should().BeEmpty();
            }
            finally { model.Close(); window.Close(); }
        });
    }

    [Theory]
    [InlineData("privacy")]
    [InlineData("control")]
    [InlineData("close")]
    [InlineData("generation")]
    [InlineData("cross-session")]
    [InlineData("revision")]
    [InlineData("selection")]
    [InlineData("hide")]
    public async Task ClarificationLateHistoryReadCannotPublishAfterAdmissionOrSourceChanges(string change)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteConversationTests.FinishInitial(f);
        await WindowsSqliteConversationTests.Submit(f, f.Request.SessionId, "exact original");
        var reader = new ClarificationReader(f);
        await using var composed = new Composition(f, conversationReader: f.Store, workspaceReader: reader);
        var model = composed.Model;
        await model.RefreshAsync();
        await model.SelectAsync(model.Sessions.Single());
        await model.ReadConversationAsync();
        var message = model.ConversationMessages.Single();
        var row = ClarificationExchangeTests.Event(message, 1);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        reader.Read = async (_, _, _, _) =>
        {
            entered.SetResult();
            await release.Task;
            return new(message.Request.SessionId, new(1), false, 1, [row], null);
        };
        reader.Exact = (_, _, _) => ValueTask.FromResult<SessionHistoryEvent?>(
            change is "revision" ? row with { SourceRevision = 3 } : row);
        var operation = model.ReadClarificationExchangeAsync(message);
        await entered.Task.WaitAsync(f.Token);
        try
        {
            if (change is "privacy") { composed.Access.CanInspect = false; }
            if (change is "control") { composed.Access.ControlRevision++; }
            if (change is "close") { model.Close(); }
            if (change is "selection") { model.RevokeSessionPresentation(message.Request.SessionId); }
            if (change is "hide") { model.ClearClarificationExchange(); }
            if (change is "generation")
            {
                await composed.Workspace.ChangeLifecycleAsync(message.Request.SessionId, new(1), false, RequestOrigin.LocalUi, f.Token);
            }
            if (change is "cross-session") { row = row with { SessionId = new(Guid.NewGuid()) }; }
        }
        finally { release.SetResult(); }
        await operation;
        (model.CurrentClarificationExchange?.Records ?? []).Should().BeEmpty();
        model.CurrentClarificationExchange?.Page.Should().BeNull();
        model.CurrentClarificationExchange?.Status.Should().Contain("unavailable");
    }

    [Fact]
    public async Task ClarificationContinuationUsesOriginalSnapshotAfterEmptyMatchPageAndSourceChangesRequireFreshRead()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            await WindowsSqliteConversationTests.Submit(f, f.Request.SessionId, "exact original");
            var reader = new ClarificationReader(f);
            await using var composed = new Composition(f, conversationReader: f.Store, workspaceReader: reader);
            var model = composed.Model;
            await model.RefreshAsync();
            await model.SelectAsync(model.Sessions.Single());
            await model.ReadConversationAsync();
            var message = model.ConversationMessages.Single();
            var cursor = new SessionHistoryCursor(message.Request.SessionId, new(1), 51, 50);
            var reads = 0;
            reader.Read = (session, after, limit, _) =>
            {
                limit.Should().Be(50);
                reads++;
                if (reads == 1)
                {
                    after.Should().BeNull();
                    return ValueTask.FromResult(new SessionHistoryPage(session, new(1), false, 51,
                        Enumerable.Range(1, 50).Select(index => ClarificationExchangeTests.Event(message, index)
                            with { TaskId = Guid.NewGuid() }).ToImmutableArray(), cursor));
                }
                after.Should().Be(cursor);
                return ValueTask.FromResult(new SessionHistoryPage(session, new(1), false, 51,
                    [ClarificationExchangeTests.Event(message, 51)], null));
            };
            reader.Exact = (_, id, _) => ValueTask.FromResult<SessionHistoryEvent?>(
                reader.LastPage!.Records.Single(row => row.Id == id));
            var control = new ClarificationExchangeView { DataContext = message, Workspace = model };
            var window = new Window { Content = control };
            window.Show();
            try
            {
                var show = control.GetVisualDescendants().OfType<Button>().Single(button => button.Name is "ShowClarificationExchange");
                var more = control.GetVisualDescendants().OfType<Button>().Single(button => button.Name is "MoreClarificationExchange");
                show.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await control.Operation;
                model.CurrentClarificationExchange!.Records.Should().BeEmpty();
                model.CurrentClarificationExchange.Page!.Next.Should().Be(cursor);
                more.IsVisible.Should().BeTrue();
                more.IsEnabled.Should().BeTrue();
                more.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await control.Operation;
                model.CurrentClarificationExchange!.Page!.Snapshot.Should().Be(51);
                model.CurrentClarificationExchange.Records.Should().ContainSingle();
                more.IsVisible.Should().BeFalse();
                composed.Access.ControlRevision++;
                model.CurrentClarificationExchange.Should().BeNull();
                await model.ReadClarificationExchangeAsync(message, next: true);
                reads.Should().Be(2, "stale continuation cannot silently start a new source snapshot");
            }
            finally { window.Close(); }
        });
    }

    private static async Task<HostRequest> RetainClarifiedTurn(InteractionStorageFixture f, string answer)
    {
        f.Request = WindowsSqliteConversationTests.Fresh(f.Request.SessionId);
        await f.RunAsync(() => f.Store.RetainConversationMessageAsync(f.Request, new(1), 0,
            "identical original body", static () => true, f.Token));
        await f.RunAsync(() => f.Store.PublishTrustedSnapshotAsync(f.Request, f.Policy, null, 0, f.Token));
        var question = (await f.RunAsync(() => f.Questions.CreateAsync(f.Request,
            new("same question", QuestionKind.Text, [], maximumTextLength: 64), f.Time.Now.AddMinutes(5), f.Token))).Question!;
        await f.RunAsync(() => f.Questions.SubmitAsync(question.Key, new([], answer), RequestOrigin.ActivatedVoice, f.Token));
        await f.RunAsync(() => f.Store.CompleteConversationSubmissionAsync(f.Request, ModelTurnOutcome.Unavailable,
            ModelTurnReason.UnregisteredModel, static () => true, f.Token));
        return f.Request;
    }

    private sealed class ClarificationReader(InteractionStorageFixture f) : ISessionWorkspaceStore, ISessionHistoryStore,
        ISessionWorkStore, ISessionRetentionControlStore
    {
        internal Func<HostId<SessionIdentity>, SessionHistoryCursor?, int, CancellationToken, ValueTask<SessionHistoryPage>>? Read { get; set; }
        internal Func<HostId<SessionIdentity>, Guid, CancellationToken, ValueTask<SessionHistoryEvent?>>? Exact { get; set; }
        internal SessionHistoryPage? LastPage { get; private set; }
        public async ValueTask<SessionHistoryPage> ReadHistoryAsync(HostId<SessionIdentity> session, SessionHistoryCursor? cursor, int limit, CancellationToken token) =>
            LastPage = await (Read?.Invoke(session, cursor, limit, token) ?? f.Store.ReadHistoryAsync(session, cursor, limit, token));
        public ValueTask<SessionHistoryEvent?> ReadHistoryEventAsync(HostId<SessionIdentity> session, Guid id, CancellationToken token) =>
            Exact?.Invoke(session, id, token) ?? f.Store.ReadHistoryEventAsync(session, id, token);
        public ValueTask<SessionPage<SessionWorkspaceEntry>> ReadMetadataPageAsync(Guid? after, int limit, CancellationToken token) => f.Store.ReadMetadataPageAsync(after, limit, token);
        public ValueTask<SessionWorkspaceEntry> ReadMetadataAsync(HostId<SessionIdentity> session, CancellationToken token) => f.Store.ReadMetadataAsync(session, token);
        public ValueTask<SessionWorkSnapshot> ReadWorkAsync(HostId<SessionIdentity> session, long revision, SessionQueueLimits limits, CancellationToken token) => f.Store.ReadWorkAsync(session, revision, limits, token);
        public ValueTask<long> ValidatePassiveInspectionAsync(CancellationToken token) => f.Store.ValidatePassiveInspectionAsync(token);
        public ValueTask<SessionRetentionObservation> ReadRetentionObservationAsync(HostId<SessionIdentity> session, CancellationToken token) => f.Store.ReadRetentionObservationAsync(session, token);
        public ValueTask<SessionRetentionObservation> SetRetentionHoldAsync(HostRequest request, SessionRetentionObservation expected, bool perpetual, Func<bool> admitted, CancellationToken token) => throw new InvalidOperationException("No retention changes.");
        public ValueTask<SessionPage<HostQuestionRecord>> ReadQuestionPageAsync(HostId<SessionIdentity> session, Guid? after, int limit, CancellationToken token) => f.Store.ReadQuestionPageAsync(session, after, limit, token);
        public ValueTask<SessionPage<HostTaskRecord>> ReadTaskPageAsync(HostId<SessionIdentity> session, Guid? after, int limit, CancellationToken token) => f.Store.ReadTaskPageAsync(session, after, limit, token);
        public ValueTask<WorkSessionAuthorization> ChangeIdleLifecycleAsync(HostRequest request, HostRevision generation, bool active, Func<bool> admitted, CancellationToken token) => f.Store.ChangeIdleLifecycleAsync(request, generation, active, admitted, token);
        public ValueTask<HostTaskRecord> RecordControlIntentAsync(HostRequest request, CancellationToken token) => f.Store.RecordControlIntentAsync(request, token);
        public ValueTask<SessionDispositionPreview> PreviewDispositionAsync(HostId<SessionIdentity> session, HostRevision generation, long revision, CancellationToken token) => throw new InvalidOperationException("No disposition.");
        public ValueTask<SessionDispositionReceipt> DisposeSessionAsync(HostRequest request, SessionDispositionPreview preview, Func<bool> admitted, CancellationToken token) => throw new InvalidOperationException("No disposal.");
        public ValueTask<SessionPage<WorkSessionAuthorization>> ReadSessionsAsync(Guid? after, int limit, CancellationToken token) => throw new InvalidOperationException("No alternate session read.");
        public ValueTask<HostTaskObservation?> ReadTaskAsync(HostId<SessionIdentity> session, HostId<TaskIdentity> task, CancellationToken token) => throw new InvalidOperationException("No task inspection.");
        public ValueTask<HostTaskObservation> CancelWaitingTaskAsync(HostRequest request, HostTaskCancellationTarget target, Func<bool> admitted, CancellationToken token) => throw new InvalidOperationException("No task control.");
        public ValueTask<SessionWorkspaceEntry> CreateNamedSessionAsync(HostRequest request, SessionName name, Func<bool> admitted, CancellationToken token) => throw new InvalidOperationException("No creation.");
        public ValueTask<SessionWorkspaceEntry> RenameSessionAsync(HostRequest request, HostRevision generation, long revision, SessionName name, Func<bool> admitted, CancellationToken token) => throw new InvalidOperationException("No rename.");
    }
}
