namespace Kora.Core.Providers;

/// <summary>An individual edit of a complete application provider tuple.</summary>
public enum ProviderProfileField
{
    /// <summary>The exact model ID; retained dependent options must remain supported.</summary>
    Model,
    /// <summary>The exact reasoning option ID for the saved model.</summary>
    Reasoning,
    /// <summary>The exact context option ID for the saved model.</summary>
    Context,
}
