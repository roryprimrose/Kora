namespace Kora.Core.Communication;

public sealed record CommunicationActivityObservation(
    Guid Generation, Guid DeviceEpoch, long Sequence, CallState State,
    DateTimeOffset ObservedAt, CommunicationActivityHealth Health)
{
    public static TimeSpan MaximumAge { get; } = TimeSpan.FromSeconds(5);
}