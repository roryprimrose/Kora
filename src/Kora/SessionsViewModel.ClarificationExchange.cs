using System.Text.Json;

using Kora.Core.Conversation;
using Kora.Core.Storage;

namespace Kora;

internal sealed partial class SessionsViewModel
{
    private ClarificationExchange? clarificationExchange;
    private long clarificationExchangeEpoch;
    public ClarificationExchange? CurrentClarificationExchange =>
        clarificationExchange?.IsCurrent == true ? clarificationExchange : null;

    public Task ReadClarificationExchangeAsync(ConversationMessage message, bool next = false) =>
        RunConversationAsync(async (target, live) =>
        {
            if (!next) { ClearClarificationExchange(); }
            var exchangeEpoch = clarificationExchangeEpoch;
            var search = ConversationSearchMessages.Any(record => ReferenceEquals(record, message));
            var searchEpoch = conversationSearchEpoch;
            bool Original() => live() && selected == target && exchangeEpoch == clarificationExchangeEpoch
                && (search ? searchEpoch == conversationSearchEpoch
                    && ConversationSearchMessages.Any(record => ReferenceEquals(record, message))
                    : ConversationMessages.Any(record => ReferenceEquals(record, message)));
            if (!Original() || message.Request.SessionId != target.Authority.SessionId)
            { throw new InvalidOperationException("Choose the exact original turn from the admitted conversation page."); }
            var exchange = next
                ? CurrentClarificationExchange is { } current && ReferenceEquals(current.Message, message) ? current
                    : throw new InvalidOperationException("Read this exact turn before advancing its exchange.")
                : new ClarificationExchange(message, Original);
            var cursor = next ? exchange.Page?.Next
                ?? throw new InvalidOperationException("No continuation for this exact exchange snapshot.") : null;
            clarificationExchange = exchange;
            exchange.Withdraw();
            NotifyClarificationExchange();
            try
            {
                await conversation!.ReadDetailAsync(target, message, Original, lifetime.Token);
                var page = await service.ReadHistoryAsync(target.Authority.SessionId, cursor,
                    SessionHistoryPage.MaximumRecords, lifetime.Token);
                if (!Original() || page.Generation != target.Authority.Generation)
                { throw new OperationCanceledException("The exact clarification source or session generation changed."); }
                exchange.ValidatePage(page, cursor);
                foreach (var record in page.Records.Where(exchange.Matches))
                {
                    var exact = await service.ReadHistoryEventAsync(target.Authority.SessionId, record.Id, lifetime.Token);
                    if (!Original() || exact is null || !JsonSerializer.SerializeToUtf8Bytes(exact)
                        .AsSpan().SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(record)))
                    { throw new InvalidOperationException("The retained exchange source changed; explicitly read again."); }
                }
                await conversation.ReadDetailAsync(target, message, Original, lifetime.Token);
                if (!Original()) { throw new OperationCanceledException("The original clarification presentation was revoked."); }
                exchange.Present(page, cursor);
            }
            catch
            {
                exchange.Withdraw();
                throw;
            }
            finally { NotifyClarificationExchange(); }
        });

    internal void ClearClarificationExchange()
    {
        clarificationExchangeEpoch++;
        clarificationExchange?.Withdraw();
        clarificationExchange = null;
        OnPropertyChanged(nameof(CurrentClarificationExchange));
    }

    private void NotifyClarificationExchange()
    {
        if (clarificationExchange?.IsCurrent == false) { ClearClarificationExchange(); }
        OnPropertyChanged(nameof(CurrentClarificationExchange));
    }
}
