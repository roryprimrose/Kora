using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using AwesomeAssertions;
using Kora.Application.Dependencies;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Memory;
using Kora.Core.Providers;
using Kora.Core.Tools;
using Kora.Windows.IntegrationTests.Storage;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionsConversationTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task NativeMemoryContextUsesCanonicalRetainedAnswerOnlyAfterCompleteReviewAndSeparateConsent(bool qualified)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            var adapter = qualified ? new MemoryAnswerAdapter() : null;
            await using var composition = new Composition(f, adapter: adapter, memoryContext: true);
            var model = composition.Model;
            var window = new SessionsWindow(model);
            window.Show();
            try
            {
                await PrepareMemoryContext(model);
                var source = model.MemoryRecords.Single();
                var list = window.FindControl<ListBox>("MemoryContextRecords")!;
                list.SelectedItems!.Add(source);
                model.MemoryContextSelection.Should().Equal(source);
                model.MemoryContextRequest = "Explain the selected decision.";
                var review = window.FindControl<Button>("ReviewMemoryContext")!;
                review.IsEnabled.Should().BeTrue();
                review.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await window.MemoryContextOperation;
                model.CanConfirmMemoryContext.Should().BeTrue(model.ConversationStatus);
                model.MemoryContextDetail.Should().Contain("private reviewed session decision")
                    .And.Contain(nameof(MemoryContentClass.Decision))
                    .And.Contain(source.Scope.Identity.ToString("D"))
                    .And.Contain("Explain the selected decision.");
                f.Count("conversation_messages").Should().Be(0);
                (adapter?.Calls ?? 0).Should().Be(0);
                var confirm = window.FindControl<Button>("ConfirmMemoryContext")!;
                confirm.IsEnabled.Should().BeTrue();
                confirm.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await window.MemoryContextOperation;
                var retained = (await f.Store.ReadConversationPageAsync(model.SelectedSessionRecord!.Authority.SessionId,
                    null, 25, f.Token)).Messages.Single();
                retained.Body.Should().Be("Explain the selected decision.");
                retained.Outcome.Should().Be(qualified ? ModelTurnOutcome.Succeeded : ModelTurnOutcome.Unavailable);
                (retained.AssistantResponse?.Body).Should().Be(qualified ? "Answer from reviewed session fact." : null);
                model.MemoryContextOutcome.Should().Contain(qualified ? "ASSISTANT" : "HOST Unavailable");
                model.MemoryContextDetail.Should().BeEmpty();
                model.MemoryContextSelection.Should().BeEmpty();
                f.Count("reviewed_memory").Should().Be(1);
                f.Count("host_questions").Should().Be(0);
                f.Count("scoped_grants").Should().Be(0);
                (await f.Tasks.ReadIncompleteAsync(100, f.Token)).Should().BeEmpty(
                    "each private source read and the retained conversation must have a confirmed terminal receipt");
                if (adapter is not null)
                {
                    adapter.Calls.Should().Be(1);
                    using var wire = JsonDocument.Parse(adapter.Wire!);
                    wire.RootElement.GetProperty("ToolMode").GetInt32().Should().Be((int)ModelToolMode.None);
                    wire.RootElement.GetProperty("Tools").GetArrayLength().Should().Be(0);
                    var evidence = wire.RootElement.GetProperty("Evidence").EnumerateArray().Single();
                    evidence.GetProperty("Disclosure").GetInt32().Should().Be((int)ModelEvidenceDisclosure.LocalOnly);
                    evidence.GetProperty("Content").GetString().Should().Contain("private reviewed session decision");
                }
                await model.ConfirmMemoryContextAsync();
                f.Count("conversation_messages").Should().Be(1, "consumed consent cannot submit again");
                window.ClosePrivate();
                model.MemoryContextOutcome.Should().BeEmpty();
                model.MemoryContextRequest.Should().BeEmpty();
            }
            finally { window.ClosePrivate(); }
        });
    }

    [Theory]
    [InlineData("disable")]
    [InlineData("forget")]
    [InlineData("cancel")]
    [InlineData("close")]
    public async Task NativeMemoryContextRevocationSuppressesNonCooperativeLateAnswerThroughCanonicalLifecycle(string revoke)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            var pending = new TaskCompletionSource<ModelProviderReply>(TaskCreationOptions.RunContinuationsAsynchronously);
            var adapter = new MemoryAnswerAdapter { Pending = pending.Task };
            await using var composition = new Composition(f, adapter: adapter, memoryContext: true);
            var model = composition.Model;
            var window = new SessionsWindow(model);
            window.Show();
            try
            {
                await PrepareMemoryContext(model);
                var source = model.MemoryRecords.Single();
                model.SelectMemoryContext([source]);
                model.MemoryContextRequest = "Answer this reviewed fact.";
                await model.ReviewMemoryContextAsync();
                model.CanConfirmMemoryContext.Should().BeTrue(model.ConversationStatus);
                var confirmation = model.ConfirmMemoryContextAsync();
                (await Task.WhenAny(adapter.Entered.Task, confirmation).WaitAsync(f.Token)).Should()
                    .BeSameAs(adapter.Entered.Task, model.ConversationStatus);
                var target = model.SelectedSessionRecord!;
                var running = await composition.Conversation.ReadRunningConversationAsync(target, static () => true, f.Token);
                running!.Phase.Should().Be(ConversationExecutionPhase.Running);
                if (revoke is "disable" or "forget")
                {
                    await composition.Memories.ExecuteAsync(new(revoke is "disable"
                        ? Kora.Core.Commands.MemoryCommandOperation.Disable : Kora.Core.Commands.MemoryCommandOperation.Forget,
                        target.Authority.SessionId.Value, source.Id, source.Revision),
                        RequestOrigin.LocalUi, static () => true, f.Token);
                }
                else if (revoke is "cancel") { model.CancelMemoryContext(); }
                else { window.ClosePrivate(); }
                await confirmation;
                model.MemoryContextDetail.Should().BeEmpty();
                model.MemoryContextOutcome.Should().NotContain("Answer from reviewed session fact.");
                var retained = (await f.Store.ReadConversationPageAsync(target.Authority.SessionId, null, 25, f.Token)).Messages.Single();
                retained.AssistantResponse.Should().BeNull();
                retained.Outcome.Should().BeNull("caller/source revocation retains an unconfirmed dispatch, not fake success");
                composition.Turns.IsQuiescent.Should().BeFalse();
                pending.SetResult(new(ModelTurnOutcome.Succeeded, ModelProviderSelection.OllamaCandidate,
                    new("late answer must not appear", null)));
                await composition.Turns.WaitForQuiescenceAsync(f.Token);
                (await f.Store.ReadConversationMessageAsync(target.Authority.SessionId, retained.Request.RequestId, f.Token))
                    .AssistantResponse.Should().BeNull();
                composition.Turns.IsQuiescent.Should().BeTrue();
                (await f.Tasks.ReadIncompleteAsync(100, f.Token)).Should().OnlyContain(task =>
                    task.Request == retained.Request && task.State == HostTaskState.DispatchRecorded,
                    "revoked conversation dispatch stays unconfirmed, but private source-read tasks must close");
                model.MemoryContextOutcome.Should().NotContain("late answer");
                Dispatcher.UIThread.RunJobs();
            }
            finally
            {
                pending.TrySetResult(new(ModelTurnOutcome.Succeeded, ModelProviderSelection.OllamaCandidate, new("late answer", null)));
                window.ClosePrivate();
            }
        });
    }

    [Fact]
    public async Task NativeMemoryContextCanonicalCancellationWaitsForQuiescenceAndRetainsTruthfulCancelledTerminal()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            var pending = new TaskCompletionSource<ModelProviderReply>(TaskCreationOptions.RunContinuationsAsynchronously);
            var adapter = new MemoryAnswerAdapter { Pending = pending.Task };
            await using var composition = new Composition(f, adapter: adapter, memoryContext: true);
            var model = composition.Model;
            var window = new SessionsWindow(model);
            window.Show();
            try
            {
                await PrepareMemoryContext(model);
                model.SelectMemoryContext([model.MemoryRecords.Single()]);
                model.MemoryContextRequest = "Question to cancel.";
                await model.ReviewMemoryContextAsync();
                model.CanConfirmMemoryContext.Should().BeTrue(model.ConversationStatus);
                var confirmation = model.ConfirmMemoryContextAsync();
                (await Task.WhenAny(adapter.Entered.Task, confirmation).WaitAsync(f.Token)).Should()
                    .BeSameAs(adapter.Entered.Task, model.ConversationStatus);
                var target = model.SelectedSessionRecord!;
                var running = await composition.Conversation.ReadRunningConversationAsync(target, static () => true, f.Token);
                var stopping = await composition.Conversation.CancelRunningConversationAsync(running!.Target, static () => true, f.Token);
                stopping.Phase.Should().Be(ConversationExecutionPhase.Cancelling);
                confirmation.IsCompleted.Should().BeFalse("accepted cancellation must join the noncooperative provider");
                composition.Turns.IsQuiescent.Should().BeFalse();
                pending.SetResult(new(ModelTurnOutcome.Succeeded, ModelProviderSelection.OllamaCandidate, new("late forbidden answer", null)));
                await confirmation;
                composition.Turns.IsQuiescent.Should().BeTrue();
                var retained = (await f.Store.ReadConversationPageAsync(target.Authority.SessionId, null, 25, f.Token)).Messages.Single();
                retained.Outcome.Should().Be(ModelTurnOutcome.Cancelled);
                retained.Reason.Should().Be(ModelTurnReason.CallerCancelled);
                retained.AssistantResponse.Should().BeNull();
                model.MemoryContextOutcome.Should().Contain("HOST Cancelled").And.NotContain("late forbidden");
                (await f.Tasks.ReadIncompleteAsync(100, f.Token)).Should().BeEmpty();
            }
            finally
            {
                pending.TrySetResult(new(ModelTurnOutcome.Succeeded, ModelProviderSelection.OllamaCandidate, new("late answer", null)));
                window.ClosePrivate();
            }
        });
    }

    [Fact]
    public async Task NativeMemoryContextQueuedReviewCannotSurviveForgetAndDefaultSendStillExcludesMemory()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            var adapter = new MemoryAnswerAdapter();
            await using var composition = new Composition(f, adapter: adapter, memoryContext: true);
            var model = composition.Model;
            var window = new SessionsWindow(model);
            window.Show();
            try
            {
                await PrepareMemoryContext(model);
                var source = model.MemoryRecords.Single();
                model.SelectMemoryContext([source]);
                model.MemoryContextRequest = "Question.";
                await model.ReviewMemoryContextAsync();
                model.CanConfirmMemoryContext.Should().BeTrue(model.ConversationStatus);
                var session = model.SelectedSessionRecord!.Authority.SessionId;
                await composition.Memories.ExecuteAsync(new(Kora.Core.Commands.MemoryCommandOperation.Forget,
                    session.Value, source.Id, source.Revision), RequestOrigin.LocalUi, static () => true, f.Token);
                model.MemoryContextDetail.Should().BeEmpty();
                model.CanConfirmMemoryContext.Should().BeFalse();
                await model.ConfirmMemoryContextAsync();
                f.Count("conversation_messages").Should().Be(0);
                adapter.Calls.Should().Be(0);
                model.ComposerBody = "Ordinary new question.";
                await model.SubmitConversationAsync();
                adapter.Calls.Should().Be(1);
                using var wire = JsonDocument.Parse(adapter.Wire!);
                wire.RootElement.GetProperty("Evidence").GetArrayLength().Should().Be(0);
            }
            finally { window.ClosePrivate(); }
        });
    }

    private static async Task PrepareMemoryContext(SessionsViewModel model)
    {
        await model.RefreshAsync();
        await model.SelectAsync(model.Sessions.Single());
        await model.ReadConversationAsync();
        model.MemoryClass = MemoryContentClass.Decision;
        model.MemoryDraft = "private reviewed session decision";
        await model.ProposeMemoryAsync();
        model.SelectMemory(model.MemoryRecords.Single());
        await model.InspectMemoryAsync();
        await model.ReviewMemoryAsync(true);
        await model.AdmitMemoryAsync();
        await model.ListMemoriesAsync();
        model.MemoryRecords.Single().Retention.Should().Be(MemoryRetentionState.Enabled);
    }

    private sealed class MemoryAnswerAdapter : IModelTurnAdapter
    {
        internal int Calls { get; private set; }
        internal byte[]? Wire { get; private set; }
        internal Task<ModelProviderReply>? Pending { get; init; }
        internal Func<Task<ModelProviderReply>>? Next { get; set; }
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<ModelProviderReply> RunAsync(ModelTurnProvenance provenance, ProviderProfileTuple? profile,
            ReadOnlyMemory<byte> context, Func<string, string, CancellationToken, ValueTask<CapabilityReply>> invokeTool,
            CancellationToken token)
        {
            Calls++;
            Wire = context.ToArray();
            Entered.TrySetResult();
            if (Next is { } next) { return await next(); }
            return Pending is { } pending ? await pending
                : new(ModelTurnOutcome.Succeeded, provenance.Selection, new("Answer from reviewed session fact.", null));
        }
    }
}
