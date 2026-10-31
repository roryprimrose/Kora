using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Interactivity;

using AwesomeAssertions;

using Kora.Application.Hosting;
using Kora.Core.Conversation;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.Core.Storage;
using Kora.Windows.IntegrationTests.Storage;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionsConversationTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task NativeRemainingWorkAnswersRunningHeldFifoAndQuestionsWhileProviderContinuesWithoutWrites(bool compact, bool manualHold)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            TestContext.Current.TestOutputHelper!.WriteLine("Initialized durable source.");
            var adapter = new PendingTurnAdapter();
            await using var composed = new Composition(f, adapter: adapter);
            var target = await f.Store.ReadMetadataAsync(f.Request.SessionId, f.Token);
            var first = await composed.Conversation.SubmitQueuedAsync(target, "private active body", () => true, f.Token);
            await adapter.NextAsync();
            TestContext.Current.TestOutputHelper!.WriteLine("First provider held.");
            try
            {
                await composed.Conversation.SubmitQueuedAsync(target, "private pending one", () => true, f.Token);
                await composed.Conversation.SubmitQueuedAsync(target, "private pending two", () => true, f.Token);
                if (manualHold)
                {
                    var pending = await composed.Conversation.ReadPendingConversationAsync(target, () => true, f.Token);
                    await composed.Conversation.SetManualDispatchHoldAsync(pending.DispatchControl
                        ?? throw new InvalidOperationException("Exact native dispatch control is unavailable."),
                        ConversationDispatchHoldReason.OriginalUser, () => true, f.Token);
                }
                else
                {
                    await composed.Conversation.SubmitQueuedAsync(target, "private withdraw", () => true, f.Token);
                    var pending = await composed.Conversation.ReadPendingConversationAsync(target, () => true, f.Token);
                    await composed.Conversation.CancelPendingConversationAsync(target, pending.Turns[^1], pending.Revision, () => true, f.Token);
                }
                TestContext.Current.TestOutputHelper!.WriteLine("Pending lane held by the exact independent control.");
                f.Request = InteractionStorageFixture.NewRequest(target.Authority.SessionId);
                await f.AdmitAsync(newSession: false);
                await f.RunAsync(() => f.Questions.CreateAsync(f.Request, new("private pending question", QuestionKind.Text, [], maximumTextLength: 512),
                    f.Time.Now.AddMinutes(2), f.Token));
                TestContext.Current.TestOutputHelper!.WriteLine("Pending question created.");
                var model = composed.Model;
                var owner = new SessionsPresentationOwner(model);
                Window window = compact ? new CompactSessionWindow(model, owner) : new SessionsWindow(model, owner);
                window.Show();
                try
                {
                    await model.RefreshAsync();
                    await model.SelectAsync(model.Sessions.Single());
                    TestContext.Current.TestOutputHelper!.WriteLine("Exact native target selected.");
                    model.ComposerBody = "private unsent next request";
                    var audits = WindowsSqliteConversationTests.AuditText(f);
                    var retention = await f.Store.ReadRetentionAsync(target.Authority.SessionId, f.Token);
                    var tasks = f.Count("host_task_events");
                    var messages = f.Count("conversation_messages");
                    var originalPending = await composed.Conversation.ReadPendingConversationAsync(target, () => true, f.Token);
                    window.FindControl<Button>("AnswerRemainingWork")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    await (compact ? ((CompactSessionWindow)window).ConversationOperation : ((SessionsWindow)window).ConversationOperation);
                    TestContext.Current.TestOutputHelper!.WriteLine("Native remaining-work query completed.");
                    model.RemainingWorkStatus.Should().Contain("Current ordinary work: running")
                        .And.Contain("Actual host step: generating an answer")
                        .And.Contain("Ordinary dispatch: held")
                        .And.Contain("Submitted FIFO position 1").And.Contain("Submitted FIFO position 2")
                        .And.Contain("Pending questions: 1").And.Contain("Bounds and omissions")
                        .And.NotContain("private").And.NotContain("position 0");
                    model.RemainingWorkStatus.Should()
                        .Contain("Manual ordinary dispatch hold: " + (manualHold ? "held." : "not observed held."))
                        .And.Contain("Failure/cancellation recovery hold: " + (manualHold ? "not observed held." : "held."));
                    (await composed.Conversation.ReadPendingConversationAsync(target, () => true, f.Token))
                        .Should().BeEquivalentTo(originalPending);
                    adapter.Calls.Should().Be(1);
                    first.Completion.IsCompleted.Should().BeFalse();
                    model.ComposerBody.Should().Be("private unsent next request");
                    f.Count("host_task_events").Should().Be(tasks);
                    f.Count("conversation_messages").Should().Be(messages);
                    f.Count("scoped_grants").Should().Be(0);
                    (await f.Store.ReadRetentionAsync(target.Authority.SessionId, f.Token)).Should().Be(retention);
                    WindowsSqliteConversationTests.AuditText(f).Should().Be(audits);
                }
                finally
                {
                    adapter.ReleaseAll();
                    TestContext.Current.TestOutputHelper!.WriteLine("Releasing held provider.");
                    await first.Completion;
                    TestContext.Current.TestOutputHelper!.WriteLine("First request completed.");
                    owner.Revoke();
                }
            }
            finally
            {
                var disposal = composed.Conversation.DisposeAsync();
                adapter.ReleaseAll();
                await disposal;
            }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativeRemainingWorkRequiresExplicitSelectionWithoutReadsOrEffects(bool compact)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            var adapter = new PendingTurnAdapter();
            var reader = new SearchReader(f.Store.ReadConversationPageAsync, readMetadata: f.Store.ReadMetadataAsync);
            var workReads = 0;
            reader.WorkRead = async (id, revision, limits, token) =>
            {
                workReads++;
                return await f.Store.ReadWorkAsync(id, revision, limits, token);
            };
            await using var composed = new Composition(f, adapter: adapter, conversationReader: reader);
            var model = composed.Model;
            var owner = new SessionsPresentationOwner(model);
            Window window = compact ? new CompactSessionWindow(model, owner) : new SessionsWindow(model, owner);
            window.Show();
            try
            {
                await model.RefreshAsync();
                var button = window.FindControl<Button>("AnswerRemainingWork")!;
                button.IsEnabled.Should().BeFalse();
                model.CanAnswerRemainingWork.Should().BeFalse();
                ControlAutomationPeer.CreatePeerForElement(button)!.GetName().Should().Contain("selected conversation");
                var audits = WindowsSqliteConversationTests.AuditText(f);
                var retention = await f.Store.ReadRetentionAsync(f.Request.SessionId, f.Token);
                var tasks = f.Count("host_task_events");
                var messages = f.Count("conversation_messages");
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await (compact ? ((CompactSessionWindow)window).ConversationOperation : ((SessionsWindow)window).ConversationOperation);
                model.RemainingWorkStatus.Should().BeEmpty();
                workReads.Should().Be(0);
                adapter.Calls.Should().Be(0);
                f.Count("host_task_events").Should().Be(tasks);
                f.Count("conversation_messages").Should().Be(messages);
                f.Count("scoped_grants").Should().Be(0);
                (await f.Store.ReadRetentionAsync(f.Request.SessionId, f.Token)).Should().Be(retention);
                WindowsSqliteConversationTests.AuditText(f).Should().Be(audits);
            }
            finally { owner.Revoke(); }
        });
    }

    [Fact]
    public async Task RemainingWorkRejectsManualHoldChangesBeforePublicationWithoutReleasingTheHold()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteConversationTests.FinishInitial(f);
        await using var composed = new Composition(f);
        var target = await f.Store.ReadMetadataAsync(f.Request.SessionId, f.Token);
        var pending = await composed.Conversation.ReadPendingConversationAsync(target, () => true, f.Token);
        SessionRemainingWorkAnswer answer;
        using (var root = HostActivity.BeginRoot(f.Request, HostActivityLayer.Application, HostOperation.Request))
        {
            answer = await composed.Conversation.ReadRemainingWorkAsync(target, () => true, f.Token);
        }
        await composed.Conversation.SetManualDispatchHoldAsync(pending.DispatchControl
            ?? throw new InvalidOperationException("Exact native dispatch control is unavailable."),
            ConversationDispatchHoldReason.OriginalUser, () => true, f.Token);
        var audits = WindowsSqliteConversationTests.AuditText(f);
        var retention = await f.Store.ReadRetentionAsync(target.Authority.SessionId, f.Token);
        var tasks = f.Count("host_task_events");
        var messages = f.Count("conversation_messages");
        composed.Conversation.IsRemainingWorkCurrent(answer, () => true).Should().BeFalse();
        using (var root = HostActivity.BeginRoot(f.Request, HostActivityLayer.Application, HostOperation.Request))
        {
            await composed.Conversation.Invoking(service => service.ValidateRemainingWorkAsync(answer, () => true, f.Token))
                .Should().ThrowAsync<InvalidOperationException>().WithMessage("*changed*");
        }
        SessionRemainingWorkAnswer fresh;
        using (var root = HostActivity.BeginRoot(f.Request, HostActivityLayer.Application, HostOperation.Request))
        {
            fresh = await composed.Conversation.ReadRemainingWorkAsync(target, () => true, f.Token);
        }
        fresh.Ordinary.ManualHoldReason.Should().Be(ConversationDispatchHoldReason.OriginalUser);
        fresh.Format().Should().Contain("Ordinary dispatch: held.").And.Contain("Manual ordinary dispatch hold: held.");
        fresh.FormatSpeech().Should().Contain("dispatch held");
        f.Count("host_task_events").Should().Be(tasks);
        f.Count("conversation_messages").Should().Be(messages);
        f.Count("scoped_grants").Should().Be(0);
        (await f.Store.ReadRetentionAsync(target.Authority.SessionId, f.Token)).Should().Be(retention);
        WindowsSqliteConversationTests.AuditText(f).Should().Be(audits);
    }

    [Fact]
    public async Task RemainingWorkCompletionAndNextDispatchBetweenSourcesRejectsMixedAnswerThenFreshReadSucceeds()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteConversationTests.FinishInitial(f);
        var adapter = new PendingTurnAdapter();
        var reader = new SearchReader(f.Store.ReadConversationPageAsync, readMetadata: f.Store.ReadMetadataAsync);
        await using var composed = new Composition(f, adapter: adapter, conversationReader: reader);
        var target = await f.Store.ReadMetadataAsync(f.Request.SessionId, f.Token);
        var first = await composed.Conversation.SubmitQueuedAsync(target, "first", () => true, f.Token);
        var active = await adapter.NextAsync();
        var second = await composed.Conversation.SubmitQueuedAsync(target, "second", () => true, f.Token);
        var reads = 0;
        reader.WorkRead = async (id, revision, limits, token) =>
        {
            var snapshot = await f.Store.ReadWorkAsync(id, revision, limits, token);
            if (++reads == 1)
            {
                active.Release();
                await first.Completion;
                await adapter.NextAsync();
            }
            return snapshot;
        };
        try
        {
            using var root = HostActivity.BeginRoot(f.Request, HostActivityLayer.Application, HostOperation.Request);
            await composed.Conversation.Invoking(service => service.ReadRemainingWorkAsync(target, () => true, f.Token))
                .Should().ThrowAsync<InvalidOperationException>().WithMessage("*changed*");
            var answer = await composed.Conversation.ReadRemainingWorkAsync(target, () => true, f.Token);
            answer.Ordinary.Running!.Target.Request.Should().Be(second.Receipt.Message.Request);
            answer.Ordinary.Turns.Count(turn => turn.Phase == ConversationPendingPhase.Pending).Should().Be(0);
            answer.Format().Should().Contain("running").And.NotContain("Submitted FIFO position");
            second.Completion.IsCompleted.Should().BeFalse();
            adapter.Calls.Should().Be(2);
        }
        finally { adapter.ReleaseAll(); await second.Completion; }
    }

    [Theory]
    [InlineData("hidden")]
    [InlineData("closed")]
    [InlineData("input")]
    [InlineData("privacy")]
    public async Task NativeRemainingWorkRetiresOriginalPresenterAndInputBeforeLateAnswer(string fault)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            var reader = new SearchReader(f.Store.ReadConversationPageAsync, readMetadata: f.Store.ReadMetadataAsync);
            await using var composed = new Composition(f, conversationReader: reader);
            var model = composed.Model;
            var owner = new SessionsPresentationOwner(model);
            var window = new CompactSessionWindow(model, owner);
            window.Show();
            try
            {
                await model.RefreshAsync();
                await model.SelectAsync(model.Sessions.Single());
                reader.WorkRead = async (id, revision, limits, token) =>
                {
                    var snapshot = await f.Store.ReadWorkAsync(id, revision, limits, token);
                    if (fault is "hidden") { window.Hide(); window.Show(); }
                    if (fault is "closed") { owner.Revoke(); }
                    if (fault is "input") { model.ComposerBody = "changed original input"; }
                    if (fault is "privacy") { composed.Access.CanInspect = false; }
                    return snapshot;
                };
                window.FindControl<Button>("AnswerRemainingWork")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await window.ConversationOperation;
                model.RemainingWorkStatus.Should().NotContain("Durable ledger observed");
                f.Count("conversation_messages").Should().Be(0);
                f.Count("scoped_grants").Should().Be(0);
            }
            finally { owner.Revoke(); }
        });
    }

    [Fact]
    public async Task RemainingWorkExplicitDoneTargetNeverObservesOrResumesOtherRunningConversation()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteConversationTests.FinishInitial(f);
        var adapter = new PendingTurnAdapter();
        await using var composed = new Composition(f, adapter: adapter);
        var active = await f.Store.ReadMetadataAsync(f.Request.SessionId, f.Token);
        var submitted = await composed.Conversation.SubmitQueuedAsync(active, "other private active request", () => true, f.Token);
        await adapter.NextAsync();
        try
        {
            var other = await composed.Workspace.CreateAsync(new("Done topic"), RequestOrigin.LocalUi, f.Token);
            await composed.Workspace.ChangeLifecycleAsync(other.Authority.SessionId, other.Authority.Generation, false, RequestOrigin.LocalUi, f.Token);
            other = await f.Store.ReadMetadataAsync(other.Authority.SessionId, f.Token);
            using var root = HostActivity.BeginRoot(f.Request, HostActivityLayer.Application, HostOperation.Request);
            var before = await f.Store.ReadRetentionAsync(other.Authority.SessionId, f.Token);
            var answer = await composed.Conversation.ReadRemainingWorkAsync(other, () => true, f.Token);
            answer.Format().Should().Contain("Done topic (Done; observed without Resume)")
                .And.Contain("no in-memory execution owner").And.NotContain("other private active request");
            submitted.Completion.IsCompleted.Should().BeFalse();
            adapter.Calls.Should().Be(1);
            (await f.Store.ReadRetentionAsync(other.Authority.SessionId, f.Token)).Should().Be(before);
            (await f.Store.ReadMetadataAsync(other.Authority.SessionId, f.Token)).Authority.IsActive.Should().BeFalse();
        }
        finally { adapter.ReleaseAll(); await submitted.Completion; }
    }

    private sealed partial class SearchReader : ISessionWorkStore
    {
        internal Func<HostId<SessionIdentity>, long, SessionQueueLimits, CancellationToken, Task<SessionWorkSnapshot>>? WorkRead { get; set; }
        public async ValueTask<SessionWorkSnapshot> ReadWorkAsync(HostId<SessionIdentity> session, long admissionRevision,
            SessionQueueLimits limits, CancellationToken token) =>
            await (WorkRead ?? throw new InvalidOperationException("The controlled work reader is unavailable."))(session, admissionRevision, limits, token);
    }
}