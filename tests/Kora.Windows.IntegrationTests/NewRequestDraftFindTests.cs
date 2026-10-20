using System.ComponentModel;

using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

using AwesomeAssertions;

using Kora.Application.Hosting;
using Kora.Controls;
using Kora.Core.Context;
using Kora.NativeUxFixture;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed class NewRequestDraftFindTests
{
    [Fact]
    public Task LiteralNavigationCountsTheWholeDirtyInvalidDraftWithoutEditingReviewingOrCreating() => Run(f =>
    {
        const string raw = "ALPHA alpha alpha aaaa .*? \U0001f642e\u0301 \u00e9\r\n" + "\ud800";
        f.Composer.Body = raw;
        f.Composer.Dirty.Should().BeTrue();
        var status = f.Composer.Status;
        var changes = 0;
        f.Composer.PropertyChanged += (_, _) => changes++;
        f.Query.Text = "alpha";
        f.Click("DraftFindFirst");
        f.AssertMatch(6, 5, 2, 1);
        f.Click("DraftFindNext");
        f.AssertMatch(12, 5, 2, 2);
        f.Click("DraftFindNext");
        f.Status.Text.Should().Contain("End of direction").And.Contain("total 2; ordinal 2").And.Contain("no wrap");
        f.Click("DraftFindPrevious");
        f.AssertMatch(6, 5, 2, 1);
        f.Click("DraftFindPrevious");
        f.Status.Text.Should().Contain("End of direction").And.Contain("ordinal 1");
        f.Query.Text = "aa";
        f.Click("DraftFindFirst");
        f.AssertMatch(18, 2, 2, 1);
        f.Click("DraftFindNext");
        f.AssertMatch(20, 2, 2, 2);
        f.Click("DraftFindPrevious");
        f.AssertMatch(18, 2, 2, 1);
        f.Query.Text = " .*? ";
        f.Click("DraftFindFirst");
        f.AssertMatch(22, 5, 1, 1);
        f.Query.Text = "missing";
        f.Click("DraftFindFirst");
        f.Status.Text.Should().Contain("total 0; ordinal 0").And.Contain("No literal match");
        f.Composer.Body.Should().Be(raw);
        f.Source.Text.Should().Be(raw);
        f.Composer.Status.Should().Be(status);
        f.Composer.ReviewedBody.Should().BeEmpty();
        f.Composer.Target.Should().BeNull();
        f.Composer.Creation.Should().BeNull();
        f.Composer.Submission.Should().BeNull();
        f.Composer.SavedRevision.Should().Be(0);
        f.Composer.AssistantResponse.Should().BeNull();
        f.Window.Operation.IsCompletedSuccessfully.Should().BeTrue();
        changes.Should().Be(0, "Find never enters the composer operation or editing paths");
    });

    [Theory]
    [InlineData("\U0001f642", 0, 2)]
    [InlineData("e\u0301", 2, 2)]
    [InlineData("\u00e9", 5, 1)]
    [InlineData(" ", 4, 1)]
    [InlineData("aa", 7, 2)]
    public Task UnicodeSelectionUsesExactUtf16AndNoNormalization(string literal, int start, int length) => Run(f =>
    {
        f.Composer.Body = "\U0001f642e\u0301 \u00e9 aaaa\r\n";
        f.Query.Text = literal;
        f.Click("DraftFindFirst");
        f.AssertMatch(start, length, literal is " " or "aa" ? 2 : 1, 1);
        f.Source.SelectedText.Should().Be(literal);
    });

    [Theory]
    [InlineData("")]
    [InlineData("surrogate")]
    [InlineData("a\r\n")]
    [InlineData("a\t")]
    [InlineData("\u202e")]
    [InlineData("\u2028")]
    [InlineData("\u2029")]
    [InlineData("characters")]
    [InlineData("bytes")]
    public Task InvalidQueriesAreRejectedWithoutTruncationOrSourceMutation(string literal) => Run(f =>
    {
        if (literal is "characters") { literal = new('a', LocalFileRetrievalPolicy.MaximumQueryCharacters + 1); }
        if (literal is "bytes") { literal = new('\u754c', LocalFileRetrievalPolicy.MaximumQueryUtf8Bytes / 3 + 1); }
        if (literal is "surrogate") { literal = "\ud800"; }
        f.Composer.Body = "alpha alpha";
        f.Query.Text = "alpha";
        f.Click("DraftFindFirst");
        f.Query.Text = literal;
        f.Click("DraftFindFirst");
        f.Status.Text.Should().Contain("Invalid literal").And.Contain("Nothing truncated");
        f.Query.Text.Should().Be(literal);
        f.Next.IsEnabled.Should().BeFalse();
        f.Composer.Body.Should().Be("alpha alpha");
    });

    [Theory]
    [InlineData("ascii")]
    [InlineData("bytes")]
    public Task ExactQueryBudgetsAreAccepted(string budget) => Run(f =>
    {
        var literal = budget is "ascii" ? new string('a', LocalFileRetrievalPolicy.MaximumQueryCharacters)
            : string.Concat(Enumerable.Repeat("\U0001f642", LocalFileRetrievalPolicy.MaximumQueryUtf8Bytes / 4));
        f.Composer.Body = literal + " " + literal;
        f.Query.Text = literal;
        f.Click("DraftFindFirst");
        f.AssertMatch(0, literal.Length, 2, 1);
    });

    [Fact]
    public Task TargetAndReviewChangesRetireFindButFindCannotConsumeAnExistingReview() => Run(async f =>
    {
        f.Composer.Body = "alpha alpha";
        f.Query.Text = "alpha";
        f.Click("DraftFindFirst");
        var creation = f.Composer.Create();
        f.Query.Text.Should().BeEmpty();
        f.Next.IsEnabled.Should().BeFalse();
        await creation;
        f.Composer.Target.Should().NotBeNull();
        f.Composer.Review();
        f.Composer.CanSubmit.Should().BeTrue();
        var reviewed = f.Composer.ReviewedBody;
        var target = f.Composer.Target;
        f.Query.Text = "alpha";
        f.Click("DraftFindFirst");
        f.AssertMatch(0, 5, 2, 1);
        f.Composer.CanSubmit.Should().BeTrue();
        f.Composer.ReviewedBody.Should().Be(reviewed);
        f.Composer.Target.Should().Be(target);
        f.Composer.Submission.Should().BeNull();
        f.Composer.Review();
        f.Query.Text.Should().BeEmpty();
        f.Status.Text.Should().BeEmpty();
        f.Query.Text = "alpha";
        f.Click("DraftFindFirst");
        await f.Composer.Inspect();
        f.Query.Text.Should().BeEmpty();
        f.Next.IsEnabled.Should().BeFalse();
        f.Composer.Body.Should().Be("alpha alpha");
    });

    [Theory]
    [InlineData("edit")]
    [InlineData("same-bytes-reset")]
    [InlineData("review")]
    [InlineData("cancel")]
    [InlineData("host-input")]
    [InlineData("control")]
    [InlineData("private")]
    [InlineData("readonly")]
    [InlineData("disabled")]
    [InlineData("hidden")]
    [InlineData("context")]
    [InlineData("detach")]
    [InlineData("close-private")]
    public Task BufferIdentityAuthorityAndControlLifetimesRetireWithoutChangingUserSelection(string change) => Run(f =>
    {
        f.Composer.Body = "alpha alpha";
        f.Query.Text = "alpha";
        f.Click("DraftFindFirst");
        f.Source.SelectionStart = 1;
        f.Source.SelectionEnd = 3;
        switch (change)
        {
            case "edit": f.Composer.Body = "new memory alpha"; break;
            case "same-bytes-reset": f.Composer.Body = f.Composer.Body; break;
            case "review": f.Composer.Review(); break;
            case "cancel": f.Composer.Cancel(); break;
            case "host-input": f.Host.Change(); break;
            case "control": f.Revision++; f.Window.ObserveDraftFindLifetime(); break;
            case "private": f.Live = false; f.Window.ObserveDraftFindLifetime(); break;
            case "readonly": f.Source.IsReadOnly = true; break;
            case "disabled": f.Source.IsEnabled = false; break;
            case "hidden": f.Source.IsVisible = false; break;
            case "context": f.Window.DataContext = null; break;
            case "detach": f.Window.Content = null; break;
            case "close-private": f.Window.ClosePrivate(); break;
        }
        f.Query.Text.Should().BeEmpty();
        f.Status.Text.Should().BeEmpty();
        f.Next.IsEnabled.Should().BeFalse();
        if (change is not ("edit" or "close-private" or "disabled" or "hidden" or "context" or "detach"))
        { f.Source.SelectionStart.Should().Be(1); f.Source.SelectionEnd.Should().Be(3); }
        f.Live = true;
        f.Click("DraftFindNext");
        f.Status.Text.Should().NotContain("Exact editable-source selection focused");
    });

    [Theory]
    [InlineData("selection")]
    [InlineData("focus")]
    [InlineData("query-focus")]
    [InlineData("status")]
    [InlineData("button")]
    [InlineData("previous-button")]
    [InlineData("capture")]
    public Task ReentrantCallbacksCannotPublishLateCountNavigationOrFocus(string boundary) => Run(f =>
    {
        f.Composer.Body = "alpha alpha";
        f.Query.Text = "alpha";
        var fired = false;
        void Revoke()
        {
            if (fired) { return; }
            fired = true;
            f.Host.Change();
            f.Source.SelectionStart = 2;
            f.Source.SelectionEnd = 4;
        }
        if (boundary is "selection")
        {
            f.Source.PropertyChanged += (_, args) => { if (args.Property == TextBox.SelectionStartProperty) { Revoke(); } };
            f.Source.SelectionStart = 1;
            fired = false;
            f.Query.Text = "alpha";
        }
        if (boundary is "focus") { f.Source.GotFocus += (_, _) => Revoke(); f.Query.Focus(); }
        if (boundary is "query-focus") { f.Query.GotFocus += (_, _) => Revoke(); f.Source.Focus(); }
        if (boundary is "status") { f.Status.PropertyChanged += (_, args) => { if (args.Property == TextBlock.TextProperty) { Revoke(); } }; }
        if (boundary is "button") { f.Next.PropertyChanged += (_, args) => { if (args.Property == InputElement.IsEnabledProperty) { Revoke(); } }; }
        if (boundary is "previous-button") { f.Control<Button>("DraftFindPrevious").PropertyChanged += (_, args) => { if (args.Property == InputElement.IsEnabledProperty) { Revoke(); } }; }
        if (boundary is "capture") { f.OnCapture = Revoke; }
        if (boundary is "query-focus") { f.Key(f.Window, Key.F, KeyModifiers.Control); }
        else { f.Click("DraftFindFirst"); }
        fired.Should().BeTrue();
        f.Query.Text.Should().BeEmpty();
        f.Next.IsEnabled.Should().BeFalse();
        f.Status.Text.Should().BeEmpty();
        f.Source.SelectionStart.Should().Be(2);
        f.Source.SelectionEnd.Should().Be(4);
    });

    [Fact]
    public Task NativeKeyboardAccessibilityFocusFailureClearAndDisposalStayLocal() => Run(f =>
    {
        f.Composer.Body = "alpha alpha";
        f.Key(f.Window, Key.F, KeyModifiers.Control);
        f.Query.IsFocused.Should().BeTrue();
        f.Query.Text = "alpha";
        f.Key(f.Query, Key.Enter);
        f.AssertMatch(0, 5, 2, 1);
        f.Key(f.Source, Key.F3);
        f.AssertMatch(6, 5, 2, 2);
        f.Key(f.Source, Key.F3, KeyModifiers.Shift);
        f.AssertMatch(0, 5, 2, 1);
        AutomationProperties.GetName(f.Query).Should().Contain("DRAFT BODY only");
        ControlAutomationPeer.CreatePeerForElement(f.Status)!.GetName().Should().Contain("total 2; ordinal 1");
        f.Source.AcceptsReturn.Should().BeTrue();
        f.Source.IsReadOnly.Should().BeFalse();
        f.Source.SelectionStart = 2;
        f.Source.SelectionEnd = 4;
        f.Click("DraftFindClear");
        f.Query.Text.Should().BeEmpty();
        f.Next.IsEnabled.Should().BeFalse();
        f.Source.SelectionStart.Should().Be(2);
        f.Source.SelectionEnd.Should().Be(4);
        f.Source.Focusable = false;
        f.Query.Focus();
        f.Query.Text = "alpha";
        f.Click("DraftFindFirst");
        f.Status.Text.Should().Contain("focus/selection unavailable");
        f.Next.IsEnabled.Should().BeFalse();
        f.Source.Focusable = true;
        f.Query.Focusable = false;
        f.Source.Focus();
        f.Key(f.Window, Key.F, KeyModifiers.Control);
        f.Status.Text.Should().Contain("query focus unavailable");
        f.Window.ClosePrivate();
        f.Host.Subscribers.Should().Be(0);
        f.Query.Text.Should().BeEmpty();
        f.Status.Text.Should().BeEmpty();
        f.Host.Change();
        f.Query.Text = "late callback";
        f.Click("DraftFindFirst");
        f.Status.Text.Should().BeEmpty();
        f.Next.IsEnabled.Should().BeFalse();
    });

    private static Task Run(Action<Fixture> test) => Run(f => { test(f); return Task.CompletedTask; });

    private static Task Run(Func<Fixture, Task> test) => HeadlessSession.RunAsync(async () =>
    {
        using var native = new NativeUxFixtureSession(Path.GetTempPath());
        await native.Tasks.InitializeAsync(native.Token);
        await native.Interactions.InitializeAsync(native.Token);
        var workspace = new SessionWorkspaceService(native.Interactions, new(native.Tasks), native.Access,
            NullLogger<SessionWorkspaceService>.Instance);
        var conversation = new SessionConversationService(native.Interactions, native.Interactions, workspace,
            native.Access, new NewRequestFindNoAdmission(), TimeProvider.System, NullLogger<SessionConversationService>.Instance);
        using var model = new NewRequestViewModel(new(workspace, conversation, native.Access, NullLogger<NewRequestComposer>.Instance));
        var window = new NewRequestWindow(model, () => throw new InvalidOperationException("Find never navigates"),
            exception => throw new InvalidOperationException("Unexpected operation", exception));
        var f = new Fixture(window, model.Composer);
        window.BindDraftFind(() =>
        {
            f.OnCapture?.Invoke();
            var revision = f.Revision;
            return f.Live ? () => f.Live && f.Revision == revision : null;
        }, f.Host);
        window.Show();
        f.CacheControls();
        try { await test(f); }
        finally { window.ClosePrivate(); }
    });

    private sealed class Fixture(NewRequestWindow window, NewRequestComposer composer)
    {
        internal NewRequestWindow Window => window;
        internal NewRequestComposer Composer => composer;
        internal FindHost Host { get; } = new();
        internal bool Live { get; set; } = true;
        internal long Revision { get; set; }
        internal Action? OnCapture { get; set; }
        private readonly Dictionary<string, Control> controls = new(StringComparer.Ordinal);
        internal void CacheControls()
        {
            foreach (var control in window.GetVisualDescendants().OfType<Control>().Where(control => control.Name is not null))
            { controls.TryAdd(control.Name!, control); }
        }
        internal T Control<T>(string name) where T : Control =>
            (T)controls[name];
        internal TextBox Source => window.FindControl<TextBox>("NewRequestBody")!;
        internal TextBox Query => Control<TextBox>("DraftFindQuery");
        internal NamedTextBlock Status => Control<NamedTextBlock>("DraftFindStatus");
        internal Button Next => Control<Button>("DraftFindNext");
        internal void Click(string name) => Control<Button>(name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        internal void Key(InputElement target, Key key, KeyModifiers modifiers = KeyModifiers.None) =>
            target.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, KeyModifiers = modifiers });
        internal void AssertMatch(int start, int length, int count, int ordinal)
        {
            Source.SelectionStart.Should().Be(start);
            Source.SelectionEnd.Should().Be(start + length);
            Source.IsFocused.Should().BeTrue();
            Status.Text.Should().Contain($"total {count}; ordinal {ordinal}").And.Contain("Exact editable-source selection focused");
        }
    }

    private sealed class FindHost : INotifyPropertyChanged
    {
        private PropertyChangedEventHandler? changed;
        internal int Subscribers { get; private set; }
        public event PropertyChangedEventHandler? PropertyChanged
        {
            add { changed += value; Subscribers++; }
            remove { changed -= value; Subscribers--; }
        }
        internal void Change() => changed?.Invoke(this, new("InputEpoch"));
    }
}
