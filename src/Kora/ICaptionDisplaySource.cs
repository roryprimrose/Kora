namespace Kora;

/// <summary>The desktop mechanism boundary for current actual display observations.</summary>
public interface ICaptionDisplaySource : IDisposable
{
    /// <summary>Occurs after the live topology, working area or DPI changes.</summary>
    event EventHandler? Changed;
    /// <summary>Observes actual sources and their current lifetime-bound geometry.</summary>
    CaptionDisplaySnapshot Capture();
}
