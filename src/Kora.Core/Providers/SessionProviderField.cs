namespace Kora.Core.Providers;

/// <summary>Individually inheritable session provider fields.</summary>
public enum SessionProviderField
{
    /// <summary>Desired service.</summary>
    Service,
    /// <summary>Exact advertised model.</summary>
    Model,
    /// <summary>Exact model-specific reasoning option.</summary>
    Reasoning,
    /// <summary>Exact model-specific context option.</summary>
    Context,
}
