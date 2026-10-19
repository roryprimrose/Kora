using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Logging;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;

using Kora.Controls;
using Kora.Core;
using Kora.Windows.Presentation;

using SkiaSharp;

namespace Presence.Performance;

internal static class Program
{
    internal static BenchmarkOptions Options { get; private set; } = null!;
    internal static BenchmarkResult? Result { get; set; }
    internal static List<string> GraphicsLog { get; } = [];

    [STAThread]
    public static int Main(string[] args)
    {
        try
        {
            Options = BenchmarkOptions.Parse(args);
            var platform = new Win32PlatformOptions();
            Logger.Sink = new BenchmarkLogSink();
            if (Options.Backend is "software")
            {
                platform.RenderingMode = [Win32RenderingMode.Software];
            }
            else if (Options.Backend is "angle")
            {
                platform.RenderingMode = [Win32RenderingMode.AngleEgl];
            }
            var exitCode = 0;
            if (Options.Variant is "native-dib")
            {
                Result = NativeBitmapWindow.Run();
            }
            else
            {
                exitCode = AppBuilder.Configure<BenchmarkApplication>()
                    .UsePlatformDetect()
                    .With(platform)
                    .StartWithClassicDesktopLifetime([]);
            }
            if (Options.Variant is "verify")
            {
                return exitCode;
            }
            if (Result is null)
            {
                throw new InvalidOperationException("Benchmark did not complete its measurement period.");
            }
            File.WriteAllText(Options.Output, JsonSerializer.Serialize(Result, BenchmarkResult.JsonOptions));
            File.WriteAllLines(Path.ChangeExtension(Options.Output, ".graphics.log"), GraphicsLog);
            Console.WriteLine($"{Options.Variant}/{Options.Scenario}/{Options.Backend}: "
                + $"{Result.CpuMillisecondsPerSecond:F2} CPU-ms/s; {Result.AllocatedBytesPerSecond:F0} B/s; "
                + $"{Result.TicksPerSecond:F2} updates/s; {Result.WorkingSetMiB:F1} MiB");
            return exitCode;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

internal sealed record BenchmarkOptions(
    string Variant, string Scenario, string Backend, double Seconds, double WarmupSeconds,
    string Output, bool PollInput, bool Layered, bool Offscreen, bool Topmost)
{
    public static BenchmarkOptions Parse(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 0; index < args.Length; index += 2)
        {
            if (index + 1 == args.Length || !args[index].StartsWith("--", StringComparison.Ordinal)
                || !values.TryAdd(args[index], args[index + 1]))
            {
                throw new ArgumentException("Expected unique --name value arguments.", nameof(args));
            }
        }
        var allowed = new HashSet<string>(
            ["--variant", "--scenario", "--backend", "--seconds", "--warmup", "--output", "--input", "--layered", "--offscreen", "--topmost"],
            StringComparer.Ordinal);
        if (values.Keys.Any(key => !allowed.Contains(key)))
        {
            throw new ArgumentException("Unknown benchmark argument.", nameof(args));
        }
        var variant = values.GetValueOrDefault("--variant", "baseline");
        var scenario = values.GetValueOrDefault("--scenario", "default");
        var backend = values.GetValueOrDefault("--backend", "default");
        if (variant is not ("baseline" or "blank" or "cached" or "skia" or "trajectory" or "adaptive15" or "trajectory15" or "pr247"
            or "atlas" or "bitmap" or "compositor" or "compositor-atlas" or "native-dib" or "verify")
            || scenario is not ("default" or "maximum" or "reactive" or "static" or "hidden")
            || backend is not ("default" or "software" or "angle"))
        {
            throw new ArgumentException("Unknown variant, scenario or backend.", nameof(args));
        }
        var seconds = double.Parse(values.GetValueOrDefault("--seconds", "15"), CultureInfo.InvariantCulture);
        var warmup = double.Parse(values.GetValueOrDefault("--warmup", "3"), CultureInfo.InvariantCulture);
        if (!double.IsFinite(seconds) || seconds is < 2 or > 300
            || !double.IsFinite(warmup) || warmup is < 1 or > 30)
        {
            throw new ArgumentOutOfRangeException(nameof(args), "Use 2-300 measured seconds and 1-30 warmup seconds.");
        }
        var output = Path.GetFullPath(values["--output"]);
        if (!Directory.Exists(Path.GetDirectoryName(output)))
        {
            throw new DirectoryNotFoundException("The explicit benchmark output directory must already exist.");
        }
        return new BenchmarkOptions(variant, scenario, backend, seconds, warmup, output,
            bool.Parse(values.GetValueOrDefault("--input", "true")),
            bool.Parse(values.GetValueOrDefault("--layered", "true")),
            bool.Parse(values.GetValueOrDefault("--offscreen", "false")),
            bool.Parse(values.GetValueOrDefault("--topmost", "true")));
    }
}

internal sealed class BenchmarkResult
{
    internal static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public required BenchmarkOptions Options { get; init; }
    public required string Os { get; init; }
    public required string Framework { get; init; }
    public required string GraphicsService { get; init; }
    public required string Transparency { get; init; }
    public int ProcessId { get; init; }
    public int LogicalProcessors { get; init; }
    public double RenderScaling { get; init; }
    public double ElapsedSeconds { get; init; }
    public double CpuMillisecondsPerSecond { get; init; }
    public double AllocatedBytesPerSecond { get; init; }
    public double WorkingSetMiB { get; init; }
    public double PrivateMiB { get; init; }
    public double PeakWorkingSetMiB { get; init; }
    public double TicksPerSecond { get; init; }
    public double TickIntervalP95Milliseconds { get; init; }
    public int Gen0Collections { get; init; }
    public int Gen1Collections { get; init; }
    public int Gen2Collections { get; init; }
    public double DrawsPerSecond { get; init; }
    public double DrawCpuMillisecondsPerSecond { get; init; }
    public bool? GpuCanvas { get; init; }
    public int CacheBytes { get; init; }
    public double ForcedRendersPerSecond { get; init; }
    public double ObservedWindowRendersPerSecond { get; init; }
    public int SnapshotNontransparentPixels { get; init; }
    public double StartupWallMilliseconds { get; init; }
    public double StartupCpuMilliseconds { get; init; }
    public bool ObservedGpu { get; init; }
    public bool ObservedSoftware { get; init; }
    public double PrivateBeforeSnapshotMiB { get; init; }
    public ResourceSnapshot? ResourcesBefore { get; init; }
    public ResourceSnapshot? ResourcesAfter { get; init; }
}

internal sealed class BenchmarkApplication : Avalonia.Application
{
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
        {
            throw new InvalidOperationException("Desktop lifetime is required for actual window rendering.");
        }
        desktop.MainWindow = new BenchmarkWindow(desktop);
        base.OnFrameworkInitializationCompleted();
    }
}

internal sealed class BenchmarkWindow : Window, IDisposable
{
    private readonly IClassicDesktopStyleApplicationLifetime desktop;
    private readonly PresenceControl? baseline;
    private readonly Pr247.PresenceControl? pr247;
    private readonly CandidateControl? candidate;
    private readonly CompositionControl? composition;
    private readonly RenderProbe probe = new();
    private readonly Panel surface = new();
    private readonly DispatcherTimer forcedTimer;
    private RenderTargetBitmap? forcedTarget;
    private long forcedRenders;
    private double nextForcedFrame;
    private readonly Stopwatch forcedClock = new();
    private readonly DispatcherTimer inputTimer;
    private readonly DispatcherTimer workloadTimer;
    private readonly Stopwatch lifetime = Stopwatch.StartNew();
    private readonly List<double> tickIntervals = [];
    private WindowsPresenceWindowInput? input;
    private long ticks;
    private long lastTick;
    private bool measuring;
    private static readonly AssistantState[] States =
    [
        AssistantState.Listening, AssistantState.Calculating, AssistantState.Waiting,
        AssistantState.Executing, AssistantState.Success, AssistantState.Failure, AssistantState.Information,
    ];

