using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

using Kora.Core;
using Kora.Windows.Presentation;

using SkiaSharp;

using BenchmarkResult = Presence.Performance.Program.BenchmarkResult;

namespace Presence.Performance;

internal sealed partial class NativeBitmapWindow : IDisposable
{
    private static readonly WindowProcedure Procedure = Dispatch;
    private static NativeBitmapWindow? current;
    private readonly ParticleScene scene;
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly Process process = Process.GetCurrentProcess();
    private readonly List<double> intervals = [];
    private readonly int size;
    private readonly double dotSize;
    private readonly nint dc;
    private readonly nint bitmap;
    private readonly nint previousBitmap;
    private readonly SKSurface surface;
    private readonly WindowsPresenceWindowInput input;
    private readonly nint window;
    private long measurementStart;
    private double cpuStart;
    private long allocatedStart;
    private int[] collectionsStart = [];
    private double nextFrame;
    private double previousFrame;
    private long draws;
    private double startupCpu;
    private double startupWall;
    private Exception? failure;
    private BenchmarkResult? result;
    private bool disposed;
    private ResourceSnapshot? resourcesBefore;
    private static readonly AssistantState[] States =
    [
        AssistantState.Listening, AssistantState.Calculating, AssistantState.Waiting,
        AssistantState.Executing, AssistantState.Success, AssistantState.Failure, AssistantState.Information,
    ];

    private NativeBitmapWindow()
    {
        var options = Program.Options;
        size = options.Scenario is "maximum" or "reactive" ? 600 : 360;
        dotSize = options.Scenario is "maximum" or "reactive" ? 200 : 100;
        scene = new ParticleScene(size == 600 ? 300 : 150, cacheTrajectories: false);
        var instance = GetModuleHandle(null);
        var windowClass = new WindowClass
        {
            Procedure = Marshal.GetFunctionPointerForDelegate(Procedure),
            Instance = instance,
            ClassName = "KoraPresenceBenchmarkOnly",
        };
        Check(RegisterClass(ref windowClass) != 0, "RegisterClass");
        window = CreateWindowEx(options.Topmost ? 0x080800A8u : 0x080800A0u, windowClass.ClassName, "Synthetic Kora presence only",
            0x80000000, 30, 30, size, size, 0, 0, instance, 0);
        Check(window != 0, "CreateWindowEx");
        dc = CreateCompatibleDC(0);
        Check(dc != 0, "CreateCompatibleDC");
        var info = new BitmapInfo { HeaderSize = 40, Width = size, Height = -size, Planes = 1, BitCount = 32 };
        bitmap = CreateDIBSection(dc, ref info, 0, out var pixels, 0, 0);
        Check(bitmap != 0 && pixels != 0, "CreateDIBSection");
        previousBitmap = SelectObject(dc, bitmap);
        Check(previousBitmap != 0 && previousBitmap != -1, "SelectObject");
        surface = SKSurface.Create(new SKImageInfo(size, size, SKColorType.Bgra8888, SKAlphaType.Premul), pixels, size * 4)
            ?? throw new InvalidOperationException("Skia could not wrap the owned DIB.");
        input = new WindowsPresenceWindowInput(window, new LayeredInputNative());
    }

    public static BenchmarkResult Run()
    {
        using var host = new NativeBitmapWindow();
        current = host;
        try
        {
            host.startupCpu = host.process.TotalProcessorTime.TotalMilliseconds;
            host.startupWall = host.clock.Elapsed.TotalMilliseconds;
            _ = ShowWindow(host.window, 4);
            Check(SetTimer(host.window, 1, 10, 0) != 0, "SetTimer");
            while (true)
            {
                var status = GetMessage(out var message, 0, 0, 0);
                Check(status != -1, "GetMessage");
                if (status == 0)
                {
                    break;
                }
                _ = TranslateMessage(ref message);
                _ = DispatchMessage(ref message);
            }
            if (host.failure is not null)
            {
                throw new InvalidOperationException("Native benchmark failed.", host.failure);
            }
            return host.result ?? throw new InvalidOperationException("Native benchmark ended before measurement completed.");
        }
        finally
        {
            current = null;
        }
    }

    private static nint Dispatch(nint hwnd, uint message, nint wparam, nint lparam)
    {
        try
        {
            if (message == 0x0113 && current is not null)
            {
                current.OnTimer();
                return 0;
            }
            if (message == 0x0002)
            {
                PostQuitMessage(0);
                return 0;
            }
            return DefWindowProc(hwnd, message, wparam, lparam);
        }
        catch (Exception exception)
        {
            if (current is null)
            {
                Environment.FailFast("A native benchmark callback failed before initialization.", exception);
            }
            current.failure = exception;
            _ = DestroyWindow(hwnd);
            return 0;
        }
    }

