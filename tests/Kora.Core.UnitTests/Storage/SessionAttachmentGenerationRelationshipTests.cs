using System.Text.Json;
using System.Text.Json.Nodes;

using AwesomeAssertions;

using Kora.Core.Storage;

namespace Kora.Core.UnitTests.Storage;

public sealed class SessionAttachmentGenerationRelationshipTests
{
    [Theory]
    [InlineData(AttachmentGenerationRelationship.Any)]
    [InlineData(AttachmentGenerationRelationship.SameObservedGeneration)]
    [InlineData(AttachmentGenerationRelationship.EarlierObservedGeneration)]
    public void RelationshipsComposeWithAllPredicatesAndPreserveDefaultAdmission(AttachmentGenerationRelationship relationship)
    {
        var original = SessionAttachmentSummaryTests.Summary();
        foreach (var captured in new long[] { 1, 2, long.MaxValue })
        foreach (var observed in new long[] { captured, long.MaxValue })
        foreach (var state in Enum.GetValues<AttachmentSessionState>())
        foreach (var copy in Enum.GetValues<AttachmentCopyStatus>())
        {
            if (state == AttachmentSessionState.Removed && copy != AttachmentCopyStatus.Unavailable) { continue; }
            var row = original with { CapturedGeneration = new(captured), SessionGeneration = new(observed),
                SessionState = state, CopyStatus = copy, StoredBytes = copy == AttachmentCopyStatus.Unavailable ? 0 : original.StoredBytes };
            foreach (var includeRemoved in new[] { false, true })
            foreach (var exact in new[] { false, true })
            foreach (var lifecycle in Enum.GetValues<SessionAttachmentFilter>())
            foreach (var variant in Enumerable.Range(0, 15))
            {
                SessionAttachmentQuery Query(AttachmentGenerationRelationship choice) => new(includeRemoved,
                    exact ? row.Session.Value.ToString("D") : null, lifecycle,
                    metadataText: variant == 1 ? "nonmatch" : "text/",
                    exactMediaType: variant == 2 ? "text/markdown" : row.MediaType,
                    copyStatus: variant == 3 ? AttachmentCopyStatus.Held : null,
                    minimumStoredBytes: variant == 4 ? original.StoredBytes + 1 : null,
                    maximumStoredBytes: variant == 5 ? 0 : null,
                    exactDigest: variant == 6 ? new string('b', 64) : row.File.Digest,
                    exactObservationSource: (variant == 7 ? Guid.Empty.ToString("D").Replace('0', '1') : row.ObservationSource.ToString("D")),
                    exactDeviceProfile: (variant == 8 ? Guid.Empty.ToString("D").Replace('0', '1') : row.Profile.Value.ToString("D")),
                    capturedFromUtc: variant == 10 ? null : variant == 9 ? row.CapturedAt.AddTicks(1) : row.CapturedAt,
                    capturedUntilUtc: variant == 10 ? row.CapturedAt.AddTicks(-1) : null,
                    minimumSessionGeneration: variant == 11 ? long.MaxValue : null,
                    maximumCapturedGeneration: variant == 12 ? 1 : null,
                    minimumStorageRevision: variant == 13 ? long.MaxValue : null,
                    exactFileSourceId: (variant == 14 ? Guid.Empty.ToString("D").Replace('0', '1') : row.File.SourceId.ToString("D")),
                    exactFileRevisionId: row.File.RevisionId.ToString("D"), exactFileItemId: row.File.ItemId.ToString("D"),
                    generationRelationship: choice);
                var baseline = Query(AttachmentGenerationRelationship.Any);
                var constrained = Query(relationship);
                var expected = baseline.Matches(row) && (relationship == AttachmentGenerationRelationship.Any
                    || copy == AttachmentCopyStatus.Confirmed && (relationship == AttachmentGenerationRelationship.SameObservedGeneration
                        ? captured == observed : captured < observed));
                constrained.Matches(row).Should().Be(expected);
            }
            var any = new SessionAttachmentQuery(includeRemoved: true);
            any.Should().Be(new SessionAttachmentQuery(true, generationRelationship: AttachmentGenerationRelationship.Any));
            any.Matches(row).Should().BeTrue();
            var constrainedOnly = new SessionAttachmentQuery(true, generationRelationship: relationship);
            constrainedOnly.Matches(row).Should().Be(relationship == AttachmentGenerationRelationship.Any
                || copy == AttachmentCopyStatus.Confirmed && (relationship == AttachmentGenerationRelationship.SameObservedGeneration
                    ? captured == observed : captured < observed));
        }
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    [InlineData(int.MaxValue)]
    public void UndefinedConstructorJsonAndMutatedPredicatesFailClosed(int invalid)
    {
        var relationship = (AttachmentGenerationRelationship)invalid;
        ((Action)(() => _ = new SessionAttachmentQuery(generationRelationship: relationship))).Should().Throw<InvalidDataException>();
        var query = new SessionAttachmentQuery();
        var json = JsonSerializer.SerializeToNode(query)!;
        json[nameof(query.GenerationRelationship)] = invalid;
        ((Action)(() => JsonSerializer.Deserialize<SessionAttachmentQuery>(json.ToJsonString()))).Should().Throw<InvalidDataException>();
        typeof(SessionAttachmentQuery).GetProperty(nameof(query.GenerationRelationship))!.SetValue(query, relationship);
        query.Invoking(value => value.Validate()).Should().Throw<InvalidDataException>();
        query.Invoking(value => value.Matches(SessionAttachmentSummaryTests.Summary())).Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void JsonRequiresDefinedRelationshipAndRoundTripsEveryChoiceWithoutHiddenVersions()
    {
        foreach (var relationship in Enum.GetValues<AttachmentGenerationRelationship>())
        {
            var query = new SessionAttachmentQuery(true, generationRelationship: relationship);
            var json = JsonSerializer.Serialize(query);
            JsonSerializer.Deserialize<SessionAttachmentQuery>(json).Should().Be(query);
            query.HasVersionPredicates.Should().Be(relationship != AttachmentGenerationRelationship.Any);
            var node = JsonNode.Parse(json)!.AsObject();
            node.Remove(nameof(query.GenerationRelationship));
            ((Action)(() => JsonSerializer.Deserialize<SessionAttachmentQuery>(node.ToJsonString()))).Should().Throw<JsonException>();
            node[nameof(query.GenerationRelationship)] = null;
            ((Action)(() => JsonSerializer.Deserialize<SessionAttachmentQuery>(node.ToJsonString()))).Should().Throw<JsonException>();
        }
        new SessionAttachmentQuery().Should().NotBe(new SessionAttachmentQuery(
            generationRelationship: AttachmentGenerationRelationship.SameObservedGeneration));
    }

    [Theory]
    [InlineData(AttachmentGenerationRelationship.Any)]
    [InlineData(AttachmentGenerationRelationship.SameObservedGeneration)]
    [InlineData(AttachmentGenerationRelationship.EarlierObservedGeneration)]
    public void ExclusionNeverHidesInvalidCandidatesAndPagesRejectWrongRelationship(AttachmentGenerationRelationship relationship)
    {
        var row = SessionAttachmentSummaryTests.Summary();
        var query = new SessionAttachmentQuery(true, exactSessionId: "11111111-1111-1111-1111-111111111111",
            generationRelationship: relationship);
        foreach (var invalid in new[] { row with { CapturedGeneration = new(long.MaxValue) },
            row with { CapturedGeneration = default }, row with { SessionGeneration = default },
            row with { File = row.File with { Digest = "invalid" } }, row with { CopyStatus = (AttachmentCopyStatus)99 } })
        { query.Invoking(value => value.Matches(invalid)).Should().Throw<InvalidDataException>(); }
        var earlier = new SessionAttachmentQuery(true, generationRelationship: AttachmentGenerationRelationship.EarlierObservedGeneration);
        var empty = new SessionAttachmentPage(earlier, [], null, DateTimeOffset.UnixEpoch, 1, new string('b', 64), 1, false);
        empty.Validate();
        (empty with { Records = [row with { CapturedGeneration = row.SessionGeneration }] })
            .Invoking(value => value.Validate()).Should().Throw<InvalidDataException>();
    }
}
