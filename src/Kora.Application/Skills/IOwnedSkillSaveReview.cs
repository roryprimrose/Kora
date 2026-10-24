using Kora.Core.Skills;

namespace Kora.Application.Skills;

public interface IOwnedSkillSaveReview
{
    Task<bool> Review(OwnedDeclarativeSkillDocument document, string destination, CancellationToken token);
    Task<bool> ConfirmPublication(OwnedDeclarativeSkillDocument document, string destination, CancellationToken token);
}
