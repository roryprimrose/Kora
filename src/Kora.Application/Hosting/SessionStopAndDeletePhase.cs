namespace Kora.Application.Hosting;

/// <summary>The exact work phase whose retained-data impact was reviewed.</summary>
public enum SessionStopAndDeletePhase
{
    /// <summary>Confirmation must request cancellation and join the original running lease.</summary>
    Running,
    /// <summary>A prior accepted compound stop physically joined; fresh review grants only deletion.</summary>
    WorkStopped,
}