    public BenchmarkWindow(IClassicDesktopStyleApplicationLifetime desktop)
    {
        this.desktop = desktop;
        var options = Program.Options;
        Width = Height = options.Scenario is "maximum" or "reactive" ? 600 : 360;
        Background = Avalonia.Media.Brushes.Transparent;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        WindowDecorations = WindowDecorations.None;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = options.Topmost;
        CanResize = false;
        IsHitTestVisible = false;
        Title = "Kora synthetic presence benchmark";
        Position = new PixelPoint(30, 30);
        if (options.Layered)
        {
            Win32Properties.AddWindowStylesCallback(this,
                static (style, extendedStyle) =>
                    (style, WindowsPresenceWindowInput.GetExtendedStyle(extendedStyle, intercept: false)));
        }
        if (options.Variant is "baseline")
        {
            baseline = new PresenceControl
            {
                State = options.Scenario is "static" ? AssistantState.Failure : AssistantState.Calculating,
                DotDensityPercent = options.Scenario is "maximum" or "reactive" ? 200 : 100,
                DotSizePercent = options.Scenario is "maximum" or "reactive" ? 200 : 100,
                MovementSpeedPercent = options.Scenario is "maximum" or "reactive" ? 200 : 100,
                SpeechScaleAmountPercent = 200,
            };
            baseline.FrameUpdating += OnBaselineTick;
            Content = baseline;
        }
        else if (options.Variant is "pr247")
        {
            pr247 = new Pr247.PresenceControl
            {
                State = options.Scenario is "static" ? AssistantState.Failure : AssistantState.Calculating,
                DotDensityPercent = options.Scenario is "maximum" or "reactive" ? 200 : 100,
                DotSizePercent = options.Scenario is "maximum" or "reactive" ? 200 : 100,
                MovementSpeedPercent = options.Scenario is "maximum" or "reactive" ? 200 : 100,
                SpeechScaleAmountPercent = 200,
            };
            pr247.FrameUpdating += OnBaselineTick;
            Content = pr247;
        }
        else if (options.Variant is "compositor" or "compositor-atlas")
        {
            if (options.Offscreen)
            {
                throw new ArgumentException("Composition visuals cannot be measured through RenderTargetBitmap.", nameof(desktop));
            }
            composition = new CompositionControl(options.Scenario is "maximum" or "reactive" ? 300 : 150,
                options.Scenario is "maximum" or "reactive" ? 200 : 100, options.Variant is "compositor-atlas");
            composition.SetInputs(options.Scenario is "static" ? AssistantState.Failure : AssistantState.Calculating,
                false, 0, options.Scenario is "maximum" or "reactive" ? 200 : 100, false);
            Content = composition;
        }
        else if (options.Variant is not ("blank" or "verify"))
        {
            candidate = new CandidateControl(options.Variant,
                options.Scenario is "maximum" or "reactive" ? 300 : 150,
                options.Scenario is "maximum" or "reactive" ? 200 : 100);
            candidate.SetInputs(options.Scenario is "static" ? AssistantState.Failure : AssistantState.Calculating,
                false, 0, options.Scenario is "maximum" or "reactive" ? 200 : 100, false);
            candidate.FrameUpdating += OnBaselineTick;
            Content = candidate;
        }
        var content = Content as Control;
        Content = null;
        surface.Children.Add(probe);
        if (content is not null)
        {
            surface.Children.Add(content);
        }
        Content = surface;
        inputTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Input, OnInputTick);
        workloadTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(33), DispatcherPriority.Background, OnWorkloadTick);
        forcedTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(1), DispatcherPriority.Background, OnForcedTick);
        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    private void OnBaselineTick(object? sender, EventArgs args)
    {
        if (!measuring)
        {
            return;
        }
        var now = Stopwatch.GetTimestamp();
        if (lastTick != 0)
        {
            tickIntervals.Add(Stopwatch.GetElapsedTime(lastTick, now).TotalMilliseconds);
        }
        lastTick = now;
        ticks++;
    }

    private void OnInputTick(object? sender, EventArgs args)
    {
        IsHitTestVisible = input?.Refresh(IsVisible) == true;
    }

    private void OnForcedTick(object? sender, EventArgs args)
    {
        if (forcedClock.Elapsed.TotalSeconds < nextForcedFrame)
        {
            return;
        }
        nextForcedFrame += 1d / 30;
        forcedTarget!.Render(surface);
        if (measuring)
        {
            forcedRenders++;
        }
    }

    private void OnWorkloadTick(object? sender, EventArgs args)
    {
        if (Program.Options.Scenario is not "reactive")
        {
            return;
        }
        var seconds = lifetime.Elapsed.TotalSeconds;
        var state = States[(int)(seconds / 2) % States.Length];
        var speaking = (int)(seconds / 5) % 2 == 0;
        var level = (Math.Sin(seconds * 18) + 1) / 2;
        var light = (int)(seconds / 7) % 2 != 0;
        if (baseline is not null)
        {
            baseline.State = state;
            baseline.IsSpeaking = speaking;
            baseline.SpeechOutputLevel = level;
        }
        if (pr247 is not null)
        {
            pr247.State = state;
            pr247.IsSpeaking = speaking;
            pr247.SpeechOutputLevel = level;
        }
        candidate?.SetInputs(state, speaking, level, 200, light);
        composition?.SetInputs(state, speaking, level, 200, light);
        RequestedThemeVariant = light ? ThemeVariant.Light : ThemeVariant.Dark;
    }

    private async void OnLoaded(object? sender, Avalonia.Interactivity.RoutedEventArgs args)
    {
        try
        {
            if (Program.Options.Variant is "verify")
            {
                FidelityVerification.Run(Program.Options.Output);
                desktop.Shutdown();
                return;
            }
            using var process = Process.GetCurrentProcess();
            var startupCpu = process.TotalProcessorTime.TotalMilliseconds;
            var startupWall = lifetime.Elapsed.TotalMilliseconds;
            if (Program.Options.Layered)
            {
                input = new WindowsPresenceWindowInput(TryGetPlatformHandle()!.Handle);
            }
            if (Program.Options.PollInput && input is not null)
            {
                inputTimer.Start();
            }
            if (Program.Options.Scenario is "reactive")
            {
                workloadTimer.Start();
            }
            if (Program.Options.Offscreen)
            {
                forcedTarget = new RenderTargetBitmap(
                    new PixelSize((int)Width, (int)Height), new Vector(96, 96));
                forcedClock.Start();
                forcedTimer.Start();
            }
            await Task.Delay(TimeSpan.FromSeconds(Program.Options.WarmupSeconds));
            if (Program.Options.Scenario is "hidden")
            {
                Hide();
                inputTimer.Stop();
                candidate?.Suspend();
                pr247?.SetAnimationEnabled(false);
                composition?.Suspend();
            }
            var cpuStart = process.TotalProcessorTime;
            var allocationsStart = GC.GetTotalAllocatedBytes(precise: true);
            var collectionsStart = Enumerable.Range(0, 3).Select(GC.CollectionCount).ToArray();
            var resourcesBefore = ResourceSnapshot.Capture();
            var drawsStart = candidate is null ? 0 : Interlocked.Read(ref candidate.Draws);
            var drawTicksStart = candidate is null ? 0 : Interlocked.Read(ref candidate.DrawTicks);
            var probeStart = Interlocked.Read(ref probe.RenderCount);
            var compositionUpdates = composition?.Updates ?? 0;
            var compositionDraws = composition?.Draws ?? 0;
            lifetime.Restart();
            measuring = true;
            var start = Stopwatch.GetTimestamp();
            await Task.Delay(TimeSpan.FromSeconds(Program.Options.Seconds));
            var elapsed = Stopwatch.GetElapsedTime(start).TotalSeconds;
            measuring = false;
            forcedTimer.Stop();
            process.Refresh();
            var cpu = (process.TotalProcessorTime - cpuStart).TotalMilliseconds / elapsed;
            var allocations = (GC.GetTotalAllocatedBytes(precise: true) - allocationsStart) / elapsed;
            var workingSet = process.WorkingSet64 / 1048576d;
            var privateMiB = process.PrivateMemorySize64 / 1048576d;
            var peakWorkingSet = process.PeakWorkingSet64 / 1048576d;
            var collections = Enumerable.Range(0, 3).Select(GC.CollectionCount).ToArray();
            var resourcesAfter = ResourceSnapshot.Capture();
            var renders = Interlocked.Read(ref probe.RenderCount) - probeStart;
            var onscreenGpu = probe.GpuCanvas;
            var observedGpu = probe.ObservedGpu;
            var observedSoftware = probe.ObservedSoftware;
            var nontransparent = 0;
            if (Program.Options.Scenario is not "hidden" && composition is null)
            {
                using var snapshot = new RenderTargetBitmap(new PixelSize((int)Width, (int)Height), new Vector(96, 96));
                snapshot.Render(surface);
                var imagePath = Path.ChangeExtension(Program.Options.Output, ".png");
                snapshot.Save(imagePath, PngBitmapEncoderOptions.Default);
                using var image = SKBitmap.Decode(imagePath);
                nontransparent = image.Pixels.Count(color => color.Alpha != 0);
                if (Program.Options.Variant is not "blank" && nontransparent < 50)
                {
                    throw new InvalidOperationException("The measured presence produced an empty synthetic snapshot.");
                }
            }
            tickIntervals.Sort();
            Program.Result = new BenchmarkResult
            {
                Options = Program.Options,
                Os = Environment.OSVersion.ToString(),
                Framework = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
                GraphicsService = string.Join(Environment.NewLine, GraphicsLog),
                Transparency = ActualTransparencyLevel.ToString(),
                ProcessId = Environment.ProcessId,
                LogicalProcessors = Environment.ProcessorCount,
                RenderScaling = RenderScaling,
                ElapsedSeconds = elapsed,
                CpuMillisecondsPerSecond = cpu,
                AllocatedBytesPerSecond = allocations,
                WorkingSetMiB = workingSet,
                PrivateMiB = privateMiB,
                PeakWorkingSetMiB = peakWorkingSet,
                TicksPerSecond = (composition is null ? ticks : composition.Updates - compositionUpdates) / elapsed,
                TickIntervalP95Milliseconds = tickIntervals.Count == 0
                    ? 0 : tickIntervals[(int)((tickIntervals.Count - 1) * 0.95)],
                Gen0Collections = collections[0] - collectionsStart[0],
                Gen1Collections = collections[1] - collectionsStart[1],
                Gen2Collections = collections[2] - collectionsStart[2],
                DrawsPerSecond = composition is not null ? (composition.Draws - compositionDraws) / elapsed
                    : candidate is null ? 0 : (Interlocked.Read(ref candidate.Draws) - drawsStart) / elapsed,
                DrawCpuMillisecondsPerSecond = candidate is null ? 0
                    : (Interlocked.Read(ref candidate.DrawTicks) - drawTicksStart) * 1000d / Stopwatch.Frequency / elapsed,
                GpuCanvas = composition?.GpuCanvas ?? onscreenGpu,
                CacheBytes = candidate?.CacheBytes ?? 0,
                ForcedRendersPerSecond = forcedRenders / elapsed,
                ObservedWindowRendersPerSecond = (renders - forcedRenders) / elapsed,
                SnapshotNontransparentPixels = nontransparent,
                StartupWallMilliseconds = startupWall,
                StartupCpuMilliseconds = startupCpu,
                ObservedGpu = observedGpu,
                ObservedSoftware = observedSoftware,
                PrivateBeforeSnapshotMiB = privateMiB,
                ResourcesBefore = resourcesBefore,
                ResourcesAfter = resourcesAfter,
            };
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

    private void OnClosed(object? sender, EventArgs args) => Dispose();

    public void Dispose()
    {
        inputTimer.Stop();
        workloadTimer.Stop();
        candidate?.Dispose();
        composition?.Dispose();
        forcedTimer.Stop();
        forcedTarget?.Dispose();
    }
}
}
