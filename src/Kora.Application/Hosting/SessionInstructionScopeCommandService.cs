using Kora.Core.Artifacts;
using Kora.Core.Commands;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Application.Hosting;

/// <summary>Shared explicit scope actions for native controls and original session commands.</summary>
public sealed class SessionInstructionScopeCommandService(SessionInstructionScopeBroker broker)
{
    public IReadOnlyList<ArtifactDefinition> Choices => broker.Choices;
    public SessionInstructionScopeReview? Pending { get; private set; }

    public void Retire() { Pending?.Retire(); Pending = null; broker.RetireReviews(); }

    public async Task<string> ReviewAsync(SessionWorkspaceEntry target, ArtifactDefinition selected,
        RequestOrigin origin, Func<bool> hostEligible, Func<bool> reviewEligible, CancellationToken token)
    {
        Retire();
        var review = await broker.ReviewAsync(target, selected, hostEligible, reviewEligible, token, origin).ConfigureAwait(false);
        if (!reviewEligible()) { review.Retire(); throw new InvalidOperationException("The original scope selection changed."); }
        Pending = review;
        return review.Preview;
    }

    public async Task<string> ExecuteAsync(SessionCommand command, SessionWorkspaceEntry target,
        RequestOrigin origin, Func<bool> hostEligible, Func<bool> reviewEligible, CancellationToken token)
    {
        SessionConversationService.RequireOriginalSource(origin);
        token.ThrowIfCancellationRequested();
        if (!hostEligible() || !reviewEligible() || !target.Authority.IsActive)
        { throw new InvalidOperationException("Select the exact current Active session with original private input."); }
        switch (command.Operation)
        {
            case SessionCommandOperation.InstructionScopeReview:
                var choices = Choices.Where(item => string.Equals(item.Name, command.ScopeInstructionName, StringComparison.Ordinal)).ToArray();
                if (choices.Length != 1)
                { Retire(); throw new InvalidOperationException("Select one unambiguous enabled owned instruction by its displayed name."); }
                return await ReviewAsync(target, choices[0], origin, hostEligible, reviewEligible, token).ConfigureAwait(false);
            case SessionCommandOperation.InstructionScopeConfirm:
                var review = Pending ?? throw new InvalidOperationException("Review the complete exact instruction and target first.");
                Pending = null;
                if (review.Target != target)
                { review.Retire(); throw new InvalidOperationException("The scope review belongs to another exact session."); }
                await broker.ConfirmAsync(review, () => hostEligible() && reviewEligible(), token).ConfigureAwait(false);
                return broker.Status(target);
            case SessionCommandOperation.InstructionScopeStop:
                Retire();
                broker.Stop(target, origin, hostEligible);
                return "Stopped applying. " + broker.Status(target);
            case SessionCommandOperation.InstructionScopeStatus:
                return broker.Status(target);
            default:
                throw new InvalidOperationException(SessionCommand.InstructionScopeSyntax);
        }
    }
}
