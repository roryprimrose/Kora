using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using AwesomeAssertions;

namespace Kora.Windows.IntegrationTests;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed class ObservationWorkflowWindowTests
{
    [Theory]
    [InlineData("approve")]
    [InlineData("cancel")]
    [InlineData("close")]
    [InlineData("escape")]
    [InlineData("token")]
    [InlineData("clear")]
    [InlineData("owner-close")]
    [InlineData("owner-hide")]
    public async Task ExactReviewOwnsModalInputKeyboardAndTermination(string decision)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            var owner = new ObservationWorkflowWindow(_ => Task.CompletedTask, static () => true);
            Window? review = null;
            using var opened = Window.WindowOpenedEvent.AddClassHandler<Window>((window, _) =>
            {
                if (!ReferenceEquals(window, owner)) { review = window; }
            });
            owner.Show();
            owner.Request.Focus().Should().BeTrue();
            var choosing = owner.Confirm("Exact review", "Complete original source", "Approve once",
                static () => true, cancellation.Token);
            try
            {
                var dialog = review!;
                Dispatcher.UIThread.RunJobs();
                AssertModalInput(owner, dialog);
                var source = dialog.GetLogicalDescendants().OfType<TextBox>().Single();
                source.IsReadOnly.Should().BeTrue();
                source.Text.Should().Be("Complete original source");
                dialog.FocusManager!.GetFocusedElement().Should().BeSameAs(source);
                dialog.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
                dialog.KeyRelease(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
                dialog.FocusManager.GetFocusedElement().Should().BeOfType<Button>().Which.Content.Should().Be("Approve once");
                choosing.IsCompleted.Should().BeFalse();
                switch (decision)
                {
                    case "approve": owner.CompleteApproval(true); break;
                    case "cancel":
                        dialog.GetLogicalDescendants().OfType<Button>().Last().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        break;
                    case "close": dialog.Close(); break;
                    case "escape":
                        dialog.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
                        break;
                    case "token": cancellation.Cancel(); break;
                    case "clear": owner.ClearReview(); break;
                    case "owner-close": owner.Close(); break;
                    case "owner-hide": owner.Hide(); break;
                }
                if (decision is "token")
                {
                    Func<Task> cancelled = async () => await choosing;
                    await cancelled.Should().ThrowAsync<OperationCanceledException>();
                }
                else { (await choosing).Should().Be(decision is "approve"); }
                dialog.IsVisible.Should().BeFalse();
                source.Text.Should().BeEmpty("retired exact review content must be erased");
                if (decision is not ("owner-close" or "owner-hide" or "token"))
                {
                    owner.IsVisible.Should().BeTrue();
                    owner.FocusManager!.GetFocusedElement().Should().BeSameAs(owner.Request);
                }
            }
            finally { review?.Close(); owner.Close(); }
        });
    }

    [Theory]
    [InlineData("approve")]
    [InlineData("retire")]
    [InlineData("token")]
    [InlineData("clear")]
    [InlineData("cancel")]
    public async Task ClickedApprovalCannotFinishBeforeNativeClosureOrSurviveRetirement(string scenario)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var current = true;
            var holdClose = true;
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            var owner = new ObservationWorkflowWindow(_ => Task.CompletedTask, () => current);
            Window? review = null;
            using var opened = Window.WindowOpenedEvent.AddClassHandler<Window>((window, _) =>
            {
                if (ReferenceEquals(window, owner)) { return; }
                review = window;
                window.Closing += (_, args) => args.Cancel = holdClose;
            });
            owner.Show();
            var choosing = owner.Confirm("Exact review", "Complete original source", "Approve once", () => current, cancellation.Token);
            try
            {
                owner.CompleteApproval(true);
                Dispatcher.UIThread.RunJobs();
                review!.IsVisible.Should().BeTrue();
                choosing.IsCompleted.Should().BeFalse("a choice is not native closure");
                if (scenario is "retire") { current = false; }
                if (scenario is "token") { cancellation.Cancel(); }
                if (scenario is "clear") { owner.ClearReview(); }
                if (scenario is "cancel") { owner.CompleteApproval(false); }
                Dispatcher.UIThread.RunJobs();
                choosing.IsCompleted.Should().BeFalse();
                holdClose = false;
                review.Close();
                (await choosing).Should().Be(scenario is "approve");
                review.IsVisible.Should().BeFalse();
            }
            finally { holdClose = false; review?.Close(); owner.Close(); }
        });
    }

    [Theory]
    [InlineData("clear")]
    [InlineData("token")]
    [InlineData("owner-close")]
    public async Task RetirementDuringNativeOpeningCannotLeaveAnOrphanReview(string scenario)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            var owner = new ObservationWorkflowWindow(_ => Task.CompletedTask, static () => true);
            Window? review = null;
            using var opened = Window.WindowOpenedEvent.AddClassHandler<Window>((window, _) =>
            {
                if (ReferenceEquals(window, owner)) { return; }
                review = window;
                if (scenario is "clear") { owner.ClearReview(); }
                if (scenario is "token") { cancellation.Cancel(); }
                if (scenario is "owner-close") { owner.Close(); }
            });
            owner.Show();
            try
            {
                var choosing = owner.Confirm("Exact review", "Private source", "Approve once", static () => true, cancellation.Token);
                if (scenario is "token")
                {
                    Func<Task> cancelled = async () => await choosing;
                    await cancelled.Should().ThrowAsync<OperationCanceledException>();
                }
                else { (await choosing).Should().BeFalse(); }
                review!.IsVisible.Should().BeFalse();
                review.Content.Should().BeNull();
            }
            finally { review?.Close(); owner.Close(); }
        });
    }

    private static void AssertModalInput(Window owner, Window review)
    {
        var ownerActivations = 0;
        var reviewActivations = 0;
        void OwnerActivated(object? sender, EventArgs args) => ownerActivations++;
        void ReviewActivated(object? sender, EventArgs args) => reviewActivations++;
        owner.Activated += OwnerActivated;
        review.Activated += ReviewActivated;
        try
        {
            // Headless exposes no public gesture for Avalonia's native disabled-owner input callback.
            var disabledInput = owner.PlatformImpl!.GetType().GetProperty("GotInputWhenDisabled",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            disabledInput.Should().NotBeNull();
            disabledInput!.GetValue(owner.PlatformImpl).Should().BeOfType<Action>().Which.Invoke();
            Dispatcher.UIThread.RunJobs();
            reviewActivations.Should().Be(1);
            ownerActivations.Should().Be(0);
        }
        finally
        {
            owner.Activated -= OwnerActivated;
            review.Activated -= ReviewActivated;
        }
    }
}
