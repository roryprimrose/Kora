using Kora.Core.Conversation;
using Kora.Core.Presentation;

namespace Kora;

internal sealed partial class SessionsViewModel
{
    internal Func<AdmittedDetailContent, Func<bool>, Func<CancellationToken, Task<AdmittedDetailContent>>,
        CancellationToken, Task<string>>? ExportExactAnswer { get; set; }

    public bool CanExportExactAnswer => CanReadConversation && access.CanControl && !conversationSourceHeld
        && ExportExactAnswer is not null;

    public Task ExportExactAnswerAsync(ConversationMessage message, Func<bool> presenter) =>
        RunConversationAsync(async (target, live) =>
        {
            if (!access.CanControl || conversationSourceHeld
                || !ConversationMessages.Any(row => ReferenceEquals(row, message)) || message.AssistantResponse is null)
            { throw new InvalidOperationException("Select one currently admitted retained final ASSISTANT row for export."); }
            var page = conversationPage;
            var completed = completedConversationMessage;
            bool Current() => live() && access.CanControl && presenter() && !conversationSourceHeld
                && ReferenceEquals(page, conversationPage) && ReferenceEquals(completed, completedConversationMessage);
            Task<AdmittedDetailContent> Resolve(CancellationToken token) =>
                service.ReadConversationDetailAsync(target, message, Current, token, assistant: true);
            var source = await Resolve(lifetime.Token);
            conversationStatus = await (ExportExactAnswer
                ?? throw new InvalidOperationException("The dedicated native export writer is unavailable."))(
                    source, Current, Resolve, lifetime.Token);
        }, requireOriginalInput: true);
}
