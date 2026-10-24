using System.ComponentModel;
using System.Runtime.InteropServices;

using SkiaSharp;

namespace Presence.Performance;

internal sealed partial class NativeDibBuffer : IDisposable
{
    private readonly nint dc;
    private readonly nint bitmap;
    private readonly nint previousBitmap;
    private readonly nint pixels;
    private readonly SKSurface? surface;
    private bool disposed;

    public NativeDibBuffer(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        Width = width;
        Height = height;
        Stride = checked(width * 4);
        ByteCount = checked(Stride * height);
        try
        {
            dc = CreateCompatibleDC(0);
            Check(dc != 0, "CreateCompatibleDC");
            var info = new BitmapInfo { HeaderSize = 40, Width = width, Height = -height, Planes = 1, BitCount = 32 };
            bitmap = CreateDIBSection(dc, ref info, 0, out pixels, 0, 0);
            Check(bitmap != 0 && pixels != 0, "CreateDIBSection");
            previousBitmap = SelectObject(dc, bitmap);
            Check(previousBitmap != 0 && previousBitmap != -1, "SelectObject");
            surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul),
                pixels, Stride) ?? throw new InvalidOperationException("Skia could not wrap the owned DIB.");
            var layout = ReadLayout();
            if (layout.Width != width || layout.Height != height || layout.Stride != Stride
                || layout.BitsPerPixel != 32 || layout.Compression != 0)
            {
                throw new InvalidOperationException($"Unexpected native DIB layout: {layout}.");
            }
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public int Width { get; }
    public int Height { get; }
    public int Stride { get; }
    public int ByteCount { get; }

    public nint DeviceContext
    {
        get
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            return dc;
        }
    }

    public Layout ReadLayout()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var section = new DibSection();
        Check(GetObject(bitmap, Marshal.SizeOf<DibSection>(), ref section) == Marshal.SizeOf<DibSection>(), "GetObjectW");
        return new Layout(section.Bitmap.Width, section.Bitmap.Height, section.Bitmap.WidthBytes,
            section.Bitmap.BitsPixel, section.Header.Compression);
    }

    public uint ReadGdiRgb(int x, int y)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (x < 0 || x >= Width)
        {
            throw new ArgumentOutOfRangeException(nameof(x));
        }
        if (y < 0 || y >= Height)
        {
            throw new ArgumentOutOfRangeException(nameof(y));
        }
        // Verify GDI's coordinate interpretation; GetObject need not retain the creation header's height sign.
        var color = GetPixel(dc, x, y);
        Check(color != uint.MaxValue, "GetPixel");
        return color;
    }

    public void Draw(ParticleScene scene, double dotSize, bool light)
    {
        ArgumentNullException.ThrowIfNull(scene);
        var canvas = BeginFrame();
        scene.Draw(canvas, Width, dotSize, light);
        canvas.Flush();
    }

    public void Draw(Action<SKCanvas> draw)
    {
        ArgumentNullException.ThrowIfNull(draw);
        var canvas = BeginFrame();
        draw(canvas);
        canvas.Flush();
    }

    public byte[] CopyBgraPixels()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var bytes = new byte[ByteCount];
        // Read the CreateDIBSection pointer, not a Skia snapshot or a second raster allocation.
        Marshal.Copy(pixels, bytes, 0, bytes.Length);
        return bytes;
    }

    public SKImage Snapshot()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        return surface!.Snapshot();
    }

    private SKCanvas BeginFrame()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var canvas = surface!.Canvas;
        canvas.Clear(SKColors.Transparent);
        return canvas;
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        try
        {
            surface?.Dispose();
        }
        finally
        {
            try
            {
                if (previousBitmap != 0 && previousBitmap != -1)
                {
                    var restored = SelectObject(dc, previousBitmap);
                    Check(restored != 0 && restored != -1, "Restoring the original DC bitmap");
                }
            }
            finally
            {
                try
                {
                    if (bitmap != 0)
                    {
                        Check(DeleteObject(bitmap), "DeleteObject");
                    }
                }
                finally
                {
                    if (dc != 0)
                    {
                        Check(DeleteDC(dc), "DeleteDC");
                    }
                }
            }
        }
    }

    private static void Check(bool succeeded, string operation)
    {
        if (!succeeded)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), operation);
        }
    }

    internal sealed record Layout(int Width, int Height, int Stride, int BitsPerPixel, uint Compression);

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

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapDescription
    {
        public int Type;
        public int Width;
        public int Height;
        public int WidthBytes;
        public ushort Planes;
        public ushort BitsPixel;
        public nint Bits;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DibSection
    {
        public BitmapDescription Bitmap;
        public BitmapInfo Header;
        public uint RedMask;
        public uint GreenMask;
        public uint BlueMask;
        public nint Section;
        public uint Offset;
    }

    [LibraryImport("gdi32.dll", SetLastError = true)]
    private static partial nint CreateCompatibleDC(nint dc);
    [LibraryImport("gdi32.dll", SetLastError = true)]
    private static partial nint CreateDIBSection(nint dc, ref BitmapInfo info, uint usage, out nint bits, nint section, uint offset);
    [LibraryImport("gdi32.dll", SetLastError = true)]
    private static partial nint SelectObject(nint dc, nint bitmap);
    [LibraryImport("gdi32.dll", EntryPoint = "GetObjectW", SetLastError = true)]
    private static partial int GetObject(nint value, int size, ref DibSection section);
    [LibraryImport("gdi32.dll", SetLastError = true)]
    private static partial uint GetPixel(nint dc, int x, int y);
    [LibraryImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteObject(nint value);
    [LibraryImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteDC(nint dc);
}
