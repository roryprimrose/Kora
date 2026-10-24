using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

using AwesomeAssertions;

using Kora.Application.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.Core.Presentation;
using Kora.Core.Storage;
using Kora.Windows.IntegrationTests.Audio;
using Kora.Windows.IntegrationTests.Storage;
using Kora.Windows.Storage;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed class SessionsHistorySearchTests
{
    private static readonly string[] OrdinaryReceiptFilterNames =
        ["HistoryKind", "HistoryTaskState", "HistoryDecision", "HistoryQuestionStatus", "HistoryAnswerChannel"];

    [Fact]
    public async Task OrdinaryReceiptChoicesComposeWithWordsAndClearWithoutChangingWorkOrActivity()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            var question = await WindowsSqliteTaskControlTests.WaitAsync(f);
            await f.RunAsync(() => f.Questions.SubmitAsync(question.Key, new(["show"]), RequestOrigin.LocalUi, f.Token));
            await WindowsSqliteSessionWorkspaceTests.FinishAsync(f, HostTaskState.Succeeded);
            var access = new WindowsSqliteSessionWorkspaceTests.Access();
            var service = WindowsSqliteSessionWorkspaceTests.Service(f, access);
            var sink = new WindowsSqliteEvidenceSink(f.Paths);
            sink.Initialize();
            var model = new SessionsViewModel(service,
                new(new WindowsSqliteEvidenceReader(sink), access, f.Time, NullLogger<DurableEvidenceQuery>.Instance),
                access, NullLogger<SessionsViewModel>.Instance);
            var window = new SessionsWindow(model);
            window.Show();
            try
            {
                await model.RefreshAsync();
                await model.SelectAsync(model.Sessions.Single());
                var retention = await f.Store.ReadRetentionAsync(f.Request.SessionId, f.Token);
                var audit = WindowsSqliteConversationTests.AuditText(f);
                foreach (var name in OrdinaryReceiptFilterNames)
                {
                    var control = window.FindControl<ComboBox>(name)!;
                    control.Items[0]!.ToString().Should().Be("Any");
                    ControlAutomationPeer.CreatePeerForElement(control)!.GetName().Should().Contain("Any").And.Contain("editing does not read");
                    control.Focus().Should().BeTrue();
                }
                model.HistoryQuery = "question task show answered succeeded";
                window.FindControl<ComboBox>("HistoryKind")!.SelectedItem =
                    model.HistoryKinds.Single(option => option.Value == SessionHistoryKind.Answer);
                window.FindControl<ComboBox>("HistoryQuestionStatus")!.SelectedItem =
                    model.HistoryQuestionStatuses.Single(option => option.Value == QuestionStatus.Answered);
                window.FindControl<ComboBox>("HistoryAnswerChannel")!.SelectedItem =
                    model.HistoryAnswerChannels.Single(option => option.Value == RequestOrigin.LocalUi);
                model.HistoryRecords.Should().BeEmpty("predicate edits require explicit Search");
                await model.SearchHistoryAsync();
                model.HistoryRecords.Should().ContainSingle().Which.Kind.Should().Be(SessionHistoryKind.Answer);
                model.HistoryRecords[0].AnswerChannel.Should().Be(RequestOrigin.LocalUi);
                model.HistoryAnswerChannelChoice = model.HistoryAnswerChannels.Single(option => option.Value == RequestOrigin.ActivatedVoice);
                model.HistoryRecords.Should().BeEmpty();
                await model.SearchHistoryAsync();
                model.HistoryRecords.Should().BeEmpty("the answer was committed on LocalUi, not voice");
                model.ClearHistorySearchInputs();
                model.HistoryQuery = "succeeded";
                model.HistoryKindChoice = model.HistoryKinds.Single(option => option.Value == SessionHistoryKind.Task);
                window.FindControl<ComboBox>("HistoryTaskState")!.SelectedItem =
                    model.HistoryTaskStates.Single(option => option.Value == HostTaskState.Succeeded);
                await model.SearchHistoryAsync();
                model.HistoryRecords.Should().NotBeEmpty().And.OnlyContain(record => record.TaskState == HostTaskState.Succeeded);
                model.ClearHistorySearchInputs();
                model.HistoryQuery = "answered";
                model.HistoryKindChoice = model.HistoryKinds.Single(option => option.Value == SessionHistoryKind.Decision);
                window.FindControl<ComboBox>("HistoryDecision")!.SelectedItem =
                    model.HistoryDecisions.Single(option => option.Value == HostInteractionOutcome.Answered);
                await model.SearchHistoryAsync();
                model.HistoryRecords.Should().ContainSingle().Which.Decision.Should().Be(HostInteractionOutcome.Answered);
                window.FindControl<Button>("ClearHistorySearch")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                model.HistoryQuery.Should().BeEmpty();
                model.HistoryRecords.Should().BeEmpty();
                model.HistoryKindChoice.Value.Should().BeNull();
                model.HistoryTaskStateChoice.Value.Should().BeNull();
                model.HistoryDecisionChoice.Value.Should().BeNull();
                model.HistoryQuestionStatusChoice.Value.Should().BeNull();
                model.HistoryAnswerChannelChoice.Value.Should().BeNull();
                (await f.Store.ReadRetentionAsync(f.Request.SessionId, f.Token)).Should().Be(retention);
                WindowsSqliteConversationTests.AuditText(f).Should().Be(audit);
            }
            finally { window.ClosePrivate(); }
        });
    }

    [WindowsFact]
    public async Task Native_search_keyboard_recovery_and_exact_detail_preserve_focus_and_pending_work()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            var question = await WindowsSqliteTaskControlTests.WaitAsync(f);
            var access = new WindowsSqliteSessionWorkspaceTests.Access();
            var service = WindowsSqliteSessionWorkspaceTests.Service(f, access);
            var sink = new WindowsSqliteEvidenceSink(f.Paths);
            sink.Initialize();
            AdmittedDetailContent? opened = null;
            var model = new SessionsViewModel(service,
                new(new WindowsSqliteEvidenceReader(sink), access, f.Time, NullLogger<DurableEvidenceQuery>.Instance),
                access, NullLogger<SessionsViewModel>.Instance, openHistoryDetail: content =>
                { opened = content; return "Exact search receipt opened."; });
            await model.RefreshAsync();
            await model.SelectAsync(model.Sessions.Single());
            var window = new SessionsWindow(model);
            window.Show();
            try
            {
                var field = window.FindControl<TextBox>("HistoryQuery")!;
                field.Focus().Should().BeTrue();
                model.HistoryQuery = "question task";
                model.CanSearchHistory.Should().BeTrue();
                var clock = await f.Store.ReadRetentionAsync(f.Request.SessionId, f.Token);
                var searched = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                void OnChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
                {
                    if (args.PropertyName is nameof(SessionsViewModel.Status)
                        && model.Status.StartsWith("Passive lexical search:", StringComparison.Ordinal)) { searched.TrySetResult(); }
                }
                model.PropertyChanged += OnChanged;
                field.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
                await searched.Task.WaitAsync(f.Token);
                model.PropertyChanged -= OnChanged;
                window.FocusManager!.GetFocusedElement().Should().BeSameAs(field);
                model.HistoryRecords.Should().NotBeEmpty();
                model.SelectHistoryRecord(model.HistoryRecords[0]);
                await model.OpenHistoryDetailAsync();
                opened!.Reference.ItemId.Value.Should().Be(model.SelectedHistoryRecord!.Id);
                model.PendingQuestions.Should().ContainSingle().Which.Key.Should().Be(question.Key);
                (await f.Store.ReadRetentionAsync(f.Request.SessionId, f.Token)).Should().Be(clock);
                ControlAutomationPeer.CreatePeerForElement(field)!.GetName().Should().Contain("whole-word Unicode");
                field.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
                model.HistoryQuery.Should().BeEmpty();
                model.HistoryRecords.Should().BeEmpty();
                model.Detail.Should().BeEmpty();
                window.IsVisible.Should().BeTrue();
                field.IsFocused.Should().BeTrue();
                model.HistoryQuery = "%%%";
                model.CanSearchHistory.Should().BeFalse();
                model.HistoryQuery = "task";
                await model.SearchHistoryAsync();
                window.FindControl<Button>("ClearHistorySearch")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                model.HistoryQuery.Should().BeEmpty();
                model.HistoryRecords.Should().BeEmpty();
                field.IsFocused.Should().BeTrue();
                model.HistoryQuery = "task";
                access.CanInspect = false;
                await model.SearchHistoryAsync();
                model.HistoryQuery.Should().BeEmpty();
                model.HistoryRecords.Should().BeEmpty();
                model.Detail.Should().BeEmpty();
            }
            finally { window.Close(); }
            model.HistoryQuery.Should().BeEmpty();
        });
    }
}
