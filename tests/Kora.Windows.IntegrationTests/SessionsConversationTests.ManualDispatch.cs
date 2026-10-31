using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;

using AwesomeAssertions;

using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Windows.IntegrationTests.Storage;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionsConversationTests
{
    [Fact]
    public async Task NativeManualDispatchHoldPreservesRunningWorkAndInputThenReleasesOnlyTheAddressedConversation()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            f.Time.UseFrozenTimers = true;
            var original = f.Request.SessionId;
            f.Request = InteractionStorageFixture.NewRequest();
            await f.AdmitAsync(newSession: true);
            await WindowsSqliteConversationTests.FinishInitial(f);
            var adapter = new PendingTurnAdapter();
            await using var composed = new Composition(f, adapter: adapter);
            var model = composed.Model;
            var window = new SessionsWindow(model);
            window.Show();
            try
            {
                await model.RefreshAsync();
                await model.SelectAsync(model.Sessions.Single(item => item.Authority.SessionId == original));
                model.ComposerBody = "running original";
                var running = model.SubmitConversationAsync();
                var first = await adapter.NextAsync();
                await WhenModelAsync(model, () => model.CanCompose && !model.ComposerDirty);
                model.ComposerBody = "pending original";
                await model.SubmitConversationAsync();
                model.ComposerBody = "unsent stays independent";
                await model.RefreshPendingConversationAsync();
                var pending = model.PendingConversationTurns.Single();
                var originalRecord = await f.Store.ReadConversationMessageAsync(original, first.Provenance.Request.RequestId, f.Token);
                var retention = await f.Store.ReadRetentionAsync(original, f.Token);
                var panel = window.GetLogicalDescendants().OfType<PendingConversationPanel>().Single();
                model.CanHoldConversationDispatch.Should().BeTrue();
                panel.FindControl<Button>("HoldDispatch")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await panel.Operation;
                model.PendingConversationStatus.Should().Contain("Manual hold", model.ConversationStatus).And.NotContain("held after failure");
                model.CanReleaseConversationDispatch.Should().BeTrue();
                model.CanResumePendingConversation.Should().BeFalse();
                model.PendingConversationTurns.Single().ExpiresAt.Should().Be(pending.ExpiresAt);
                running.IsCompleted.Should().BeFalse();
                (await f.Store.ReadConversationMessageAsync(original, first.Provenance.Request.RequestId, f.Token)).Should().Be(originalRecord);
                (await f.Store.ReadRetentionAsync(original, f.Token)).Should().Be(retention);
                model.ComposerBody.Should().Be("unsent stays independent");
                first.Release();
                await running;
                composed.Turns.IsQuiescent.Should().BeTrue();
                adapter.Calls.Should().Be(1);
                (await f.Store.ReadConversationMessageAsync(original, pending.Submission.Message.Request.RequestId, f.Token))
                    .Execution.Should().BeNull();
                await model.SelectAsync(model.Sessions.Single(item => item.Authority.SessionId != original));
                await model.RefreshPendingConversationAsync();
                model.CanReleaseConversationDispatch.Should().BeFalse();
                model.ComposerBody = "independent other conversation";
                var otherOperation = model.SubmitConversationAsync();
                var other = await adapter.NextAsync();
                other.Provenance.Request.SessionId.Should().NotBe(original);
                other.Release();
                await otherOperation;
                await model.SelectAsync(model.Sessions.Single(item => item.Authority.SessionId == original));
                model.ComposerBody.Should().Be("unsent stays independent");
                await model.RefreshPendingConversationAsync();
                panel.FindControl<Button>("ReleaseDispatch")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await panel.Operation;
                var resumed = await adapter.NextAsync();
                resumed.Provenance.Request.Should().Be(pending.Submission.Message.Request);
                resumed.Body.Should().Be("pending original");
                var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                composed.Conversation.SubmittedConversationCompleted += (_, result) =>
                {
                    if (result.Receipt?.Message.Request == pending.Submission.Message.Request) { completed.TrySetResult(); }
                };
                resumed.Release();
                await completed.Task.WaitAsync(f.Token);
                model.CanReleaseConversationDispatch.Should().BeFalse();
                f.Count("scoped_grants").Should().Be(0);
                f.Count("host_questions").Should().Be(0);
                (await f.Store.ReadConversationDraftAsync(original, f.Token)).Draft.Body.Should().BeNull();
                var history = await f.Store.ReadConversationPageAsync(original, null, 25, f.Token);
                history.Messages.Select(item => item.Body).Should().Equal("running original", "pending original");
                history.Messages.Should().OnlyContain(item => item.Outcome == ModelTurnOutcome.Succeeded);
            }
            finally { adapter.ReleaseAll(); window.ClosePrivate(); await composed.Conversation.DisposeAsync(); }
        });
    }
}
