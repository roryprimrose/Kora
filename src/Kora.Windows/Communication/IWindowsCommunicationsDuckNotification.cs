using System.Runtime.InteropServices;

namespace Kora.Windows.Communication;

/// <summary>Windows Core Audio callback ABI; supplies observations, never host authority.</summary>
[ComVisible(true), Guid("C3B284D4-6D39-4359-B3CF-B56DDB3BB39C"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IWindowsCommunicationsDuckNotification
{
    /// <summary>Receives a communications-stream start notification.</summary>
    [PreserveSig]
    int OnVolumeDuckNotification([MarshalAs(UnmanagedType.LPWStr)] string? session, uint count);

    /// <summary>Receives a communications-stream end notification.</summary>
    [PreserveSig]
    int OnVolumeUnduckNotification([MarshalAs(UnmanagedType.LPWStr)] string? session);
}