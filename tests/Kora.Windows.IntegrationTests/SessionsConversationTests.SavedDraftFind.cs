using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

using AwesomeAssertions;

using Kora.Core.Context;
using Kora.Core.Storage;
using Kora.Windows.IntegrationTests.Storage;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionsConversationTests
{
    private static readonly string[] SavedDraftFindLiterals = ["Aa", "🙂", "é", " "];

    [Fact]
    public async Task SavedDraftMetadataFindIsCountedNativeAccessibleAndNeverReadsOrRetargetsDirtyComposer()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            var original = f.Request.SessionId;
            await WindowsSqliteConversationTests.Save(f,
                await f.Store.ReadConversationDraftAsync(original, f.Token), "BODY_ONLY_SENTINEL");
            await WindowsSqliteSessionWorkspaceTests.Service(f, new()).RenameAsync(original, new(1), 0,
                new("aaaaa Aa Aa 🙂 é"), Kora.Core.Hosting.RequestOrigin.LocalUi, f.Token);
            f.Request = InteractionStorageFixture.NewRequest();
            await f.AdmitAsync(newSession: true);
            await WindowsSqliteConversationTests.FinishInitial(f);
            await WindowsSqliteConversationTests.Save(f,
                await f.Store.ReadConversationDraftAsync(f.Request.SessionId, f.Token), "OTHER_BODY_SENTINEL");
            var checkpoint = new CatalogueCheckpoint();
            var reads = 0;
            checkpoint.Read = () => reads++;
            f.Reopen(checkpoint);
            await f.Store.InitializeAsync(f.Token);
            await using var composed = new Composition(f);
            await composed.Model.RefreshAsync();
            await composed.Model.SelectAsync(composed.Model.Sessions.Single(row => row.Authority.SessionId == original));
            composed.Model.ComposerBody = "  unchanged dirty buffer 🙂  ";
            var target = composed.Model.SelectedSessionRecord;
            using var model = new SavedDraftCatalogueViewModel(WindowsSqliteSessionWorkspaceTests.Service(f, composed.Access),
                (_, _, _) => throw new InvalidOperationException("Find is never navigation authority."),
                composed.Access, () => true, NullLogger<SavedDraftCatalogueViewModel>.Instance);
            var window = new SavedDraftCatalogueWindow(model);
            window.Show();
            try
            {
                var query = window.FindControl<TextBox>("MetadataFindQuery")!;
                var first = window.FindControl<Button>("MetadataFindFirst")!;
                var next = window.FindControl<Button>("MetadataFindNext")!;
                var previous = window.FindControl<Button>("MetadataFindPrevious")!;
                var clear = window.FindControl<Button>("MetadataFindClear")!;
                var span = window.FindControl<TextBlock>("MetadataFindSpan")!;
                var status = window.FindControl<TextBlock>("MetadataFindStatus")!;
                var position = window.FindControl<TextBlock>("MetadataFindPosition")!;
                query.IsEnabled.Should().BeFalse();
                model.FindMetadata(SnapshotFindDirection.First);
                reads.Should().Be(0);
                await model.Refresh();
                var row = model.Records.Single(record => record.Session.SessionId == original);
                model.Select(row);
                var details = model.SelectedDetails;
                var readCount = reads;
                var auditCount = f.Count("security_audit_events");
                foreach (var control in new Control[] { query, first, next, previous, clear, span, status, position })
                {
                    ControlAutomationPeer.CreatePeerForElement(control)!.GetName().Should().NotBeNullOrWhiteSpace(control.Name);
                }
                query.IsUndoEnabled.Should().BeFalse();
                var ctrlF = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.F, KeyModifiers = KeyModifiers.Control };
                window.RaiseEvent(ctrlF);
                ctrlF.Handled.Should().BeTrue();
                query.IsFocused.Should().BeTrue();
                query.Text = "aa";
                next.IsEnabled.Should().BeFalse();
                first.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                model.MetadataFindCount.Should().Be(2);
                model.MetadataFindOrdinal.Should().Be(1);
                span.Text.Should().Be("aa");
                span.IsFocused.Should().BeTrue();
                position.Text.Should().Contain("1 of 2").And.Contain("string only");
                ControlAutomationPeer.CreatePeerForElement(span)!.GetName().Should().Contain("aa");
                window.UpdateLayout();
                var viewport = span.GetVisualAncestors().OfType<ScrollViewer>().First();
                var location = span.TranslatePoint(default, viewport)!.Value;
                location.Y.Should().BeGreaterThanOrEqualTo(0);
                (location.Y + span.Bounds.Height).Should().BeLessThanOrEqualTo(viewport.Bounds.Height);
                next.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                model.MetadataFindOrdinal.Should().Be(2);
                next.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                model.MetadataFindCount.Should().Be(2);
                model.HasMetadataFindMatch.Should().BeFalse();
                status.Text.Should().Contain("no wrap");
                previous.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                model.MetadataFindOrdinal.Should().Be(1);
                foreach (var hidden in new[] { "BODY_ONLY_SENTINEL", "OTHER_BODY_SENTINEL", "e\u0301",
                    row.Source.ToString("D"), row.Profile.Value.ToString("D"), row.AuthorityDigest,
                    model.Records.Single(record => record.Session.SessionId != original).Session.SessionId.Value.ToString("D") })
                {
                    details.Should().NotContain(hidden);
                    query.Text = hidden;
                    first.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    model.MetadataFindCount.Should().Be(0);
                    status.Text.Should().StartWith("No literal match");
                }
                foreach (var literal in SavedDraftFindLiterals)
                {
                    query.Text = literal;
                    query.Focus();
                    query.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
                    model.MetadataFindMatch.Should().Be(literal);
                    model.MetadataFindCount.Should().Be(details.Split(literal, StringSplitOptions.None).Length - 1);
                }
                foreach (var invalid in new[] { "", "\r", "\n", "\t", "\0", "\u200b", "\u2028", "\u2029",
                    new string('x', 257), new string('界', 171), new string('\ud800', 1) })
                {
                    model.MetadataFindQuery = invalid;
                    first.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    status.Text.Should().StartWith("Invalid");
                    model.MetadataFindCount.Should().Be(0);
                    next.IsEnabled.Should().BeFalse();
                }
                query.Text = "Aa";
                first.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                span.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.F3 });
                model.MetadataFindOrdinal.Should().Be(2);
                span.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.F3, KeyModifiers = KeyModifiers.Shift });
                model.MetadataFindOrdinal.Should().Be(1);
                foreach (var transfer in new[] { TextBox.CopyingToClipboardEvent, TextBox.CuttingToClipboardEvent, TextBox.PastingFromClipboardEvent })
                {
                    var args = new RoutedEventArgs(transfer);
                    query.RaiseEvent(args);
                    args.Handled.Should().BeTrue();
                }
                foreach (var key in new[] { Key.C, Key.X, Key.V, Key.Insert })
                {
                    var args = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, KeyModifiers = KeyModifiers.Control };
                    query.RaiseEvent(args);
                    args.Handled.Should().BeTrue();
                }
                var notifications = 0;
                model.PropertyChanged += (_, _) => notifications++;
                for (var i = 0; i < 3; i++)
                {
                    model.MetadataFindMatch.Should().Be("Aa");
                    model.MetadataFindCount.Should().Be(2);
                    model.SelectedDetails.Should().Be(details);
                }
                notifications.Should().Be(0);
                clear.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                query.Text.Should().BeEmpty();
                span.Text.Should().BeEmpty();
                query.Text = "Aa";
                first.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                query.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
                window.IsVisible.Should().BeTrue();
                query.Text.Should().BeEmpty();
                model.SelectedRecord.Should().BeSameAs(row);
                model.SelectedDetails.Should().Be(details);
                composed.Model.SelectedSessionRecord.Should().BeSameAs(target);
                composed.Model.ComposerBody.Should().Be("  unchanged dirty buffer 🙂  ");
                reads.Should().Be(readCount);
                f.Count("security_audit_events").Should().Be(auditCount);
                f.Count("conversation_messages").Should().Be(0);
                query.Text = "Aa";
                first.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                window.Close();
                query.Text = "late";
                first.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                model.MetadataFindQuery.Should().BeEmpty();
                model.MetadataFindCount.Should().Be(0);
                span.Text.Should().BeEmpty();
                reads.Should().Be(readCount);
            }
            finally { window.Close(); composed.Model.ComposerBody = ""; }
        });
    }

    [Theory]
    [InlineData("selection")]
    [InlineData("order")]
    [InlineData("filter")]
    [InlineData("query")]
    [InlineData("mode")]
    [InlineData("metadata")]
    [InlineData("page")]
    [InlineData("refresh")]
    [InlineData("cancel")]
    [InlineData("private")]
    [InlineData("admission")]
    [InlineData("control")]
    [InlineData("source")]
    [InlineData("dispose")]
    [InlineData("pending-cancel")]
    public async Task SavedDraftFindRetiresAtEveryCatalogueBoundary(string boundary)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteConversationTests.FinishInitial(f);
        await WindowsSqliteConversationTests.Save(f,
            await f.Store.ReadConversationDraftAsync(f.Request.SessionId, f.Token), "body");
        var access = new WindowsSqliteSessionWorkspaceTests.Access();
        var checkpoint = new CatalogueCheckpoint();
        f.Reopen(checkpoint);
        await f.Store.InitializeAsync(f.Token);
        var admitted = true;
        using var model = new SavedDraftCatalogueViewModel(WindowsSqliteSessionWorkspaceTests.Service(f, access),
            (_, _, _) => throw new InvalidOperationException("Find cannot navigate."),
            access, () => admitted, NullLogger<SavedDraftCatalogueViewModel>.Instance);
        await model.Refresh();
        model.Select(model.Records.Single());
        model.MetadataFindQuery = "session";
        model.FindMetadata(SnapshotFindDirection.First);
        model.MetadataFindCount.Should().Be(1);
        switch (boundary)
        {
            case "selection": model.Select(null); break;
            case "order": model.Order = SavedDraftPageOrder.SessionNameAscending; break;
            case "filter": model.Filter = SessionListFilter.Done; break;
            case "query": model.QueryText = "changed"; break;
            case "mode": model.Mode = SavedDraftCatalogueMode.NameSubstring; break;
            case "metadata": model.MinimumBytesText = "1"; break;
            case "page": await model.Refresh(next: true); break;
            case "refresh": await model.Refresh(); break;
            case "cancel": model.Cancel(); break;
            case "private": access.CanInspect = false; break;
            case "admission": admitted = false; break;
            case "control": access.ControlRevision++; break;
            case "source": model.Revoke(); break;
            case "dispose": model.Dispose(); break;
            case "pending-cancel":
                checkpoint.Read = model.Cancel;
                await model.Refresh();
                model.Records.Should().BeEmpty();
                break;
        }
        model.SynchronizeAuthority();
        model.FindMetadata(SnapshotFindDirection.Next);
        model.ClearMetadataFind();
        model.MetadataFindFocusUnavailable();
        model.MetadataFindQuery = "late";
        model.MetadataFindQuery.Should().BeEmpty();
        model.MetadataFindMatch.Should().BeEmpty();
        model.MetadataFindStatus.Should().BeEmpty();
        model.MetadataFindCount.Should().Be(0);
        model.MetadataFindOrdinal.Should().Be(0);
        model.MetadataFindPosition.Should().BeEmpty();
        model.CanFindMetadata.Should().BeFalse();
    }

    [Theory]
    [InlineData("bring")]
    [InlineData("notify")]
    [InlineData("focus")]
    [InlineData("focus-failure")]
    [InlineData("query-bring")]
    [InlineData("query-focus-failure")]
    public async Task SavedDraftFindReentrantCallbacksCannotPublishOrFocusRetiredMatch(string boundary)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            await WindowsSqliteConversationTests.Save(f,
                await f.Store.ReadConversationDraftAsync(f.Request.SessionId, f.Token), "body");
            using var model = new SavedDraftCatalogueViewModel(WindowsSqliteSessionWorkspaceTests.Service(f, new()),
                (_, _, _) => throw new InvalidOperationException("No navigation."),
                new WindowsSqliteSessionWorkspaceTests.Access(), () => true, NullLogger<SavedDraftCatalogueViewModel>.Instance);
            var window = new SavedDraftCatalogueWindow(model);
            window.Show();
            try
            {
                await model.Refresh();
                model.Select(model.Records.Single());
                model.MetadataFindQuery = "session";
                var span = window.FindControl<TextBlock>("MetadataFindSpan")!;
                var query = window.FindControl<TextBox>("MetadataFindQuery")!;
                if (boundary is "bring" or "query-bring")
                {
                    window.AddHandler(Control.RequestBringIntoViewEvent, (_, _) => model.Revoke());
                }
                else if (boundary is "focus")
                {
                    span.GotFocus += (_, _) => model.Revoke();
                }
                else if (boundary is "focus-failure") { span.Focusable = false; }
                else if (boundary is "query-focus-failure") { query.Focusable = false; }
                else
                {
                    var revoked = false;
                    model.PropertyChanged += (_, args) =>
                    {
                        if (!revoked && args.PropertyName is nameof(model.MetadataFindStatus))
                        { revoked = true; model.Select(null); }
                    };
                }
                if (boundary is "query-bring" or "query-focus-failure")
                {
                    window.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.F, KeyModifiers = KeyModifiers.Control });
                }
                else { window.FindControl<Button>("MetadataFindFirst")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }
                model.HasMetadataFindMatch.Should().BeFalse();
                model.MetadataFindCount.Should().Be(0);
                model.CanNavigateMetadataFind.Should().BeFalse();
                span.Text.Should().BeEmpty();
                if (boundary is "focus-failure" or "query-focus-failure")
                {
                    model.MetadataFindStatus.Should().Contain("focus is unavailable");
                    span.Focusable = true;
                    query.Focusable = true;
                    window.FindControl<Button>("MetadataFindFirst")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    model.HasMetadataFindMatch.Should().BeTrue();
                    span.IsFocused.Should().BeTrue();
                }
                else
                {
                    model.MetadataFindQuery.Should().BeEmpty();
                    model.SelectedDetails.Should().BeEmpty();
                }
            }
            finally { window.Close(); }
        });
    }
}
