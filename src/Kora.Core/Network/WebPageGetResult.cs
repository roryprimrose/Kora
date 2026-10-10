namespace Kora.Core.Network;

public sealed record WebPageGetResult(
    WebPageGetOutcome Outcome,
    string Reason,
    Uri? FinalAddress = null,
    string? MediaType = null,
    string? Text = null,
    bool Truncated = false,
    int RedirectCount = 0);
