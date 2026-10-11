namespace Kora.Core.Artifacts;

public sealed record InstructionWriteResult(
    bool Published, bool Deleted, bool Conflict, OwnedInstructionSnapshot? Current, bool Unchanged = false);
