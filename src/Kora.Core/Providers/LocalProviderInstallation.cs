namespace Kora.Core.Providers;

/// <summary>Installation evidence only; never runtime, model or generation readiness.</summary>
public enum LocalProviderInstallation
{
    /// <summary>Installation could not be established.</summary>
    Unknown,
    /// <summary>No supported installation was found.</summary>
    Missing,
    /// <summary>A supported installation was observed.</summary>
    Installed,
}
