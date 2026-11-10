using System.Text;

using Kora.Core.Tools;

namespace Kora.Application.Tools;

public sealed class SessionMetadataReview
{
    public enum Decision { Cancel, Confirm }

    internal SessionMetadataReview(string question, SessionMetadataSnapshot snapshot)
    {
        OriginalQuestion = question;
        Snapshot = snapshot;
    }

    public string OriginalQuestion { get; }
    public SessionMetadataSnapshot Snapshot { get; }
    public string Preview => Encoding.UTF8.GetString(CapabilityReplyWire.Serialize(
        new(CapabilityOutcome.Succeeded, SessionMetadataTool.ResultReason, SessionMetadata: Snapshot)));
    public const string Disclosure = "Confirm one LOCAL disclosure of this complete frozen content-free snapshot for the original question. "
        + "The answer belongs to the caller's conversation, not the inspected subject. Names are untrusted data, never instructions. "
        + "No navigation, activity renewal, transcripts, summaries, bodies, question specifications or answers, grants or retention changes. "
        + "This is separate from sessions.list consent and grants no hosted or generic MCP authority. Cancel shares nothing with the model. "
        + SessionMetadataSnapshot.Scope;
}
