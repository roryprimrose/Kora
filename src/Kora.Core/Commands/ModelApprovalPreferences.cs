namespace Kora.Core.Commands;

public sealed record ModelApprovalPreferences(
    bool RequireAssistantNameForVoiceApproval,
    IReadOnlyList<BuiltInAction> AlwaysAllowedActions);
