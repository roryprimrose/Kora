using System.ComponentModel;
using System.Runtime.InteropServices;

using AwesomeAssertions;

using Kora.Windows.IntegrationTests.Audio;
using Kora.Windows.Presentation;

namespace Kora.Windows.IntegrationTests.Presentation;

public sealed partial class WindowsPresenceWindowInputTests
{
    private const uint TransparentStyle = 0x20;
    private const uint LayeredStyle = 0x80000;
    private const uint NoActivateStyle = 0x8000000;

    [Theory]
    [InlineData(0u)]
    [InlineData(0x8000088u)]
    [InlineData(0x200000u)]
    public void Window_styles_preserve_unrelated_flags_and_never_activate_the_presence(uint original)
    {
        var passthrough = WindowsPresenceWindowInput.GetExtendedStyle(original, intercept: false);
        var interactive = WindowsPresenceWindowInput.GetExtendedStyle(passthrough, intercept: true);

        passthrough.Should().Be(original | TransparentStyle | LayeredStyle | NoActivateStyle);
        interactive.Should().Be((original | LayeredStyle | NoActivateStyle) & ~TransparentStyle);
        WindowsPresenceWindowInput.GetExtendedStyle(interactive, intercept: false).Should().Be(passthrough);
    }

    [Fact]
    public void Presence_starts_click_through_and_intercepts_only_while_control_is_held()
    {
        var native = new FakeNative();
        var input = new WindowsPresenceWindowInput(1, native);

        input.InterceptsMouse.Should().BeFalse();
        native.Style.Should().Be(TransparentStyle | LayeredStyle | NoActivateStyle);
        input.Refresh(canInteract: true).Should().BeFalse();
        native.ControlPressed = true;
        input.Refresh(canInteract: true).Should().BeTrue();
        (native.Style & TransparentStyle).Should().Be(0);
        native.ControlPressed = false;
        input.Refresh(canInteract: true).Should().BeFalse();
        (native.Style & TransparentStyle).Should().Be(TransparentStyle);
        native.Writes.Should().Be(3);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Ctrl_changes_do_not_transfer_an_in_progress_mouse_gesture(bool beginsOnPresence)
    {
        var native = new FakeNative { ControlPressed = beginsOnPresence };
        var input = new WindowsPresenceWindowInput(1, native);
        input.Refresh(canInteract: true).Should().Be(beginsOnPresence);

        native.MouseButtonPressed = true;
        native.ControlPressed = !beginsOnPresence;
        input.Refresh(canInteract: true).Should().Be(beginsOnPresence);

        native.MouseButtonPressed = false;
        input.Refresh(canInteract: true).Should().Be(!beginsOnPresence);
    }

    [Fact]
    public void Hidden_or_ineligible_presence_cannot_capture_input_even_during_a_gesture()
    {
        var native = new FakeNative { ControlPressed = true };
        var input = new WindowsPresenceWindowInput(1, native);
        input.Refresh(canInteract: true).Should().BeTrue();
        native.MouseButtonPressed = true;

        input.Refresh(canInteract: false).Should().BeFalse();
        (native.Style & TransparentStyle).Should().Be(TransparentStyle);
    }

    [Fact]
    public void Native_failures_propagate_without_claiming_the_input_mode_changed()
    {
        var native = new FakeNative();
        var input = new WindowsPresenceWindowInput(1, native);
        native.ControlPressed = true;
        native.WriteException = new Win32Exception(5);

        var action = () => input.Refresh(canInteract: true);

        action.Should().Throw<Win32Exception>();
        input.InterceptsMouse.Should().BeFalse();
    }

    [Fact]
    public void Missing_native_handle_is_rejected()
    {
        var action = () => new WindowsPresenceWindowInput(nint.Zero, new FakeNative());

        action.Should().Throw<ArgumentException>().WithParameterName("window");
    }

    [Fact]
    public void Move_cursor_updates_during_interception_even_without_pointer_movement()
    {
        var native = new FakeNative();
        var input = new WindowsPresenceWindowInput(1, native);
        input.Refresh(canInteract: true);
        native.MoveCursorUpdates.Should().Be(0);

        native.ControlPressed = true;
        input.Refresh(canInteract: true);
        native.MoveCursorUpdates.Should().Be(1);
        input.Refresh(canInteract: true);
        native.MoveCursorUpdates.Should().Be(2);

        native.ControlPressed = false;
        input.Refresh(canInteract: true);
        native.MoveCursorUpdates.Should().Be(2);
    }

    [Fact]
    public void Move_cursor_is_not_requested_for_a_hidden_or_privacy_ineligible_presence()
    {
        var native = new FakeNative { ControlPressed = true };
        var input = new WindowsPresenceWindowInput(1, native);

        input.Refresh(canInteract: false);

        native.MoveCursorUpdates.Should().Be(0);
    }

    [Fact]
    public void Cursor_failures_propagate_for_the_window_recovery_handler()
    {
        var native = new FakeNative
        {
            ControlPressed = true,
            CursorException = new Win32Exception(5),
        };
        var input = new WindowsPresenceWindowInput(1, native);
        var action = () => input.Refresh(canInteract: true);

        action.Should().Throw<Win32Exception>();
    }

    [WindowsFact]
    public void Native_move_cursor_uses_the_shared_four_way_Windows_cursor()
    {
        WindowsPresenceInputNative.LoadMoveCursor().Should().Be(LoadCursor(0, 32646));
    }

    [WindowsFact]
    public void Native_window_styles_switch_click_through_without_changing_position_or_activation_flags()
    {
        var handle = CreateWindowEx(0x88, "STATIC", string.Empty, 0x80000000,
            -30000, -30000, 100, 100, 0, 0, 0, 0);
        handle.Should().NotBe(nint.Zero);
        var native = new WindowsPresenceInputNative();
        try
        {
            var original = native.GetExtendedStyle(handle);
            var fake = new FakeNative(native) { ControlPressed = true };
            var input = new WindowsPresenceWindowInput(handle, fake);
            native.GetExtendedStyle(handle).Should().Be(
                WindowsPresenceWindowInput.GetExtendedStyle(original, intercept: false));
            GetLayeredWindowAttributes(handle, out _, out var alpha, out var flags).Should().BeTrue();
            alpha.Should().Be(byte.MaxValue);
            flags.Should().Be(2u);

            input.Refresh(canInteract: true).Should().BeTrue();
            native.GetExtendedStyle(handle).Should().Be(
                WindowsPresenceWindowInput.GetExtendedStyle(original, intercept: true));
            fake.ControlPressed = false;
            input.Refresh(canInteract: true).Should().BeFalse();
            native.GetExtendedStyle(handle).Should().Be(
                WindowsPresenceWindowInput.GetExtendedStyle(original, intercept: false));
            GetWindowRect(handle, out var rectangle).Should().BeTrue();
            rectangle.Left.Should().Be(-30000);
            rectangle.Top.Should().Be(-30000);
        }
        finally
        {
            DestroyWindow(handle).Should().BeTrue();
        }
    }

    [WindowsFact]
    public void Native_operations_reject_a_destroyed_handle()
    {
        var native = new WindowsPresenceInputNative();
        var read = () => native.GetExtendedStyle(-1);
        var write = () => native.SetExtendedStyle(-1, 0);
        var render = () => native.EnableLayeredRendering(-1);

        read.Should().Throw<Win32Exception>();
        write.Should().Throw<Win32Exception>();
        render.Should().Throw<Win32Exception>();
    }

    [WindowsFact]
    public async Task Native_hit_testing_reaches_the_underlying_window_except_during_Ctrl_interaction()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                VerifyNativeHitTesting();
                completion.SetResult();
            }
            catch (Exception exception)
            {
                completion.SetException(exception);
            }
        }) { IsBackground = true };
        thread.Start();
        await completion.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
    }

    private static void VerifyNativeHitTesting()
    {
        // A separate, never-activated desktop keeps test windows away from the user's desktop.
        var previousDesktop = GetThreadDesktop(GetCurrentThreadId());
        var desktop = CreateDesktop($"KoraPresenceTest_{Guid.NewGuid():N}", 0, 0, 0, 0x83, 0);
        desktop.Should().NotBe(nint.Zero);
        nint underlying = 0;
        nint presence = 0;
        try
        {
            SetThreadDesktop(desktop).Should().BeTrue();
            underlying = CreateWindowEx(0, "STATIC", string.Empty, 0x90000104,
                10, 10, 100, 100, 0, 0, 0, 0);
            presence = CreateWindowEx(8, "STATIC", string.Empty, 0x90000104,
                10, 10, 100, 100, 0, 0, 0, 0);
            underlying.Should().NotBe(nint.Zero);
            presence.Should().NotBe(nint.Zero);
            var point = new NativePoint(50, 50);
            WindowFromPoint(point).Should().Be(presence);
            var native = new FakeNative(new WindowsPresenceInputNative());
            var input = new WindowsPresenceWindowInput(presence, native);

            WindowFromPoint(point).Should().Be(underlying);
            native.ControlPressed = true;
            input.Refresh(canInteract: true).Should().BeTrue();
            WindowFromPoint(point).Should().Be(presence);
            native.ControlPressed = false;
            input.Refresh(canInteract: true).Should().BeFalse();
            WindowFromPoint(point).Should().Be(underlying);
        }
        finally
        {
            if (presence != 0)
            {
                DestroyWindow(presence).Should().BeTrue();
            }
            if (underlying != 0)
            {
                DestroyWindow(underlying).Should().BeTrue();
            }
            SetThreadDesktop(previousDesktop).Should().BeTrue();
            CloseDesktop(desktop).Should().BeTrue();
        }
    }

    private sealed class FakeNative(IWindowsPresenceInputNative? actual = null) : IWindowsPresenceInputNative
    {
        public bool ControlPressed { get; set; }
        public bool MouseButtonPressed { get; set; }
        public uint Style { get; private set; }
        public int Writes { get; private set; }
        public Win32Exception? WriteException { get; set; }
        public Win32Exception? CursorException { get; set; }
        public int MoveCursorUpdates { get; private set; }

        public bool IsControlPressed() => ControlPressed;
        public bool IsMouseButtonPressed() => MouseButtonPressed;
        public uint GetExtendedStyle(nint window) => actual?.GetExtendedStyle(window) ?? Style;
        public void SetExtendedStyle(nint window, uint style)
        {
            if (WriteException is not null)
            {
                throw WriteException;
            }
            actual?.SetExtendedStyle(window, style);
            Style = style;
            Writes++;
        }
        public void EnableLayeredRendering(nint window) => actual?.EnableLayeredRendering(window);
        public void ShowMoveCursorIfOverWindow(nint window)
        {
            if (CursorException is not null)
            {
                throw CursorException;
            }
            MoveCursorUpdates++;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rectangle
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct NativePoint(int X, int Y);

    [LibraryImport("user32.dll", EntryPoint = "CreateWindowExW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint CreateWindowEx(uint extendedStyle, string className, string title, uint style,
        int x, int y, int width, int height, nint parent, nint menu, nint instance, nint parameter);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyWindow(nint window);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetLayeredWindowAttributes(nint window, out uint color, out byte alpha, out uint flags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetWindowRect(nint window, out Rectangle rectangle);

    [LibraryImport("user32.dll", EntryPoint = "CreateDesktopW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint CreateDesktop(string name, nint device, nint mode, uint flags, uint access, nint security);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetThreadDesktop(nint desktop);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseDesktop(nint desktop);

    [LibraryImport("user32.dll")]
    private static partial nint GetThreadDesktop(uint thread);

    [LibraryImport("kernel32.dll")]
    private static partial uint GetCurrentThreadId();

    [LibraryImport("user32.dll")]
    private static partial nint WindowFromPoint(NativePoint point);

    [LibraryImport("user32.dll", EntryPoint = "LoadCursorW")]
    private static partial nint LoadCursor(nint instance, nint cursorName);
}
