using Kora.Core.Commands;
using System.Text.Json.Serialization;

namespace Kora.Core.Dependencies;

public sealed record LocalModelResponse(
    string? Answer,
    BuiltInAction? Action,
    GrantChange? GrantChange = null,
    LocalModelQuestion? Question = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? SettingsFilter = null);
