using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AwesomeAssertions;
using Kora.Application.Network.ExternalBrowser;
using Kora.Application.Presentation;
using Kora.CodeHighlighting;
using Kora.Core.Hosting;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Network.ExternalBrowser;
using Kora.Core.Presentation;
using Kora.NativeUxFixture;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed class FinalDocumentInteractionTests
{
    private const string Address = "https://example.com/final?q=generated%20target";
    private const string Link = "[original site](" + Address + ")";

    [Theory]
    [InlineData(false, ExternalBrowserLaunchOutcome.Accepted)]
    [InlineData(true, ExternalBrowserLaunchOutcome.Accepted)]
    [InlineData(true, ExternalBrowserLaunchOutcome.Failed)]
    public Task ActualFinalLinkPointerOrKeyboardRequiresOriginalSourceReviewAndSeparateConfirm(
        bool keyboard, ExternalBrowserLaunchOutcome outcome) => HeadlessSession.RunAsync(async () =>
    {
        using var fixture = new NativeUxFixtureSession(Path.GetFullPath("."));
        await fixture.InitializeAsync();
        var launcher = new Launcher { Outcome = outcome };
        fixture.Main.BindExternalBrowserWorkflow(new(launcher, new Network(), fixture.Audit, TimeProvider.System));
        var state = State(Link, () => fixture.Main.CanRevealPrivatePresentation, retained: keyboard);
        var result = Render(Link);
        var view = new DetailContentView(state, result, (_, _, _) => throw new InvalidOperationException("No copy authority."), fixture.Main);
        var window = new Window { Content = view, Width = 900, Height = 1000 };
        window.Show();
        try
        {
            window.UpdateLayout();
            var link = window.GetVisualDescendants().OfType<Button>().Single(button => button.Tag is NativeExternalDocumentLink);
            ControlAutomationPeer.CreatePeerForElement(link)!.GetName().Should().Contain("untrusted final-answer");
            if (keyboard)
            {
                link.Focus().Should().BeTrue();
                Press(window, Key.Enter, PhysicalKey.Enter);
            }
            else
            {
                link.BringIntoView();
                window.UpdateLayout();
                var point = link.TranslatePoint(new Point(link.Bounds.Width / 2, link.Bounds.Height / 2), window)!.Value;
                window.MouseDown(point, MouseButton.Left);
                window.MouseUp(point, MouseButton.Left);
            }
            Dispatcher.UIThread.RunJobs();
            var review = window.OwnedWindows.OfType<ExternalBrowserReviewWindow>().Single();
            var destination = review.GetVisualDescendants().OfType<TextBox>().Single();
            destination.Text.Should().Be(Address);
            destination.SelectAll();
            destination.Copy();
            foreach (var transfer in new[] { TextBox.CopyingToClipboardEvent, TextBox.CuttingToClipboardEvent, TextBox.PastingFromClipboardEvent })
            {
                var args = new RoutedEventArgs(transfer);
                destination.RaiseEvent(args);
                args.Handled.Should().BeTrue();
            }
            var menu = new ContextRequestedEventArgs();
            destination.RaiseEvent(menu);
            menu.Handled.Should().BeTrue();
            review.GetVisualDescendants().OfType<TextBlock>().Should().Contain(block =>
                block.Text != null && block.Text.Contains(Link, StringComparison.Ordinal)
                && block.Text.Contains("not retrieval provenance", StringComparison.Ordinal));
            launcher.Calls.Should().Be(0);
            Click(Button(review, "Cancel"));
            launcher.Calls.Should().Be(0);
            link.Focus();
            Press(window, Key.Space, PhysicalKey.Space);
            review = window.OwnedWindows.OfType<ExternalBrowserReviewWindow>().Single(item => item.IsVisible);
            var confirm = Button(review, "Open this exact destination once");
            Click(confirm);
            Click(confirm);
            launcher.Calls.Should().Be(1);
            launcher.Address.Should().Be(Address);
            fixture.Main.WebResultDetailStatus.Should().Contain(
                outcome == ExternalBrowserLaunchOutcome.Accepted ? "Windows accepted" : "Windows declined");
            state.Status.Should().Contain(
                outcome == ExternalBrowserLaunchOutcome.Accepted ? "Windows accepted" : "Windows declined");
            review.GetVisualDescendants().OfType<TextBox>().Single().Text.Should().BeEmpty();
            state.Content!.WebResult.Should().BeNull();
        }
        finally { view.ClearPresentation(); window.Close(); }
    });

    [Theory]
    [InlineData("close")]
    [InlineData("private")]
    [InlineData("rerender")]
    [InlineData("source-view")]
    public Task FinalSourceWithdrawalClearsPrivateControlsAndOldReviewsNeverLaunch(string closure) => HeadlessSession.RunAsync(async () =>
    {
        using var fixture = new NativeUxFixtureSession(Path.GetFullPath("."));
        await fixture.InitializeAsync();
        var launcher = new Launcher();
        fixture.Main.BindExternalBrowserWorkflow(new(launcher, new Network(), fixture.Audit, TimeProvider.System));
        var live = true;
        var state = State(Link, () => live);
        var view = new DetailContentView(state, Render(Link), (_, _, _) => Task.CompletedTask, fixture.Main);
        var window = new Window { Content = view, Width = 900, Height = 1000 };
        window.Show();
        var link = window.GetVisualDescendants().OfType<Button>().Single(button => button.Tag is NativeExternalDocumentLink);
        Click(link);
        var review = window.OwnedWindows.OfType<ExternalBrowserReviewWindow>().Single();
        var confirm = Button(review, "Open this exact destination once");
        switch (closure)
        {
            case "close": view.ClearPresentation(); break;
            case "private": live = false; state.Content.Should().BeNull(); break;
            case "rerender": state.CompleteRender(state.Generation, "replacement", "Rendered", false); break;
            case "source-view": state.SetSource(true); break;
        }
        Click(confirm);
        Click(link);
        launcher.Calls.Should().Be(0);
        view.ClearPresentation();
        link.Tag.Should().BeNull();
        link.Content.Should().BeNull();
        Click(link);
        review.GetVisualDescendants().OfType<TextBox>().Single().Text.Should().BeEmpty();
        window.Close();
    });

    [Fact]
    public Task ActualFinalCodeKeyboardSelectionWrapScrollResizeKeepsLiteralTextAndDeniesEveryClipboardRoute() => HeadlessSession.RunAsync(async () =>
    {
        var literal = "\tα = \"é😀\"; " + new string('x', 240) + "\r\n"
            + string.Concat(Enumerable.Range(0, 60).Select(index => $"line {index:D2}\r\n"));
        var markdown = "```cs\r\n" + literal + "```\r\n";
        var live = true;
        var state = State(markdown, () => live, retained: true);
        var result = Render(markdown);
        var copies = 0;
        var view = new DetailContentView(state, result, (_, _, _) => { copies++; return Task.CompletedTask; });
        var window = new Window { Content = view, Width = 650, Height = 1000 };
        window.Show();
        var block = window.GetVisualDescendants().OfType<NativeCodeBlock>().Single();
        await block.Pending;
        try
        {
            block.Reader.Should().NotBeOfType<SelectableTextBlock>();
            block.SelectToggle!.Focus().Should().BeTrue();
            Press(window, Key.Space, PhysicalKey.Space);
            var reader = block.SelectionReader!;
            reader.IsFocused.Should().BeTrue();
            reader.Text.Should().Be(literal);
            reader.IsReadOnly.Should().BeTrue();
            ControlAutomationPeer.CreatePeerForElement(reader)!.GetName().Should().Contain("local visual selection only");
            Press(window, Key.Home, PhysicalKey.Home, RawInputModifiers.Control);
            Press(window, Key.Right, PhysicalKey.ArrowRight, RawInputModifiers.Shift);
            reader.SelectedText.Should().Be("\t");
            Press(window, Key.A, PhysicalKey.A, RawInputModifiers.Control);
            reader.SelectedText.Should().Be(literal);
            view.FindControl<CheckBox>("ReadingTextToggle")!.IsChecked.Should().NotBeTrue();
            foreach (var transfer in new[] { TextBox.CopyingToClipboardEvent, TextBox.CuttingToClipboardEvent, TextBox.PastingFromClipboardEvent })
            {
                var args = new RoutedEventArgs(transfer);
                reader.RaiseEvent(args);
                args.Handled.Should().BeTrue();
            }
            reader.Copy();
            var menu = new ContextRequestedEventArgs();
            reader.RaiseEvent(menu);
            menu.Handled.Should().BeTrue();
            foreach (var key in new[] { Key.C, Key.X, Key.V, Key.Insert })
            {
                var args = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, KeyModifiers = KeyModifiers.Control };
                reader.RaiseEvent(args);
                args.Handled.Should().BeTrue();
            }
            copies.Should().Be(0);
            reader.Text.Should().Be(literal);
            block.WrapToggle!.IsChecked = false;
            window.UpdateLayout();
            reader.TextWrapping.Should().Be(TextWrapping.NoWrap);
            var viewport = block.ReadingViewport!;
            viewport.Extent.Width.Should().BeGreaterThan(viewport.Viewport.Width);
            viewport.Offset = new(80, 120);
            var offset = viewport.Offset;
            var selection = (reader.SelectionStart, reader.SelectionEnd, reader.CaretIndex);
            window.Width = 500;
            window.UpdateLayout();
            viewport.Offset.Should().Be(offset);
            (reader.SelectionStart, reader.SelectionEnd, reader.CaretIndex).Should().Be(selection);
            reader.Text.Should().Be(literal);
            block.WrapToggle.IsChecked = true;
            window.UpdateLayout();
            reader.TextWrapping.Should().Be(TextWrapping.Wrap);
            viewport.Offset.X.Should().Be(0);
            reader.Text.Should().Be(literal);
            live = false;
            state.Content.Should().BeNull();
            reader.Text.Should().BeEmpty();
            reader.SelectedText.Should().BeEmpty();
            reader.IsEnabled.Should().BeFalse();
            block.Reader.Text.Should().BeEmpty();
            copies.Should().Be(0);
        }
        finally { view.ClearPresentation(); window.Close(); await block.JoinAsync(); }
    });

    [Fact]
    public Task ParsedFinalLinkMetadataUsesTheExistingParserAndDoesNotActivateFragmentsOrCode() => HeadlessSession.RunAsync(() =>
    {
        var source = "[local](#heading)\n\n[ref][id] and <https://example.com/auto>\n\n`[not](https://example.com/code)`\n\n[id]: https://example.com/reference\n";
        var result = Render(source);
        var links = result.Sections.SelectMany(section => section.ExternalLinks).ToArray();
        links.Select(link => link.Destination).Should().Equal("https://example.com/reference", "https://example.com/auto");
        source.Substring(links[0].SourceStart, links[0].SourceLength).Should().Be("[ref][id]");
        source.Substring(links[1].SourceStart, links[1].SourceLength).Should().Be("<https://example.com/auto>");
        MarkdownDocumentRenderer.CreateControls(result, selectable: false).Should().OnlyContain(control => control is TextBlock);
    });

    [Theory]
    [InlineData(315, false)]
    [InlineData(316, true)]
    public Task FinalInteractionNativeControlBudgetKeepsTheExactInclusiveBoundAndHonestSourceFallback(int codeBlocks, bool fallback) =>
        HeadlessSession.RunAsync(() =>
    {
        var source = string.Concat(Enumerable.Repeat("```\nliteral\n```\n\n", codeBlocks)) + "last paragraph";
        var result = Render(source);
        result.IsFallback.Should().BeFalse();
        (codeBlocks * 13 + 1 > NativeDetailProfile.MaximumNodes).Should().Be(fallback);
        var state = State(source, () => true);
        var view = new DetailContentView(state, result, (_, _, _) => Task.CompletedTask);
        var window = new Window { Content = view, Width = 650, Height = 1000 };
        window.Show();
        try
        {
            window.UpdateLayout();
            MarkdownDocumentRenderer.RequiresFinalSourceFallback(result).Should().Be(fallback);
            state.RenderState.Should().Be(fallback ? DetailRenderState.SourceFallback : DetailRenderState.Rendered);
            var blocks = view.GetVisualDescendants().OfType<NativeCodeBlock>().ToArray();
            blocks.Length.Should().Be(fallback ? 0 : codeBlocks);
            if (fallback)
            {
                state.ActiveText.Should().Be(source);
                state.Status.Should().Contain("control bound").And.Contain("source fallback");
                view.FindControl<TextBox>("ContentReader")!.Text.Should().Be(source);
            }
        }
        finally { view.ClearPresentation(); window.Close(); }
    });

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public Task FinalCodeRechecksPrivateAdmissionBeforeAutomationSelectionOrNativeFocus(bool selection) =>
        HeadlessSession.RunAsync(async () =>
    {
        const string markdown = "```cs\nprivate literal code\n```";
        var live = true;
        var state = State(markdown, () => live, retained: true);
        var view = new DetailContentView(state, Render(markdown), (_, _, _) => throw new InvalidOperationException("No clipboard authority."));
        var window = new Window { Content = view, Width = 650, Height = 1000 };
        window.Show();
        var block = window.GetVisualDescendants().OfType<NativeCodeBlock>().Single();
        await block.Pending;
        try
        {
            block.SelectToggle!.IsChecked = true;
            var reader = block.SelectionReader!;
            view.FindControl<CheckBox>("SourceToggle")!.Focus();
            live = false;
            if (selection) { reader.SelectAll(); }
            else { reader.Focus(); }
            reader.Text.Should().BeEmpty();
            reader.SelectedText.Should().BeEmpty();
            block.Reader.Text.Should().BeEmpty();
            state.Content.Should().BeNull();
        }
        finally { view.ClearPresentation(); window.Close(); await block.JoinAsync(); }
    });

    [Theory]
    [InlineData("review")]
    [InlineData("confirm")]
    [InlineData("dispose-late")]
    [InlineData("success")]
    public Task ActualRetainedFinalControllerRevalidatesSourceAtReviewAndConfirmationAndOwnsLateValidation(string boundary) =>
        HeadlessSession.RunAsync(async () =>
    {
        using var fixture = new NativeUxFixtureSession(Path.GetFullPath("."));
        await fixture.InitializeAsync();
        var launcher = new Launcher();
        fixture.Main.BindExternalBrowserWorkflow(new(launcher, new Network(), fixture.Audit, TimeProvider.System));
        var content = State(Link, () => true, retained: true).Content!;
        var reads = 0;
        var unavailable = false;
        var held = false;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<AdmittedDetailContent> Resolve(CancellationToken token)
        {
            reads++;
            if (held) { entered.TrySetResult(); await release.Task; }
            if (unavailable) { throw new InvalidDataException("Synthetic exact retained source no longer exists."); }
            return content;
        }
        DetailWindow? detail = null;
        using var controller = new DetailWindowController(fixture.Documents, () => fixture.Main.CanRevealPrivatePresentation,
            new NativeDetailRenderer(NullLogger<NativeDetailRenderer>.Instance), new DeniedClipboard(),
            (state, result, copy) => detail = new(state, result, copy, fixture.Main),
            NullLogger<DetailWindowController>.Instance, finalizedMarkdownFormat: NativeDetailFormat.TextV1);
        var owner = new Window();
        owner.Show();
        try
        {
            await controller.OpenConversationDetailAsync(content, owner, () => true, Resolve, TestContext.Current.CancellationToken);
            reads.Should().Be(2);
            var opened = detail ?? throw new InvalidOperationException("The exact native detail was not created.");
            var link = opened.GetVisualDescendants().OfType<Button>().Single(button => button.Tag is NativeExternalDocumentLink);
            if (boundary is "review") { unavailable = true; }
            if (boundary is "dispose-late") { held = true; }
            Click(link);
            if (boundary is "dispose-late")
            {
                await entered.Task;
                var join = opened.Reader.PendingNavigation;
                controller.Dispose();
                join.IsCompleted.Should().BeFalse();
                release.SetResult();
                await join;
                reads.Should().Be(3);
                launcher.Calls.Should().Be(0);
                opened.IsVisible.Should().BeFalse();
                return;
            }
            await opened.Reader.PendingNavigation;
            reads.Should().Be(3);
            if (boundary is "review")
            {
                opened.IsVisible.Should().BeFalse();
                launcher.Calls.Should().Be(0);
                return;
            }
            var review = opened.OwnedWindows.OfType<ExternalBrowserReviewWindow>().Single();
            if (boundary is "confirm") { unavailable = true; }
            Click(Button(review, "Open this exact destination once"));
            reads.Should().Be(4);
            launcher.Calls.Should().Be(boundary is "success" ? 1 : 0);
            if (boundary is "confirm") { opened.IsVisible.Should().BeFalse(); }
        }
        finally { controller.Dispose(); detail?.ClearAndClose(); owner.Close(); }
    });

    private static DetailViewerState State(string text, Func<bool> current, bool retained = false)
    {
        var request = HostRequest.Create(RequestOrigin.ActivatedVoice);
        var provenance = new ModelTurnProvenance(new(Guid.NewGuid()), request, ModelProviderSelection.OllamaCandidate);
        var message = new ConversationMessage(request, new(7), 1, 3, DateTimeOffset.UnixEpoch,
            "Original USER question", ModelTurnOutcome.Succeeded, ModelTurnReason.ProviderOutcome)
        {
            Version = 2, Execution = provenance, ExecutionMetadataRevision = 0, AssistantResponse = new(provenance, text),
        };
        var content = new AdmittedDetailContent(new(new(provenance.Turn.Value), 3), DetailContentKind.Markdown,
            retained ? DetailContentOrigin.RetainedAssistant : DetailContentOrigin.FinalizedResponse, DetailSensitivity.DisclosureConfirmationRequired,
            "Exact final answer", "Untrusted generated final answer", text,
            retained ? null : new(request.SessionId, request.RequestId, request.TaskId), conversationMessage: retained ? message : null);
        var state = new DetailViewerState(content, current);
        var result = Render(text);
        state.CompleteRender(state.Generation, result.SemanticText, result.Status, result.IsFallback).Should().BeTrue();
        return state;
    }

    private static NativeDocumentResult Render(string source) => new NativeDetailRenderer(NullLogger<NativeDetailRenderer>.Instance).Render(source);
    private static Button Button(Window window, string label) =>
        window.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, label));
    private static void Click(Button button)
    {
        button.RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
    }
    private static void Press(Window window, Key key, PhysicalKey physical, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        window.KeyPress(key, modifiers, physical, null);
        window.KeyRelease(key, modifiers, physical, null);
        Dispatcher.UIThread.RunJobs();
    }

    private sealed class Network : IExternalBrowserNetworkPolicy
    {
        public ExternalBrowserNetworkAdmission Capture() => new(true, () => true);
    }
    private sealed class DeniedClipboard : IDetailClipboard
    {
        public Task WritePlainTextAsync(IDetailView view, string source) =>
            throw new InvalidOperationException("Final documents have no clipboard authority.");
    }
    private sealed class Launcher : IExternalBrowserLauncher
    {
        public int Calls { get; private set; }
        public string? Address { get; private set; }
        public ExternalBrowserLaunchOutcome Outcome { get; init; } = ExternalBrowserLaunchOutcome.Accepted;
        public Task<ExternalBrowserLaunchOutcome> LaunchAsync(ExternalBrowserDestination destination, Func<bool> admitted, CancellationToken cancellationToken)
        {
            admitted().Should().BeTrue();
            cancellationToken.IsCancellationRequested.Should().BeFalse();
            Calls++;
            Address = destination.ExactAddress;
            return Task.FromResult(Outcome);
        }
    }
}
