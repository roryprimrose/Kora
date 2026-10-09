namespace Kora.Core.Commands;

public sealed record SessionCommandQuestion(Guid Id, long Revision, string State);
