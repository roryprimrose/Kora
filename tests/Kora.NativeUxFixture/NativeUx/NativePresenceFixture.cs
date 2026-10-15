using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;

using Kora.Application.Visuals;
using Kora.Controls;
using Kora.Core;
using Kora.Windows.Presentation;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.NativeUxFixture;

internal static partial class NativePresenceFixture
{
    private static readonly JsonSerializerOptions Serialization = new() { WriteIndented = true };
    public static int Run(string scratchParent)
    {
        var output = Path.Combine(scratchParent, $"presence-integration-{Guid.NewGuid():N}.json");
        Console.WriteLine($"Synthetic production presence integration only. Output: {output}");
        Console.WriteLine("No host startup, microphone, speech provider, model, clipboard, network or saved preferences.");
        return AppBuilder.Configure(() => new FixtureApplication(output))
            .UsePlatformDetect().StartWithClassicDesktopLifetime([]);
    }

    private sealed class FixtureApplication(string output) : Avalonia.Application
    {
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
        private readonly IClassicDesktopStyleApplicationLifetime desktop;
        private readonly string output;
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private PresenceRenderInputs inputs = new(AssistantState.Calculating, false, 0, true, 200,
            200, 200, 200, false, false, true, 600, 600, 1);
        private Exception? failure;
        private bool eligible = true;
        private int samples;

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
            IsHitTestVisible = true;
            Win32Properties.AddWindowStylesCallback(this, static (style, extended) =>
                (style, WindowsPresenceWindowInput.GetExtendedStyle(extended, true)));
            host = new NativePresenceHost(
                () => eligible && IsVisible ? inputs with { Width = hostWidth(), Height = hostHeight() } : null,
                () =>
                {
                    samples++;
                    inputs = inputs with { IsSpeaking = true, SpeechOutputLevel = samples % 2 == 0 ? 0.1 : 0.9 };
                },
                exception => failure = exception, NullLogger<PresenceRenderWorker>.Instance);
            Content = host;
            Loaded += OnLoaded;
            Closed += (_, _) => Dispose();

            double hostWidth() => host?.Bounds.Width ?? 600;
            double hostHeight() => host?.Bounds.Height ?? 600;
        }

        private async void OnLoaded(object? sender, Avalonia.Interactivity.RoutedEventArgs args)
        {
            try
            {
                host.Refresh(false);
                await Task.Delay(600);
                Require(failure is null, $"Native startup failed: {failure}");
                Require(host.FramesPresented >= 3, "No sustained native child-window frames.");
                var rootHandle = TryGetPlatformHandle()!.Handle;
                Require(GetWindowRect(rootHandle, out var bounds), "Could not inspect the fixture window bounds.");
                var hit = WindowFromPoint(new NativePoint
                {
                    X = (bounds.Left + bounds.Right) / 2,
                    Y = (bounds.Top + bounds.Bottom) / 2,
                });
                Require(hit == rootHandle, "The native child or its intermediate holder intercepted shell pointer routing.");
                var first = host.FramesPresented;
                inputs = inputs with { IsLightTheme = true, Scaling = 1.5, Density = 25, DotSize = 50 };
                Width = Height = 360;
                host.Refresh(true);
                await Task.Delay(350);
                Require(failure is null && host.FramesPresented > first && samples > 0,
                    $"Resizing, DPI projection or sampled speech failed: {failure}");
                host.Refresh(false);
                var sampled = samples;
                eligible = false;
                host.Suspend();
                Hide();
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
                host.Suspend();
                var frames = host.FramesPresented;
                host.Dispose();
                await Task.Delay(100);
                Require(host.FramesPresented == frames, "Queued native pixels survived disposal.");
                var result = new
                {
                    Success = true,
                    FramesPresented = frames,
                    PlaybackSamples = samples,
                    ElapsedSeconds = clock.Elapsed.TotalSeconds,
                    Checks = new[] { "native-child", "shell-pointer-routing", "resize", "dpi-projection", "speech-sampling", "privacy-hide",
                        "resume", "reduced-motion", "dispose-without-ui-drain" },
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

        private static void Require(bool value, string message)
        {
            if (!value)
            {
                throw new InvalidOperationException(message);
            }
        }

        public void Dispose() => host.Dispose();

        [StructLayout(LayoutKind.Sequential)]
        private struct NativePoint { public int X; public int Y; }
        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect { public int Left; public int Top; public int Right; public int Bottom; }
        [LibraryImport("user32.dll")]
        private static partial nint WindowFromPoint(NativePoint point);
        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool GetWindowRect(nint window, out NativeRect rectangle);
    }
}