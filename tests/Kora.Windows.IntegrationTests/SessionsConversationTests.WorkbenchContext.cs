using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

using AwesomeAssertions;

using Kora.Application.Definitions;
using Kora.Application.Dependencies;
using Kora.Core.Artifacts;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.NativeUxFixture;
using Kora.Windows.IntegrationTests.Storage;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionsConversationTests
{
    [Theory]
    [InlineData("confirm")]
    [InlineData("hide-return")]
    [InlineData("switch-tab")]
    [InlineData("session")]
    public async Task WorkbenchHistoryContextCompletesOrRetiresTheOriginalReviewWithoutAnExtraWindow(string boundary)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var native = new NativeUxFixtureSession(Path.GetFullPath("."), silentCaption: true);
            await native.InitializeAsync();
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            var adapter = new HistoryAdapter();
            await using var composed = new Composition(f, adapter: adapter);
            var model = composed.Model;
            await model.RefreshAsync();
            await model.SelectAsync(model.Sessions.Single());
            model.ComposerBody = "earlier source";
            await model.SubmitConversationAsync();
            await model.ReadConversationAsync();
            model.ComposerBody = "original reviewed follow-up";
            var window = new ResponseWindow(native.Main);
            var presentation = new SessionsPresentationOwner(model);
            window.AttachSessions(model, presentation, static () => { }, static () => { });
            using var management = new SessionsManagementView(model, presentation, window);
            window.ShowHostedView(WorkbenchActivity.SessionManagement, management);
            Dispatcher.UIThread.RunJobs();
            var extraWindows = 0;
            using var opened = Window.WindowOpenedEvent.AddClassHandler<ConversationHistoryWindow>((_, _) => extraWindows++);
            try
            {
                management.FindControl<Button>("ReviewHistoryContext")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await management.HistoryContextOperation;
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                var host = window.FindControl<ContentControl>("NativeReviewContent")!;
                var view = host.GetVisualDescendants().OfType<ConversationHistoryView>().Single();
                view.FindControl<ListBox>("Sources")!.SelectedItems!.Add(model.HistoryContextCandidates[0]);
                view.FindControl<Button>("Review")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await view.Operation;
                model.CanConfirmHistoryContext.Should().BeTrue(model.ConversationStatus);
                extraWindows.Should().Be(0);
                if (string.Equals(boundary, "confirm", StringComparison.Ordinal))
                {
                    view.FindControl<Button>("Confirm")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    await view.Operation;
                    adapter.Calls.Should().Be(2);
                    model.ConversationUpdates.Should().ContainSingle().Which.Body.Should().Be("original reviewed follow-up");
                    model.ComposerBody.Should().BeEmpty();
                    f.Count("conversation_messages").Should().Be(2);
                }
                else
                {
                    switch (boundary)
                    {
                        case "hide-return": window.Hide(); window.Show(); window.Activate(); break;
                        case "switch-tab":
                            var body = new TextBox { IsReadOnly = true };
                            window.ShowNativeReview(new Border(), "Other original review", body,
                                static () => true, shown => body.Text = shown ? "other" : string.Empty);
                            break;
                        case "session": await model.SelectAsync(null); break;
                    }
                    Dispatcher.UIThread.RunJobs();
                    model.CanConfirmHistoryContext.Should().BeFalse();
                    model.HistoryContextCandidates.Should().BeEmpty();
                    model.HistoryContextReviewText.Should().BeEmpty();
                    await model.ConfirmHistoryContextAsync();
                    adapter.Calls.Should().Be(1);
                    f.Count("conversation_messages").Should().Be(1);
                    if (!string.Equals(boundary, "session", StringComparison.Ordinal))
                    { model.ComposerBody.Should().Be("original reviewed follow-up"); }
                }
            }
            finally { window.Close(); }
        });
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task WorkbenchMixedContextUsesTheExactNativeSelectionAndSeparateConfirmation(bool confirm)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var native = new NativeUxFixtureSession(Path.GetFullPath("."), silentCaption: true);
            await native.InitializeAsync();
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            var source = new NativeInstructionSource();
            var snapshot = source.Snapshot!;
            var revisions = new InstructionRevisionCatalogue(f.Paths);
            using var catalogue = new ArtifactCatalogue(revisions.Reconcile(
                [snapshot.Document.Definition with { OwnedInstructionRevision = snapshot.Revision }]));
            revisions.CompleteActivation(snapshot, revisions.PrepareActivation(snapshot), catalogue);
            var adapter = new MemoryAnswerAdapter();
            await using var composition = new Composition(f, adapter: adapter, memoryContext: true,
                instructionFactory: access => new(catalogue, revisions, source, f.Store, access, f.Time));
            var model = composition.Model;
            await PrepareMemoryContext(model);
            model.ComposerBody = "earlier original";
            await model.SubmitConversationAsync();
            await model.ReadConversationAsync();
            model.ComposerBody = "original mixed request";
            var window = new ResponseWindow(native.Main);
            var presentation = new SessionsPresentationOwner(model);
            window.AttachSessions(model, presentation, static () => { }, static () => { });
            using var management = new SessionsManagementView(model, presentation, window);
            window.ShowHostedView(WorkbenchActivity.SessionManagement, management);
            Dispatcher.UIThread.RunJobs();
            var extraWindows = 0;
            using var opened = Window.WindowOpenedEvent.AddClassHandler<ReviewedContextWindow>((_, _) => extraWindows++);
            try
            {
                management.FindControl<Button>("OpenMixedContext")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await management.MixedContextOperation;
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                var view = window.FindControl<ContentControl>("NativeReviewContent")!
                    .GetVisualDescendants().OfType<ReviewedContextView>().Single();
                foreach (var item in model.MixedHistoryChoices) { view.FindControl<ListBox>("MixedHistory")!.SelectedItems!.Add(item); }
                view.FindControl<ListBox>("MixedMemories")!.SelectedItems!.Add(model.MixedMemoryChoices.Single());
                view.FindControl<ComboBox>("MixedInstruction")!.SelectedItem = model.MixedInstructionChoices.Single();
                view.FindControl<Button>("ReviewMixedContext")!.Command!.Execute(null);
                await view.Operation;
                model.CanConfirmMixedContext.Should().BeTrue(model.ConversationStatus);
                model.MixedReviewText.Should().Contain("original mixed request").And.Contain(snapshot.Document.Text);
                var before = adapter.Calls;
                if (!confirm)
                {
                    window.Hide();
                    window.Show();
                    Dispatcher.UIThread.RunJobs();
                    model.CanConfirmMixedContext.Should().BeFalse();
                    model.MixedHistoryChoices.Should().BeEmpty();
                    model.MixedReviewText.Should().BeEmpty();
                }
                await model.ConfirmMixedContextAsync();
                adapter.Calls.Should().Be(before + (confirm ? 1 : 0));
                f.Count("conversation_messages").Should().Be(confirm ? 2 : 1);
                f.Count("reviewed_memory").Should().Be(1);
                f.Count("scoped_grants").Should().Be(0);
                extraWindows.Should().Be(0);
            }
            finally { window.Close(); }
        });
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task WorkbenchModelHistoryAdvisoryRetainsItsOriginalQuestionAndUnsentInput(bool confirm)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var native = new NativeUxFixtureSession(Path.GetFullPath("."), silentCaption: true);
            await native.InitializeAsync();
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            var transport = new HistoryProposalTransport();
            await using var adapter = new BoundedModelTurnAdapter(transport, f.Time, NullLogger<BoundedModelTurnAdapter>.Instance);
            await using var composed = new Composition(f, adapter: adapter);
            var model = composed.Model;
            await model.RefreshAsync();
            await model.SelectAsync(model.Sessions.Single());
            model.ComposerBody = "earlier original source";
            await model.SubmitConversationAsync();
            model.ComposerBody = "original advisory question";
            await model.SubmitConversationAsync();
            await model.ReadConversationAsync();
            model.ComposerBody = "unrelated unsent input";
            model.SelectedAnswerRecord = model.ConversationMessages[^1];
            var window = new ResponseWindow(native.Main);
            var presentation = new SessionsPresentationOwner(model);
            window.AttachSessions(model, presentation, static () => { }, static () => { });
            using var management = new SessionsManagementView(model, presentation, window);
            window.ShowHostedView(WorkbenchActivity.SessionManagement, management);
            Dispatcher.UIThread.RunJobs();
            var extraWindows = 0;
            using var opened = Window.WindowOpenedEvent.AddClassHandler<ModelHistoryContextWindow>((_, _) => extraWindows++);
            try
            {
                management.FindControl<Button>("ReviewModelHistoryContext")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await management.ModelHistoryContextOperation;
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                var view = window.FindControl<ContentControl>("NativeReviewContent")!
                    .GetVisualDescendants().OfType<ModelHistoryContextView>().Single();
                model.ModelHistoryContextQuestion.Should().Contain("original advisory question").And.NotContain("unrelated unsent input");
                view.FindControl<ListBox>("Sources")!.SelectedItems!.Add(model.HistoryContextCandidates[0]);
                view.FindControl<Button>("Review")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await view.Operation;
                model.CanConfirmHistoryContext.Should().BeTrue(model.ConversationStatus);
                if (!confirm)
                {
                    window.Hide();
                    window.Show();
                    model.CanConfirmHistoryContext.Should().BeFalse();
                    model.ModelHistoryContextQuestion.Should().BeEmpty();
                }
                await model.ConfirmHistoryContextAsync();
                transport.Requests.Should().HaveCount(confirm ? 3 : 2);
                model.ComposerBody.Should().Be("unrelated unsent input");
                if (confirm) { model.ConversationUpdates.Should().ContainSingle().Which.Body.Should().Be("original advisory question"); }
                extraWindows.Should().Be(0);
            }
            finally { window.Close(); }
        });
    }
}
