using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Text;

using Kora.Core.Conversation;
using Kora.Core.Hosting;
using Kora.Core.Storage;
using Kora.Application.Hosting.ModelHistoryContext;

namespace Kora.Application.Hosting;

/// <summary>One source-bound, expiring review. Its text is never submission authority.</summary>
public sealed class ConversationHistoryReview : IDisposable
{
    private readonly Lock gate = new();
    private readonly SessionConversationService owner;
    private readonly Func<bool> current;
    private readonly TimeProvider time;
    private ImmutableArray<ConversationHistorySelection> selections;
    private string body;
    private string text;
    private bool consumed;
    private bool disposed;

    internal ConversationHistoryReview(SessionConversationService owner, SessionWorkspaceEntry target, string body,
        ImmutableArray<ConversationHistorySelection> selections, RequestOrigin origin, Func<bool> current,
        TimeProvider time, DateTimeOffset createdAt, DateTimeOffset expiresAt, ActivityLink cause,
        ModelHistoryContextSource? advisorySource = null)
    {
        this.owner = owner;
        Target = target;
        this.body = body;
        this.selections = selections;
        Origin = origin;
        this.current = current;
        this.time = time;
        CreatedAt = createdAt;
        ExpiresAt = expiresAt;
        Cause = cause;
        AdvisorySource = advisorySource;
        var envelope = ConversationHistoryContext.CreateEnvelope(
            new(new(Guid.NewGuid()), target.Authority.SessionId, new(Guid.NewGuid()), origin), body, selections, createdAt, expiresAt);
        var preview = new StringBuilder(ConversationHistoryContext.Policy).Append("\n\nComplete serialized input: ")
            .Append(envelope.Serialize().Length.ToString(CultureInfo.InvariantCulture))
            .Append(" / 32768 UTF-8 bytes; ").Append(envelope.Evidence.Length.ToString(CultureInfo.InvariantCulture))
            .Append(" / 16 evidence items.\n\n");
        foreach (var item in selections)
        {
            preview.Append(item.Role == ConversationHistoryRole.User ? "USER" : "ASSISTANT")
                .Append(" - earlier message ").Append(item.Message.Sequence.ToString(CultureInfo.InvariantCulture)).Append('\n')
                .Append(item.Role == ConversationHistoryRole.User ? item.Message.Body : item.Message.AssistantResponse!.Body)
                .Append("\n\n");
        }
        text = preview.Append(advisorySource is null ? "NEW USER REQUEST\n" : "UNCHANGED ORIGINAL USER REQUEST - LOCAL ONLY, NO TOOLS\n")
            .Append(body).ToString();
    }

    internal SessionWorkspaceEntry Target { get; }
    internal RequestOrigin Origin { get; }
    internal ActivityLink Cause { get; }
    internal ModelHistoryContextSource? AdvisorySource { get; }
    internal DateTimeOffset CreatedAt { get; }
    public DateTimeOffset ExpiresAt { get; }
    public bool IsCurrent
    {
        get
        {
            lock (gate)
            {
                if (disposed) { return false; }
                if (!current() || AdvisorySource is { IsCurrent: false }
                    || time.GetUtcNow() < CreatedAt || time.GetUtcNow() >= ExpiresAt)
                { Clear(); return false; }
                return true;
            }
        }
    }
    public string Text
    {
        get { lock (gate) { return IsCurrent ? text : string.Empty; } }
    }

    internal (string Body, ImmutableArray<ConversationHistorySelection> Selections) Consume(SessionConversationService service)
    {
        lock (gate)
        {
            if (!ReferenceEquals(owner, service) || consumed || !IsCurrent)
            { throw new InvalidOperationException("The exact original history review is missing, expired, revoked or already consumed."); }
            AdvisorySource?.Consume(service);
            consumed = true;
            return (body, selections);
        }
    }

    public void Dispose()
    {
        lock (gate) { Clear(); }
    }

    private void Clear()
    {
        disposed = true;
        selections = [];
        body = string.Empty;
        text = string.Empty;
    }
}
