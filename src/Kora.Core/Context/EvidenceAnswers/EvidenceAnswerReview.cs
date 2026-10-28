using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.Core.Context.EvidenceAnswers;

/// <summary>One volatile, complete diagnostic page and original question, never source or session authority.</summary>
public sealed class EvidenceAnswerReview
{
    private static readonly JsonSerializerOptions Format = new()
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter() },
        MaxDepth = 32,
    };
    private int consumed;

    public EvidenceAnswerReview(string serializedPage, string question, HostRequest original,
        DateTimeOffset now)
    {
        ConversationText.Validate(question, submission: true);
        if (!LocalFileRetrievalPolicy.TryQuery(question, out _)
            || original.Origin != RequestOrigin.LocalUi || original.InvocationId is not null)
        { throw new InvalidDataException("A bounded original local user question is required."); }
        if (new UTF8Encoding(false, true).GetByteCount(serializedPage) > EvidencePage.MaximumBytes)
        { throw new InvalidDataException("The selected complete page exceeds its budget."); }
        var page = JsonSerializer.Deserialize<EvidencePage>(serializedPage, Format)
            ?? throw new InvalidDataException("The exact diagnostic page is missing.");
        if (page.Status != EvidencePageStatus.Available || page.UnavailableSources.Count != 0
            || page.DailyReport is not null || page.Records.Count is < 1 or > EvidencePage.MaximumRecords)
        { throw new InvalidDataException("Only a complete nonempty ordinary SQLite diagnostic page is admitted."); }
        var records = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);
        var expires = now.AddMinutes(2);
        foreach (var record in page.Records)
        {
            record.Reference.Id.Validate();
            if (record.Reference.Source != EvidenceSource.Log || record.ContentOmitted
                || record.Retention != EvidenceSegmentStatus.Present || record.DueUtc is not { } due || due <= now
                || record.Audit is not null || record.AuthorityProvenance is not null
                || !records.TryAdd(record.Reference.Citation, JsonSerializer.Serialize(record, Format)))
            { throw new InvalidDataException("Missing, expired, partial, duplicate or non-diagnostic records cannot be used."); }
            if (due < expires) { expires = due; }
        }
        Page = serializedPage;
        Question = question;
        Original = original;
        Destination = HostRequest.Create(RequestOrigin.LocalUi);
        ExpiresAt = expires;
        Records = records.ToImmutable();
    }

    public string Page { get; }
    public string Question { get; }
    public HostRequest Original { get; }
    public HostRequest Destination { get; }
    public DateTimeOffset ExpiresAt { get; }
    public ImmutableDictionary<string, string> Records { get; }
    public bool TryConsume() => Interlocked.Exchange(ref consumed, 1) == 0;

    public ModelContextEnvelope Bind(HostRequest request, HostRevision generation, DateTimeOffset now)
    {
        if (request.SessionId != Destination.SessionId || request.Origin != Original.Origin
            || request.InvocationId is not null || generation.Value <= 0 || now >= ExpiresAt)
        { throw new InvalidDataException("The exact fresh reviewed destination or page lifetime changed."); }
        var expires = now.AddSeconds(30);
        if (expires > ExpiresAt) { expires = ExpiresAt; }
        var context = new ModelContextEnvelope(request, EvidenceAnswerResponse.Policy, Question,
            [new(new(Guid.NewGuid()), request, generation, Page, ModelEvidenceDisclosure.LocalOnly)],
            now, expires, ModelToolMode.None);
        context.RequireCompleteInputLimit();
        return context;
    }
}
