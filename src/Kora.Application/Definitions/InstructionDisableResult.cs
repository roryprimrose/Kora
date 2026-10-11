using Kora.Core.Artifacts;

namespace Kora.Application.Definitions;

public sealed record InstructionDisableResult(
    OwnedInstructionSnapshot Reviewed, Guid ControlReceipt, bool Cancelled, bool WasEnabled);
