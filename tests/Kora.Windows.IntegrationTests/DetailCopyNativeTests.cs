using System.Text;

using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

using AwesomeAssertions;

using Kora.Application.Documentation;
using Kora.Application.Presentation;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed class DetailCopyNativeTests
{
    [Fact]
    public Task EmbeddedDocumentationButtonsPublishCompleteActiveViewAndExactSourceThroughNativeTransfer() =>
        HeadlessSession.RunAsync(() =>
        {
            var documents = new EmbeddedUserDocumentationProvider();
            var page = documents.GetPages().Single(page => string.Equals(page.Id, "getting-started", StringComparison.Ordinal));
            using var clipboard = new TransferClipboard();
            DetailWindow? window = null;
            DetailViewerState? observed = null;
            using var controller = new DetailWindowController(documents, () => true,
                new(NullLogger<NativeDetailRenderer>.Instance), clipboard, (state, result, copy) =>
                {
                    observed = state;
                    return window = new(state, result, copy);
                }, NullLogger<DetailWindowController>.Instance);
            var owner = new Window();
            owner.Show();
            try
            {
                controller.OpenEmbeddedPage(page, owner).Should().Contain("Rendered");
                var state = observed!;
                state.Content!.Source.Should().Be(page.Markdown);
                state.RenderState.Should().Be(DetailRenderState.Rendered);
                var semantic = state.ActiveText;
                semantic.Should().StartWith("Getting started").And.NotBe(page.Markdown)
                    .And.Contain("1. Confirm the microphone shown in Settings.")
                    .And.EndWith("or launch Kora.");
                var all = window!.FindControl<Button>("CopyAll")!;
                var source = window.FindControl<Button>("CopySource")!;
                ControlAutomationPeer.CreatePeerForElement(all)!.GetName().Should().Contain("complete active view");
                ControlAutomationPeer.CreatePeerForElement(source)!.GetName().Should().Contain("exact original source");
                window.FindControl<CheckBox>("ReadingTextToggle")!.IsChecked = true;
                window.FindControl<CheckBox>("WrapTextToggle")!.IsChecked = false;
                state.Search("microphone").Should().BeTrue();
                var match = state.MatchStart;
                all.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                clipboard.Writes.Should().Be(1);
                var renderedTransfer = clipboard.Current.Should().BeOfType<DataTransfer>().Subject;
                renderedTransfer.TryGetText().Should().Be(semantic);
                var html = Encoding.UTF8.GetString(renderedTransfer.TryGetValue(NativeDetailClipboard.HtmlFormat)!);
                html.Should().Contain("<h1>Getting started</h1>").And.Contain("<ol start=\"1\">")
                    .And.Contain("or launch Kora.").And.NotContain("href=").And.NotContain("SHA-256:");
                state.Status.Should().StartWith("Complete rendered Markdown copied");
                state.MatchStart.Should().Be(match);
                source.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                clipboard.Writes.Should().Be(2);
                clipboard.Current!.TryGetText().Should().Be(page.Markdown);
                clipboard.Current.Formats.Should().Equal(DataFormat.Text);
                state.Status.Should().StartWith("Exact source copied");
                state.IsSource.Should().BeFalse();
                state.ActiveText.Should().Be(semantic);
                window.FindControl<CheckBox>("SourceToggle")!.IsChecked = true;
                all.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                clipboard.Writes.Should().Be(3);
                clipboard.Current!.TryGetText().Should().Be(page.Markdown);
                clipboard.Current.Formats.Should().Equal(DataFormat.Text);
                var previous = clipboard.Current;
                window.FindControl<CheckBox>("SourceToggle")!.IsChecked = false;
                clipboard.Fail = true;
                all.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                clipboard.Writes.Should().Be(4, "a failed rich transfer must not retry as plain text");
                clipboard.Current.Should().BeSameAs(previous);
                state.Status.Should().Contain("failed").And.NotContain("copied");
                controller.ClearForPrivacy();
                source.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                all.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                clipboard.Writes.Should().Be(4);
            }
            finally { window?.ClearAndClose(); owner.Close(); }
        });

    private sealed class TransferClipboard : IDetailClipboard, IDisposable
    {
        internal int Writes { get; private set; }
        internal bool Fail { get; set; }
        internal DataTransfer? Current { get; private set; }

        public Task WriteAsync(IDetailView view, string source, string? htmlFragment) =>
            NativeDetailClipboard.WriteDataAsync(transfer =>
            {
                Writes++;
                if (Fail) { throw new IOException("Deterministic native transfer failure."); }
                ((IAsyncDataTransfer?)Current)?.Dispose();
                Current = (DataTransfer)transfer;
                return Task.CompletedTask;
            }, source, htmlFragment);

        public void Dispose() => ((IAsyncDataTransfer?)Current)?.Dispose();
    }
}
