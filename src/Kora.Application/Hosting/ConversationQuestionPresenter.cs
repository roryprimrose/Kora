using Kora.Core.Hosting;
using Kora.Core.Interaction;

namespace Kora.Application.Hosting;

/// <summary>Trusted native presentation uses the existing host question services and returns both exact receipts.</summary>
/// <param name="request">The original exact host request, never an incoming provider identity.</param>
/// <param name="generation">The admitted original session generation.</param>
/// <param name="spec">The complete bounded clarification shape; no approval authority.</param>
/// <param name="expiresAt">The original turn's fixed deadline.</param>
/// <param name="admitted">The exact turn's live admission fence.</param>
/// <param name="token">The owning turn's cancellation.</param>
/// <returns>The original presentation and explicit submitted answer, or a truthful non-answer decision.</returns>
public delegate Task<ConversationQuestionPresentation> ConversationQuestionPresenter(
    HostRequest request, HostRevision generation, QuestionSpec spec, DateTimeOffset expiresAt,
    Func<bool> admitted, CancellationToken token);
