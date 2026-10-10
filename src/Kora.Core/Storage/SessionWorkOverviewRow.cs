namespace Kora.Core.Storage;

/// <summary>An exact metadata subject and either its current bounded work observation or an explicit unavailable reason.</summary>
public sealed record SessionWorkOverviewRow(
    SessionWorkspaceEntry Session, SessionWorkOverviewAvailability Availability, SessionWorkOverviewObservation? Work);
