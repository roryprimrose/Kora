using System.Text.Json;

using Avalonia.Controls;
using Avalonia.Interactivity;

using AwesomeAssertions;

using Kora.Application.Definitions;
using Kora.Core.Artifacts;
using Kora.Core.Dependencies;
using Kora.Windows.IntegrationTests.Storage;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionsConversationTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task MixedContextNativeControlsReviewCompleteSourcesAndSubmitOnlyAfterSeparateConfirmation(bool qualified)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            var source = new NativeInstructionSource();
            var snapshot = source.Snapshot!;
            var revisions = new InstructionRevisionCatalogue(f.Paths);
            var catalogue = new ArtifactCatalogue(revisions.Reconcile(
                [snapshot.Document.Definition with { OwnedInstructionRevision = snapshot.Revision }]));
            revisions.CompleteActivation(snapshot, revisions.PrepareActivation(snapshot), catalogue);
            var adapter = new MemoryAnswerAdapter();
            await using var composition = new Composition(f, adapter: adapter, memoryContext: true,
                instructionFactory: access => new(catalogue, revisions, source, f.Store, access, f.Time));
            var model = composition.Model;
            var parent = new SessionsWindow(model);
            parent.Show();
            ReviewedContextWindow? window = null;
            using var opened = Window.WindowOpenedEvent.AddClassHandler<Window>((candidate, _) =>
            {
                if (candidate is ReviewedContextWindow mixed) { window = mixed; }
            });
            try
            {
                await PrepareMemoryContext(model);
                model.ComposerBody = "Earlier original input";
                await model.SubmitConversationAsync();
                await model.ReadConversationAsync();
                var previous = model.ConversationMessages.Single();
                model.ComposerBody = "  New unchanged mixed request.\r\n  ";
                parent.FindControl<Button>("OpenMixedContext")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                window.Should().NotBeNull();
                var native = window!;
                var history = native.FindControl<ListBox>("MixedHistory")!;
                foreach (var row in model.MixedHistoryChoices.Reverse()) { history.SelectedItems!.Add(row); }
                native.FindControl<ListBox>("MixedMemories")!.SelectedItems!.Add(model.MixedMemoryChoices.Single());
                native.FindControl<ComboBox>("MixedInstruction")!.SelectedItem = model.MixedInstructionChoices.Single();
                model.CanReviewMixedContext.Should().BeTrue();
                var before = adapter.Calls;
                native.FindControl<Button>("ReviewMixedContext")!.Command!.Execute(null);
                await native.Operation;
                model.CanConfirmMixedContext.Should().BeTrue(model.ConversationStatus);
                model.MixedReviewText.Should().Contain(previous.Body).And.Contain(previous.AssistantResponse!.Body)
                    .And.Contain(snapshot.Document.Text).And.Contain("private reviewed session decision")
                    .And.Contain("  New unchanged mixed request.\r\n  ").And.Contain("4 / 16 evidence items");
                adapter.Calls.Should().Be(before);
                f.Count("conversation_messages").Should().Be(1);
                if (!qualified) { composition.Access.CanControl = false; composition.Access.ControlRevision++; }
                native.FindControl<Button>("ConfirmMixedContext")!.Command!.Execute(null);
                await native.Operation;
                if (qualified)
                {
                    model.MixedOutcome.Should().Be("ASSISTANT\nAnswer from reviewed session fact.");
                    adapter.Calls.Should().Be(before + 1);
                    var retained = model.ConversationMessages.Single();
                    retained.Body.Should().Be("  New unchanged mixed request.\r\n  ");
                    retained.Outcome.Should().Be(ModelTurnOutcome.Succeeded);
                    using var wire = JsonDocument.Parse(adapter.Wire!);
                    wire.RootElement.GetProperty("Evidence").GetArrayLength().Should().Be(4);
                    wire.RootElement.GetProperty("Tools").GetArrayLength().Should().Be(0);
                    model.MixedReviewText.Should().BeEmpty();
                    await model.ConfirmMixedContextAsync();
                    adapter.Calls.Should().Be(before + 1);
                }
                else
                {
                    adapter.Calls.Should().Be(before);
                    f.Count("conversation_messages").Should().Be(1);
                    model.MixedReviewText.Should().BeEmpty();
                }
                f.Count("reviewed_memory").Should().Be(1);
                f.Count("scoped_grants").Should().Be(0);
                f.Count("host_questions").Should().Be(0);
            }
            finally { window?.Close(); parent.ClosePrivate(); }
            model.MixedBody.Should().BeEmpty();
            model.MixedOutcome.Should().BeEmpty();
        });
    }

    [Theory]
    [InlineData("input")]
    [InlineData("instruction")]
    [InlineData("memory")]
    [InlineData("cancel")]
    [InlineData("close")]
    public async Task MixedContextNativeChangesCannotReusePreviousReviewOrCopySourcesIntoHistory(string change)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            var source = new NativeInstructionSource();
            var snapshot = source.Snapshot!;
            var revisions = new InstructionRevisionCatalogue(f.Paths);
            var catalogue = new ArtifactCatalogue(revisions.Reconcile(
                [snapshot.Document.Definition with { OwnedInstructionRevision = snapshot.Revision }]));
            revisions.CompleteActivation(snapshot, revisions.PrepareActivation(snapshot), catalogue);
            var adapter = new MemoryAnswerAdapter();
            await using var composition = new Composition(f, adapter: adapter, memoryContext: true,
                instructionFactory: access => new(catalogue, revisions, source, f.Store, access, f.Time));
            var model = composition.Model;
            try
            {
                await PrepareMemoryContext(model);
                model.ComposerBody = "Earlier original";
                await model.SubmitConversationAsync();
                await model.ReadConversationAsync();
                model.OpenMixedContext();
                model.SelectMixedHistory(model.MixedHistoryChoices);
                model.SelectMixedMemories(model.MixedMemoryChoices);
                model.MixedInstruction = model.MixedInstructionChoices.Single();
                model.MixedBody = "New mixed question";
                await model.ReviewMixedContextAsync();
                model.CanConfirmMixedContext.Should().BeTrue(model.ConversationStatus);
                if (change is "input") { model.MixedBody += " changed"; }
                if (change is "instruction") { revisions.DisableExact(snapshot, catalogue); }
                if (change is "memory")
                {
                    var row = model.MixedMemoryChoices.Single();
                    await composition.Memories.ExecuteAsync(new(Kora.Core.Commands.MemoryCommandOperation.Disable,
                        model.SelectedSessionRecord!.Authority.SessionId.Value, row.Id, row.Revision),
                        Kora.Core.Hosting.RequestOrigin.LocalUi, static () => true, f.Token);
                }
                if (change is "cancel") { model.CancelMixedContext(); }
                if (change is "close") { model.ClearMixedContext(); }
                await model.ConfirmMixedContextAsync();
                adapter.Calls.Should().Be(1);
                f.Count("conversation_messages").Should().Be(1);
                model.MixedReviewText.Should().BeEmpty();
            }
            finally { model.Close(); }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MixedContextNativeCancellationClearsSourcesButCannotClaimQuiescenceBeforeTransportTerminates(bool close)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            var source = new NativeInstructionSource();
            var snapshot = source.Snapshot!;
            var revisions = new InstructionRevisionCatalogue(f.Paths);
            var catalogue = new ArtifactCatalogue(revisions.Reconcile(
                [snapshot.Document.Definition with { OwnedInstructionRevision = snapshot.Revision }]));
            revisions.CompleteActivation(snapshot, revisions.PrepareActivation(snapshot), catalogue);
            var adapter = new MemoryAnswerAdapter();
            await using var composition = new Composition(f, adapter: adapter, memoryContext: true,
                instructionFactory: access => new(catalogue, revisions, source, f.Store, access, f.Time));
            var model = composition.Model;
            try
            {
                await PrepareMemoryContext(model);
                model.ComposerBody = "Earlier original";
                await model.SubmitConversationAsync();
                await model.ReadConversationAsync();
                model.OpenMixedContext();
                model.SelectMixedHistory(model.MixedHistoryChoices);
                model.SelectMixedMemories(model.MixedMemoryChoices);
                model.MixedInstruction = model.MixedInstructionChoices.Single();
                model.MixedBody = "New mixed question";
                await model.ReviewMixedContextAsync();
                var pending = new TaskCompletionSource<Kora.Application.Dependencies.ModelProviderReply>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                // The original answer has completed; this request gets its own cancellation-ignoring transport.
                adapter.Next = async () =>
                {
                    entered.SetResult();
                    return await pending.Task;
                };
                var submission = model.ConfirmMixedContextAsync();
                (await Task.WhenAny(entered.Task, submission).WaitAsync(f.Token)).Should()
                    .BeSameAs(entered.Task, model.ConversationStatus);
                if (close) { model.ClearMixedContext(); }
                else { model.CancelMixedContext(); }
                await submission;
                model.MixedReviewText.Should().BeEmpty();
                model.MixedOutcome.Should().BeEmpty();
                composition.Turns.IsQuiescent.Should().BeFalse();
                pending.SetResult(new(ModelTurnOutcome.Succeeded, ModelProviderSelection.OllamaCandidate,
                    new("late mixed answer must not publish", null)));
                await composition.Turns.DisposeAsync();
                composition.Turns.IsQuiescent.Should().BeTrue();
                var messages = (await f.Store.ReadConversationPageAsync(model.SelectedSessionRecord!.Authority.SessionId,
                    null, 25, f.Token)).Messages;
                messages.Should().HaveCount(2);
                messages.Count(message => message.AssistantResponse is not null).Should().Be(1);
                messages.Should().NotContain(message => message.AssistantResponse != null
                    && message.AssistantResponse.Body.Contains("late mixed answer", StringComparison.Ordinal));
            }
            finally { model.Close(); }
        });
    }
}
