using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Application.Hosting;

/// <summary>A volatile, single-use applicability review. Only its issuing broker can install it.</summary>
public sealed class SessionInstructionScopeReview
{
    internal SessionInstructionScopeReview(SessionInstructionScopeBroker owner,
        ScopedInstructionUseBroker.InstructionSourceReview source, DateTimeOffset createdAt, Func<bool> eligible)
    { Owner = owner; Source = source; CreatedAt = createdAt; Eligible = eligible; }

    internal SessionInstructionScopeBroker Owner { get; }
    internal ScopedInstructionUseBroker.InstructionSourceReview Source { get; }
    internal DateTimeOffset CreatedAt { get; }
    internal Func<bool> Eligible { get; }
    internal int Consumed;
    public SessionWorkspaceEntry Target => Source.Target;
    public RequestOrigin Origin => Source.Origin;
    public string Name => Source.Name;
    public string InstructionText => Source.InstructionText;
    public bool CanConfirm => Owner.IsReviewCurrent(this);
    public void Retire() => Interlocked.Exchange(ref Consumed, 1);
    public string Preview => "Apply " + Name + " to "
        + (Target.Metadata?.Name.Value ?? "this Active session") + ".\n\n"
        + SessionInstructionScopeBroker.Boundary + "\n\nCOMPLETE PINNED INSTRUCTION\n" + InstructionText;
}
