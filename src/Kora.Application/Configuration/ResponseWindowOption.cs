namespace Kora.Application.Configuration;

/// <summary>Identifies one device-local response-window Boolean.</summary>
public enum ResponseWindowOption
{
    /// <summary>Suppresses inactivity hiding of an already admitted response.</summary>
    AlwaysShow,

    /// <summary>Keeps only Kora's response window on top.</summary>
    Topmost,
}
