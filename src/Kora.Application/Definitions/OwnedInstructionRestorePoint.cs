using Kora.Core.Artifacts;

namespace Kora.Application.Definitions;

public sealed record OwnedInstructionRestorePoint(OwnedInstructionSnapshot Previous, OwnedInstructionSnapshot Current);