using System.Buffers.Binary;
using AwesomeAssertions;

namespace Kora.Rt2;

public sealed class ObserverTests
{
    [Fact]
    public void OwnershipRequiresExactImageAndHostParent()
    {
        LifecycleObserver.IsOwnedRoot(11, 11, @"Q:\owned\runtime.exe", @"Q:\owned\runtime.exe").Should().BeTrue();
        LifecycleObserver.IsOwnedRoot(12, 11, @"Q:\owned\runtime.exe", @"Q:\owned\runtime.exe").Should().BeFalse();
        LifecycleObserver.IsOwnedRoot(11, 11, @"Q:\other\runtime.exe", @"Q:\owned\runtime.exe").Should().BeFalse();
        LifecycleObserver.IsOwnedRoot(11, 11, null, @"Q:\owned\runtime.exe").Should().BeFalse();
    }

    [Theory]
    [InlineData(false, false, 24)]
    [InlineData(true, false, 56)]
    [InlineData(false, true, 12)]
    [InlineData(true, true, 28)]
    public void NativeTableShapeAndNetworkByteOrderAreValidated(bool ipv6, bool udp, int width)
    {
        var bytes = new byte[4 + width];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, 1);
        var row = bytes.AsSpan(4);
        BinaryPrimitives.WriteUInt16BigEndian(row[(ipv6 ? 20 : udp ? 4 : 8)..], 49152);
        BinaryPrimitives.WriteUInt32LittleEndian(row[(udp ? ipv6 ? 24 : 8 : ipv6 ? 52 : 20)..], 1234);
        var receipt = NativeSnapshot.ParseTable(bytes, ipv6, udp).Single();
        receipt.LocalPort.Should().Be(49152);
        receipt.ProcessId.Should().Be(1234);
        receipt.Protocol.Should().Be(udp ? "UDP" : "TCP");
        if (udp) receipt.RemoteAddress.Should().BeNull();
        var truncated = () => NativeSnapshot.ParseTable(bytes.AsSpan(0, bytes.Length - 1), ipv6, udp);
        truncated.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void MalformedTableCannotBecomeEmptySuccessfulObservation()
    {
        var missing = () => NativeSnapshot.ParseTable([], false, false);
        missing.Should().Throw<InvalidDataException>();
        var bytes = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, uint.MaxValue);
        var hostile = () => NativeSnapshot.ParseTable(bytes, false, false);
        hostile.Should().Throw<InvalidDataException>();
    }
}
