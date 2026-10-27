using Kora.Core.Conversation;

namespace Kora.Application.Configuration.SettingInspection;

public sealed record SettingInspectionResult(SettingInspectionOutcome Outcome, ConversationSubmissionReceipt? Receipt = null);
