using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

using AwesomeAssertions;

using Kora.Application.Hosting;
using Kora.Core.Context;
using Kora.Core.Storage;
using Kora.NativeUxFixture;
using Kora.Windows.IntegrationTests.Audio;
using Kora.Windows.IntegrationTests.Storage;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

public sealed partial class AllWorkWindowTests
{
    [WindowsFact]
    public async Task FindOnlySearchesDisplayedSelectedMetadataWithCountedNativeKeysAndNoAuthority()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            var question = await WindowsSqliteTaskControlTests.WaitAsync(f);
            var store = new GatedWorkStore(f.Store);
            var access = new WindowsSqliteSessionWorkspaceTests.Access();
            var service = new SessionWorkspaceService(store, new(f.Tasks), access, NullLogger<SessionWorkspaceService>.Instance);
            await WindowsSqliteSessionWorkspaceTests.Service(f, access)
                .CreateAsync(new("Other row private name"), Kora.Core.Hosting.RequestOrigin.LocalUi, f.Token);
            var model = new AllWorkViewModel(service, access, NullLogger<AllWorkViewModel>.Instance,
                (_, _, _) => throw new InvalidOperationException("Find grants no navigation."));
            var window = new AllWorkWindow(model);
            window.Show();
            try
            {
                var query = window.FindControl<TextBox>("DetailsFindQuery")!;
                var match = window.FindControl<Kora.Controls.NamedTextBlock>("DetailsFindMatch")!;
                var details = window.FindControl<Kora.Controls.NamedTextBlock>("SelectedMetadataDetails")!;
                query.IsEnabled.Should().BeFalse();
                await model.RefreshAsync();
                model.Select(model.Records.Single(row => row.Session.Authority.SessionId == f.Request.SessionId));
                var row = model.SelectedRecord;
                var displayed = model.SelectedDetails;
                details.Text.Should().Be(displayed);
                var reads = store.Reads;
                var audits = f.Count("security_audit_events");
                var tasks = f.Count("host_tasks");
                var retention = await f.Store.ReadRetentionAsync(f.Request.SessionId, f.Token);
                window.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.F, KeyModifiers = KeyModifiers.Control });
                query.IsFocused.Should().BeTrue();
                ControlAutomationPeer.CreatePeerForElement(query)!.GetName().Should().Contain("typing performs no read")
                    .And.Contain("256 UTF-16").And.Contain("512 UTF-8");
                ControlAutomationPeer.CreatePeerForElement(match)!.GetName().Should().Contain("not a row selection or action target");
                query.IsUndoEnabled.Should().BeFalse();
                foreach (var hidden in new[] { "Other row private name", question.Spec.Text })
                {
                    displayed.Should().NotContain(hidden);
                    query.Text = hidden;
                    query.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
                    model.FindStatus.Should().Contain("No literal match");
                    match.IsVisible.Should().BeFalse();
                }
                query.Text = " | ";
                var expected = SnapshotLiteralFind.FindCounted(displayed, " | ", SnapshotFindDirection.First);
                query.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
                model.FindStatus.Should().Contain($"selected 1 of {expected.Count}");
                match.IsFocused.Should().BeTrue();
                match.Text.Should().Be(" | ");
                for (var i = 2; i <= expected.Count; i++)
                {
                    match.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.F3 });
                    model.FindStatus.Should().Contain($"selected {i} of {expected.Count}");
                }
                match.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.F3 });
                model.FindStatus.Should().Contain("no wrap").And.Contain("no displayed match");
                match.IsVisible.Should().BeFalse();
                window.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.F3, KeyModifiers = KeyModifiers.Shift });
                model.FindStatus.Should().Contain($"selected {expected.Count - 1} of {expected.Count}");
                model.SelectedRecord.Should().BeSameAs(row);
                model.SelectedDetails.Should().BeSameAs(displayed);
                model.CanNavigate.Should().BeTrue("Find is not the separate exact-session navigation selection");
                var notifications = 0;
                model.PropertyChanged += (_, _) => notifications++;
                for (var i = 0; i < 20; i++)
                {
                    model.SelectedDetails.Should().BeSameAs(displayed);
                    model.FindQuery.Should().Be(" | ");
                    model.FindStatus.Should().NotBeEmpty();
                    model.FindMatch.Should().Be(" | ");
                    model.CanFind.Should().BeTrue();
                    model.CanFindNavigate.Should().BeTrue();
                }
                notifications.Should().Be(0, "binding reads are pure cached values");
                query.Text = "Exact";
                model.FindMatch.Should().BeEmpty();
                model.FindStatus.Should().BeEmpty();
                model.CanFindNavigate.Should().BeFalse();
                query.Focus().Should().BeTrue();
                var transfer = new RoutedEventArgs(TextBox.CopyingToClipboardEvent);
                query.RaiseEvent(transfer);
                transfer.Handled.Should().BeTrue();
                foreach (var key in new[] { Key.C, Key.X, Key.V, Key.Insert })
                {
                    var args = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, KeyModifiers = KeyModifiers.Control };
                    query.RaiseEvent(args);
                    args.Handled.Should().BeTrue();
                }
                query.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
                window.IsVisible.Should().BeTrue();
                model.FindQuery.Should().BeEmpty();
                model.CanFind.Should().BeTrue();
                query.Text = "Exact";
                window.FindControl<Button>("DetailsFindFirst")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                window.FindControl<Button>("DetailsFindClear")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                model.FindQuery.Should().BeEmpty();
                model.FindMatch.Should().BeEmpty();
                model.FindStatus.Should().BeEmpty();
                store.Reads.Should().Be(reads);
                store.Writes.Should().Be(0);
                f.Count("security_audit_events").Should().Be(audits);
                f.Count("host_tasks").Should().Be(tasks);
                (await f.Store.ReadRetentionAsync(f.Request.SessionId, f.Token)).Should().Be(retention);
            }
            finally { window.Close(); }
        });
    }

    [WindowsFact]
    public async Task FindRetiresOnEveryExistingSelectionBoundaryAndFocusRevocationCannotRestoreIt()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            var store = new GatedWorkStore(f.Store);
            var access = new WindowsSqliteSessionWorkspaceTests.Access();
            var service = new SessionWorkspaceService(store, new(f.Tasks), access, NullLogger<SessionWorkspaceService>.Instance);
            var model = new AllWorkViewModel(service, access, NullLogger<AllWorkViewModel>.Instance,
                (_, _, _) => throw new InvalidOperationException("Find grants no navigation."));
            var window = new AllWorkWindow(model);
            window.Show();
            var query = window.FindControl<TextBox>("DetailsFindQuery")!;
            var match = window.FindControl<Kora.Controls.NamedTextBlock>("DetailsFindMatch")!;
            try
            {
                foreach (var retire in new Action[]
                {
                    () => model.Select(null), model.Revoke, model.Cancel,
                    () => model.QueryText = "query", model.ClearQuery,
                    () => model.Filter = SessionListFilter.Active,
                    () => model.NeedsAnswer = true,
                    () => model.Availability = SessionWorkOverviewAvailability.Observed,
                    () => model.Content = SessionWorkOverviewContent.Task,
                    () => model.TaskIdText = Guid.NewGuid().ToString("D"),
                    () => model.RangeInputs[0].Minimum = "1"
                })
                {
                    model.QueryText = string.Empty;
                    model.ClearTaskQuery();
                    model.ClearRanges();
                    model.Filter = SessionListFilter.All;
                    model.NeedsAnswer = false;
                    model.Availability = null;
                    model.Content = SessionWorkOverviewContent.Any;
                    await model.RefreshAsync();
                    model.Select(model.Records.Single());
                    query.Text = "Exact";
                    window.FindControl<Button>("DetailsFindFirst")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    match.IsFocused.Should().BeTrue();
                    retire();
                    AssertRetired();
                }
                model.ClearRanges();
                await model.RefreshAsync();
                model.Select(model.Records.Single());
                query.Text = "Exact";
                window.FocusFindMatch = () => false;
                window.FindControl<Button>("DetailsFindFirst")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                model.FindStatus.Should().Contain("focus is unavailable").And.Contain("no match or focus claimed");
                model.FindMatch.Should().BeEmpty();
                model.CanFindNavigate.Should().BeFalse();
                window.FocusFindMatch = () => { model.Revoke(); return true; };
                window.FindControl<Button>("DetailsFindFirst")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                AssertRetired();
                await model.RefreshAsync();
                model.Select(model.Records.Single());
                query.Text = "Exact";
                var version = model.FindMetadata(SnapshotFindDirection.First);
                access.ControlRevision++;
                model.HasCurrentFindMatch(version).Should().BeFalse();
                AssertRetired();
                model.Select(model.SelectedRecord);
                model.CanFind.Should().BeFalse("a new selection cannot re-admit a page from a retired control epoch");
                await model.RefreshAsync();
                model.Select(model.Records.Single());
                query.Text = "Exact";
                store.Fail = true;
                await model.RefreshAsync();
                model.Select(model.Records.Single());
                model.SelectedDetails.Should().Contain("Current work unavailable").And.NotContain("Observed question");
                query.Text = "Current work unavailable";
                window.FocusFindMatch = null;
                window.FindControl<Button>("DetailsFindFirst")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                model.FindMatch.Should().Be("Current work unavailable");
                query.Text = "Durable task";
                window.FindControl<Button>("DetailsFindFirst")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                model.FindMatch.Should().BeEmpty();
                model.FindStatus.Should().Contain("No literal match");
                query.Text = "Exact";
                Dispatcher.UIThread.Post(() => window.FindControl<Button>("DetailsFindFirst")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)));
                window.Close();
                Dispatcher.UIThread.RunJobs();
                AssertRetired();
                model.EditFind("retired");
                model.FindMetadata(SnapshotFindDirection.First);
                model.ClearFind();
                model.Dispose();
                model.Dispose();
                AssertRetired();
                store.Writes.Should().Be(0);
            }
            finally { window.Close(); }

            void AssertRetired()
            {
                model.FindQuery.Should().BeEmpty();
                model.FindMatch.Should().BeEmpty();
                model.FindStatus.Should().BeEmpty();
                model.CanFind.Should().BeFalse();
                model.CanFindNavigate.Should().BeFalse();
                query.Text.Should().BeEmpty();
                match.IsVisible.Should().BeFalse();
                match.IsFocused.Should().BeFalse();
            }
        });
    }

    [WindowsFact]
    public async Task ReentrantEditsSelectionAndLateReadCallbacksCannotPublishOrFocusRetiredMetadata()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            var store = new GatedWorkStore(f.Store);
            var access = new WindowsSqliteSessionWorkspaceTests.Access();
            var service = new SessionWorkspaceService(store, new(f.Tasks), access, NullLogger<SessionWorkspaceService>.Instance);
            var model = new AllWorkViewModel(service, access, NullLogger<AllWorkViewModel>.Instance,
                (_, _, _) => throw new InvalidOperationException("Find grants no navigation."));
            var window = new AllWorkWindow(model);
            window.Show();
            try
            {
                await model.RefreshAsync();
                model.Select(model.Records.Single());
                model.EditFind("Exact");
                window.FocusFindQuery = () => false;
                window.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.F, KeyModifiers = KeyModifiers.Control });
                model.FindStatus.Should().Contain("query focus is unavailable");
                model.FindQuery.Should().BeEmpty();
                window.FocusFindQuery = null;
                model.EditFind("Exact");
                var focusAttempts = 0;
                window.FocusFindMatch = () =>
                {
                    focusAttempts++;
                    model.EditFind("edited during native focus");
                    return true;
                };
                window.FindControl<Button>("DetailsFindFirst")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                focusAttempts.Should().Be(1);
                model.FindQuery.Should().Be("edited during native focus");
                model.FindMatch.Should().BeEmpty();
                model.FindStatus.Should().BeEmpty();
                window.FindControl<Kora.Controls.NamedTextBlock>("DetailsFindMatch")!.IsFocused.Should().BeFalse();
                model.EditFind("Exact");
                var reenter = true;
                model.PropertyChanged += (_, args) =>
                {
                    if (!reenter || !string.Equals(args.PropertyName, nameof(AllWorkViewModel.FindMatch), StringComparison.Ordinal)
                        || model.FindMatch.Length == 0) { return; }
                    reenter = false;
                    model.Select(null);
                };
                window.FindControl<Button>("DetailsFindFirst")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                focusAttempts.Should().Be(1, "notification reentrancy revokes the old match before focus");
                model.FindQuery.Should().BeEmpty();
                model.FindStatus.Should().BeEmpty();
                model.CanFind.Should().BeFalse();
                model.Select(model.Records.Single());
                model.EditFind("Exact");
                var version = model.FindMetadata(SnapshotFindDirection.First);
                access.CanInspect = false;
                model.ValidateFind().Should().BeFalse();
                model.HasCurrentFindMatch(version).Should().BeFalse();
                model.FindQuery.Should().BeEmpty();
                access.CanInspect = true;
                model.ValidateFind().Should().BeFalse("restoring access never revives a retired capture");
                await model.RefreshAsync();
                model.Select(model.Records.Single());
                model.EditFind("Exact");
                var details = window.FindControl<Kora.Controls.NamedTextBlock>("SelectedMetadataDetails")!;
                details.Text = "Changed native projection";
                window.FindControl<Button>("DetailsFindFirst")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                model.CanFind.Should().BeFalse("Find cannot scan an old projection while different text is displayed");
                model.FindQuery.Should().BeEmpty();
                model.Select(model.Records.Single());
                details.Text = model.SelectedDetails;
                model.EditFind("Exact");
                store.Hold = true;
                var pending = model.RefreshAsync();
                await store.Entered.Task.WaitAsync(f.Token);
                model.CanFind.Should().BeFalse();
                model.FindQuery.Should().BeEmpty();
                model.Cancel();
                store.Release.TrySetResult();
                await pending;
                model.SelectedRecord.Should().BeNull();
                model.Records.Should().BeEmpty();
                model.FindStatus.Should().BeEmpty();
                store.Writes.Should().Be(0);
            }
            finally { window.Close(); }
        });
    }
}
