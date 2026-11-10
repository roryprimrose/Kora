using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Chrome;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

using AwesomeAssertions;

using Kora.Core.Configuration;

namespace Kora.Windows.IntegrationTests;

public sealed partial class AdaptiveInteractionTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Compact_expand_button_opens_same_workbench_without_selecting_or_submitting(bool selected, bool keyboard)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new Fixture();
            await f.Initialize();
            var windows = new List<Window>();
            using var opened = Window.WindowOpenedEvent.AddClassHandler<Window>((window, _) => windows.Add(window));
            if (selected)
            {
                await f.Run("show sessions");
                await f.Run("select the first session");
                f.Model.ComposerBody = "Unsent selected-conversation input";
                f.Window!.FindControl<Button>("CollapseWorkbench")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }
            else { await f.Sessions.ShowInteractionAsync(false, static () => true); }
            var window = f.Window!;
            var originalSelection = f.Model.SelectedSessionRecord;
            f.Native.Main.CommandText = "Unsent global input";
            var expand = window.FindControl<Button>("ExpandSessions")!;
            window.IsExpanded.Should().BeFalse();
            expand.IsEffectivelyVisible.Should().BeTrue();
            expand.Content.Should().BeOfType<PathIcon>();
            ToolTip.GetTip(expand).Should().Be("Expand to the full workbench");
            AutomationProperties.GetName(expand).Should().Be("Expand to the full workbench");
            expand.BringIntoView();
            window.UpdateLayout();
            expand.Focus().Should().BeTrue();
            if (keyboard)
            {
                window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
                window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
            }
            else { expand.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }
            await f.Sessions.InteractionOperation;
            f.Window.Should().BeSameAs(window);
            window.IsExpanded.Should().BeTrue();
            window.CanResize.Should().BeTrue();
            expand.IsEffectivelyVisible.Should().BeFalse();
            window.FindControl<Button>("CollapseWorkbench")!.IsEffectivelyVisible.Should().BeTrue();
            f.Model.SelectedSessionRecord.Should().Be(originalSelection);
            f.Model.ComposerBody.Should().Be(selected ? "Unsent selected-conversation input" : string.Empty);
            f.Native.Main.CommandText.Should().Be("Unsent global input");
            f.Model.ConversationMessages.Should().BeEmpty();
            f.Admission.Calls.Should().Be(0);
            windows.Distinct().Should().Equal(window);
        });
    }

    [Fact]
    public async Task Caption_roles_are_native_and_collapse_keeps_one_window_and_unsent_input()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new Fixture();
            await f.Initialize();
            await f.Run("show sessions");
            await f.Run("select the first session");
            f.Model.ComposerBody = "unchanged by caption actions";
            var window = f.Window!;
            window.ShowInTaskbar.Should().BeTrue();
            window.ExtendClientAreaToDecorationsHint.Should().BeTrue();
            window.CanMaximize.Should().BeTrue();
            var collapse = window.FindControl<Button>("CollapseWorkbench")!;
            var minimize = window.FindControl<Button>("MinimizeWorkbench")!;
            var maximize = window.FindControl<Button>("MaximizeWorkbench")!;
            var close = window.FindControl<Button>("CloseWorkbench")!;
            var buttons = ((StackPanel)collapse.Parent!).Children;
            buttons.Should().Equal(collapse, minimize, maximize, close);
            WindowDecorationProperties.GetElementRole(collapse).Should().Be(WindowDecorationsElementRole.User);
            WindowDecorationProperties.GetElementRole(minimize).Should().Be(WindowDecorationsElementRole.MinimizeButton);
            WindowDecorationProperties.GetElementRole(maximize).Should().Be(WindowDecorationsElementRole.MaximizeButton);
            WindowDecorationProperties.GetElementRole(close).Should().Be(WindowDecorationsElementRole.CloseButton);
            maximize.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            window.WindowState.Should().Be(WindowState.Maximized);
            AutomationProperties.GetName(maximize).Should().Be("Restore");
            maximize.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            window.WindowState.Should().Be(WindowState.Normal);
            collapse.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            window.IsExpanded.Should().BeFalse();
            collapse.IsEffectivelyVisible.Should().BeFalse();
            f.Model.ComposerBody.Should().Be("unchanged by caption actions");
            await f.Run("show sessions");
            f.Window.Should().BeSameAs(window);
            f.Admission.Calls.Should().Be(0);
        });
    }

    [Fact]
    public async Task Narrow_navigation_is_reachable_and_captured_ordinals_do_not_revive_after_resize()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new Fixture();
            await f.Initialize();
            await f.Run("show sessions");
            var window = f.Window!;
            var captured = f.Sessions.CaptureSessionSelection(1, static () => true);
            window.Width = 640;
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            window.IsSessionListPresented.Should().BeFalse();
            window.IsExpanded.Should().BeTrue();
            var show = window.SessionView.FindControl<Button>("ToggleSessionSidebar")!;
            show.IsEffectivelyVisible.Should().BeTrue();
            show.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            window.IsSessionListPresented.Should().BeTrue();
            var back = window.SessionView.FindControl<Button>("HideSessionSidebar")!;
            back.IsEffectivelyVisible.Should().BeTrue();
            back.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            window.Width = 1120;
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            await captured();
            f.Model.SelectedSessionRecord.Should().BeNull();
            f.Model.ConversationMessages.Should().BeEmpty();
            f.Admission.Calls.Should().Be(0);
        });
    }

    [Fact]
    public async Task Minimize_restore_and_unhandled_Escape_do_not_cancel_or_retarget_the_composer()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new Fixture();
            await f.Initialize();
            await f.Run("show sessions");
            await f.Run("select the first session");
            f.Model.ComposerBody = "keep through minimize";
            var window = f.Window!;
            window.FindControl<Button>("MinimizeWorkbench")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            window.WindowState.Should().Be(WindowState.Minimized);
            window.IsSessionWorkspacePresented.Should().BeFalse();
            f.Native.Main.ShowPresentation();
            window.WindowState.Should().Be(WindowState.Minimized, "passive response presentation cannot restore a minimized shell");
            f.Native.Main.ShowApplication();
            window.WindowState.Should().Be(WindowState.Normal);
            f.Model.ComposerBody.Should().Be("keep through minimize");
            window.Activate();
            window.Focus();
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            window.IsVisible.Should().BeFalse();
            f.Model.ComposerBody.Should().Be("keep through minimize");
            f.Admission.Calls.Should().Be(0);
        });
    }

    [Fact]
    public async Task Placement_readback_preserves_separate_mode_bounds_and_normal_restore_rectangle()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new Fixture();
            await f.Initialize();
            var saved = new MemoryPlacement
            {
                Value = new(new(30, 40, 500, 600, 1), new(100, 50, 1280, 820, 1), false),
            };
            var window = new ResponseWindow(f.Native.Main, null, null, () => new PixelRect(0, 0, 1920, 1080), saved);
            try
            {
                window.ShowResponse();
                await window.PlacementOperation;
                window.Position.Should().Be(new PixelPoint(30, 40));
                window.Width.Should().Be(500);
                window.ShowSessions(true);
                window.Position.Should().Be(new PixelPoint(100, 50));
                window.Width.Should().Be(1280);
                window.FindControl<Button>("MaximizeWorkbench")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                window.FindControl<Button>("MinimizeWorkbench")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                window.ShowSessions(true);
                window.WindowState.Should().Be(WindowState.Maximized);
                window.FindControl<Button>("CollapseWorkbench")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                window.WindowState.Should().Be(WindowState.Normal);
                window.Position.Should().Be(new PixelPoint(30, 40));
                window.HideResponse();
                await window.PlacementOperation;
                saved.Value!.Expanded!.Width.Should().Be(1280);
                saved.Value.ExpandedMaximized.Should().BeTrue();
                saved.Value.Compact!.X.Should().Be(30);
            }
            finally
            {
                window.Close();
                await window.PlacementOperation;
            }
        });
    }

    [Theory]
    [InlineData(320)]
    [InlineData(640)]
    public async Task Narrow_Details_and_chat_switch_release_private_reader_and_keep_unsent_input(int width)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new Fixture();
            var view = await OpenExpansionResult(f);
            var window = f.Window!;
            var originalReader = view.GetVisualDescendants().OfType<DetailContentView>().Single();
            var source = originalReader.FindControl<TextBox>("ContentReader")!;
            window.Width = width;
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            view.DetailsVisible.Should().BeTrue();
            view.FindControl<Grid>("ConversationPane")!.IsEffectivelyVisible.Should().BeFalse();
            var chat = view.FindControl<Button>("ShowChatPane")!;
            chat.IsEffectivelyVisible.Should().BeTrue();
            source.Text.Should().Be("An exact report with wide source and an exact ending.");
            var admissionCalls = f.Admission.Calls;
            chat.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            window.UpdateLayout();
            source.Text.Should().BeEmpty();
            view.DetailsVisible.Should().BeFalse();
            view.FindControl<TextBox>("SessionComposer")!.IsEffectivelyVisible.Should().BeTrue();
            f.Model.ComposerBody.Should().Be("independent unsent input");
            f.Admission.Calls.Should().Be(admissionCalls);
            window.IsExpanded.Should().BeTrue();
        });
    }

    [Fact]
    public async Task Delayed_placement_read_does_not_overwrite_user_geometry_in_either_mode()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new Fixture();
            await f.Initialize();
            using var preferences = new DelayedPlacement();
            var window = new ResponseWindow(f.Native.Main, null, null,
                () => new PixelRect(0, 0, 1920, 1080), preferences);
            try
            {
                window.ShowResponse();
                await preferences.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
                window.Position = new(55, 65);
                window.Width = 520;
                window.Height = 630;
                window.ShowSessions(true);
                window.Position = new(220, 30);
                window.Width = 1360;
                window.Height = 900;
                preferences.Release();
                await window.PlacementOperation;
                window.Position.Should().Be(new PixelPoint(220, 30));
                window.Width.Should().Be(1360);
                window.FindControl<Button>("CollapseWorkbench")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                window.Position.Should().Be(new PixelPoint(55, 65));
                window.Width.Should().Be(520);
                window.HideResponse();
                await window.PlacementOperation;
                preferences.Saved!.Compact!.Width.Should().Be(520);
                preferences.Saved.Expanded!.Width.Should().Be(1360);
            }
            finally
            {
                preferences.Release();
                window.Close();
                await window.PlacementOperation;
            }
        });
    }

    private sealed class DelayedPlacement : IWorkbenchPlacementPreferences, IDisposable
    {
        private readonly ManualResetEventSlim release = new(false);
        internal TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal WorkbenchPlacement? Saved { get; private set; }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "VSTHRD002",
            Justification = "Blocks only the deterministic preference fake on its Task.Run worker; the UI owns the release barrier.")]
        public WorkbenchPlacement? Load()
        {
            Started.TrySetResult();
            release.Wait();
            return new(new(10, 10, 480, 570, 1), new(20, 20, 1120, 820, 1), false);
        }

        public void Save(WorkbenchPlacement placement) => Saved = placement;
        internal void Release() => release.Set();
        public void Dispose() => release.Dispose();
    }

    private sealed class MemoryPlacement : IWorkbenchPlacementPreferences
    {
        internal WorkbenchPlacement? Value { get; set; }
        public WorkbenchPlacement? Load() => Value;
        public void Save(WorkbenchPlacement placement) => Value = placement;
    }
}
