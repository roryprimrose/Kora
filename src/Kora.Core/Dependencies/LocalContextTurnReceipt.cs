using Kora.Core.Hosting;

namespace Kora.Core.Dependencies;

/// <summary>A volatile answer and content-free lifecycle receipt, not conversation or execution authority.</summary>
public sealed record LocalContextTurnReceipt(
    HostRequest Request,
    ModelTurnOutcome Outcome,
    LocalContextTurnReason Reason,
    HostTaskState? DurableState = null,
    ModelTurnResult? ModelResult = null)
{
    public string? Answer => Outcome == ModelTurnOutcome.Succeeded ? ModelResult?.Response?.Answer : null;
}