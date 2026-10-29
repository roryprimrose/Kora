namespace Kora.Application.Configuration;

/// <summary>The closed original-user startup grammar. Setters prepare review, never commit.</summary>
public enum LogonStartupOperation { Inspect, Enable, Disable, Reset, Confirm, Cancel, Invalid }
