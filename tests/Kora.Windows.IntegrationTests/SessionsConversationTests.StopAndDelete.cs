using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;

using AwesomeAssertions;

using Kora.Core.Commands;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Storage;
using Kora.Windows.IntegrationTests.Storage;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionsConversationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativeStopAndDeleteJoinsActualProviderBeforeExistingAuditedDispositionAndDiscardsOnlyReviewedInput(bool nativeSubmission)
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
            var other = f.Request.SessionId;
            var adapter = new GateableConversationAdapter();
            await using var composed = new Composition(f, adapter: adapter);
            var model = composed.Model;
            var window = new SessionsWindow(model);
            window.Show();
            Task? submission = null;
            try
            {
                await model.RefreshAsync();
                await model.SelectAsync(model.Sessions.Single(row => row.Authority.SessionId == other));
                model.ComposerBody = "other conversation memory";
                await model.SelectAsync(model.Sessions.Single(row => row.Authority.SessionId == original));
                var target = model.SelectedSessionRecord!;
                var reviewedInput = nativeSubmission ? "running request" : "reviewed unsent text";
                model.ComposerBody = reviewedInput;
                if (nativeSubmission)
                {
                    window.FindControl<Button>("SubmitConversation")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    submission = window.ConversationOperation;
                }
                else { submission = composed.Conversation.SubmitAsync(target, "running request", static () => true, f.Token); }
                var provenance = await adapter.Started.Task;
                var review = window.FindControl<Button>("ReviewStopAndDelete")!;
                AutomationProperties.GetName(review).Should().Contain("selected conversation");
                if (nativeSubmission)
                {
                    window.Activate();
                    review.Focus().Should().BeTrue();
                    window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "\r");
                }
                else { review.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }
                await window.StopAndDeleteOperation;
                model.StopAndDeleteStatus.Should().Contain(reviewedInput).And.Contain("Review only");
                model.ComposerBody.Should().Be(reviewedInput);
                composed.Audit.Events.Should().NotContain(value => string.Equals(value.ActionId, "conversation.cancel-request", StringComparison.Ordinal));
                model.CanConfirmStopAndDelete.Should().BeTrue();
                var confirmation = window.FindControl<Button>("ConfirmStopAndDelete")!;
                if (nativeSubmission)
                {
                    confirmation.Focus().Should().BeTrue();
                    window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "\r");
                }
                else { confirmation.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }
                var operation = window.StopAndDeleteOperation;
                operation.IsCompleted.Should().BeFalse();
                (await f.Store.ReadMetadataAsync(original, f.Token)).Should().Be(target);
                (await f.Store.ReadConversationPageAsync(original, null, 25, f.Token)).Messages.Should().ContainSingle();
                model.StopAndDeleteStatus.Should().Contain("not deleted yet");
                model.CanConfirmStopAndDelete.Should().BeFalse();
                await model.ConfirmStopAndDeleteAsync();
                var auditCount = composed.Audit.Events.Count(value => string.Equals(value.ActionId, "conversation.cancel-request", StringComparison.Ordinal));
                auditCount.Should().Be(2, "double confirmation cannot accept another cancellation");
                adapter.Release.SetResult(new(ModelTurnOutcome.Succeeded, provenance.Selection, new("late answer", null)));
                await operation;
                try { await submission; }
                catch (ConversationSubmissionUnconfirmedException) { }
                model.StopAndDeleteStatus.Should().Contain("logically deleted");
                model.ComposerBody.Should().BeEmpty();
                (await f.Store.ReadHistoryAsync(original, null, 50, f.Token)).Disposed.Should().BeTrue();
                (await f.Store.ReadMetadataAsync(other, f.Token)).Authority.IsActive.Should().BeTrue();
                f.Count("security_audit_events").Should().BeGreaterThan(0);
                await model.RefreshAsync();
                await model.SelectAsync(model.Sessions.Single(row => row.Authority.SessionId == other));
                model.ComposerBody.Should().Be("other conversation memory");
                await composed.Conversation.Invoking(value => value.SubmitAsync(target, "late new request",
                    static () => true, f.Token)).Should().ThrowAsync<InvalidOperationException>();
            }
            finally
            {
                adapter.Release.TrySetResult(new(ModelTurnOutcome.Unknown, ModelProviderSelection.OllamaCandidate));
                if (submission is not null)
                {
                    try { await submission; }
                    catch (ConversationSubmissionUnconfirmedException) { }
                }
                window.ClosePrivate();
            }
        });
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("expiry")]
    [InlineData("rename")]
    [InlineData("privacy")]
    [InlineData("hide")]
    public async Task NativeStopAndDeleteNeverDeletesWhenReviewRetiresDuringPhysicalJoin(string change)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            var adapter = new GateableConversationAdapter();
            await using var composed = new Composition(f, adapter: adapter);
            var model = composed.Model;
            var window = new SessionsWindow(model);
            window.Show();
            try
            {
                await model.RefreshAsync();
                await model.SelectAsync(model.Sessions.Single());
                model.ComposerBody = "keep this memory";
                var target = model.SelectedSessionRecord!;
                var submission = composed.Conversation.SubmitAsync(target, "running", static () => true, f.Token);
                var provenance = await adapter.Started.Task;
                var accepted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                composed.Conversation.RunningConversationChanged += observed =>
                {
                    if (observed.Phase == ConversationExecutionPhase.Cancelling) { accepted.TrySetResult(); }
                };
                await model.ReviewStopAndDeleteAsync();
                model.CanConfirmStopAndDelete.Should().BeTrue();
                var confirmation = model.ConfirmStopAndDeleteAsync();
                confirmation.IsCompleted.Should().BeFalse();
                await accepted.Task;
                if (change is "cancel") { model.CancelStopAndDeleteReview(); }
                if (change is "expiry") { f.Time.Now = f.Time.Now.AddMinutes(2); }
                if (change is "rename")
                {
                    await composed.Workspace.RenameAsync(target.Authority.SessionId, target.Authority.Generation,
                        target.Metadata?.Revision.Value ?? 0, new("Changed conversation"), RequestOrigin.LocalUi, f.Token);
                }
                if (change is "privacy") { composed.Access.CanControl = false; composed.Access.ControlRevision++; }
                if (change is "hide") { window.Hide(); }
                confirmation.IsCompleted.Should().BeFalse("Cancel/privacy must retain ownership of physical completion");
                adapter.Release.SetResult(new(ModelTurnOutcome.Succeeded, provenance.Selection, new("late forbidden answer", null)));
                try { await submission; }
                catch (ConversationSubmissionUnconfirmedException) { }
                await confirmation;
                if (change is not "privacy")
                {
                    model.StopAndDeleteStatus.Should().Contain("Work stopped; not deleted");
                    model.ComposerBody.Should().Be("keep this memory");
                }
                (await f.Store.ReadHistoryAsync(target.Authority.SessionId, null, 50, f.Token)).Disposed.Should().BeFalse();
                (await f.Store.ReadConversationMessageAsync(target.Authority.SessionId, provenance.Request.RequestId, f.Token))
                    .AssistantResponse.Should().BeNull();
                if (change is "cancel")
                {
                    await model.ReviewStopAndDeleteAsync();
                    model.StopAndDeleteStatus.Should().Contain("previously reviewed work physically stopped");
                    await model.ConfirmStopAndDeleteAsync();
                    model.StopAndDeleteStatus.Should().Contain("logically deleted");
                    model.ComposerBody.Should().BeEmpty();
                }
            }
            finally
            {
                adapter.Release.TrySetResult(new(ModelTurnOutcome.Unknown, ModelProviderSelection.OllamaCandidate));
                window.ClosePrivate();
            }
        });
    }

    [Theory]
    [InlineData("decline")]
    [InlineData("buffer")]
    [InlineData("metadata")]
    [InlineData("new-task")]
    [InlineData("voice")]
    public async Task StopAndDeletePreviewAndInvalidConfirmationNeverStopWorkOrDiscardInput(string change)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            var adapter = new GateableConversationAdapter();
            await using var composed = new Composition(f, adapter: adapter);
            var model = composed.Model;
            var window = new SessionsWindow(model);
            window.Show();
            try
            {
                await model.RefreshAsync();
                await model.SelectAsync(model.Sessions.Single());
                model.ComposerBody = "unsent original";
                var target = model.SelectedSessionRecord!;
                var submission = composed.Conversation.SubmitAsync(target, "running", static () => true, f.Token);
                var provenance = await adapter.Started.Task;
                if (change is "voice")
                {
                    await composed.Conversation.Invoking(value => value.ReviewStopAndDeleteAsync(target,
                        RequestOrigin.ActivatedVoice, static () => true, f.Token)).Should().ThrowAsync<InvalidOperationException>();
                }
                else
                {
                    await model.ReviewStopAndDeleteAsync();
                    if (change is "decline") { model.CancelStopAndDeleteReview(); }
                    if (change is "buffer") { model.ComposerBody = "new unreviewed memory"; }
                    if (change is "metadata")
                    {
                        await composed.Workspace.RenameAsync(target.Authority.SessionId, target.Authority.Generation,
                            target.Metadata?.Revision.Value ?? 0, new("Changed target"), RequestOrigin.LocalUi, f.Token);
                    }
                    if (change is "new-task")
                    {
                        var added = new HostRequest(new(Guid.NewGuid()), target.Authority.SessionId,
                            new(Guid.NewGuid()), RequestOrigin.LocalUi);
                        using var activity = Kora.Core.Diagnostics.HostActivity.BeginRoot(added,
                            Kora.Core.Diagnostics.HostActivityLayer.Application, Kora.Core.Diagnostics.HostOperation.Request);
                        var intent = await f.Store.RecordControlIntentAsync(added, f.Token);
                        await f.Tasks.CommitAsync(intent.Next(HostTaskState.Succeeded), intent.Revision.Value, f.Token);
                    }
                    await model.ConfirmStopAndDeleteAsync();
                }
                composed.Audit.Events.Should().NotContain(value => string.Equals(value.ActionId, "conversation.cancel-request", StringComparison.Ordinal));
                (await composed.Conversation.ReadRunningConversationAsync(
                    await f.Store.ReadMetadataAsync(target.Authority.SessionId, f.Token), static () => true, f.Token))!
                    .Phase.Should().Be(ConversationExecutionPhase.Running);
                model.ComposerBody.Should().Be(change is "buffer" ? "new unreviewed memory" : "unsent original");
                adapter.Release.SetResult(new(ModelTurnOutcome.Unknown, provenance.Selection));
                try { await submission; }
                catch (ConversationSubmissionUnconfirmedException) { }
            }
            finally
            {
                adapter.Release.TrySetResult(new(ModelTurnOutcome.Unknown, ModelProviderSelection.OllamaCandidate));
                window.ClosePrivate();
            }
        });
    }

    [Theory]
    [InlineData("typed")]
    [InlineData("voice")]
    [InlineData("wrong-target")]
    [InlineData("metadata")]
    [InlineData("native-revoked")]
    public async Task NativeCompoundCommandReviewUsesOnlySelectedExactMetadataAndNeverRelabelsVoice(string source)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            var adapter = new GateableConversationAdapter();
            await using var composed = new Composition(f, adapter: adapter);
            var model = composed.Model;
            var window = new SessionsWindow(model);
            window.Show();
            try
            {
                await model.RefreshAsync();
                await model.SelectAsync(model.Sessions.Single());
                var target = model.SelectedSessionRecord!;
                model.ComposerBody = "keep exact memory";
                var submission = composed.Conversation.SubmitAsync(target, "running request", static () => true, f.Token);
                var provenance = await adapter.Started.Task;
                var command = new SessionDispositionReviewCommand(
                    source is "wrong-target" ? Guid.NewGuid() : target.Authority.SessionId.Value,
                    target.Authority.Generation.Value, source is "metadata" ? 42 : target.Metadata?.Revision.Value ?? 0)
                { StopRunningWork = true };
                var review = () => model.ReviewStopAndDeleteCommandAsync(command,
                    source is "voice" ? RequestOrigin.ActivatedVoice : RequestOrigin.LocalUi,
                    static () => true, () => source is not "native-revoked", f.Token);
                if (source is "voice" or "wrong-target" or "metadata")
                { await review.Should().ThrowAsync<InvalidOperationException>(); }
                else { (await review()).Should().Be(source is "typed"); }
                composed.Audit.Events.Should().NotContain(value =>
                    string.Equals(value.ActionId, "conversation.cancel-request", StringComparison.Ordinal));
                model.ComposerBody.Should().Be("keep exact memory");
                adapter.Release.SetResult(new(ModelTurnOutcome.Unknown, provenance.Selection));
                await submission;
            }
            finally
            {
                adapter.Release.TrySetResult(new(ModelTurnOutcome.Unknown, ModelProviderSelection.OllamaCandidate));
                window.ClosePrivate();
            }
        });
    }
}
