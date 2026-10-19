using System.Runtime.InteropServices;

using AwesomeAssertions;

using Kora.Windows.IntegrationTests.Audio;
using Kora.Windows.Presentation;

namespace Kora.Windows.IntegrationTests.Presentation;

public sealed partial class WindowsPresenceRegionNativeTests
{
    [WindowsFact]
    public void ActualRegionsAreEmptyOrFullWithoutChangingRootLayering()
    {
        var window = CreateWindowEx(0, "STATIC", string.Empty, 0x80000000, -30000, -30000, 360, 240, 0, 0, 0, 0);
        window.Should().NotBe(0);
        var native = new WindowsPresenceRegionNative();
        var probe = native.CreateRegion(0, 0);
        try
        {
            var region = new WindowsPresenceRegion(window, native);
            region.Initialize();
            GetWindowRgn(window, probe).Should().Be(1, "an empty region is not a NULL region");
            region.Apply(true);
            GetWindowRgn(window, probe).Should().Be(2);
            GetRgnBox(probe, out var bounds).Should().Be(2);
            bounds.Right.Should().Be(360);
            bounds.Bottom.Should().Be(240);
            region.Apply(false);
            GetWindowRgn(window, probe).Should().Be(1);
            new WindowsPresenceInputNative().GetExtendedStyle(window).Should().Be(0);
            var conflict = () => native.Validate(window);
            conflict.Should().Throw<InvalidOperationException>();
        }
        finally
        {
            native.DeleteRegion(probe);
            DestroyWindow(window).Should().BeTrue();
        }
    }

    [WindowsFact]
    public void LayeredOrDestroyedRootsAreRejectedWithoutResettingThem()
    {
        var window = CreateWindowEx(0x80000, "STATIC", string.Empty, 0x80000000, -30000, -30000, 100, 100, 0, 0, 0, 0);
        window.Should().NotBe(0);
        var native = new WindowsPresenceRegionNative();
        try
        {
            var validate = () => native.Validate(window);
            validate.Should().Throw<InvalidOperationException>();
            new WindowsPresenceInputNative().GetExtendedStyle(window).Should().Be(0x80000);
        }
        finally
        {
            DestroyWindow(window).Should().BeTrue();
        }
        var destroyed = () => native.GetSize(window);
        destroyed.Should().Throw<InvalidOperationException>();
    }

    [WindowsFact]
    public void IncompatibleStyleIntroducedAfterAdmissionIsNotSilentlyAccepted()
    {
        var window = CreateWindowEx(0, "STATIC", string.Empty, 0x80000000, -30000, -30000, 100, 100, 0, 0, 0, 0);
        window.Should().NotBe(0);
        try
        {
            var region = new WindowsPresenceRegion(window, new WindowsPresenceRegionNative());
            region.Initialize();
            new WindowsPresenceInputNative().SetExtendedStyle(window, 0x80000);
            var apply = () => region.Apply(false);
            apply.Should().Throw<InvalidOperationException>();
        }
        finally
        {
            DestroyWindow(window).Should().BeTrue();
        }
    }

    [WindowsFact]
    public void RenderTargetIsOwnedPopupWithCompatiblePrivateClassAndDiesWithOwner()
    {
        var owner = CreateWindowEx(0, "STATIC", string.Empty, 0x80000000, -30000, -30000, 100, 100, 0, 0, 0, 0);
        owner.Should().NotBe(0);
        try
        {
            using var target = new WindowsPresenceRenderTarget(owner);
            GetWindow(target.Handle, 4).Should().Be(owner);
            GetAncestor(target.Handle, 2).Should().Be(target.Handle);
            (GetClassLong(target.Handle, -26) & 0x60).Should().Be(0u, "CS_OWNDC and CS_CLASSDC are incompatible with layering");
            var extended = new WindowsPresenceInputNative().GetExtendedStyle(target.Handle);
            (extended & 0x080800A0).Should().Be(0x080800A0u);
            target.Move(12, 18);
            GetWindowRect(target.Handle, out var bounds).Should().BeTrue();
            bounds.Left.Should().Be(-29988);
            bounds.Top.Should().Be(-29982);
            var popup = target.Handle;
            DestroyWindow(owner).Should().BeTrue();
            owner = 0;
            IsWindow(popup).Should().BeFalse();
        }
        finally
        {
            if (owner != 0) { DestroyWindow(owner).Should().BeTrue(); }
        }
    }

    [WindowsFact]
    public void PopupRejectsMissingOrChildOwner()
    {
        var missing = () => new WindowsPresenceRenderTarget(0);
        missing.Should().Throw<ArgumentException>();
        var owner = CreateWindowEx(0, "STATIC", string.Empty, 0x80000000, -30000, -30000, 100, 100, 0, 0, 0, 0);
        owner.Should().NotBe(0);
        try
        {
            var child = CreateWindowEx(0, "STATIC", string.Empty, 0x40000000, 0, 0, 10, 10, owner, 0, 0, 0);
            child.Should().NotBe(0);
            var create = () => new WindowsPresenceRenderTarget(child);
            create.Should().Throw<InvalidOperationException>();
        }
        finally
        {
            DestroyWindow(owner).Should().BeTrue();
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left; public int Top; public int Right; public int Bottom; }
    [LibraryImport("user32.dll", EntryPoint = "CreateWindowExW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint CreateWindowEx(uint extended, string className, string title, uint style,
        int x, int y, int width, int height, nint owner, nint menu, nint instance, nint parameter);
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyWindow(nint window);
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsWindow(nint window);
    [LibraryImport("user32.dll")]
    private static partial int GetWindowRgn(nint window, nint region);
    [LibraryImport("gdi32.dll")]
    private static partial int GetRgnBox(nint region, out Rect bounds);
    [LibraryImport("user32.dll")]
    private static partial nint GetWindow(nint window, uint command);
    [LibraryImport("user32.dll")]
    private static partial nint GetAncestor(nint window, uint flags);
    [LibraryImport("user32.dll", EntryPoint = "GetClassLongW")]
    private static partial uint GetClassLong(nint window, int index);
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetWindowRect(nint window, out Rect bounds);
}
