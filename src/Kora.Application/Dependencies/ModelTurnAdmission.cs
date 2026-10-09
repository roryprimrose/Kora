using Kora.Core.Dependencies;

namespace Kora.Application.Dependencies;

public sealed record ModelTurnAdmission(ModelTurnResult Result, ModelTurn? Turn = null);
