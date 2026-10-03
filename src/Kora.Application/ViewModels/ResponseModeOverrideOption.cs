using Kora.Core.Voice;

namespace Kora.Application.ViewModels;

public sealed record ResponseModeOverrideOption(
    string Label,
    ResponseOutputMode? Mode);
