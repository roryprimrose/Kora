using Kora.Core.Storage;

namespace Kora.Application.Hosting;

/// <summary>A terminal observation with an existing disposition receipt only for confirmed deletion.</summary>
public sealed record SessionStopAndDeleteResult(SessionStopAndDeleteOutcome Outcome, SessionDispositionReceipt? Receipt = null,
    string? Recovery = null);
