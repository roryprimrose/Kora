using Kora.Core.Skills;

namespace Kora.Windows.Skills;

/// <summary>Private assigned-right activation confined to one synchronous native operation.</summary>
internal interface IWindowsShutdownAuthority : IDisposable
{
    /// <summary>Enables only an already-assigned shutdown right on the private token.</summary>
    int Enable();

    /// <summary>Requests graceful power control under that private token.</summary>
    FixedSessionEffectResult Request(FixedSessionAction action);

    /// <summary>Restores the private token's original privilege state.</summary>
    int Restore();
}