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
    private static readonly string[] MetadataControlNames =
        ["HistoryKind", "HistoryTaskState", "HistoryDecision", "HistoryQuestionStatus", "HistoryAnswerChannel"];
    private static readonly string[] RangeControlNames =
        ["HistoryMinimumSequence", "HistoryMaximumSequence", "HistoryMinimumGeneration", "HistoryMaximumGeneration",
            "HistoryMinimumSourceRevision", "HistoryMaximumSourceRevision", "HistoryMinimumAuditSequence", "HistoryMaximumAuditSequence"];
    private static readonly string[] CorrelationControlNames = ["HistoryRequestId", "HistoryTaskId", "HistorySourceId"];

    [WindowsFact]
    public async Task NativeRetainedCorrelationFieldsAreAccessibleStrictAndRetireResultsWithoutRetargeting()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            var question = await WindowsSqliteTaskControlTests.WaitAsync(f);
            var access = new WindowsSqliteSessionWorkspaceTests.Access();
            var sink = new WindowsSqliteEvidenceSink(f.Paths);
            sink.Initialize();
            var model = new SessionsViewModel(WindowsSqliteSessionWorkspaceTests.Service(f, access),
                new(new WindowsSqliteEvidenceReader(sink), access, f.Time, NullLogger<DurableEvidenceQuery>.Instance),
                access, NullLogger<SessionsViewModel>.Instance, openHistoryDetail: _ => "Passive receipt.");
            await model.RefreshAsync();
            await model.SelectAsync(model.Sessions.Single());
            var window = new SessionsWindow(model);
            window.Show();
            try
            {
                var clock = await f.Store.ReadRetentionAsync(f.Request.SessionId, f.Token);
                var audits = f.Count("security_audit_events");
                var tasks = f.Count("host_task_events");
                foreach (var name in CorrelationControlNames)
                {
                    await model.RefreshAsync();
                    await model.SelectAsync(model.Sessions.Single());
                    var field = window.FindControl<TextBox>(name)!;
                    var label = ControlAutomationPeer.CreatePeerForElement(field)!.GetName();
                    label.Should().Contain("Exact retained receipt").And.Contain("Any")
                        .And.Contain("canonical lowercase D GUID").And.Contain("historical correlation only");
                    field.Text.Should().BeEmpty();
                    field.Focus().Should().BeTrue();
                    model.HistoryQuery = "question task";
                    await model.SearchHistoryAsync();
                    model.SelectHistoryRecord(model.HistoryRecords[0]);
                    model.CanOpenHistoryDetail.Should().BeTrue();
                    field.Text = " ";
                    model.HistoryRecords.Should().BeEmpty();
                    model.SelectedHistoryRecord.Should().BeNull();
                    model.CanNextHistorySearch.Should().BeFalse();
                    model.CanOpenHistoryDetail.Should().BeFalse();
                    model.Detail.Should().BeEmpty();
                    await model.SearchHistoryAsync();
                    model.Status.Should().Contain("canonical lowercase");
                    field.IsFocused.Should().BeTrue();
                    field.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
                    foreach (var controlName in CorrelationControlNames)
                    { window.FindControl<TextBox>(controlName)!.Text.Should().BeEmpty(); }
                    model.HistoryQuery.Should().BeEmpty();
                }
                await model.RefreshAsync();
                await model.SelectAsync(model.Sessions.Single());
                model.HistoryQuery = "question task";
                model.HistoryRequestId = f.Request.RequestId.Value.ToString("D");
                model.HistoryTaskId = f.Request.TaskId.Value.ToString("D");
                await model.SearchHistoryAsync();
                model.HistoryRecords.Should().NotBeEmpty().And.OnlyContain(x =>
                    x.RequestId == f.Request.RequestId.Value && x.TaskId == f.Request.TaskId.Value);
                var receipt = model.HistoryRecords.First(x => x.SourceId is not null);
                model.HistorySourceId = receipt.SourceId!.Value.ToString("D");
                await model.SearchHistoryAsync();
                model.HistoryRecords.Should().NotBeEmpty().And.OnlyContain(x => x.SourceId == receipt.SourceId);
                model.PendingQuestions.Should().ContainSingle().Which.Key.Should().Be(question.Key);
                model.HistorySessionId.Should().Be(f.Request.SessionId.Value.ToString("D"));
                (await f.Store.ReadRetentionAsync(f.Request.SessionId, f.Token)).Should().Be(clock);
                f.Count("security_audit_events").Should().Be(audits);
                f.Count("host_task_events").Should().Be(tasks);
                window.FindControl<Button>("ClearHistorySearch")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                foreach (var name in CorrelationControlNames) { window.FindControl<TextBox>(name)!.Text.Should().BeEmpty(); }
                model.HistoryRecords.Should().BeEmpty();
            }
            finally { window.Close(); }
        });
    }

    [WindowsFact]
    public async Task Native_Any_controls_bind_exact_metadata_without_reads_and_retire_receipts_not_pending_targets()
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
            var model = new SessionsViewModel(service,
                new(new WindowsSqliteEvidenceReader(sink), access, f.Time, NullLogger<DurableEvidenceQuery>.Instance),
                access, NullLogger<SessionsViewModel>.Instance, openHistoryDetail: _ => "Passive receipt opened.");
            await model.RefreshAsync();
            await model.SelectAsync(model.Sessions.Single());
            var window = new SessionsWindow(model);
            window.Show();
            try
            {
                foreach (var name in MetadataControlNames)
                {
                    var control = window.FindControl<ComboBox>(name)!;
                    control.SelectedItem!.ToString().Should().Be("Any");
                    ControlAutomationPeer.CreatePeerForElement(control)!.GetName().Should().Contain("Exact").And.Contain("Any");
                    control.Focus().Should().BeTrue();
                }
                foreach (var name in RangeControlNames)
                {
                    var control = window.FindControl<TextBox>(name)!;
                    control.Text.Should().BeEmpty();
                    ControlAutomationPeer.CreatePeerForElement(control)!.GetName().Should().Contain("inclusive").And.Contain("Any");
                    control.Focus().Should().BeTrue();
                    control.Text = "1";
                }
                model.HistoryMinimumSequence.Should().Be("1");
                model.HistoryMaximumSequence.Should().Be("1");
                model.HistoryMinimumGeneration.Should().Be("1");
                model.HistoryMaximumGeneration.Should().Be("1");
                model.HistoryMinimumSourceRevision.Should().Be("1");
                model.HistoryMaximumSourceRevision.Should().Be("1");
                model.HistoryMinimumAuditSequence.Should().Be("1");
                model.HistoryMaximumAuditSequence.Should().Be("1");
                window.FindControl<TextBox>("HistoryMaximumAuditSequence")!.RaiseEvent(new KeyEventArgs
                    { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
                foreach (var name in RangeControlNames) { window.FindControl<TextBox>(name)!.Text.Should().BeEmpty(); }
                model.HistoryQuery = "question task";
                await model.SearchHistoryAsync();
                model.SelectHistoryRecord(model.HistoryRecords[0]);
                model.CanOpenHistoryDetail.Should().BeTrue();
                window.FindControl<TextBox>("HistoryMinimumSequence")!.Text = " ";
                model.HistoryRecords.Should().BeEmpty();
                model.SelectedHistoryRecord.Should().BeNull();
                model.Detail.Should().BeEmpty();
                model.CanOpenHistoryDetail.Should().BeFalse();
                await model.SearchHistoryAsync();
                model.Status.Should().Contain("decimal");
                model.HistoryRecords.Should().BeEmpty();
                await model.RefreshAsync();
                await model.SelectAsync(model.Sessions.Single());
                model.HistoryQuery = "question task";
                window.FindControl<TextBox>("HistoryMinimumSequence")!.Text = "1";
                await model.SearchHistoryAsync();
                model.HistoryRecords.Should().NotBeEmpty();
                window.FindControl<ComboBox>("HistoryKind")!.SelectedItem = model.HistoryKinds.Single(x => x.Value == SessionHistoryKind.Question);
                model.HistoryKindChoice.Value.Should().Be(SessionHistoryKind.Question);
                model.HistoryRecords.Should().BeEmpty();
                model.SelectedHistoryRecord.Should().BeNull();
                model.Detail.Should().BeEmpty();
                model.CanNextHistorySearch.Should().BeFalse();
                model.CanOpenHistoryDetail.Should().BeFalse();
                window.FindControl<ComboBox>("HistoryQuestionStatus")!.SelectedItem =
                    model.HistoryQuestionStatuses.Single(x => x.Value == QuestionStatus.Pending);
                model.Detail.Should().BeEmpty();
                var clock = await f.Store.ReadRetentionAsync(f.Request.SessionId, f.Token);
                var audits = f.Count("security_audit_events");
                var tasks = f.Count("host_task_events");
                await model.SearchHistoryAsync();
                model.HistoryRecords.Should().NotBeEmpty().And.OnlyContain(x =>
                    x.Kind == SessionHistoryKind.Question && x.QuestionStatus == QuestionStatus.Pending);
                window.FindControl<ComboBox>("HistoryTaskState")!.SelectedItem =
                    model.HistoryTaskStates.Single(x => x.Value == HostTaskState.Succeeded);
                window.FindControl<ComboBox>("HistoryDecision")!.SelectedItem =
                    model.HistoryDecisions.Single(x => x.Value == HostInteractionOutcome.Approved);
                window.FindControl<ComboBox>("HistoryAnswerChannel")!.SelectedItem =
                    model.HistoryAnswerChannels.Single(x => x.Value == RequestOrigin.LocalUi);
                model.HistoryTaskStateChoice.Value.Should().Be(HostTaskState.Succeeded);
                model.HistoryDecisionChoice.Value.Should().Be(HostInteractionOutcome.Approved);
                model.HistoryAnswerChannelChoice.Value.Should().Be(RequestOrigin.LocalUi);
                await model.SearchHistoryAsync();
                model.HistoryRecords.Should().BeEmpty();
                model.Status.Should().StartWith("Passive lexical search: 0 matches");
                model.PendingQuestions.Should().ContainSingle().Which.Key.Should().Be(question.Key);
                (await f.Store.ReadRetentionAsync(f.Request.SessionId, f.Token)).Should().Be(clock);
                f.Count("security_audit_events").Should().Be(audits);
                f.Count("host_task_events").Should().Be(tasks);
                window.FindControl<Button>("ClearHistorySearch")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                foreach (var name in MetadataControlNames) { window.FindControl<ComboBox>(name)!.SelectedItem!.ToString().Should().Be("Any"); }
                model.HistoryQuery.Should().BeEmpty();
                model.HistoryRecords.Should().BeEmpty();
                window.FindControl<TextBox>("HistoryQuery")!.IsFocused.Should().BeTrue();
                model.HistoryKindChoice = new(SessionHistoryKind.Answer);
                window.FindControl<TextBox>("HistoryQuery")!.RaiseEvent(new KeyEventArgs
                    { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
                model.HistoryKindChoice.Value.Should().BeNull();
                var invalid = () => model.HistoryKindChoice = new((SessionHistoryKind)999);
                invalid.Should().Throw<ArgumentOutOfRangeException>();
                model.HistoryQuery = "question";
                model.HistoryKindChoice = new(SessionHistoryKind.Question);
                access.ControlRevision++;
                model.NameDraft = "refresh presentation";
                model.HistoryKindChoice.Value.Should().BeNull();
                model.HistoryQuery.Should().BeEmpty();
                model.Detail.Should().BeEmpty();
            }
            finally { window.Close(); }
            model.HistoryKindChoice = new(SessionHistoryKind.Question);
            model.HistoryQuery = "closed";
            model.HistoryKindChoice.Value.Should().BeNull();
            model.HistoryQuery.Should().BeEmpty();
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
