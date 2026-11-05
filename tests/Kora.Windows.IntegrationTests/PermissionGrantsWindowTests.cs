using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

using AwesomeAssertions;

using Kora.Application.Configuration;
using Kora.Application.Infrastructure;
using Kora.Application.Interaction;
using Kora.Controls;
using Kora.Core.Auditing;
using Kora.Core.Authorization;
using Kora.Core.Network;
using Kora.Windows.IntegrationTests.Audio;
using Kora.Windows.IntegrationTests.Storage;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed class PermissionGrantsWindowTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task KeyboardReviewReadsCompleteExactMetadataBeforeSeparateRevocation(bool batch)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            for (var index = 0; index < 3; index++)
            {
                if (index > 0)
                {
                    f.Request = InteractionStorageFixture.NewRequest(f.Request.SessionId);
                    await f.AdmitAsync(newSession: false);
                }
                await f.GrantAsync("perpetual");
            }
            await using var control = new ExactGrantControlAdmission(f.Store, f.Store, new(f.Tasks), f.Authorization);
            using var model = new ExactGrantsViewModel(f.Store, control, new WindowsSqliteSessionWorkspaceTests.Access(),
                () => true, NullLogger<ExactGrantsViewModel>.Instance);
            var window = new ExactGrantsWindow(model);
            window.Show();
            try
            {
                await model.RefreshAsync(f.Token);
                window.UpdateLayout();
                var before = model.Records.Select(record => record.Grant).ToArray();
                var audit = f.Count("security_audit_events");
                var list = window.FindControl<ScrollableListBox>("PermissionInventory")!;
                list.SelectedIndex = 0;
                list.BringIntoView();
                window.UpdateLayout();
                list.ContainerFromIndex(0)!.Focus().Should().BeTrue();
                Press(window, Key.End);
                if (batch) { Press(window, Key.Up, RawInputModifiers.Shift); }
                var selected = list.SelectedItems!.OfType<PermissionGrantRow>()
                    .Select(row => ((ExactOperationPermissionGrant)row.Grant).Inspection.Grant.Id).ToArray();
                selected.Should().HaveCount(batch ? 2 : 1);
                var reviewCommand = batch ? model.PreviewBatchCommand : model.InspectCommand;
                var review = window.GetVisualDescendants().OfType<Button>().Single(button => ReferenceEquals(button.Command, reviewCommand));
                TabTo(window, review, backwards: true);
                await ActivateAsync(window, model, f.Token);
                window.UpdateLayout();
                var label = batch ? "Complete exact selected batch preview" : "Current exact grant inspection";
                var reader = window.GetVisualDescendants().OfType<Control>()
                    .Single(item => string.Equals(AutomationProperties.GetName(item), label, StringComparison.Ordinal));
                TabTo(window, reader);
                var text = reader.Should().BeOfType<TextBox>().Which;
                var value = ControlAutomationPeer.CreatePeerForElement(text)!.GetProvider<IValueProvider>()!;
                value.IsReadOnly.Should().BeTrue();
                value.Value.Should().Be(batch ? model.BatchInspection : model.Inspection);
                var reviewedText = value.Value;
                foreach (var id in selected) { value.Value.Should().Contain(id.Value.ToString("D")); }
                value.Value.Should().EndWith(ExactGrantsViewModel.Disclosure);
                Press(window, Key.A, RawInputModifiers.Control);
                text.SelectedText.Should().Be(text.Text);
                window.KeyTextInput("must not edit");
                text.Text.Should().Be(reviewedText);
                foreach (var transfer in new[] { TextBox.CopyingToClipboardEvent, TextBox.CuttingToClipboardEvent, TextBox.PastingFromClipboardEvent })
                {
                    var args = new RoutedEventArgs(transfer);
                    text.RaiseEvent(args);
                    args.Handled.Should().BeTrue();
                }
                foreach (var key in new[] { Key.C, Key.X, Key.V, Key.Insert })
                {
                    var args = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, KeyModifiers = KeyModifiers.Control };
                    text.RaiseEvent(args);
                    args.Handled.Should().BeTrue();
                }
                var context = new ContextRequestedEventArgs();
                text.RaiseEvent(context);
                context.Handled.Should().BeTrue();
                text.Text.Should().Be(reviewedText);
                Press(window, Key.End, RawInputModifiers.Control);
                window.UpdateLayout();
                text.CaretIndex.Should().Be(text.Text!.Length);
                text.GetVisualDescendants().OfType<ScrollViewer>().First().Offset.Y.Should().BeGreaterThan(0);
                f.Count("security_audit_events").Should().Be(audit);
                (await f.Store.ReadGrantsAsync(f.Token)).Should().BeEquivalentTo(before);
                model.CanRevoke.Should().BeFalse();
                model.CanRevokeBatch.Should().BeFalse();
                var confirmation = window.GetVisualDescendants().OfType<CheckBox>().Single(item =>
                    string.Equals(AutomationProperties.GetName(item), batch
                        ? "Separate explicit confirmation of the complete exact selected batch"
                        : "Explicit confirmation of the displayed exact approval ID and revision", StringComparison.Ordinal));
                TabTo(window, confirmation);
                Press(window, Key.Space);
                confirmation.IsChecked.Should().BeTrue();
                var revokeCommand = batch ? model.RevokeBatchCommand : model.RevokeCommand;
                var revoke = window.GetVisualDescendants().OfType<Button>().Single(button => ReferenceEquals(button.Command, revokeCommand));
                TabTo(window, revoke);
                await ActivateAsync(window, model, f.Token);
                model.Message.Should().Contain("committed and read back");
                foreach (var grant in before)
                {
                    var current = (await f.Store.InspectExactGrantAsync(grant.Id, f.Token))!.Grant;
                    current.Should().Be(selected.Contains(grant.Id) ? grant.Revoke("explicit-user-revocation") : grant);
                }
                revokeCommand.CanExecute(null).Should().BeFalse();
            }
            finally { window.Close(); }
        });
    }

    [Theory]
    [InlineData(false, "selection")]
    [InlineData(true, "selection")]
    [InlineData(false, "filter")]
    [InlineData(true, "filter")]
    [InlineData(false, "group")]
    [InlineData(true, "group")]
    [InlineData(false, "authority")]
    [InlineData(true, "authority")]
    [InlineData(false, "privacy")]
    [InlineData(true, "privacy")]
    [InlineData(false, "hide")]
    [InlineData(true, "hide")]
    [InlineData(false, "dispose")]
    [InlineData(true, "dispose")]
    [InlineData(false, "close")]
    [InlineData(true, "close")]
    public async Task RetiringReviewRemovesNativeTextSelectionAndAccessibleValueWithoutRevocation(bool batch, string retirement)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            var grant = await f.GrantAsync("perpetual");
            var access = new WindowsSqliteSessionWorkspaceTests.Access();
            await using var control = new ExactGrantControlAdmission(f.Store, f.Store, new(f.Tasks), f.Authorization);
            ExactGrantsWindow? window = null;
            using var model = new ExactGrantsViewModel(f.Store, control, access,
                () => window?.IsVisible == true, NullLogger<ExactGrantsViewModel>.Instance);
            window = new ExactGrantsWindow(model);
            window.Show();
            try
            {
                await model.RefreshAsync(f.Token);
                var list = window.FindControl<ScrollableListBox>("PermissionInventory")!;
                list.SelectedIndex = 0;
                if (batch) { await model.PreviewBatchAsync(f.Token); }
                else { await model.InspectAsync(f.Token); }
                window.UpdateLayout();
                var text = window.FindControl<TextBox>(batch ? "ExactBatchInspection" : "ExactInspection")!;
                var value = ControlAutomationPeer.CreatePeerForElement(text)!.GetProvider<IValueProvider>()!;
                value.Value.Should().Contain(grant.Id.Value.ToString("D"));
                text.Focus();
                Press(window, Key.A, RawInputModifiers.Control);
                text.SelectedText.Should().Be(text.Text);
                var audit = f.Count("security_audit_events");
                if (batch) { model.BatchReviewed = true; model.CanRevokeBatch.Should().BeTrue(); }
                else { model.Reviewed = true; model.CanRevoke.Should().BeTrue(); }
                switch (retirement)
                {
                    case "selection": list.SelectedItem = null; break;
                    case "filter": model.ActionIdFilter = "other.action"; break;
                    case "group": model.Grouping = "Artefact type"; break;
                    case "authority": access.ControlRevision++; model.SynchronizeAuthority(); break;
                    case "privacy": access.CanInspect = false; model.SynchronizeAuthority(); break;
                    case "hide": window.Hide(); model.SynchronizeAuthority(); break;
                    case "dispose": model.Dispose(); break;
                    case "close": window.Close(); break;
                    default: throw new InvalidOperationException("Unknown test retirement.");
                }
                window.UpdateLayout();
                text.Text.Should().NotContain(grant.Id.Value.ToString("D"));
                text.SelectedText.Should().BeEmpty();
                text.CaretIndex.Should().Be(0);
                value.Value.Should().NotContain(grant.Id.Value.ToString("D"));
                model.CanRevoke.Should().BeFalse();
                model.CanRevokeBatch.Should().BeFalse();
                f.Count("security_audit_events").Should().Be(audit);
                (await f.Store.InspectExactGrantAsync(grant.Id, f.Token))!.Grant.Should().Be(grant);
            }
            finally { window.Close(); }
        });
    }

    private static void Press(Window window, Key key, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        window.KeyPress(key, modifiers, PhysicalKey.None, null);
        window.KeyRelease(key, modifiers, PhysicalKey.None, null);
    }

    private static void TabTo(Window window, Control target, bool backwards = false)
    {
        for (var index = 0; index < 100 && !ReferenceEquals(window.FocusManager!.GetFocusedElement(), target); index++)
        {
            Press(window, Key.Tab, backwards ? RawInputModifiers.Shift : RawInputModifiers.None);
        }
        window.FocusManager!.GetFocusedElement().Should().BeSameAs(target);
    }

    private static async Task ActivateAsync(Window window, ExactGrantsViewModel model, CancellationToken token)
    {
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Changed(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
        {
            if (string.Equals(args.PropertyName, nameof(model.Busy), StringComparison.Ordinal) && !model.Busy)
            { completed.TrySetResult(); }
        }
        model.PropertyChanged += Changed;
        try
        {
            Press(window, Key.Space);
            await completed.Task.WaitAsync(TimeSpan.FromSeconds(10), token);
            await Dispatcher.UIThread.InvokeAsync(() => { });
        }
        finally { model.PropertyChanged -= Changed; }
    }

    [WindowsFact]
    public async Task NativeSharedInventoryGroupsPassivelyAndRoutesUriEditsWithoutExactGrantAuthority()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            var grant = await f.GrantAsync("perpetual");
            var preferences = new Preferences { Settings = PreapprovedUriSettings.Create(["https://example.com/*"]) };
            var policy = new PreapprovedUriConfigurationService(preferences, new Audit());
            await using var control = new ExactGrantControlAdmission(f.Store, f.Store, new(f.Tasks), f.Authorization);
            using var model = new ExactGrantsViewModel(f.Store, control, new WindowsSqliteSessionWorkspaceTests.Access(),
                () => true, NullLogger<ExactGrantsViewModel>.Instance, new(f.Store, policy), () => true);
            var window = new ExactGrantsWindow(model);
            window.Show();
            try
            {
                await model.RefreshAsync(f.Token);
                var list = window.FindControl<ScrollableListBox>("PermissionInventory")!;
                list.ItemCount.Should().Be(2);
                var uriRow = model.PermissionRows.Single(row => row.Grant is UriPermissionGrant);
                list.SelectedItem = uriRow;
                model.SelectedPermission.Should().BeSameAs(uriRow);
                model.Selected.Should().BeNull();
                model.CanRemoveUriRule.Should().BeTrue();
                model.PreviewBatchCommand.CanExecute(null).Should().BeFalse();
                var events = f.Count("security_audit_events");
                var grouping = window.FindControl<ComboBox>("PermissionGrouping")!;
                ControlAutomationPeer.CreatePeerForElement(grouping)!.GetName().Should().Contain("no read or group selection");
                grouping.SelectedItem = "Artefact type";
                model.PermissionRows.Select(row => row.GroupHeading).Should().Equal("URI pattern (1)", "Unknown (1)");
                list.SelectedItem.Should().BeNull();
                model.CanRemoveUriRule.Should().BeFalse();
                f.Count("security_audit_events").Should().Be(events);
                preferences.Saves.Should().Be(0);
                list.SelectedItem = model.PermissionRows.Single(row => row.Grant is UriPermissionGrant);
                var remove = window.FindControl<Button>("RemoveUriPermission")!;
                remove.IsEnabled.Should().BeTrue();
                await ((AsyncCommand)remove.Command!).ExecuteAsync();
                policy.IsPreapproved(new("https://example.com/page")).Should().BeFalse();
                list.ItemCount.Should().Be(1);
                var input = window.FindControl<TextBox>("UriPermissionPattern")!;
                input.Text = "https://new.example/*";
                var add = window.FindControl<Button>("AddUriPermission")!;
                add.IsEnabled.Should().BeTrue();
                await ((AsyncCommand)add.Command!).ExecuteAsync();
                policy.IsPreapproved(new("https://new.example/page")).Should().BeTrue();
                list.ItemCount.Should().Be(2);
                (await f.Store.InspectExactGrantAsync(grant.Id, f.Token))!.Grant.Should().Be(grant);
            }
            finally { window.Close(); }
        });
    }

    private sealed class Preferences : IPreapprovedUriPreferences
    {
        internal PreapprovedUriSettings Settings { get; set; } = PreapprovedUriSettings.Empty;
        internal int Saves { get; private set; }
        public PreapprovedUriSettings Load() => Settings;
        public void Save(PreapprovedUriSettings settings) { Settings = settings; Saves++; }
    }

    private sealed class Audit : ISecurityAuditLog
    {
        public void Write(SecurityAuditEvent auditEvent) { }
    }
}
