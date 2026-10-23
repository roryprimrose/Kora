namespace Kora.Core.Voice;

/// <summary>Requested compute policy, not a hardware capability or measured resource guarantee.</summary>
public enum SpeechInputResource
{
    /// <summary>The existing Windows adapter controls its own resources.</summary>
    WindowsManaged,
    /// <summary>Explicit CPU execution, unavailable until an exact adapter is qualified.</summary>
    Cpu,
    /// <summary>Explicit GPU execution, unavailable until an exact adapter is qualified.</summary>
    Gpu,
}
