using Kora.Core.Hosting;

namespace Kora.Core.Interaction;

public sealed record HostQuestionKey
{
    public HostQuestionKey(HostRequest request, HostId<QuestionIdentity> questionId, HostRevision revision)
    {
        ArgumentNullException.ThrowIfNull(request);
        questionId.Validate();
        if (revision.Value <= 0)
        {
            throw new InvalidDataException("A question revision is required.");
        }
        Request = request;
        QuestionId = questionId;
        Revision = revision;
    }

    public HostRequest Request { get; }
    public HostId<QuestionIdentity> QuestionId { get; }
    public HostRevision Revision { get; }
    public HostQuestionKey Next() => new(Request, QuestionId, new(checked(Revision.Value + 1)));
}