    private void OnTimer()
    {
        var options = Program.Options;
        var seconds = clock.Elapsed.TotalSeconds;
        if (measurementStart == 0 && seconds >= options.WarmupSeconds)
        {
            if (options.Scenario is "hidden")
            {
                _ = ShowWindow(window, 0);
            }
            measurementStart = Stopwatch.GetTimestamp();
            cpuStart = process.TotalProcessorTime.TotalMilliseconds;
            allocatedStart = GC.GetTotalAllocatedBytes(precise: true);
            collectionsStart = Enumerable.Range(0, 3).Select(GC.CollectionCount).ToArray();
            resourcesBefore = ResourceSnapshot.Capture();
            previousFrame = seconds;
        }
        if (measurementStart != 0 && Stopwatch.GetElapsedTime(measurementStart).TotalSeconds >= options.Seconds)
        {
            Complete();
            _ = DestroyWindow(window);
            return;
        }
        if (options.PollInput && options.Scenario is not "hidden")
        {
            _ = input.Refresh(canInteract: true);
        }
        if (seconds < nextFrame || options.Scenario is "hidden")
        {
            return;
        }
        nextFrame += 1d / 30;
        var elapsed = TimeSpan.FromSeconds(seconds - previousFrame);
        previousFrame = seconds;
        var timeline = measurementStart == 0 ? seconds : Stopwatch.GetElapsedTime(measurementStart).TotalSeconds;
        var state = options.Scenario is "static" ? AssistantState.Failure
            : options.Scenario is "reactive" ? States[(int)(timeline / 2) % States.Length] : AssistantState.Calculating;
        var speaking = options.Scenario is "reactive" && (int)(timeline / 5) % 2 == 0;
        var changed = scene.Advance(state, speaking, (Math.Sin(timeline * 18) + 1) / 2, elapsed,
            size == 600 ? 200 : 100, timeBased: true);
        if (options.Scenario is "static" && !changed && measurementStart != 0)
        {
            return;
        }
        surface.Canvas.Clear(SKColors.Transparent);
        scene.Draw(surface.Canvas, size, dotSize, options.Scenario is "reactive" && (int)(timeline / 7) % 2 != 0);
        surface.Canvas.Flush();
        var destination = new NativePoint { X = 30, Y = 30 };
        var source = new NativePoint();
        var dimensions = new NativeSize { Width = size, Height = size };
        var blend = new BlendFunction { Alpha = 255, Format = 1 };
        Check(UpdateLayeredWindow(window, 0, ref destination, ref dimensions, dc, ref source, 0, ref blend, 2),
            "UpdateLayeredWindow");
        if (measurementStart != 0)
        {
            draws++;
            intervals.Add(elapsed.TotalMilliseconds);
        }
    }

    private void Complete()
    {
        var elapsed = Stopwatch.GetElapsedTime(measurementStart).TotalSeconds;
        process.Refresh();
        var cpu = (process.TotalProcessorTime.TotalMilliseconds - cpuStart) / elapsed;
        var allocations = (GC.GetTotalAllocatedBytes(precise: true) - allocatedStart) / elapsed;
        var workingSet = process.WorkingSet64 / 1048576d;
        var privateMiB = process.PrivateMemorySize64 / 1048576d;
        var peakWorkingSet = process.PeakWorkingSet64 / 1048576d;
        var collections = Enumerable.Range(0, 3).Select(GC.CollectionCount).ToArray();
        var resourcesAfter = ResourceSnapshot.Capture();
        intervals.Sort();
        var pixels = 0;
        using (var image = surface.Snapshot())
        {
            using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
            using (var stream = File.Create(Path.ChangeExtension(Program.Options.Output, ".png")))
            {
                encoded.SaveTo(stream);
            }
            using var decoded = SKBitmap.FromImage(image);
            pixels = decoded.Pixels.Count(color => color.Alpha != 0);
        }
        if (pixels < 50 && Program.Options.Scenario is not "hidden")
        {
            throw new InvalidOperationException("The native benchmark rendered an empty image.");
        }
        result = new BenchmarkResult
        {
            Options = Program.Options,
            Os = Environment.OSVersion.ToString(),
            Framework = RuntimeInformation.FrameworkDescription,
            GraphicsService = "Skia raster BGRA premultiplied DIB + UpdateLayeredWindow",
            Transparency = "per-pixel alpha",
            ProcessId = Environment.ProcessId,
            LogicalProcessors = Environment.ProcessorCount,
            RenderScaling = 1,
            ElapsedSeconds = elapsed,
            CpuMillisecondsPerSecond = cpu,
            AllocatedBytesPerSecond = allocations,
            WorkingSetMiB = workingSet,
            PrivateMiB = privateMiB,
            PeakWorkingSetMiB = peakWorkingSet,
            TicksPerSecond = draws / elapsed,
            TickIntervalP95Milliseconds = intervals.Count == 0 ? 0 : intervals[(int)((intervals.Count - 1) * 0.95)],
            DrawsPerSecond = draws / elapsed,
            ObservedWindowRendersPerSecond = draws / elapsed,
            SnapshotNontransparentPixels = pixels,
            GpuCanvas = false,
            StartupCpuMilliseconds = startupCpu,
            StartupWallMilliseconds = startupWall,
            Gen0Collections = collections[0] - collectionsStart[0],
            Gen1Collections = collections[1] - collectionsStart[1],
            Gen2Collections = collections[2] - collectionsStart[2],
            ObservedSoftware = true,
            PrivateBeforeSnapshotMiB = privateMiB,
            ResourcesBefore = resourcesBefore,
            ResourcesAfter = resourcesAfter,
        };
    }

