using Kora.Core.Commands;

namespace Kora.Core.Dependencies;

public sealed record LocalModelResponse(
    string? Answer,
    BuiltInAction? Action,
    GrantChange? GrantChange = null,
    LocalModelQuestion? Question = null);
