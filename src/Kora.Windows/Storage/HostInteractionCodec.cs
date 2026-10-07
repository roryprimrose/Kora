using System.Text.Json;
using System.Text.Json.Serialization;

using Kora.Core.Authorization;
using Kora.Core.Hosting;
using Kora.Core.Interaction;

namespace Kora.Windows.Storage;

internal static class HostInteractionCodec
{
    private const int MaximumBytes = 131072;
    private static readonly JsonSerializerOptions Options = new()
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectRequiredConstructorParameters = true,
        AllowDuplicateProperties = false,
        MaxDepth = 16,
    };

    internal static string Encode<T>(T value)
    {
        var json = JsonSerializer.Serialize(value, Options);
        if (System.Text.Encoding.UTF8.GetByteCount(json) > MaximumBytes)
        {
            throw new InvalidDataException("The interaction record exceeds its bounded storage envelope.");
        }
        return json;
    }

    internal static T Decode<T>(string json)
    {
        if (System.Text.Encoding.UTF8.GetByteCount(json) > MaximumBytes)
        {
            throw new InvalidDataException("The persisted interaction envelope is oversized.");
        }
        try
        {
            return JsonSerializer.Deserialize<T>(json, Options)
                ?? throw new InvalidDataException("A persisted interaction envelope is null.");
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or NullReferenceException)
        {
            throw new InvalidDataException("A persisted typed interaction record is invalid.", exception);
        }
    }

    internal sealed record AnswerData(string[] Choices, string? Text)
    {
        internal QuestionAnswer ToAnswer() => new(Choices, Text);
        internal static AnswerData? From(QuestionAnswer? answer) =>
            answer is null ? null : new(answer.Choices.ToArray(), answer.Text);
    }

    internal sealed record QuestionData(HostQuestionKey Key, string Text, QuestionKind Kind,
        QuestionOption[] Options, int Minimum, int Maximum, int MaximumTextLength, string Purpose,
        string SourceId, HostRevision SessionGeneration, DateTimeOffset ExpiresAt, QuestionStatus Status,
        AnswerData? Draft, RequestOrigin? AnswerChannel, HostOperationProposal? Proposal)
    {
        internal static QuestionData From(HostQuestionRecord question) =>
            new(question.Key, question.Spec.Text, question.Spec.Kind, question.Spec.Options.ToArray(),
                question.Spec.Minimum, question.Spec.Maximum, question.Spec.MaximumTextLength,
                question.Spec.Purpose, question.Spec.SourceId, question.SessionGeneration, question.ExpiresAt,
                question.Status, AnswerData.From(question.Draft), question.AnswerChannel, question.Proposal);

        internal HostQuestionRecord ToQuestion()
        {
            try
            {
                var spec = new QuestionSpec(Text, Kind, Options, Minimum, Maximum, MaximumTextLength, Purpose, SourceId);
                var question = new HostQuestionRecord(Key, spec, SessionGeneration, ExpiresAt, Status,
                    Draft?.ToAnswer(), AnswerChannel, Proposal);
                Validate(question);
                return question;
            }
            catch (Exception exception) when (exception is ArgumentException or NullReferenceException)
            {
                throw new InvalidDataException("The persisted typed question schema is malformed.", exception);
            }
        }
    }

    internal static void Validate(HostQuestionRecord question)
    {
        if (question.Key is null || question.Spec is null)
        {
            throw new InvalidDataException("The persisted typed question has no key or schema.");
        }
        question.Key.QuestionId.Validate();
        if (question.Key.Revision.Value <= 0 || question.SessionGeneration.Value <= 0
            || !Enum.IsDefined(question.Status)
            || (question.AnswerChannel is { } channel && channel is not (RequestOrigin.LocalUi or RequestOrigin.ActivatedVoice))
            || (question.Draft is { } answer && !question.Spec.Accepts(answer, question.Status == QuestionStatus.Answered))
            || (question.Status == QuestionStatus.Answered && (question.Draft is null || question.AnswerChannel is null))
            || (question.Proposal is { } proposal && proposal.Request != question.Key.Request))
        {
            throw new InvalidDataException("The question identity, answer or proposal is invalid.");
        }
    }
}
