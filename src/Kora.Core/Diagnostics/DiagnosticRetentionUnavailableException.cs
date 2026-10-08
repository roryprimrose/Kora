namespace Kora.Core.Diagnostics;

/// <summary>Ordinary SQLite evidence has no confirmed policy; independent audit delivery remains mandatory.</summary>
public sealed class DiagnosticRetentionUnavailableException()
    : InvalidOperationException("SQLite diagnostic retention is unconfirmed; inspect saved preferences and explicitly refresh.");
