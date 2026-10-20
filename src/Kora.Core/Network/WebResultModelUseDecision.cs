namespace Kora.Core.Network;

/// <summary>Exact web-source policy outcome; only an affirmative decision permits local context construction.</summary>
public enum WebResultModelUseDecision
{
    /// <summary>No maintained source-specific sensitivity/source-use decision is available.</summary>
    Unavailable,

    /// <summary>The exact source is denied model use.</summary>
    Denied,

    /// <summary>The exact complete snapshot is admitted for this local-only use.</summary>
    Allowed,
}
