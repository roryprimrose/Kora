using Kora.Core.Dependencies;

namespace Kora.Application.Dependencies;

internal sealed record ModelProviderReply(
    ModelTurnOutcome Outcome, ModelProviderSelection ReportedSelection, LocalModelResponse? Response = null);
