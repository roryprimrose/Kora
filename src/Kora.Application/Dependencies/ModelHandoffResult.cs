using Kora.Core.Dependencies;

namespace Kora.Application.Dependencies;

public sealed record ModelHandoffResult(ModelHandoffOutcome Outcome, ModelTurnReason Reason,
    ModelHandoffOffer? Offer = null, ModelHandoffReview? Review = null);
