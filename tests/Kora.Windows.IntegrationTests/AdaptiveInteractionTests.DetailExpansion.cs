using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

using AwesomeAssertions;

namespace Kora.Windows.IntegrationTests;

public sealed partial class AdaptiveInteractionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExpandAndRestoreDetailsPreservesTheExactReaderAndConversation(bool keyboard)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new Fixture();
            var view = await OpenExpansionResult(f);
            var window = f.Window!;
            var panes = view.FindControl<Grid>("ReadingPanes")!;
            panes.ColumnDefinitions[0].Width = new(2, GridUnitType.Star);
            panes.ColumnDefinitions[2].Width = new(1, GridUnitType.Star);
            window.UpdateLayout();
            var originalDetailWidth = panes.ColumnDefinitions[2].ActualWidth;
            var originalWindowWidth = window.Width;
            var originalWindowHeight = window.Height;
            var reader = view.GetVisualDescendants().OfType<DetailContentView>().Single();
            reader.FindControl<CheckBox>("SourceToggle")!.IsChecked = true;
            var text = reader.FindControl<TextBox>("ContentReader")!;
            text.SelectionStart = 2;
            text.SelectionEnd = 8;
            var search = reader.FindControl<TextBox>("SearchInput")!;
            search.Text = "exact";
            reader.FindControl<Button>("FirstMatch")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var findSummary = reader.FindControl<TextBlock>("FindSummary")!.Text;
            var conversationScroll = view.FindControl<ScrollViewer>("ConversationScroll")!;
            var originalOffset = conversationScroll.Offset;
            var calls = f.Admission.Calls;
            var expand = RequireExpansionButton(view);

            ActivateExpansion(window, expand, keyboard);
            window.UpdateLayout();

            panes.ColumnDefinitions[2].ActualWidth.Should().BeGreaterThan(originalDetailWidth + 100);
            panes.ColumnDefinitions[0].ActualWidth.Should().BeApproximately(320, 1);
            panes.ColumnDefinitions[2].ActualWidth.Should().BeGreaterThanOrEqualTo(400);
            expand.Content.Should().Be("Restore split");
            ControlAutomationPeer.CreatePeerForElement(expand)!.GetName().Should().Be("Restore split");
            view.FindControl<TextBox>("SessionComposer")!.IsEffectivelyVisible.Should().BeTrue();
            view.FindControl<GridSplitter>("DetailsDivider")!.IsEffectivelyVisible.Should().BeTrue();
            expand.IsFocused.Should().BeTrue();

            var divider = view.FindControl<GridSplitter>("DetailsDivider")!;
            divider.Focus().Should().BeTrue();
            window.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, null);
            window.KeyRelease(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, null);
            window.UpdateLayout();
            panes.ColumnDefinitions[0].ActualWidth.Should().BeGreaterThan(320, "expansion keeps manual keyboard resizing usable");
            panes.ColumnDefinitions[2].ActualWidth.Should().BeGreaterThanOrEqualTo(400);

            ActivateExpansion(window, expand, keyboard);
            window.UpdateLayout();

            panes.ColumnDefinitions[0].Width.Should().Be(new GridLength(2, GridUnitType.Star));
            panes.ColumnDefinitions[2].Width.Should().Be(new GridLength(1, GridUnitType.Star));
            panes.ColumnDefinitions[2].ActualWidth.Should().BeApproximately(originalDetailWidth, 1);
            expand.Content.Should().Be("Expand details");
            ControlAutomationPeer.CreatePeerForElement(expand)!.GetName().Should().Be("Expand details");
            window.Width.Should().Be(originalWindowWidth);
            window.Height.Should().Be(originalWindowHeight);
            view.GetVisualDescendants().OfType<DetailContentView>().Single().Should().BeSameAs(reader);
            text.Text.Should().Be("An exact report with wide source and an exact ending.");
            text.SelectionStart.Should().Be(2);
            text.SelectionEnd.Should().Be(8);
            search.Text.Should().Be("exact");
            reader.FindControl<TextBlock>("FindSummary")!.Text.Should().Be(findSummary);
            conversationScroll.Offset.Should().Be(originalOffset);
            f.Model.ComposerBody.Should().Be("independent unsent input");
            f.Model.SelectedSessionRecord.Should().Be(f.Conversation);
            f.Model.ConversationMessages.Should().ContainSingle();
            f.Admission.Calls.Should().Be(calls, "changing a reading split cannot submit or execute work");
        });
    }

    [Theory]
    [InlineData("hide")]
    [InlineData("session")]
    [InlineData("privacy")]
    public async Task RetiredExpansionCannotRevealContentOrChangeTheHiddenSplit(string boundary)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new Fixture();
            var view = await OpenExpansionResult(f);
            var panes = view.FindControl<Grid>("ReadingPanes")!;
            panes.ColumnDefinitions[0].Width = new(2, GridUnitType.Star);
            panes.ColumnDefinitions[2].Width = new(1, GridUnitType.Star);
            f.Window!.UpdateLayout();
            var reader = view.GetVisualDescendants().OfType<DetailContentView>().Single();
            var expand = RequireExpansionButton(view);
            ActivateExpansion(f.Window, expand, keyboard: false);
            if (string.Equals(boundary, "hide", StringComparison.Ordinal)) { view.HideDetails(); }
            else if (string.Equals(boundary, "session", StringComparison.Ordinal))
            { await f.Model.SelectAsync(f.Model.Sessions.First(row => row.Authority != f.Conversation.Authority)); }
            else
            {
                f.Native.Main.CloseForObservedPrivacyEvent("Synthetic expansion retirement", hidePresentation: true);
                await f.Native.Main.PrivacyClosureTask;
            }
            var hiddenConversationWidth = panes.ColumnDefinitions[0].Width;
            expand.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            f.Window.UpdateLayout();

            view.DetailsVisible.Should().BeFalse();
            reader.FindControl<TextBox>("ContentReader")!.Text.Should().BeEmpty();
            panes.ColumnDefinitions[0].Width.Should().Be(hiddenConversationWidth);
            panes.ColumnDefinitions[2].Width.Should().Be(new GridLength(0));
            expand.Content.Should().Be("Expand details");
            f.Admission.Calls.Should().Be(1);
        });
    }

    [Fact]
    public async Task ExpandedDetailsRetainsMinimumsAndAUsableDividerAtTheMinimumWindowWidth()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new Fixture();
            var view = await OpenExpansionResult(f);
            f.Window!.Width = f.Window.MinWidth;
            f.Window.UpdateLayout();
            var expand = RequireExpansionButton(view);
            ActivateExpansion(f.Window, expand, keyboard: true);
            f.Window.UpdateLayout();
            expand.Content.Should().Be("Restore split");
            var panes = view.FindControl<Grid>("ReadingPanes")!;
            panes.ColumnDefinitions[0].ActualWidth.Should().BeGreaterThanOrEqualTo(320);
            panes.ColumnDefinitions[2].ActualWidth.Should().BeGreaterThanOrEqualTo(400);
            var divider = view.FindControl<GridSplitter>("DetailsDivider")!;
            divider.Focus().Should().BeTrue();
            f.Window.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, null);
            f.Window.KeyRelease(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, null);
            f.Window.UpdateLayout();
            panes.ColumnDefinitions[0].ActualWidth.Should().BeGreaterThanOrEqualTo(320);
            panes.ColumnDefinitions[2].ActualWidth.Should().BeGreaterThanOrEqualTo(400);
            view.DetailsVisible.Should().BeTrue();
            f.Model.ComposerBody.Should().Be("independent unsent input");
            f.Admission.Calls.Should().Be(1);
        });
    }

    private static async Task<SessionInteractionView> OpenExpansionResult(Fixture f)
    {
        await f.Initialize();
        await f.Run("show sessions");
        await f.SelectConversation();
        f.Admission.AllowUnavailable = true;
        f.Model.ComposerBody = "An exact report with wide source and an exact ending.";
        await f.Model.SubmitConversationAsync();
        await f.Model.ReadConversationAsync();
        Dispatcher.UIThread.RunJobs();
        await f.Model.OpenConversationDetailAsync(f.Model.ConversationMessages.Single());
        f.Model.ComposerBody = "independent unsent input";
        f.Window!.Width = 1600;
        f.Window.Height = 1000;
        f.Window.UpdateLayout();
        return f.Window.SessionView;
    }

    private static Button RequireExpansionButton(SessionInteractionView view)
    {
        var button = view.FindControl<Button>("ExpandSessionDetails");
        button.Should().NotBeNull("the admitted native Details interaction includes in-place expansion");
        return button!;
    }

    private static void ActivateExpansion(Window window, Button button, bool keyboard)
    {
        button.Focus().Should().BeTrue();
        if (keyboard)
        {
            window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
            window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
        }
        else
        {
            button.BringIntoView();
            window.UpdateLayout();
            var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
        }
    }
}
