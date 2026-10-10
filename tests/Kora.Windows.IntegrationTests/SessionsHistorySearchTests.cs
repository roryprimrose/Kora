using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

using AwesomeAssertions;

using Kora.Application.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.Core.Presentation;
using Kora.Windows.IntegrationTests.Audio;
using Kora.Windows.IntegrationTests.Storage;
using Kora.Windows.Storage;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed class SessionsHistorySearchTests
{
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
