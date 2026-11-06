using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

using AwesomeAssertions;

using Kora.Application.Presentation;
using Kora.Core.Conversation;
using Kora.Core.Hosting;
using Kora.Core.Presentation;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed class DetailLiteralMatchRevealTests
{
    [Theory]
    [InlineData("plain", false)]
    [InlineData("markdown", false)]
    [InlineData("markdown", true)]
    [InlineData("fallback", false)]
    public Task RevealSelectsAndScrollsToTheExactOccurrenceInTheCurrentProjection(string profile, bool exactSource) =>
        HeadlessSession.RunAsync(() =>
        {
            var source = "# Heading\r\n\r\n**needle** near the beginning\r\n\r\n"
                + string.Join("\r\n\r\n", Enumerable.Repeat("Unchanged surrounding text.", 80))
                + "\r\n\r\nlast context \U0001f642: needle after a long result";
            if (string.Equals(profile, "fallback", StringComparison.Ordinal)) { source = "<script>inert</script>\n" + source; }
            var content = Content(source, string.Equals(profile, "plain", StringComparison.Ordinal)
                ? DetailContentKind.PlainText : DetailContentKind.Markdown);
            var state = State(content);
            var writes = 0;
            var window = Window(state, () => writes++);
            window.Show();
            try
            {
                Required<CheckBox>(window, "SourceToggle").IsChecked = exactSource;
                var button = Required<Button>(window, "RevealMatch");
                button.IsEnabled.Should().BeFalse();
                Required<TextBox>(window, "SearchInput").Text = "needle";
                Click(window, "FirstMatch");
                Click(window, "NextMatch");
                var start = state.ActiveText.LastIndexOf("needle", StringComparison.Ordinal);
                var projection = state.ActiveText;
                var digest = content.Digest;
                button.IsEnabled.Should().BeTrue();
                var peer = ControlAutomationPeer.CreatePeerForElement(button)!;
                peer.GetName().Should().Contain("exact selected literal match");
                peer.GetHelpText().Should().Contain("surrounding text");
                peer.GetProvider<IInvokeProvider>()!.Invoke();
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();

                var reader = Required<TextBox>(window, "ContentReader");
                reader.IsVisible.Should().BeTrue();
                reader.IsFocused.Should().BeTrue();
                reader.SelectionStart.Should().Be(start);
                reader.SelectionEnd.Should().Be(start + "needle".Length);
                reader.SelectedText.Should().Be("needle");
                reader.Text.Should().Be(projection);
                reader.GetVisualDescendants().OfType<ScrollViewer>().Single().Offset.Y.Should().BeGreaterThan(0);
                state.IsSource.Should().Be(exactSource);
                state.MatchOrdinal.Should().Be(2);
                state.MatchCount.Should().Be(2);
                state.Status.Should().Contain("Selected the exact literal occurrence");
                state.Content.Should().BeSameAs(content);
                content.Source.Should().Be(source);
                content.Digest.Should().Be(digest);
                writes.Should().Be(0);

                Click(window, "PreviousMatch");
                reader.SelectedText.Should().BeEmpty("moving the Find cursor retires its previous revealed selection");
                button.Focus().Should().BeTrue();
                window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
                window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
                reader.IsFocused.Should().BeTrue();
                reader.SelectionStart.Should().Be(state.ActiveText.IndexOf("needle", StringComparison.Ordinal));
                reader.SelectedText.Should().Be("needle");
                writes.Should().Be(0);
            }
            finally { window.ClearAndClose(); }
        });

    [Theory]
    [InlineData("edit")]
    [InlineData("cancel")]
    [InlineData("clear")]
    [InlineData("projection")]
    [InlineData("exhaust")]
    public Task ChangedOrRetiredMatchesCannotRevealAnOldSelection(string change) => HeadlessSession.RunAsync(() =>
    {
        var state = State(Content("context needle end", DetailContentKind.PlainText));
        var window = Window(state);
        window.Show();
        try
        {
            Required<TextBox>(window, "SearchInput").Text = "needle";
            Click(window, "FirstMatch");
            Click(window, "RevealMatch");
            var reader = Required<TextBox>(window, "ContentReader");
            reader.SelectedText.Should().Be("needle");
            switch (change)
            {
                case "edit": Required<TextBox>(window, "SearchInput").Text = "context"; break;
                case "cancel": Click(window, "CancelFind"); break;
                case "clear": Click(window, "ClearFind"); break;
                case "projection": Required<CheckBox>(window, "SourceToggle").IsChecked = true; break;
                case "exhaust": Click(window, "NextMatch"); break;
            }
            reader.SelectedText.Should().BeEmpty();
            Required<Button>(window, "RevealMatch").IsEnabled.Should().BeFalse();
            Click(window, "RevealMatch");
            reader.SelectedText.Should().BeEmpty();
            state.Status.Should().Contain("Reader reveal unavailable");
        }
        finally { window.ClearAndClose(); }
    });

    [Fact]
    public Task QueryEditsPreserveAnUnrelatedManualReaderSelection() => HeadlessSession.RunAsync(() =>
    {
        var state = State(Content("context needle end", DetailContentKind.PlainText));
        var window = Window(state);
        window.Show();
        try
        {
            Required<TextBox>(window, "SearchInput").Text = "needle";
            Click(window, "FirstMatch");
            Click(window, "RevealMatch");
            var reader = Required<TextBox>(window, "ContentReader");
            reader.SelectionStart = 0;
            reader.SelectionEnd = "context".Length;
            Required<TextBox>(window, "SearchInput").Text = "end";
            reader.SelectedText.Should().Be("context");
            Required<Button>(window, "RevealMatch").IsEnabled.Should().BeFalse();
        }
        finally { window.ClearAndClose(); }
    });

    [Fact]
    public Task SeparateHostMatchesNeverRevealOrCopyTheUserBody() => HeadlessSession.RunAsync(() =>
    {
        var message = new ConversationMessage(HostRequest.Create(RequestOrigin.LocalUi), new(1), 1, 1,
            DateTimeOffset.UnixEpoch, "Original USER body", null, null);
        var content = new AdmittedDetailContent(new(new(message.Request.RequestId.Value), 1), DetailContentKind.PlainText,
            DetailContentOrigin.RetainedConversation, DetailSensitivity.DisclosureConfirmationRequired,
            "Original USER", "Private retained original", message.Body, conversationMessage: message);
        var state = State(content);
        var window = Window(state);
        window.Show();
        try
        {
            Required<TextBox>(window, "SearchInput").Text = "USER";
            Click(window, "FirstMatch");
            Click(window, "RevealMatch");
            var reader = Required<TextBox>(window, "ContentReader");
            reader.SelectedText.Should().Be("USER");
            state.TryGetCopySelection(true, true, reader.SelectionStart, reader.SelectedText.Length, out var copy).Should().BeFalse();
            copy.Should().BeNull();
            Required<CheckBox>(window, "FindHostToggle").IsChecked = true;
            reader.SelectedText.Should().BeEmpty();
            Required<TextBox>(window, "SearchInput").Text = "HOST";
            Click(window, "FirstMatch");
            Required<Button>(window, "RevealMatch").IsEnabled.Should().BeFalse();
            Click(window, "RevealMatch");
            state.Status.Should().Contain("Separate HOST matches stay in their own observation");
            reader.Text.Should().Be(message.Body);
            reader.SelectedText.Should().BeEmpty();
            Required<Button>(window, "CopySelection").IsEnabled.Should().BeFalse();
        }
        finally { window.ClearAndClose(); }
    });

    [Theory]
    [InlineData("private")]
    [InlineData("close")]
    [InlineData("during-selection")]
    [InlineData("during-focus")]
    [InlineData("edit-during-selection")]
    [InlineData("focus-unavailable")]
    public Task RevocationAndReentrantChangesFenceRevealWithoutClaimingFocusOrSelection(string reason) =>
        HeadlessSession.RunAsync(() =>
        {
            var allowed = true;
            var state = State(Content("context needle end", DetailContentKind.PlainText), () => allowed);
            var window = Window(state);
            window.Show();
            try
            {
                Required<TextBox>(window, "SearchInput").Text = "needle";
                Click(window, "FirstMatch");
                var reader = Required<TextBox>(window, "ContentReader");
                if (string.Equals(reason, "private", StringComparison.Ordinal)) { allowed = false; }
                else if (string.Equals(reason, "close", StringComparison.Ordinal)) { window.ClearAndClose(); }
                else if (string.Equals(reason, "during-selection", StringComparison.Ordinal))
                {
                    reader.PropertyChanged += (_, args) =>
                    {
                        if (args.Property == TextBox.SelectionStartProperty && reader.SelectionStart > 0) { state.Close(); }
                    };
                }
                else if (string.Equals(reason, "during-focus", StringComparison.Ordinal))
                {
                    reader.GotFocus += (_, _) => state.Close();
                }
                else if (string.Equals(reason, "edit-during-selection", StringComparison.Ordinal))
                {
                    reader.PropertyChanged += (_, args) =>
                    {
                        if (args.Property == TextBox.SelectionEndProperty && reader.SelectionEnd > 0)
                        { Required<TextBox>(window, "SearchInput").Text = "changed"; }
                    };
                }
                else { reader.Focusable = false; }
                Click(window, "RevealMatch");
                reader.SelectedText.Should().BeEmpty();
                state.Status.Should().NotContain("Selected the exact literal occurrence");
                if (reason is "edit-during-selection" or "focus-unavailable")
                {
                    state.Content.Should().NotBeNull();
                    state.Status.Should().Contain("reveal unavailable");
                }
                else
                {
                    window.IsVisible.Should().BeFalse();
                    state.Content.Should().BeNull();
                    reader.Text.Should().BeEmpty();
                    Required<Button>(window, "RevealMatch").IsEnabled.Should().BeFalse();
                }
            }
            finally { window.ClearAndClose(); }
        });

    private static AdmittedDetailContent Content(string source, DetailContentKind kind) =>
        new(new(new(Guid.NewGuid()), 1), kind, DetailContentOrigin.EmbeddedDocument, DetailSensitivity.Public,
            "Exact reader", "Embedded immutable document", source);

    private static DetailViewerState State(AdmittedDetailContent content, Func<bool>? admission = null)
    {
        var state = new DetailViewerState(content, admission);
        var result = Render(content);
        state.CompleteRender(state.Generation, result.SemanticText, result.Status, result.IsFallback);
        return state;
    }

    private static NativeDocumentResult Render(AdmittedDetailContent content) =>
        new NativeDetailRenderer(NullLogger<NativeDetailRenderer>.Instance).Render(content.Source, content.Kind);

    private static DetailWindow Window(DetailViewerState state, Action? copy = null) =>
        new(state, Render(state.Content!), (_, _, _) =>
        {
            if (copy is null) { throw new InvalidOperationException("Reveal grants no clipboard authority."); }
            copy();
            return Task.CompletedTask;
        }) { Width = 640, Height = 760 };

    private static T Required<T>(DetailWindow window, string name) where T : Control =>
        window.FindControl<T>(name) ?? throw new InvalidOperationException("Missing native control.");

    private static void Click(DetailWindow window, string name) =>
        Required<Button>(window, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
}
