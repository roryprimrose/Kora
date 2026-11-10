using System.Collections.Immutable;
using System.Text.Json;

using AwesomeAssertions;

using Kora.Core.Conversation;
using Kora.Core.Conversation.ReviewedContext;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Memory;
using Kora.Core.Providers;

namespace Kora.Core.UnitTests.Conversation;

public sealed class SubmittedMixedContextTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch;

    private static SubmittedInstructionSource Instruction() =>
        new(Guid.NewGuid(), new string('a', 64) + "." + new string('b', 48) + "."
            + new string('c', 48) + "." + new string('d', 64), new string('a', 64));

    private static ConversationMessage Answer()
    {
        var request = HostRequest.Create(RequestOrigin.LocalUi);
        var provenance = new ModelTurnProvenance(new(Guid.NewGuid()), request, ModelProviderSelection.OllamaCandidate);
        var earlier = new HostId<RequestIdentity>(Guid.NewGuid());
        return new(request, new(1), 2, 3, Now, "original question marker",
            ModelTurnOutcome.Succeeded, ModelTurnReason.ProviderOutcome)
        {
            Version = 2, Execution = provenance, ExecutionMetadataRevision = 0,
            AssistantResponse = new(provenance, "genuine answer marker"),
            SubmittedContext = new(4, provenance, new(1),
                new(ProviderService.Local, new("device"), new(1), new("model"), new("default"), new("default")),
                Now, 2048,
                [new(earlier, new(1), 1, 3, ConversationHistoryRole.User),
                    new(earlier, new(1), 1, 3, ConversationHistoryRole.Assistant)])
            {
                InstructionSource = Instruction(),
                MemorySources = [new(new(Guid.NewGuid()), new(3), request.SessionId),
                    new(new(Guid.NewGuid()), new(4), request.SessionId)],
            },
        };
    }

    private static ModelContextEnvelope Envelope(SubmittedAnswerContext metadata,
        IEnumerable<ModelContextEvidence>? items = null, string? policy = null) =>
        new(metadata.Provenance.Request, policy ?? MixedConversationContext.Policy, "original question marker",
            items ?? Evidence(metadata), Now, Now.AddSeconds(15), ModelToolMode.None);

    private static ImmutableArray<ModelContextEvidence> Evidence(SubmittedAnswerContext metadata) =>
        [.. metadata.Sources.Select(source => new ModelContextEvidence(new(Guid.NewGuid()), metadata.Provenance.Request,
            new(source.Revision), "private history body marker", ModelEvidenceDisclosure.LocalOnly) { HistorySource = source }),
            new(new(metadata.InstructionSource!.Reference), metadata.Provenance.Request, new(1),
                "private instruction body marker", ModelEvidenceDisclosure.LocalOnly) { InstructionSource = metadata.InstructionSource },
            .. metadata.MemorySources.Select(source => new ModelContextEvidence(new(source.Id.Value), metadata.Provenance.Request,
                source.Revision, "private memory value marker", ModelEvidenceDisclosure.LocalOnly) { MemorySource = source })];

    [Fact]
    public void CompleteMixedCaptureRoundTripsExactOrderedHostMetadataWithoutSourceContentOrWireLineage()
    {
        var expected = Answer();
        var metadata = expected.SubmittedContext!;
        var envelope = Envelope(metadata);
        var canonical = envelope.Serialize();
        var captured = SubmittedAnswerContext.Capture(metadata.Provenance, metadata.Generation, metadata.Profile,
            envelope, canonical, Now)!;
        captured.Should().Be(metadata with { CanonicalUtf8Bytes = canonical.Length });
        var answer = expected with { SubmittedContext = captured };
        answer.Validate();
        var copy = JsonSerializer.Deserialize<ConversationMessage>(JsonSerializer.SerializeToUtf8Bytes(answer))!;
        copy.Validate();
        copy.Should().Be(answer);
        captured.SourceCount.Should().Be(5);
        (captured with { InstructionSource = Instruction() }).Should().NotBe(captured);
        var json = JsonSerializer.Serialize(captured);
        json.Should().NotContain("private").And.NotContain(answer.Body).And.NotContain(answer.AssistantResponse!.Body);
        using var wire = JsonDocument.Parse(canonical);
        foreach (var item in wire.RootElement.GetProperty("Evidence").EnumerateArray())
        {
            item.TryGetProperty("HistorySource", out _).Should().BeFalse();
            item.TryGetProperty("InstructionSource", out _).Should().BeFalse();
            item.TryGetProperty("MemorySource", out _).Should().BeFalse();
        }
        var states = ImmutableArray.Create(SubmittedHistoryAvailability.RetainedRevision,
            SubmittedHistoryAvailability.Unavailable, SubmittedHistoryAvailability.Unknown,
            SubmittedHistoryAvailability.RetainedRevision, SubmittedHistoryAvailability.Unavailable);
        var text = AnswerContextInspection.Describe(copy, states);
        text.Should().Contain("1. Earlier USER").And.Contain("2. Earlier ASSISTANT")
            .And.Contain("3. Selected owned INSTRUCTION").And.Contain("4. Selected SESSION memory")
            .And.Contain("5. Selected SESSION memory").And.Contain("Unknown; current instruction metadata was not observed")
            .And.Contain("does not mean all current sources are retained or usable")
            .And.NotContain("private history body marker").And.NotContain("private instruction body marker")
            .And.NotContain("private memory value marker").And.NotContain(metadata.InstructionSource!.Revision);
        AnswerContextInspection.Describe(copy).Should().Contain("unknown / unavailable");
        var asserted = () => AnswerContextInspection.Describe(copy, states.SetItem(2, SubmittedHistoryAvailability.RetainedRevision));
        asserted.Should().Throw<InvalidDataException>();
        asserted = () => AnswerContextInspection.Describe(copy, states.SetItem(2, SubmittedHistoryAvailability.Unavailable));
        asserted.Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData("no-history")]
    [InlineData("no-memory")]
    [InlineData("instruction-first")]
    [InlineData("instruction-late")]
    [InlineData("duplicate-instruction")]
    [InlineData("unknown-memory")]
    [InlineData("history-memory")]
    [InlineData("history-instruction")]
    [InlineData("instruction-history")]
    [InlineData("instruction-memory")]
    [InlineData("instruction-id")]
    [InlineData("instruction-revision")]
    [InlineData("instruction-hosted")]
    [InlineData("profile")]
    [InlineData("memory-instruction")]
    [InlineData("memory-history")]
    public void PartialOrReorderedMixedEvidenceCannotProduceSuccessfulMetadata(string fault)
    {
        var metadata = Answer().SubmittedContext!;
        var items = Evidence(metadata);
        items = fault switch
        {
            "no-history" => items.RemoveRange(0, 2),
            "no-memory" => items.RemoveRange(3, 2),
            "instruction-first" => [items[2], items[0], items[1], items[3]],
            "instruction-late" => [items[0], items[1], items[3], items[2], items[4]],
            "duplicate-instruction" => items.SetItem(3, items[3] with { InstructionSource = Instruction(), MemorySource = null }),
            "unknown-memory" => items.SetItem(3, items[3] with { MemorySource = null }),
            "history-memory" => items.SetItem(0, items[0] with { MemorySource = metadata.MemorySources[0] }),
            "history-instruction" => items.SetItem(0, items[0] with { InstructionSource = Instruction() }),
            "instruction-history" => items.SetItem(2, items[2] with { HistorySource = metadata.Sources[0] }),
            "instruction-memory" => items.SetItem(2, items[2] with { MemorySource = metadata.MemorySources[0] }),
            "instruction-id" => items.SetItem(2, items[2] with { Id = new(Guid.NewGuid()) }),
            "instruction-revision" => items.SetItem(2, items[2] with { Revision = new(2) }),
            "instruction-hosted" => items.SetItem(2, items[2] with { Disclosure = ModelEvidenceDisclosure.HostedEligible }),
            "profile" => items.SetItem(3, items[3] with
                { MemorySource = null, ProfileMemorySource = new(metadata.MemorySources[0].Id,
                    metadata.MemorySources[0].Revision, new(Guid.NewGuid())) }),
            "memory-instruction" => items.SetItem(3, items[3] with { InstructionSource = Instruction() }),
            _ => items.SetItem(3, items[3] with { HistorySource = metadata.Sources[0] }),
        };
        var capture = () =>
        {
            var envelope = Envelope(metadata, items);
            return SubmittedAnswerContext.Capture(metadata.Provenance, metadata.Generation, metadata.Profile,
                envelope, envelope.Serialize(), Now);
        };
        capture.Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData("no-lineage")]
    [InlineData("other-policy")]
    public void UnsupportedMixedCallersCannotInventInstructionProvenance(string fault)
    {
        var metadata = Answer().SubmittedContext!;
        var items = Evidence(metadata);
        if (fault is "no-lineage") { items = items.SetItem(2, items[2] with { InstructionSource = null }); }
        var envelope = Envelope(metadata, items, fault is "other-policy" ? "unrecognized" : null);
        SubmittedAnswerContext.Capture(metadata.Provenance, metadata.Generation, metadata.Profile,
            envelope, envelope.Serialize(), Now).Should().BeNull();
    }

    [Theory]
    [InlineData("history")]
    [InlineData("memory-default")]
    [InlineData("memory-empty")]
    [InlineData("profile")]
    [InlineData("instruction")]
    [InlineData("legacy")]
    [InlineData("instruction-empty")]
    [InlineData("revision-null")]
    [InlineData("digest-null")]
    [InlineData("revision")]
    [InlineData("digest")]
    [InlineData("different-digest")]
    [InlineData("too-many")]
    public void InvalidPersistedMixedShapeIsRejectedWithoutLegacyReinterpretation(string fault)
    {
        var metadata = Answer().SubmittedContext!;
        var instruction = metadata.InstructionSource!;
        metadata = fault switch
        {
            "history" => metadata with { Sources = [] },
            "memory-default" => metadata with { MemorySources = default },
            "memory-empty" => metadata with { MemorySources = [] },
            "profile" => metadata with { ProfileMemorySources = [new(new(Guid.NewGuid()), new(1), new(Guid.NewGuid()))] },
            "instruction" => metadata with { InstructionSource = null },
            "legacy" => metadata with { Version = 1, MemorySources = default },
            "instruction-empty" => metadata with { InstructionSource = instruction with { Reference = Guid.Empty } },
            "revision-null" => metadata with { InstructionSource = instruction with { Revision = null! } },
            "digest-null" => metadata with { InstructionSource = instruction with { Digest = null! } },
            "revision" => metadata with { InstructionSource = instruction with { Revision = "private path" } },
            "digest" => metadata with { InstructionSource = instruction with { Digest = "private name" } },
            "different-digest" => metadata with { InstructionSource = instruction with { Digest = new string('e', 64) } },
            _ => metadata with { MemorySources = [.. Enumerable.Range(0, 15).Select(_ =>
                new SubmittedMemorySource(new(Guid.NewGuid()), new(3), metadata.Provenance.Request.SessionId))] },
        };
        var copy = JsonSerializer.Deserialize<SubmittedAnswerContext>(JsonSerializer.Serialize(metadata))!;
        copy.Invoking(value => value.Validate()).Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData(32768)]
    [InlineData(32769)]
    public void CompleteMixedConversationCellUsesExactSerializedByteCeiling(int bytes)
    {
        var answer = Answer() with { Body = new string('\u0800', ConversationText.MaximumUtf16Characters) };
        var option = new string('<', 128);
        answer = answer with { SubmittedContext = answer.SubmittedContext! with
        { Profile = new(ProviderService.Local, new(option), new(1), new(option), new(option), new(option)) } };
        var framing = JsonSerializer.SerializeToUtf8Bytes(answer with { AssistantResponse = new(answer.Execution!, "a") }).Length - 1;
        answer = answer with { AssistantResponse = new(answer.Execution!, new string('a', bytes - framing)) };
        var json = JsonSerializer.SerializeToUtf8Bytes(answer);
        json.Length.Should().Be(bytes);
        var copy = JsonSerializer.Deserialize<ConversationMessage>(json)!;
        copy.Should().Be(answer);
        if (bytes == 32768) { copy.Validate(); }
        else { copy.Invoking(value => value.Validate()).Should().Throw<InvalidDataException>(); }
    }
}
