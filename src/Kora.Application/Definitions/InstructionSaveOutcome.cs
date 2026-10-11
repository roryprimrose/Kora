using Kora.Core.Artifacts;

namespace Kora.Application.Definitions;

public sealed record InstructionSaveOutcome(bool Saved, bool Deleted, bool Reloaded, OwnedInstructionSnapshot? Current, bool Unchanged = false);
