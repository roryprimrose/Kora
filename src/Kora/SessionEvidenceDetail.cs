using Kora.Core.Conversation;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora;

internal sealed record SessionEvidenceDetail(EvidenceSource Source, EvidencePage Page, ConversationMessage? RequestMessage = null)
{
    internal Guid Id { get; } = Guid.NewGuid();
    internal Func<bool>? RequestAdmission { get; init; }
    public string Heading => RequestMessage is { } message ? $"Message {message.Sequence}: recorded request audit"
        : Source == EvidenceSource.AuthorityAudit ? "Session typed security audit" : "Session diagnostic evidence";
    public string Label => (RequestMessage is { } message ? $"Message {message.Sequence} request audit: "
        : $"{(Source == EvidenceSource.AuthorityAudit ? "Typed security audit" : "Diagnostic log")}: ")
        + $"{Page.Status}, {Page.Records.Count} records. HOST observation, not an assistant answer or authorization.";

    internal EvidenceQuery Query(HostId<SessionIdentity> session) => new()
    {
        Source = Source,
        SessionId = session,
        RequestId = RequestMessage?.Request.RequestId,
        TaskId = RequestMessage?.Request.TaskId,
    };
}
