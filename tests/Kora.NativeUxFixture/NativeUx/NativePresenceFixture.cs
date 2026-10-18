using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.LogicalTree;

using Kora.Application.Visuals;
using Kora.Controls;
using Kora.Core;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Windows.Presentation;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging;

namespace Kora.NativeUxFixture;

internal static partial class NativePresenceFixture
{
    private static readonly JsonSerializerOptions Serialization = new() { WriteIndented = true };
    public static int Run(string scratchParent)
    {
        var output = Path.Combine(scratchParent, $"presence-integration-{Guid.NewGuid():N}.json");
        Console.WriteLine($"Synthetic production presence integration only. Output: {output}");
        Console.WriteLine("No real host startup, microphone, speech provider, model, clipboard, network or user preferences. Replacement tests use scratch-only services.");
        var desktop = CreateDesktop($"KoraPresenceFixture_{Guid.NewGuid():N}", 0, 0, 0, 0x1FF, 0);
        if (desktop == 0) { throw new System.ComponentModel.Win32Exception(Marshal.GetLastPInvokeError()); }
        var exitCode = 1;
        var thread = new Thread(() =>
        {
            try
            {
                if (!ImmDisableIME(GetCurrentThreadId()) || !SetThreadDesktop(desktop))
                {
                    throw new System.ComponentModel.Win32Exception(Marshal.GetLastPInvokeError());
                }
                exitCode = AppBuilder.Configure(() => new FixtureApplication(output))
                    .UsePlatformDetect().StartWithClassicDesktopLifetime([]);
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(exception);
            }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        try
        {
            thread.Start();
            if (!thread.Join(TimeSpan.FromSeconds(30)))
            {
                Console.Error.WriteLine("The isolated native fixture did not terminate within its bound.");
                exitCode = 1;
            }
        }
        finally
        {
            if (!CloseDesktop(desktop))
            {
                Console.Error.WriteLine(new System.ComponentModel.Win32Exception(Marshal.GetLastPInvokeError()));
                exitCode = 1;
            }
        }
        return exitCode;
    }

    [LibraryImport("user32.dll", EntryPoint = "CreateDesktopW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial nint CreateDesktop(string name, nint device, nint mode, uint flags, uint access, nint security);
    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetThreadDesktop(nint desktop);
    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseDesktop(nint desktop);
    [LibraryImport("imm32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ImmDisableIME(uint thread);
    [LibraryImport("kernel32.dll")]
    private static partial uint GetCurrentThreadId();

    private sealed class FixtureApplication(string output) : Avalonia.Application
    {
        public override void Initialize() => Styles.Add(new Avalonia.Themes.Fluent.FluentTheme());

        public override void OnFrameworkInitializationCompleted()
        {
            var desktop = ApplicationLifetime as IClassicDesktopStyleApplicationLifetime
                ?? throw new InvalidOperationException("The fixture requires a real desktop lifetime.");
            desktop.MainWindow = new FixtureWindow(desktop, output);
            base.OnFrameworkInitializationCompleted();
        }
    }

    private sealed partial class FixtureWindow : Window, IDisposable
    {
        private readonly NativePresenceHost host;
        private readonly PresenceControl legacy;
        private readonly Grid panel = new();
        private readonly SyntheticInputNative inputNative = new();
        private readonly WindowsPresenceWindowInput presenceInput;
        private readonly IClassicDesktopStyleApplicationLifetime desktop;
        private readonly string output;
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private readonly List<RendererSwitchSample> rendererSwitchSamples = [];
        private PresenceRenderInputs inputs = new(AssistantState.Hidden, false, 0, true, 200,
            200, 200, 200, false, true, true, 600, 600, 1);
        private Exception? failure;
        private bool eligible = true;
        private int samples;
        private nint underlying;
        private Window? underlyingWindow;

        public FixtureWindow(IClassicDesktopStyleApplicationLifetime desktop, string output)
        {
            this.desktop = desktop;
            this.output = output;
            Width = Height = 600;
            Background = Avalonia.Media.Brushes.Transparent;
            TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
            WindowDecorations = WindowDecorations.None;
            Topmost = true;
            ShowActivated = false;
            ShowInTaskbar = false;
            IsHitTestVisible = false;
            legacy = new PresenceControl { IsPresentationEligible = true };
            legacy.SetAnimationEnabled(false);
            Content = legacy;
            Opacity = 0;
            Win32Properties.AddWindowStylesCallback(this, (style, extended) =>
                (style, extended | 0x08000000));
            presenceInput = new WindowsPresenceWindowInput(TryGetPlatformHandle()!.Handle, inputNative,
                new WindowsPresenceRegionNative());
            host = new NativePresenceHost(
                () => eligible && IsVisible && Opacity > 0 && legacy.IsPresentationEligible
                    ? inputs with { Width = hostWidth(), Height = hostHeight() } : null,
                () =>
                {
                    samples++;
                    inputs = inputs with { IsSpeaking = true, SpeechOutputLevel = samples % 2 == 0 ? 0.1 : 0.9 };
                },
                exception => failure = exception, NullLogger<PresenceRenderWorker>.Instance);
            legacy.IsVisible = false;
            Content = null;
            panel.Children.Add(legacy);
            panel.Children.Add(host);
            Content = panel;
            Loaded += OnLoaded;
            Closed += (_, _) => Dispose();

            double hostWidth() => host?.Bounds.Width ?? 600;
            double hostHeight() => host?.Bounds.Height ?? 600;
        }

        private async void OnLoaded(object? sender, Avalonia.Interactivity.RoutedEventArgs args)
        {
            try
            {
                await Task.Delay(150);
                Require(host.FramesPresented == 0 && !IsWindowVisible(host.NativeHandle),
                    "Initial zero-opacity shell admitted native frames.");
                Opacity = 1;
                host.Refresh(false);
                await Task.Delay(200);
                Require(!host.CanInteract, "Hidden state acquired an interactive positive-opacity frame.");
                inputs = inputs with { State = AssistantState.Information };
                host.Refresh(false);
                await Task.Delay(600);
                Require(failure is null, $"Native startup failed: {failure}");
                Require(host.FramesPresented >= 3, "No sustained native popup frames.");
                Require(host.CanInteract,
                    "Information/reduced-motion admission never presented a positive-opacity frame.");
                var rootHandle = TryGetPlatformHandle()!.Handle;
                Require(GetWindow(host.NativeHandle, 4) == rootHandle && GetAncestor(host.NativeHandle, 2) == host.NativeHandle,
                    "The native surface is not an owned top-level popup.");
                Require((inputNative.GetExtendedStyle(rootHandle) & 0x80000) == 0 && !presenceInput.InterceptsMouse,
                    "The native path forced layering on the Avalonia shell.");
                Require(ActualTransparencyLevel == WindowTransparencyLevel.Transparent,
                    "The native path requires a transparent non-layered shell.");
                Require(GetWindowRect(rootHandle, out var bounds), "Could not inspect the fixture window bounds.");
                var point = new NativePoint
                {
                    X = (bounds.Left + bounds.Right) / 2,
                    Y = (bounds.Top + bounds.Bottom) / 2,
                };
                underlyingWindow = new Window
                {
                    Width = Width, Height = Height, Background = Avalonia.Media.Brushes.Black,
                    WindowDecorations = WindowDecorations.None, Topmost = true, ShowActivated = false,
                    ShowInTaskbar = false, Position = Position,
                };
                underlyingWindow.Show();
                underlyingWindow.Position = Position;
                underlying = underlyingWindow.TryGetPlatformHandle()!.Handle;
                Require(SetWindowPos(underlying, -1, 0, 0, 0, 0, 0x13)
                    && SetWindowPos(rootHandle, -1, 0, 0, 0, 0, 0x13),
                    "Could not order the two synthetic routing windows without activation.");
                await Task.Delay(250);
                var passiveHit = WindowFromPoint(point);
                Require(GetWindowRect(underlying, out var underlyingBounds), "Could not inspect the synthetic routing target.");
                Require(GetAncestor(passiveHit, 2) == underlying,
                    $"Empty-region routing failed: shellHit={passiveHit == rootHandle}, "
                    + $"popupHit={passiveHit == host.NativeHandle}, underlyingVisible={IsWindowVisible(underlying)}, "
                    + $"shellDescendant={GetAncestor(passiveHit, 2) == rootHandle}, "
                    + $"noTarget={passiveHit == 0}, point=({point.X},{point.Y}), "
                    + $"syntheticBounds=({underlyingBounds.Left},{underlyingBounds.Top},{underlyingBounds.Right},{underlyingBounds.Bottom}).");
                inputNative.ControlPressed = true;
                IsHitTestVisible = presenceInput.Refresh(host.CanInteract);
                Require(IsHitTestVisible, "Synthetic Ctrl did not enable host-owned interaction.");
                Require(inputNative.CursorRequests > 0, "Ctrl interaction did not request the host move cursor.");
                await Task.Delay(150);
                var hit = WindowFromPoint(point);
                Require(GetAncestor(hit, 2) == rootHandle,
                    $"Synthetic Ctrl did not route to the shell: nativeChildHit={hit == host.NativeHandle}, "
                    + $"childVisible={IsWindowVisible(host.NativeHandle)}, positiveOpacityPresented={host.CanInteract}, "
                    + $"interceptsMouse={presenceInput.InterceptsMouse}, "
                    + $"rootClickThrough={(inputNative.GetExtendedStyle(rootHandle) & 0x20) != 0}.");
                inputNative.ControlPressed = false;
                IsHitTestVisible = presenceInput.Refresh(host.CanInteract);
                Require(!IsHitTestVisible, "Releasing synthetic Ctrl did not restore click-through.");
                Require(GetAncestor(WindowFromPoint(point), 2) == underlying,
                    "Releasing Ctrl did not restore routing to the synthetic underlying top level.");
                inputs = inputs with { State = AssistantState.Calculating, ReducedMotion = false };
                host.Refresh(false);
                host.Margin = new Thickness(12, 18, 24, 30);
                await Task.Delay(250);
                Require(GetWindowRect(host.NativeHandle, out var childBounds), "Could not inspect the native child bounds.");
                var origin = new NativePoint();
                Require(ClientToScreen(rootHandle, ref origin), "Could not resolve the shell client origin.");
                Require(childBounds.Left == origin.X + (int)Math.Round(host.Bounds.X * RenderScaling)
                    && childBounds.Top == origin.Y + (int)Math.Round(host.Bounds.Y * RenderScaling),
                    "The popup did not track host-relative layout at the current DPI.");
                host.Margin = default;
                Position = new PixelPoint(Position.X + 20, Position.Y + 12);
                await Task.Delay(150);
                origin = default;
                Require(GetWindowRect(host.NativeHandle, out var movedBounds) && ClientToScreen(rootHandle, ref origin),
                    "Could not inspect synthetic move geometry.");
                Require(movedBounds.Left == origin.X + (int)Math.Round(host.Bounds.X * RenderScaling)
                    && movedBounds.Top == origin.Y + (int)Math.Round(host.Bounds.Y * RenderScaling),
                    $"Popup move mismatch: popup=({movedBounds.Left},{movedBounds.Top}), "
                    + $"ownerOrigin=({origin.X},{origin.Y}), anchor=({host.Bounds.X},{host.Bounds.Y}), scaling={RenderScaling}.");
                WindowState = WindowState.Minimized;
                Require(!host.CanInteract && !IsWindowVisible(host.NativeHandle),
                    "Minimizing the owner left its popup visible or interactive.");
                var minimizedFrames = host.FramesPresented;
                await Task.Delay(150);
                Require(host.FramesPresented == minimizedFrames, "Minimized owner admitted queued pixels.");
                WindowState = WindowState.Normal;
                host.Refresh(false);
                await Task.Delay(350);
                Require(failure is null && host.FramesPresented > minimizedFrames && host.CanInteract,
                    "Restoring the owner did not admit a fresh positive-opacity frame.");
                var first = host.FramesPresented;
                inputs = inputs with { IsLightTheme = true, Scaling = 1.5, Density = 25, DotSize = 50 };
                Width = Height = 360;
                host.Refresh(true);
                await Task.Delay(350);
                Require(failure is null && host.FramesPresented > first && samples > 0,
                    $"Resizing, DPI projection or sampled speech failed: {failure}");
                host.Refresh(false);
                var sampled = samples;
                host.IsVisible = false;
                var locallyHiddenFrames = host.FramesPresented;
                Require(!IsWindowVisible(host.NativeHandle) && !host.CanInteract,
                    "A logically hidden native control remained visible or interactive.");
                await Task.Delay(150);
                Require(host.FramesPresented == locallyHiddenFrames,
                    "A queued frame survived logical native-control hiding.");
                host.IsVisible = true;
                eligible = false;
                host.Suspend();
                Hide();
                Require(!IsWindowVisible(host.NativeHandle), "The child outlived its shell's native visibility.");
                var hiddenFrames = host.FramesPresented;
                await Task.Delay(150);
                Require(host.FramesPresented == hiddenFrames && samples == sampled,
                    "A hidden or ineligible native surface kept publishing or sampling playback.");
                eligible = true;
                inputs = inputs with { State = AssistantState.Failure, ReducedMotion = true, IsSpeaking = false };
                Show();
                host.Refresh(false);
                await Task.Delay(600);
                Require(failure is null && host.FramesPresented > hiddenFrames, $"Native reveal failed: {failure}");
                var detachedHandle = host.NativeHandle;
                Content = null;
                Require(!IsWindow(detachedHandle), "Detaching left a native child or owned drawing worker alive.");
                var detachedFrames = host.FramesPresented;
                Content = panel;
                await Task.Delay(600);
                Require(failure is null && GetWindow(host.NativeHandle, 4) == rootHandle
                    && host.FramesPresented > detachedFrames, $"Reattachment failed: {failure}");
                host.Suspend();
                var frames = host.FramesPresented;
                host.Dispose();
                await Task.Delay(100);
                Require(host.FramesPresented == frames, "Queued native pixels survived disposal.");
                var replacements = await VerifyWindowReplacementAsync();
                var result = new
                {
                    Success = true,
                    FramesPresented = frames,
                    PlaybackSamples = samples,
                    ProductionWindowReplacements = replacements,
                    TrayRevealSamples = revealSamples,
                    RendererSwitchSamples = rendererSwitchSamples,
                    ElapsedSeconds = clock.Elapsed.TotalSeconds,
                    Checks = new[] { "initial-opacity-gate", "hidden-legacy-sibling", "hidden-to-information",
                        "positive-opacity-presentation", "non-layered-root", "owned-popup", "empty-region-passthrough",
                        "full-region-ctrl-routing", "restored-empty-region", "host-relative-layout", "shell-move",
                        "minimize-restore", "resize", "dpi-projection", "speech-sampling", "logical-visibility", "privacy-hide",
                        "resume", "reduced-motion", "detach-reattach", "dispose-without-ui-drain",
                        "production-window-replacement", "settings-selector", "owned-dialog-deferral",
                        "preserved-hidden-state", "preserved-minimized-state", "retired-handles",
                        "hidden-replacement-tray-reveal", "modeless-settings-remains-open",
                        "first-click-reveal-under-one-second", "first-positive-frame-under-one-second-with-blocked-sqlite" },
                    Qualification = "Synthetic integration only; not post-composition pixel or laptop performance qualification.",
                };
                File.WriteAllText(output, JsonSerializer.Serialize(result, Serialization));
                Console.WriteLine($"Production native presence integration passed. Frames: {frames}. Output: {output}");
                desktop.Shutdown();
            }
            catch (OperationCanceledException exception)
            {
                Console.Error.WriteLine(exception);
                desktop.Shutdown(1);
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(exception);
                desktop.Shutdown(1);
            }
        }

        private async Task<int> VerifyWindowReplacementAsync()
        {
            var scratchParent = Path.GetDirectoryName(output)
                ?? throw new InvalidOperationException("The fixture scratch directory is unavailable.");
            using var delayedCommit = new DelayedEvidenceCommit();
            using var fixture = new NativeUxFixtureSession(scratchParent, bufferedEvidence: true,
                evidenceCheckpoint: delayedCommit);
            await fixture.InitializeAsync();
            using var activity = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi),
                HostActivityLayer.Desktop, HostOperation.Presentation);
            fixture.Main.ShowApplication();
            var originalMainWindow = desktop.MainWindow;
            var originalShutdown = desktop.ShutdownMode;
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            MainWindow? current = null;
            var controller = new PresenceWindowController((native, _) =>
            {
                // Fixture host services were initialized once above; renderer windows must never start them.
                var next = new MainWindow(fixture.Main, fixture.Loggers.CreateLogger<MainWindow>(), native, false,
                    fixture.Loggers.CreateLogger<PresenceRenderWorker>());
                current = next;
                desktop.MainWindow = next;
                return next;
            }, () => true);
            try
            {
                controller.Start(true);
                var initial = current ?? throw new InvalidOperationException("The fixture window was not created.");
                ((IPresenceWindow)initial).RestoreAfterReplacement(new(initial.Position,
                    WindowState.Normal, true, true, null, Stopwatch.GetTimestamp()));
                await Task.Delay(150);
                Require(initial.IsVisible && ((IPresenceWindow)initial).CanReplace,
                    "The production presence window did not become replaceable.");
                var settings = new SettingsWindow(fixture.Main);
                var selector = settings.GetLogicalDescendants().OfType<ComboBox>()
                    .Single(control => ReferenceEquals(control.ItemsSource, fixture.Main.PresenceRenderers));
                settings.Show();
                fixture.Main.PropertyChanged += OnRendererChoice;
                var replacements = 0;
                try
                {
                    for (var index = 0; index < 6; index++)
                    {
                        var previous = current ?? throw new InvalidOperationException("The current fixture window is unavailable.");
                        var previousHandle = previous.TryGetPlatformHandle()!.Handle;
                        var previousPopup = (previous.Content as Grid)?.Children.OfType<NativePresenceHost>().SingleOrDefault()?.NativeHandle ?? 0;
                        var native = index % 2 != 0;
                        if (index == 0)
                        {
                            // Commit an explicit native preference before selecting Avalonia through the UI.
                            fixture.Main.SelectedPresenceRenderer = fixture.Main.PresenceRenderers[1];
                            await fixture.Main.AppearanceUpdate;
                        }
                        var selectionStarted = Stopwatch.GetTimestamp();
                        selector.SelectedItem = fixture.Main.PresenceRenderers[native ? 1 : 0];
                        var selectionElapsed = Stopwatch.GetElapsedTime(selectionStarted);
                        Require(selectionElapsed < TimeSpan.FromMilliseconds(500),
                            $"Renderer selector blocked the UI for {selectionElapsed.TotalMilliseconds:F1} ms.");
                        await fixture.Main.AppearanceUpdate;
                        Require(fixture.Main.IsNativePresenceRenderer == native && controller.HasPendingChange,
                            "The actual settings selector did not persist and request its renderer choice.");
                        if (index == 0)
                        {
                            var owned = new Window();
                            owned.Show(previous);
                            Require(!controller.TryApply() && IsWindow(previousHandle),
                                "An owned dialog did not defer production-window replacement.");
                            owned.Close();
                        }
                        if (index is 2 or 3) { previous.Hide(); }
                        if (index == 4) { previous.WindowState = WindowState.Minimized; }
                        var wasVisible = previous.IsVisible;
                        var previousState = previous.WindowState;
                        var previousPosition = previous.Position;
                        var replacementStarted = Stopwatch.GetTimestamp();
                        Require(controller.TryApply(), "Production-window replacement was unexpectedly deferred.");
                        var replacementElapsed = Stopwatch.GetElapsedTime(replacementStarted);
                        Require(replacementElapsed < TimeSpan.FromMilliseconds(500),
                            $"Renderer retirement/replacement blocked for {replacementElapsed.TotalMilliseconds:F1} ms.");
                        rendererSwitchSamples.Add(new(native ? "native" : "avalonia",
                            selectionElapsed.TotalMilliseconds, replacementElapsed.TotalMilliseconds));
                        Console.WriteLine($"Synthetic renderer switch ({(native ? "native" : "avalonia")}): "
                            + $"selector {selectionElapsed.TotalMilliseconds:F1} ms, replacement {replacementElapsed.TotalMilliseconds:F1} ms.");
                        replacements++;
                        Require(!IsWindow(previousHandle) && (previousPopup == 0 || !IsWindow(previousPopup)),
                            "A retired shell or native popup handle survived replacement.");
                        var next = current ?? throw new InvalidOperationException("The replacement fixture window is unavailable.");
                        Require(ReferenceEquals(next.DataContext, fixture.Main) && next.Position == previousPosition,
                            "Replacement did not retain the exact host view model and position.");
                        Require(next.IsVisible == wasVisible && next.WindowState == previousState,
                            "Replacement changed hidden or minimized state.");
                        if (!wasVisible)
                        {
                            Require(settings.IsVisible, "The modeless settings window closed during renderer replacement.");
                            await VerifyTrayRevealAsync(fixture, next, native, delayedCommit);
                        }
                        next.WindowState = WindowState.Normal;
                        await Task.Delay(75);
                        if (index >= 4)
                        {
                            await VerifyTrayRevealAsync(fixture, next, native, delayedCommit);
                        }
                    }
                    activity.Complete(HostOperationOutcome.Completed);
                    return replacements;
                }
                finally
                {
                    fixture.Main.PropertyChanged -= OnRendererChoice;
                    settings.Close();
                }
                void OnRendererChoice(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
                {
                    if (string.Equals(args.PropertyName, nameof(Kora.Application.ViewModels.MainViewModel.IsNativePresenceRenderer),
                        StringComparison.Ordinal))
                    {
                        controller.Request(fixture.Main.IsNativePresenceRenderer);
                    }
                }
            }
            finally
            {
                delayedCommit.Release.Set();
                controller.Stop();
                if (current is not null) { ((IPresenceWindow)current).CloseForReplacement(); }
                desktop.MainWindow = originalMainWindow;
                desktop.ShutdownMode = originalShutdown;
            }
        }

        private static void Require(bool value, string message)
        {
            if (!value)
            {
                throw new InvalidOperationException(message);
            }
        }

        private sealed record RendererSwitchSample(string Renderer, double SelectionMilliseconds,
            double ReplacementMilliseconds);

        public void Dispose()
        {
            host.Dispose();
            legacy.Dispose();
            underlyingWindow?.Close();
            underlyingWindow = null;
            underlying = 0;
        }

        private sealed class SyntheticInputNative : IWindowsPresenceInputNative
        {
            private readonly WindowsPresenceInputNative native = new();
            public bool ControlPressed { get; set; }
            public int CursorRequests { get; private set; }
            public bool IsControlPressed() => ControlPressed;
            public bool IsMouseButtonPressed() => false;
            public uint GetExtendedStyle(nint window) => native.GetExtendedStyle(window);
            public void SetExtendedStyle(nint window, uint style) => native.SetExtendedStyle(window, style);
            public void EnableLayeredRendering(nint window) => native.EnableLayeredRendering(window);
            public void ShowMoveCursorIfOverWindow(nint window) => CursorRequests++;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativePoint { public int X; public int Y; }
        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect { public int Left; public int Top; public int Right; public int Bottom; }
        [LibraryImport("user32.dll")]
        private static partial nint WindowFromPoint(NativePoint point);
        [LibraryImport("user32.dll")]
        private static partial nint GetWindow(nint window, uint command);
        [LibraryImport("user32.dll")]
        private static partial nint GetAncestor(nint window, uint flags);
        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);
        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool ClientToScreen(nint window, ref NativePoint point);
        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool IsWindow(nint window);
        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool IsWindowVisible(nint window);
        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool GetWindowRect(nint window, out NativeRect rectangle);
        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool GetLayeredWindowAttributes(nint window, out uint colorKey, out byte alpha, out uint flags);
    }
}