using System.Diagnostics;

using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

using AwesomeAssertions;

using Kora.Controls;
using Kora.Windows.IntegrationTests.Storage;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionsConversationTests
{
    [Fact]
    public async Task ExplicitCompareFreezesHistoricalObservedRevisionAndExactBufferWithoutReadsOrAnyAuthorityMutation()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            var saved = await WindowsSqliteConversationTests.Save(f,
                await f.Store.ReadConversationDraftAsync(f.Request.SessionId, f.Token), "  historical e\u0301\U0001f642\r\nbody  ");
            var reader = new SearchReader(f.Store.ReadConversationPageAsync, f.Store.ReadConversationDraftAsync);
            await using var composed = new Composition(f, conversationReader: reader);
            var model = composed.Model;
            var window = new SessionsWindow(model);
            window.Show();
            try
            {
                await model.RefreshAsync();
                await model.SelectAsync(model.Sessions.Single());
                model.CompareComposer().Should().BeNull("Compare must not read an unobserved saved body");
                reader.DraftReads.Should().Be(0);
                await model.ReadConversationAsync();
                const string raw = "  current raw \U0001f642 e\u0301\r\n body \t ";
                window.FindControl<TextBox>("ConversationComposer")!.Text = raw;
                var target = model.ComposerTarget;
                var review = model.SavedConversationDraftReview;
                var retention = await f.Store.ReadRetentionAsync(f.Request.SessionId, f.Token);
                await WindowsSqliteConversationTests.Save(f, saved, "newer disk body is NOT the observed baseline");
                var audit = WindowsSqliteConversationTests.AuditText(f);
                var auditEvents = composed.Audit.Events.Count;
                var activities = 0;
                var presentationThread = Environment.CurrentManagedThreadId;
                using var listener = new ActivityListener
                {
                    ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
                    Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
                    ActivityStarted = _ =>
                    {
                        if (Environment.CurrentManagedThreadId == presentationThread) { activities++; }
                    },
                };
                ActivitySource.AddActivityListener(listener);
                reader.DenyReads = true;
                var decisions = 0;
                model.DecideComposerDraft = () => { decisions++; return Task.FromResult(ComposerDraftDecision.Cancel); };
                var dialog = OpenComposerComparison(window);
                var panes = dialog.GetVisualDescendants().OfType<SnapshotComparisonFindPane>().ToArray();
                panes.Should().HaveCount(2);
                CompareSource(panes[0]).Text.Should().Be(saved.Draft.Body);
                CompareSource(panes[1]).Text.Should().Be(raw);
                panes[0].GetVisualDescendants().OfType<TextBlock>().Should().Contain(block =>
                    block.Text != null && block.Text.Contains(f.Request.SessionId.Value.ToString("D"), StringComparison.Ordinal)
                    && block.Text.Contains("observed saved revision 1", StringComparison.Ordinal)
                    && block.Text.Contains("generation 1", StringComparison.Ordinal)
                    && block.Text.Contains("not current disk", StringComparison.Ordinal));
                dialog.GetVisualDescendants().OfType<TextBlock>().Should().Contain(block =>
                    block.Text != null && block.Text.Contains("frozen bodies differ", StringComparison.Ordinal));
                CompareQuery(panes[1]).Text = "body";
                CompareClick(panes[1], "Find first");
                panes[1].GetVisualDescendants().OfType<NamedTextBlock>().Single(block => block.Focusable).Text.Should().Be("body");
                CompareQuery(panes[0]).Text.Should().BeNullOrEmpty("Find is pane-local");
                foreach (var pane in panes)
                {
                    foreach (var control in new[] { CompareSource(pane), CompareQuery(pane) })
                    {
                        var transfer = new RoutedEventArgs(TextBox.CopyingToClipboardEvent);
                        control.RaiseEvent(transfer);
                        transfer.Handled.Should().BeTrue();
                        control.IsUndoEnabled.Should().BeFalse();
                        control.ContextMenu.Should().BeNull();
                    }
                }
                dialog.Close();
                activities.Should().Be(0, "Compare, Find and Close are pure snapshot presentation");
                reader.Reads.Should().Be(1);
                reader.DraftReads.Should().Be(1);
                model.ComposerBody.Should().Be(raw);
                model.ComposerDirty.Should().BeTrue();
                model.ComposerTarget.Should().Be(target);
                model.SavedConversationDraftReview.Should().Be(review);
                decisions.Should().Be(0);
                composed.Audit.Events.Count.Should().Be(auditEvents);
                WindowsSqliteConversationTests.AuditText(f).Should().Be(audit);
                (await f.Store.ReadRetentionAsync(f.Request.SessionId, f.Token)).Should().Be(retention);
                f.Count("conversation_messages").Should().Be(0);
                (await model.ProtectComposerAsync()).Should().BeFalse();
                decisions.Should().Be(1, "Compare never resolves the unsaved-close decision");
            }
            finally { window.ClosePrivate(); }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativeCompareDistinguishesAbsentSavedBodyFromActuallySavedEmptyBody(bool savedEmpty)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            if (savedEmpty)
            {
                await WindowsSqliteConversationTests.Save(f,
                    await f.Store.ReadConversationDraftAsync(f.Request.SessionId, f.Token), string.Empty);
            }
            await using var composed = new Composition(f);
            var model = composed.Model;
            var window = new SessionsWindow(model);
            window.Show();
            try
            {
                await model.RefreshAsync();
                await model.SelectAsync(model.Sessions.Single());
                await model.ReadConversationAsync();
                var dialog = OpenComposerComparison(window);
                var before = dialog.GetVisualDescendants().OfType<SnapshotComparisonFindPane>().First();
                CompareSource(before).Text.Should().BeEmpty();
                CompareQuery(before).IsEnabled.Should().Be(savedEmpty);
                dialog.GetVisualDescendants().OfType<TextBlock>().Should().Contain(block => block.Text != null
                    && block.Text.Contains(savedEmpty ? "ordinally equal" : "Absent observed saved body; no equality", StringComparison.Ordinal));
                model.ComposerDirty.Should().BeFalse();
                dialog.Close();
            }
            finally { window.ClosePrivate(); }
        });
    }

    [Theory]
    [InlineData("characters")]
    [InlineData("bytes")]
    [InlineData("unicode")]
    [InlineData("control")]
    [InlineData("credential")]
    public async Task CompareRejectsTheCompleteInvalidBufferWithoutRepairAuditOrSourceAccess(string invalid)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            var reader = new SearchReader(f.Store.ReadConversationPageAsync, f.Store.ReadConversationDraftAsync);
            await using var composed = new Composition(f, conversationReader: reader);
            var model = composed.Model;
            var window = new SessionsWindow(model);
            window.Show();
            try
            {
                await model.RefreshAsync();
                await model.SelectAsync(model.Sessions.Single());
                await model.ReadConversationAsync();
                var raw = invalid switch
                {
                    "characters" => new string('x', 4097),
                    "bytes" => new string('界', 5462),
                    "unicode" => new string('\ud800', 1),
                    "control" => "exact\u0000raw",
                    _ => "password=abcdefghij123456",
                };
                model.ComposerBody = raw;
                var audit = WindowsSqliteConversationTests.AuditText(f);
                reader.DenyReads = true;
                var opened = 0;
                using var subscription = Window.WindowOpenedEvent.AddClassHandler<ConversationDraftComparisonWindow>((_, _) => opened++);
                CompareClick(window, "CompareComposer");
                opened.Should().Be(0);
                model.ConversationStatus.Should().StartWith("Compare refused:");
                model.ComposerBody.Should().Be(raw);
                model.ComposerDirty.Should().BeTrue();
                reader.DraftReads.Should().Be(1);
                reader.Reads.Should().Be(1);
                WindowsSqliteConversationTests.AuditText(f).Should().Be(audit);
            }
            finally { window.ClosePrivate(); }
        });
    }

    [Theory]
    [InlineData("edit")]
    [InlineData("same-edit")]
    [InlineData("compact-edit")]
    [InlineData("discard")]
    [InlineData("read")]
    [InlineData("save")]
    [InlineData("reload")]
    [InlineData("review-clear")]
    [InlineData("clear")]
    [InlineData("submit")]
    [InlineData("control")]
    [InlineData("control-permission")]
    [InlineData("privacy")]
    [InlineData("source")]
    [InlineData("selection")]
    [InlineData("generation")]
    [InlineData("protect")]
    [InlineData("close")]
    [InlineData("replace")]
    public async Task ComposerChangesRetireWindowSourcesFindQueriesAndRetainedCallbacksWithoutAutoCompare(string change)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            var original = f.Request.SessionId;
            await WindowsSqliteConversationTests.Save(f,
                await f.Store.ReadConversationDraftAsync(original, f.Token), "PRIVATE observed body");
            f.Request = InteractionStorageFixture.NewRequest();
            await f.AdmitAsync(newSession: true);
            await WindowsSqliteConversationTests.FinishInitial(f);
            await using var composed = new Composition(f);
            var model = composed.Model;
            var owner = new SessionsPresentationOwner(model);
            var window = new SessionsWindow(model, owner);
            window.Show();
            try
            {
                await model.RefreshAsync();
                await model.SelectAsync(model.Sessions.Single(row => row.Authority.SessionId == original));
                await model.ReadConversationAsync();
                model.ComposerBody = "PRIVATE unsaved buffer";
                model.ReviewClearConversationDraft();
                var dialog = OpenComposerComparison(window);
                var panes = dialog.GetVisualDescendants().OfType<SnapshotComparisonFindPane>().ToArray();
                foreach (var pane in panes)
                {
                    CompareQuery(pane).Text = "PRIVATE";
                    CompareClick(pane, "Find first");
                }
                model.DecideComposerDraft = () => Task.FromResult(ComposerDraftDecision.Discard);
                ConversationDraftComparisonWindow? replacement = null;
                switch (change)
                {
                    case "edit": model.ComposerBody = "replacement"; break;
                    case "same-edit": model.ComposerBody = "PRIVATE unsaved buffer"; break;
                    case "compact-edit":
                        var compact = new CompactSessionWindow(model, owner);
                        compact.Show();
                        compact.FindControl<TextBox>("ConversationComposer")!.Text = "shared compact edit";
                        break;
                    case "discard": model.DiscardComposerBuffer(); break;
                    case "read": await model.ReadConversationAsync(); break;
                    case "save": await model.SaveConversationDraftAsync(); break;
                    case "reload": await model.ReloadConversationDraftAsync(); break;
                    case "review-clear": model.ReviewClearConversationDraft(); break;
                    case "clear": await model.ClearSavedConversationDraftAsync(); break;
                    case "submit": await model.SubmitConversationAsync(); break;
                    case "control": composed.Access.ControlRevision++; break;
                    case "control-permission": composed.Access.CanControl = false; break;
                    case "privacy": composed.Access.CanInspect = false; break;
                    case "source": model.RevokeSessionList(); break;
                    case "selection": await model.SelectAsync(model.Sessions.Single(row => row.Authority.SessionId != original)); break;
                    case "generation":
                        await composed.Workspace.ChangeLifecycleAsync(original, new(1), false, Kora.Core.Hosting.RequestOrigin.LocalUi, f.Token);
                        await model.RefreshWorkAsync();
                        break;
                    case "protect": await model.ProtectComposerAsync(); break;
                    case "close": window.ClosePrivate(); break;
                    case "replace": replacement = OpenComposerComparison(window); break;
                }
                // A retained callback must check admission even before the passive retirement timer next runs.
                CompareClick(panes[1], "Find next");
                dialog.IsVisible.Should().BeFalse();
                foreach (var pane in panes)
                {
                    CompareSource(pane).Text.Should().BeEmpty();
                    CompareQuery(pane).Text.Should().BeEmpty();
                    pane.GetVisualDescendants().OfType<TextBlock>().Should().NotContain(block =>
                        block.Text != null && block.Text.Contains("PRIVATE", StringComparison.Ordinal));
                    CompareClick(pane, "Find first");
                    pane.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.F3 });
                    pane.GetVisualDescendants().OfType<NamedTextBlock>().Single(block => block.Focusable).Text.Should().BeNull();
                    var transfer = new RoutedEventArgs(TextBox.CopyingToClipboardEvent);
                    CompareSource(pane).RaiseEvent(transfer);
                    transfer.Handled.Should().BeTrue();
                }
                if (replacement is not null)
                {
                    replacement.IsVisible.Should().BeTrue("old callbacks cannot retire a replacement comparison");
                    replacement.Close();
                }
                if (change is "source" or "control")
                {
                    model.CanCompareComposer.Should().BeFalse("new Compare needs an observation under the current source/private control epoch");
                }
            }
            finally { window.ClosePrivate(); }
        });
    }

    [Theory]
    [InlineData("opening")]
    [InlineData("match")]
    [InlineData("focus")]
    public async Task ReentrantComposerRevocationCannotPublishOrResurrectPrivateSnapshotControls(string boundary)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            await using var composed = new Composition(f);
            var model = composed.Model;
            var window = new SessionsWindow(model);
            window.Show();
            try
            {
                await model.RefreshAsync();
                await model.SelectAsync(model.Sessions.Single());
                await model.ReadConversationAsync();
                model.ComposerBody = "PRIVATE buffer";
                ConversationDraftComparisonWindow? dialog = null;
                SnapshotComparisonFindPane[] panes = [];
                using var opened = Window.WindowOpenedEvent.AddClassHandler<ConversationDraftComparisonWindow>((value, _) =>
                {
                    dialog = value;
                    panes = value.GetVisualDescendants().OfType<SnapshotComparisonFindPane>().ToArray();
                    if (boundary == "opening") { model.ComposerBody = "changed during opening"; }
                });
                CompareClick(window, "CompareComposer");
                dialog.Should().NotBeNull();
                if (boundary != "opening")
                {
                    var match = panes[1].GetVisualDescendants().OfType<NamedTextBlock>().Single(block => block.Focusable);
                    match.PropertyChanged += (_, args) =>
                    {
                        if (boundary == "match" && args.Property == TextBlock.TextProperty && match.Text == "PRIVATE")
                        { model.ComposerBody = "changed during publication"; }
                    };
                    match.GotFocus += (_, _) =>
                    {
                        if (boundary == "focus") { model.ComposerBody = "changed during focus"; }
                    };
                    CompareQuery(panes[1]).Text = "PRIVATE";
                    CompareClick(panes[1], "Find first");
                }
                dialog!.IsVisible.Should().BeFalse();
                foreach (var pane in panes)
                {
                    CompareSource(pane).Text.Should().BeEmpty();
                    CompareQuery(pane).Text.Should().BeEmpty();
                    pane.GetVisualDescendants().OfType<NamedTextBlock>().Single(block => block.Focusable).Text.Should().BeNull();
                }
            }
            finally { window.ClosePrivate(); }
        });
    }

    private static ConversationDraftComparisonWindow OpenComposerComparison(SessionsWindow owner)
    {
        ConversationDraftComparisonWindow? dialog = null;
        using var opened = Window.WindowOpenedEvent.AddClassHandler<ConversationDraftComparisonWindow>((window, _) => dialog = window);
        CompareClick(owner, "CompareComposer");
        return dialog ?? throw new InvalidOperationException("Expected an explicit composer comparison.");
    }

    private static TextBox CompareSource(SnapshotComparisonFindPane pane) =>
        pane.GetVisualDescendants().OfType<TextBox>().Single(box => box.IsReadOnly);
    private static TextBox CompareQuery(SnapshotComparisonFindPane pane) =>
        pane.GetVisualDescendants().OfType<TextBox>().Single(box => !box.IsReadOnly);
    private static void CompareClick(Control root, string nameOrCaption)
    {
        var button = root.FindControl<Button>(nameOrCaption)
            ?? root.GetVisualDescendants().OfType<Button>().Single(value => Equals(value.Content, nameOrCaption));
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }
}
