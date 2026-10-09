namespace Kora.Application.Configuration;

public sealed record OutputDeviceCommandChoice(string Id, string Name, bool System, bool Available, bool Muted);
