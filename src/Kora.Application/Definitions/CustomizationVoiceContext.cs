using Kora.Core.Customizations;

namespace Kora.Application.Definitions;

public sealed record CustomizationVoiceContext(CustomizationKind Kind, CustomizationScope Scope,
    string Name, string? Component, bool IsReadOnly, bool RecoveryRequired);
