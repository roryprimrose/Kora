using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

using Kora.Core.Presentation;

namespace Kora.Core.Conversation;

/// <summary>Inert native metadata projection. It never resolves a source or infers lineage from prose.</summary>
public static class AnswerContextInspection
{
    public static DetailContentReference Reference(ConversationMessage message,
        ImmutableArray<SubmittedHistoryAvailability> availability = default) =>
        new(new(new Guid(SHA256.HashData(Encoding.UTF8.GetBytes(
            "kora.answer-context.v1:" + message.Request.RequestId.Value.ToString("D") + ":"
                + (availability.IsDefault ? "unknown" : string.Join(",", availability.Select(state => (int)state)))))
            .AsSpan(0, 16))), message.Revision);

    public static string Describe(ConversationMessage message, ImmutableArray<SubmittedHistoryAvailability> availability = default)
    {
        message.Validate();
        if (message.AssistantResponse is null)
        { throw new InvalidOperationException("Select one exact retained successful ASSISTANT answer."); }
        var text = new StringBuilder("Context submitted for this answer\n\n");
        if (message.SubmittedContext is not { } context)
        {
            return text.Append("Unknown / not recorded. This legacy or unsupported answer has no host-recorded canonical context handoff metadata. "
                + "Reviewed or selected inputs and model-claimed sources cannot establish what was submitted.\n\n"
                + "Source bodies are unavailable here. No source was read, recalled or reconstructed. No model was called.").ToString();
        }
        if (!availability.IsDefault && (availability.Length != context.Sources.Length || availability.Any(state => !Enum.IsDefined(state))))
        { throw new InvalidDataException("Current source metadata must describe the entire recorded ordered collection."); }
        text.Append("Evidence: the host serialized the canonical context and handed those bytes to its admitted local adapter. "
            + "This is not transport confirmation and does not prove provider delivery, processing, use or reasoning.\n\n")
            .Append("Original destination: local Ollama adapter; LocalOnly; tool-free.\n")
            .Append("Original local model option: ").Append(context.Profile.Model.Value)
            .Append("; reasoning option: ").Append(context.Profile.Reasoning.Value)
            .Append("; context option: ").Append(context.Profile.Context.Value)
            .Append(". These are the original host-admitted option IDs, not current catalogue labels or permission. Account/revision bindings remain internal.\n")
            .Append("Host handoff UTC: ").Append(context.SubmittedAt.ToString("O", CultureInfo.InvariantCulture)).Append('\n')
            .Append("Complete canonical envelope: ").Append(context.CanonicalUtf8Bytes.ToString(CultureInfo.InvariantCulture))
            .Append(" UTF-8 bytes. Includes host policy and the new USER request; neither body is copied here.\n\n")
            .Append("Ordered serialized evidence sources (whole same-session messages):\n");
        for (var index = 0; index < context.Sources.Length; index++)
        {
            var source = context.Sources[index];
            text.Append((index + 1).ToString(CultureInfo.InvariantCulture)).Append(". Earlier ")
                .Append(source.Role == ConversationHistoryRole.User ? "USER message" : "ASSISTANT answer")
                .Append(" - historical reference only. Current availability: ")
                .Append((availability.IsDefault ? SubmittedHistoryAvailability.Unknown : availability[index]) switch
                {
                    SubmittedHistoryAvailability.RetainedRevision => "exact source-row revision is retained; source body not read, validated or admitted here.",
                    SubmittedHistoryAvailability.Unavailable => "unavailable; exact source-row metadata is missing or changed. No newer source substituted.",
                    _ => "unknown / unavailable; current source metadata was not recorded or observed.",
                }).Append('\n');
        }
        return text.Append("\nOmissions: source bodies, values, excerpts, private labels/paths, credentials and transport tokens; "
            + "provider delivery/processing evidence; current source-body admission; any merely reviewed but unsubmitted inputs. "
            + "This subset records only explicit same-session history, not memory, instructions, attachments, tools or hosted context. "
            + "Those other source types are unsupported, not inferred absent from model prose.\n\n"
            + "Historical references confer no live use grant, retention extension, context transfer, source opening or provider fallback. "
            + "Use a separate existing fresh source-admission operation to inspect a source body.").ToString();
    }
}
