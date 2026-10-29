using System.Globalization;
using Kora.Core.Context;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Mcp.ResultFollowUp;

namespace Kora.Application.Hosting.McpResultFollowUp;

internal sealed class McpResultFollowUpReview : IDisposable
{
    private bool retired;
    private bool consumed;
    private string text;

    internal McpResultFollowUpReview(McpResultFollowUpWorkflow owner, McpResultObservation observation,
        HostTaskRecord task, string question, RequestOrigin origin, McpResultSourceUseDecision decision,
        DateTimeOffset createdAt, DateTimeOffset expiresAt)
    {
        Owner = owner;
        Observation = observation;
        SourceTask = task;
        Question = question;
        Origin = origin;
        Decision = decision;
        CreatedAt = createdAt;
        ExpiresAt = expiresAt;
        ConversationText.Validate(question, submission: true);
        var contextBytes = decision.Kind == McpResultSourceUseKind.QualifiedLocalOnly
            ? CreateEnvelope(new(new(Guid.NewGuid()), observation.Request.SessionId, new(Guid.NewGuid()), origin), createdAt)
                .Serialize().Length.ToString(CultureInfo.InvariantCulture)
            : "(not constructed: source use is denied or unavailable)";
        text = "ONE local-only tool-free answer. Separate fresh model-use consent is required; the spent MCP call grant grants nothing here.\n"
            + "Conversation: " + (observation.Target.Metadata?.Name.Value ?? "Unnamed conversation")
            + " (the exact currently selected Active conversation)\n"
            + "Source channel: " + observation.Request.Origin + " | New question channel: " + origin + "\n"
            + "Observed UTC: " + observation.Result.ObservedAt.ToString("O", CultureInfo.InvariantCulture) + "\n"
            + "Server: " + observation.Provenance.Server + "\nEndpoint: " + observation.Provenance.Endpoint
            + "\nAccount: " + observation.Provenance.Account + "\nRead-only mapped tool: " + observation.Provenance.Tool
            + "\nComplete schema:\n" + observation.Mapping.InputSchema
            + "\nCOMPLETE UNCHANGED TEXT\n" + observation.Result.Text
            + "\nCOMPLETE UNCHANGED STRUCTURED JSON\n" + (observation.Result.StructuredJson ?? "(none)")
            + "\nUNCHANGED NEW QUESTION\n" + question
            + "\nComplete serialized context: " + contextBytes + " / 32768 UTF-8 bytes."
            + "\n" + LocalSourceRiskPolicy.Disclosure
            + "\nRetain only this new question and genuine final answer or HOST terminal in this conversation. "
            + "No extra raw source copy, hosted transfer, tool, server refresh, memory write or export. "
            + "Cancellation cannot retract bytes already processed or roll back retained data.";
    }

    internal McpResultFollowUpWorkflow Owner { get; }
    internal McpResultObservation Observation { get; }
    internal HostTaskRecord SourceTask { get; }
    internal string Question { get; }
    internal RequestOrigin Origin { get; }
    internal McpResultSourceUseDecision Decision { get; }
    internal DateTimeOffset CreatedAt { get; }
    internal DateTimeOffset ExpiresAt { get; }
    internal bool Current => !retired;
    internal bool CanConfirm => !consumed && Current && Decision.Kind == McpResultSourceUseKind.QualifiedLocalOnly;
    internal string Text => Current ? text : string.Empty;

    internal void Consume(McpResultFollowUpWorkflow owner)
    {
        if (!ReferenceEquals(owner, Owner) || !CanConfirm)
        { throw new InvalidOperationException("The complete unchanged review requires separate, single-use model consent."); }
        consumed = true;
    }

    internal ModelContextEnvelope CreateEnvelope(HostRequest request, DateTimeOffset now)
    {
        if (!Current || request.SessionId != Observation.Request.SessionId || request.Origin != Origin)
        { throw new InvalidOperationException("The original observation review or exact question target was retired."); }
        return McpResultContext.Create(request, Question, Observation.Result, Observation.Provenance,
            now, ExpiresAt < now.AddSeconds(15) ? ExpiresAt : now.AddSeconds(15));
    }

    public void Dispose()
    {
        retired = true;
        text = string.Empty;
    }
}
