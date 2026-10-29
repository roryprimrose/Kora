using Kora.Core.Customizations;

namespace Kora.Application.Definitions;

public sealed record ProfileInstructionSaveOutcome(bool Saved, bool Reloaded, CustomizationSnapshot Current);
