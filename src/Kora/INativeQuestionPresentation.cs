namespace Kora;

/// <summary>Native presentation lifetime and focus, separate from question or approval authority.</summary>
internal interface INativeQuestionPresentation
{
    /// <summary>Signals disposal of this exact presentation.</summary>
    event EventHandler? Closed;
    /// <summary>Gets whether this exact presentation is currently shown.</summary>
    bool IsPresented { get; }
    /// <summary>Gets whether its native owner is active.</summary>
    bool IsActive { get; }
    /// <summary>Explicitly activates this original presentation.</summary>
    void Activate();
    /// <summary>Closes this presentation using its existing question dismissal contract.</summary>
    void Close();
    /// <summary>Reports the exact owning operation's outcome.</summary>
    void ReportOutcome(string message);
    /// <summary>Captures this presentation's current lifetime.</summary>
    Func<bool> CaptureFocusLifetime();
    /// <summary>Focuses only this exact original presentation under fresh admission.</summary>
    Task<bool> FocusExistingAsync(Func<bool> admitted);
}
