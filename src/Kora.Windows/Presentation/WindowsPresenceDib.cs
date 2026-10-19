using System.ComponentModel;
using System.Runtime.InteropServices;

using Kora.Core.Presentation;

using SkiaSharp;

namespace Kora.Windows.Presentation;

internal sealed partial class WindowsPresenceDib : IDisposable
{
    private readonly nint bitmap;
    private readonly nint previousBitmap;
    private readonly nint pixels;
    private readonly SKSurface? surface;
    private readonly SKPaint paint = new() { IsAntialias = true };
    private bool disposed;

    public WindowsPresenceDib(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        if (width > 4096 || height > 4096)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Native presence buffers are bounded to 4096 pixels per dimension.");
        }
        Width = width;
        Height = height;
        Stride = checked(width * 4);
        try
        {
            DeviceContext = CreateCompatibleDC(0);
            Check(DeviceContext != 0, "Creating the presence buffer DC");
            var info = new BitmapInfo { HeaderSize = 40, Width = width, Height = -height, Planes = 1, BitCount = 32 };
            bitmap = CreateDIBSection(DeviceContext, ref info, 0, out pixels, 0, 0);
            Check(bitmap != 0 && pixels != 0, "Creating the presence DIB");
            previousBitmap = SelectObject(DeviceContext, bitmap);
            Check(previousBitmap != 0 && previousBitmap != -1, "Selecting the presence DIB");
            surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul),
                pixels, Stride) ?? throw new InvalidOperationException("The presence DIB could not be wrapped for drawing.");
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public nint DeviceContext { get; }
    public int Width { get; }
    public int Height { get; }
    public int Stride { get; }

    public void Draw(ReadOnlySpan<PresenceParticle> particles, double scaling, byte red, byte green, byte blue, double opacity)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var canvas = surface!.Canvas;
        canvas.Clear(SKColors.Transparent);
        foreach (var particle in particles)
        {
            paint.Color = new SKColor(red, green, blue, (byte)Math.Round((70 + particle.Depth * 28) * opacity));
            canvas.DrawCircle((float)(particle.X * scaling), (float)(particle.Y * scaling),
                (float)(particle.Radius * scaling), paint);
        }
        canvas.Flush();
    }

    internal byte[] CopyPixels()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var bytes = new byte[Stride * Height];
        Marshal.Copy(pixels, bytes, 0, bytes.Length);
        return bytes;
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
            paint.Dispose();
        }
        finally
        {
            try
            {
                if (previousBitmap != 0 && previousBitmap != -1)
                {
                    var restored = SelectObject(DeviceContext, previousBitmap);
                    Check(restored != 0 && restored != -1, "Restoring the presence DC bitmap");
                }
            }
            finally
            {
                try
                {
                    if (bitmap != 0)
                    {
                        Check(DeleteObject(bitmap), "Deleting the presence DIB");
                    }
                }
                finally
                {
                    if (DeviceContext != 0)
                    {
                        Check(DeleteDC(DeviceContext), "Deleting the presence DC");
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

    [LibraryImport("gdi32.dll", SetLastError = true)]
    private static partial nint CreateCompatibleDC(nint dc);
    [LibraryImport("gdi32.dll", SetLastError = true)]
    private static partial nint CreateDIBSection(nint dc, ref BitmapInfo info, uint usage, out nint bits, nint section, uint offset);
    [LibraryImport("gdi32.dll", SetLastError = true)]
    private static partial nint SelectObject(nint dc, nint bitmap);
    [LibraryImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteObject(nint value);
    [LibraryImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteDC(nint dc);
}