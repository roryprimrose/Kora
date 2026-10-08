namespace Kora.Application.Communication;

/// <summary>Owned generation fence and asynchronous resource release, not permission to reopen input.</summary>
public sealed record ManualCallRetirement(Task Completion, Func<bool> RemainsEligible);
