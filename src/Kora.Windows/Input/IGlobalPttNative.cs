using Kora.Core.Voice;

namespace Kora.Windows.Input;

/// <summary>The replaceable Windows hotkey/message-pump boundary.</summary>
internal interface IGlobalPttNative
{
    /// <summary>Creates the current thread's message queue.</summary>
    uint Initialize();
    /// <summary>Wakes the exact owning thread.</summary>
    void Wake(uint threadId);
    /// <summary>Registers one chord without repeated press messages.</summary>
    void Register(GlobalPttShortcut shortcut);
    /// <summary>Removes the owning thread's registration.</summary>
    void Unregister();
    /// <summary>Waits indefinitely when idle; checks release only during a hold.</summary>
    GlobalPttNativeMessage Wait(bool held);
    /// <summary>Checks only the assigned chord's current key state.</summary>
    bool IsHeld(GlobalPttShortcut shortcut);
}