    private static void Check(bool succeeded, string operation)
    {
        if (!succeeded)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), operation);
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        surface.Dispose();
        scene.Dispose();
        _ = SelectObject(dc, previousBitmap);
        _ = DeleteObject(bitmap);
        _ = DeleteDC(dc);
        process.Dispose();
    }

    private sealed class LayeredInputNative : IWindowsPresenceInputNative
    {
        private readonly WindowsPresenceInputNative native = new();
        public bool IsControlPressed() => native.IsControlPressed();
        public bool IsMouseButtonPressed() => native.IsMouseButtonPressed();
        public uint GetExtendedStyle(nint window) => native.GetExtendedStyle(window);
        public void SetExtendedStyle(nint window, uint style) => native.SetExtendedStyle(window, style);
        public void ShowMoveCursorIfOverWindow(nint window) => native.ShowMoveCursorIfOverWindow(window);
        public void EnableLayeredRendering(nint window)
        {
            // UpdateLayeredWindow supplies per-pixel alpha; SetLayeredWindowAttributes would disable this path.
            if ((native.GetExtendedStyle(window) & 0x00080000) == 0)
            {
                throw new InvalidOperationException("The native bitmap window lost its required layered style.");
            }
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate nint WindowProcedure(nint hwnd, uint message, nint wparam, nint lparam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WindowClass
    {
        public uint Style;
        public nint Procedure;
        public int ClassExtra;
        public int WindowExtra;
        public nint Instance;
        public nint Icon;
        public nint Cursor;
        public nint Background;
        public nint MenuName;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string ClassName;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { public int X; public int Y; }
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeSize { public int Width; public int Height; }
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMessage
    {
        public nint Window;
        public uint Message;
        public nint Wparam;
        public nint Lparam;
        public uint Time;
        public NativePoint Point;
        public uint Private;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfo
    {
        public uint HeaderSize;
        public int Width;
        public int Height;
        public ushort Planes;
        public ushort BitCount;
        public uint Compression;
        public uint ImageSize;
        public int XPixelsPerMeter;
        public int YPixelsPerMeter;
        public uint ColorsUsed;
        public uint ColorsImportant;
    }
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct BlendFunction { public byte Operation; public byte Flags; public byte Alpha; public byte Format; }

    [DllImport("user32.dll", EntryPoint = "RegisterClassW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClass(ref WindowClass windowClass);
    [LibraryImport("kernel32.dll", EntryPoint = "GetModuleHandleW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint GetModuleHandle(string? name);
    [LibraryImport("user32.dll", EntryPoint = "CreateWindowExW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial nint CreateWindowEx(uint extendedStyle, string className, string title, uint style,
        int x, int y, int width, int height, nint parent, nint menu, nint instance, nint parameter);
    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial nuint SetTimer(nint window, nuint id, uint milliseconds, nint callback);
    [LibraryImport("user32.dll", EntryPoint = "GetMessageW", SetLastError = true)]
    private static partial int GetMessage(out NativeMessage message, nint window, uint minimum, uint maximum);
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool TranslateMessage(ref NativeMessage message);
    [LibraryImport("user32.dll", EntryPoint = "DispatchMessageW")]
    private static partial nint DispatchMessage(ref NativeMessage message);
    [LibraryImport("user32.dll", EntryPoint = "DefWindowProcW")]
    private static partial nint DefWindowProc(nint window, uint message, nint wparam, nint lparam);
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyWindow(nint window);
    [LibraryImport("user32.dll")]
    private static partial void PostQuitMessage(int exitCode);
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ShowWindow(nint window, int command);
    [LibraryImport("gdi32.dll", SetLastError = true)]
    private static partial nint CreateCompatibleDC(nint dc);
    [LibraryImport("gdi32.dll", SetLastError = true)]
    private static partial nint CreateDIBSection(nint dc, ref BitmapInfo info, uint usage, out nint bits, nint section, uint offset);
    [LibraryImport("gdi32.dll", SetLastError = true)]
    private static partial nint SelectObject(nint dc, nint bitmap);
    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteObject(nint value);
    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteDC(nint dc);
    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UpdateLayeredWindow(nint window, nint destinationDc, ref NativePoint destination,
        ref NativeSize size, nint sourceDc, ref NativePoint source, uint colorKey, ref BlendFunction blend, uint flags);
}
