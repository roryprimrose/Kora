using System.Globalization;
using System.Text;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Storage;

namespace Kora.Core.Context;

/// <summary>Renders only claims resolved by the existing exact file citation binder.</summary>
public static class SessionAttachmentAnswerResponse
{
    public static ModelTurnResult Resolve(SessionFileAttachment source, LocalFileAnswerContext binding,
        LocalFileSearchResult search, ModelTurnResult result)
    {
        if (result.Outcome != ModelTurnOutcome.Succeeded) { return result; }
        var answer = binding.Resolve(result, search);
        if (answer.Outcome is not (LocalFileAnswerOutcome.Succeeded or LocalFileAnswerOutcome.Unsupported)
            || search.Truncated)
        { return new(ModelTurnOutcome.Unknown, ModelTurnReason.InvalidProviderResponse, result.Provenance); }
        var body = new StringBuilder(answer.Outcome == LocalFileAnswerOutcome.Unsupported
            ? "The selected evidence does not support an answer."
            : "Model-generated claims; verify the retained source references. Citation identity is checked, not semantic truth.");
        foreach (var claim in answer.Claims)
        {
            body.Append("\n\n").Append(claim.Text);
            foreach (var citation in claim.Citations)
            {
                _ = LocalFileLexicalRetrieval.ResolveCitation(source.File, citation, search.Mode);
                body.Append(CultureInfo.InvariantCulture,
                    $"\n[Retained session {source.Session.Value:D}; storage {source.StorageRevision.Value}; source {citation.Source.SourceId:D}; revision {citation.Source.RevisionId:D}; item {citation.Source.ItemId:D}; sha256 {citation.Source.Digest}; UTF-16 {citation.Start}+{citation.Length}; {citation.StartLine}:{citation.StartColumn}-{citation.EndLine}:{citation.EndColumn}]");
            }
        }
        var resolved = new ModelTurnResult(ModelTurnOutcome.Succeeded, result.Reason, result.Provenance,
            new(body.ToString(), null));
        try { _ = ConversationResponse.FromResult(resolved, result.Provenance!); }
        catch (Exception exception) when (exception is InvalidDataException or ConversationCredentialException)
        { return new(ModelTurnOutcome.Unknown, ModelTurnReason.InvalidProviderResponse, result.Provenance); }
        return resolved;
    }
}
