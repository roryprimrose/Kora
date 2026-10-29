namespace Kora.Application.Skills;

public sealed record OwnedSkillGenerationRequest(
    string Purpose, string Input, string Output, OwnedSkillAuthoringAuthority Authority);
