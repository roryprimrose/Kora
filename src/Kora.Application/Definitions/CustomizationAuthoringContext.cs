namespace Kora.Application.Definitions;

public sealed record CustomizationAuthoringContext(string? Component,
    CustomizationAuthoringMode Mode = CustomizationAuthoringMode.FromRequest);
