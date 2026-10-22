using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Input;

using AwesomeAssertions;

namespace Kora.Windows.IntegrationTests;

public sealed partial class LocalFileLiteralFindWindowTests
{
    [Fact]
    public async Task AccessibleNativeMatchCaseDefaultsOnAndToggleRequiresSeparateFreshFindWithoutIo()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var file = Folder("É é É é").Files[0];
            var failures = new List<string>();
            var reads = 0;
            var window = new LocalFilePreviewWindow(failures.Add);
            window.ShowAttachment(new(file.Review.Request.SessionId, new(1), new(1), file),
                (_, _, _) => throw new InvalidOperationException("No ranked retrieval."),
                () => true, () => Task.CompletedTask, resolveCurrent: _ =>
                {
                    reads++;
                    return Task.FromResult(file);
                });
            window.Show();
            window.UpdateLayout();
            try
            {
                var toggle = MatchCase(window);
                toggle.IsChecked.Should().BeTrue();
                toggle.IsThreeState.Should().BeFalse();
                var peer = ControlAutomationPeer.CreatePeerForElement(toggle)!;
                peer.GetName().Should().Be("Match case");
                peer.GetAutomationControlType().Should().Be(AutomationControlType.CheckBox);
                Query(window).Text = "é";
                reads.Should().Be(0);
                await Press(window, "Find first");
                Status(window).Text.Should().Contain("1 of 2").And.Contain("case-sensitive");
                Match(window)!.Text.Should().Be("é");
                Toggle(toggle);
                toggle.IsChecked.Should().BeFalse();
                reads.Should().Be(1);
                Query(window).Text.Should().Be("é");
                Match(window).Should().BeNull();
                Status(window).Text.Should().BeNull();
                Button(window, "Find next").IsEnabled.Should().BeFalse();
                Button(window, "Find previous").IsEnabled.Should().BeFalse();
                await Press(window, "Find first");
                Match(window)!.Text.Should().Be("É");
                Status(window).Text.Should().Contain("1 of 4").And.Contain("ordinal ignore case");
                Location(window).Text.Should().Contain("UTF-16 [0, 1)");
                await Press(window, "Find next");
                Match(window)!.Text.Should().Be("é");
                Status(window).Text.Should().Contain("2 of 4");
                Toggle(toggle);
                reads.Should().Be(3);
                Match(window).Should().BeNull();
                Status(window).Text.Should().BeNull();
                await Press(window, "Find first");
                Status(window).Text.Should().Contain("1 of 2");
                Source(window).Text.Should().Be(file.Text);
                failures.Should().BeEmpty();
            }
            finally { window.ClearAndClose(); }
        });
    }

    [Fact]
    public async Task OldCheckboxCannotRetireNewCompositionOrReuseItsSelection()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var file = Folder("alpha ALPHA").Files[0];
            var failures = new List<string>();
            var window = new LocalFilePreviewWindow(failures.Add);
            void Show() => window.ShowRevision(file, (_, _, _) => throw new InvalidOperationException("No ranked retrieval."),
                () => true, () => Task.CompletedTask);
            Show();
            window.Show();
            try
            {
                var retired = MatchCase(window);
                Toggle(retired);
                Query(window).Text = "alpha";
                await Press(window, "Find first");
                Status(window).Text.Should().Contain("1 of 2");
                Show();
                window.UpdateLayout();
                MatchCase(window).IsChecked.Should().BeTrue();
                Query(window).Text = "alpha";
                await Press(window, "Find first");
                retired.IsChecked = true;
                Status(window).Text.Should().Contain("1 of 1");
                Match(window)!.Text.Should().Be("alpha");
                failures.Should().BeEmpty();
            }
            finally { window.ClearAndClose(); }
        });
    }

    [Theory]
    [InlineData("layout")]
    [InlineData("bring")]
    public async Task ReentrantCaseRoundTripRetiresAlreadyRevealedSpanBeforeFocus(string boundary)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var file = Folder("ALPHA alpha").Files[0];
            var failures = new List<string>();
            var window = new LocalFilePreviewWindow(failures.Add);
            window.ShowRevision(file, (_, _, _) => throw new InvalidOperationException("No ranked retrieval."),
                () => true, () => Task.CompletedTask);
            window.Show();
            window.UpdateLayout();
            try
            {
                var toggle = MatchCase(window);
                toggle.IsChecked = false;
                Query(window).Text = "alpha";
                Query(window).Focus().Should().BeTrue();
                var retired = false;
                void Retire()
                {
                    if (retired || Match(window) is null) { return; }
                    retired = true;
                    toggle.IsChecked = true;
                    toggle.IsChecked = false;
                }
                window.LayoutUpdated += (_, _) => { if (string.Equals(boundary, "layout", StringComparison.Ordinal)) { Retire(); } };
                window.AddHandler(Control.RequestBringIntoViewEvent, (_, _) =>
                {
                    if (string.Equals(boundary, "bring", StringComparison.Ordinal)) { Retire(); }
                });
                await Press(window, "Find first");
                retired.Should().BeTrue();
                Match(window).Should().BeNull();
                Status(window).Text.Should().BeNull();
                Button(window, "Find next").IsEnabled.Should().BeFalse();
                Query(window).IsFocused.Should().BeTrue();
                failures.Should().BeEmpty();
            }
            finally { window.ClearAndClose(); }
        });
    }

    private static void Toggle(CheckBox checkbox)
    {
        checkbox.Focus().Should().BeTrue();
        checkbox.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Space });
        checkbox.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyUpEvent, Key = Key.Space });
    }
}
