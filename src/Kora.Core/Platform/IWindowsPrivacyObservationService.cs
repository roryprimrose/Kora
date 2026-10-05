namespace Kora.Core.Platform;

/// <summary>OS observations only: does not grant consent, ownership or recovery enablement.</summary>
public interface IWindowsPrivacyObservationService : IDisposable
{
    /// <summary>Raised synchronously on the observer thread; close privacy gates before dispatching UI work.</summary>
    event EventHandler<WindowsPrivacyChangedEventArgs>? Changed;

    WindowsPrivacySnapshot Current { get; }

    WindowsPrivacySnapshot Refresh();
}
