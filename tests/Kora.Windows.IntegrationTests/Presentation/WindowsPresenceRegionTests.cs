using System.ComponentModel;

using AwesomeAssertions;

using Kora.Windows.Presentation;

namespace Kora.Windows.IntegrationTests.Presentation;

public sealed class WindowsPresenceRegionTests
{
    [Fact]
    public void PassiveUsesRealEmptyRegionAndStablePollingDoesNotAllocate()
    {
        var regions = new RegionNative();
        var input = new WindowsPresenceWindowInput(1, new InputNative(), regions);
        for (var index = 0; index < 100; index++) { input.Refresh(false).Should().BeFalse(); }
        regions.Sizes.Should().Equal([(0, 0)]);
        regions.Transferred.Should().Equal([1]);
        regions.Deleted.Should().BeEmpty();
    }

    [Fact]
    public void FullRegionResizesAndGestureRecipientIsRetainedUntilReleaseOrPrivacyClosure()
    {
        var regions = new RegionNative();
        var native = new InputNative { Control = true };
        var input = new WindowsPresenceWindowInput(1, native, regions);
        input.Refresh(true).Should().BeTrue();
        native.Pressed = true;
        native.Control = false;
        regions.Size = (600, 480);
        input.Refresh(true).Should().BeTrue();
        input.Refresh(false).Should().BeFalse();
        regions.Sizes.Should().Equal([(0, 0), (360, 360), (600, 480), (0, 0)]);
        regions.Deleted.Should().BeEmpty();
    }

    [Fact]
    public void GestureBeginningUnderneathDoesNotTransferOnControlPress()
    {
        var native = new InputNative { Pressed = true, Control = true };
        var input = new WindowsPresenceWindowInput(1, native, new RegionNative());
        input.Refresh(true).Should().BeFalse();
        native.Pressed = false;
        input.Refresh(true).Should().BeTrue();
    }

    [Fact]
    public void FailedRegionTransferDeletesOnlyUntransferredHandleAndRetainsInputMode()
    {
        var regions = new RegionNative();
        var native = new InputNative { Control = true };
        var input = new WindowsPresenceWindowInput(1, native, regions);
        regions.Fail = true;
        var refresh = () => input.Refresh(true);
        refresh.Should().Throw<Win32Exception>();
        input.InterceptsMouse.Should().BeFalse();
        regions.Transferred.Should().Equal([1]);
        regions.Deleted.Should().Equal([2]);
        regions.Fail = false;
        input.Refresh(true).Should().BeTrue();
        regions.Transferred.Should().Equal([1, 3]);
    }

    [Fact]
    public void ReentrantPositionNotificationDoesNotAllocateRecursively()
    {
        var regions = new RegionNative();
        var input = new WindowsPresenceWindowInput(1, new InputNative { Control = true }, regions);
        regions.Reenter = () => input.Refresh(true);
        input.Refresh(true).Should().BeTrue();
        regions.Sizes.Should().Equal([(0, 0), (360, 360)]);
    }

    [Fact]
    public void UnknownOrConflictingOwnershipIsRejectedBeforeApplyingAnyRegion()
    {
        var regions = new RegionNative { ValidationFailure = new InvalidOperationException("Conflicting owner.") };
        var create = () => new WindowsPresenceWindowInput(1, new InputNative(), regions);
        create.Should().Throw<InvalidOperationException>();
        regions.Sizes.Should().BeEmpty();
    }

    [Fact]
    public void ReentrantRefreshCannotCommitInputBeforeAnOuterFailedRegionTransfer()
    {
        var regions = new RegionNative();
        var input = new WindowsPresenceWindowInput(1, new InputNative { Control = true }, regions);
        regions.Reenter = () =>
        {
            input.Refresh(true).Should().BeFalse();
            throw new Win32Exception(5);
        };
        var refresh = () => input.Refresh(true);
        refresh.Should().Throw<Win32Exception>();
        input.InterceptsMouse.Should().BeFalse();
        regions.Transferred.Should().Equal([1]);
        regions.Deleted.Should().Equal([2]);
    }

    private sealed class InputNative : IWindowsPresenceInputNative
    {
        public bool Control { get; set; }
        public bool Pressed { get; set; }
        public bool IsControlPressed() => Control;
        public bool IsMouseButtonPressed() => Pressed;
        public void ShowMoveCursorIfOverWindow(nint window) { }
        public uint GetExtendedStyle(nint window) => throw new InvalidOperationException("Region input must not read layered styles.");
        public void SetExtendedStyle(nint window, uint style) => throw new InvalidOperationException("Region input must not mutate styles.");
        public void EnableLayeredRendering(nint window) => throw new InvalidOperationException("Region input must not set constant alpha.");
    }

    private sealed class RegionNative : IWindowsPresenceRegionNative
    {
        public List<(int Width, int Height)> Sizes { get; } = [];
        public List<nint> Transferred { get; } = [];
        public List<nint> Deleted { get; } = [];
        public (int Width, int Height) Size { get; set; } = (360, 360);
        public bool Fail { get; set; }
        public Action? Reenter { get; set; }
        public InvalidOperationException? ValidationFailure { get; init; }
        public void Validate(nint window)
        {
            if (ValidationFailure is not null) { throw ValidationFailure; }
        }
        public (int Width, int Height) GetSize(nint window) => Size;
        public nint CreateRegion(int width, int height)
        {
            Sizes.Add((width, height));
            return Sizes.Count;
        }
        public void SetRegion(nint window, nint region)
        {
            if (Fail) { throw new Win32Exception(5); }
            Reenter?.Invoke();
            Transferred.Add(region);
        }
        public void DeleteRegion(nint region) => Deleted.Add(region);
    }
}
