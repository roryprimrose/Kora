using System.Collections.Immutable;
using System.Text.Json;

using AwesomeAssertions;

using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Presentation;
using Kora.Core.Providers;

namespace Kora.Core.UnitTests.Conversation;

public sealed class SubmittedAnswerContextTests
{
    private static ConversationMessage Answer()
    {
        var request = HostRequest.Create(RequestOrigin.LocalUi);
        var provenance = new ModelTurnProvenance(new(Guid.NewGuid()), request, ModelProviderSelection.OllamaCandidate);
        return new(request, new(1), 2, 3, DateTimeOffset.UnixEpoch, "original user", ModelTurnOutcome.Succeeded, ModelTurnReason.ProviderOutcome)
        {
            Version = 2, Execution = provenance, ExecutionMetadataRevision = 0, AssistantResponse = new(provenance, "final answer"),
            SubmittedContext = new(1, provenance, new(1),
                new(ProviderService.Local, new("device"), new(1), new("model"), new("default"), new("default")),
                DateTimeOffset.UnixEpoch, 1024, [new(new(Guid.NewGuid()), new(1), 1, 2, ConversationHistoryRole.User)]),
        };
    }

    [Theory]
    [InlineData("recorded")]
    [InlineData("no-profile")]
    [InlineData("hosted")]
    [InlineData("tools")]
    [InlineData("policy")]
    [InlineData("no-evidence")]
    [InlineData("unknown-source")]
    [InlineData("wire")]
    [InlineData("request")]
    public void CaptureRecordsOnlyCompleteCanonicalHostHistoryHandoffAndNeverInfersUnknownInputs(string scenario)
    {
        var metadata = Answer().SubmittedContext!;
        var request = metadata.Provenance.Request;
        var item = new ModelContextEvidence(new(Guid.NewGuid()), request, new(2), "source body", ModelEvidenceDisclosure.LocalOnly)
        { HistorySource = scenario is "unknown-source" ? null : metadata.Sources[0] };
        var context = new ModelContextEnvelope(request, scenario is "policy" ? "other host policy" : ConversationHistoryContext.Policy,
            "new original", scenario is "no-evidence" ? [] : [item], DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddMinutes(1),
            scenario is "tools" ? ModelToolMode.ReadOnly : ModelToolMode.None);
        var canonical = context.Serialize();
        if (scenario is "wire") { canonical[0] ^= 1; }
        var provenance = scenario is "hosted" ? metadata.Provenance with { Selection = ModelProviderSelection.CopilotCandidate }
            : scenario is "request" ? metadata.Provenance with { Request = HostRequest.Create(RequestOrigin.LocalUi) } : metadata.Provenance;
        var capture = () => SubmittedAnswerContext.Capture(provenance, metadata.Generation,
            scenario is "no-profile" ? null : metadata.Profile, context, canonical, DateTimeOffset.UnixEpoch);
        if (scenario is "wire" or "request") { capture.Should().Throw<InvalidDataException>(); }
        else if (scenario is not "recorded") { capture().Should().BeNull(); }
        else
        {
            var actual = capture()!;
            actual.Sources.Should().Equal(metadata.Sources);
            actual.CanonicalUtf8Bytes.Should().Be(canonical.Length);
            actual.Profile.Should().Be(metadata.Profile);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SourceLineageMustMatchTheCanonicalEvidenceRevisionAndLocalDisclosure(bool hosted)
    {
        var metadata = Answer().SubmittedContext!;
        var request = metadata.Provenance.Request;
        var evidence = new ModelContextEvidence(new(Guid.NewGuid()), request, new(hosted ? 2 : 3), "source body",
            hosted ? ModelEvidenceDisclosure.HostedEligible : ModelEvidenceDisclosure.LocalOnly)
        { HistorySource = metadata.Sources[0] };
        var create = () => new ModelContextEnvelope(request, ConversationHistoryContext.Policy, "original", [evidence],
            DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddMinutes(1));
        create.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void MetadataRoundTripsWithoutBodiesAndMaintainsValueEqualityAndExactAnswerBinding()
    {
        var answer = Answer();
        answer.Validate();
        var bytes = JsonSerializer.SerializeToUtf8Bytes(answer);
        var copy = JsonSerializer.Deserialize<ConversationMessage>(bytes)!;
        copy.Validate();
        copy.Should().Be(answer);
        copy.SubmittedContext!.GetHashCode().Should().Be(answer.SubmittedContext!.GetHashCode());
        var metadata = JsonSerializer.Serialize(answer.SubmittedContext);
        metadata.Should().NotContain(answer.Body).And.NotContain(answer.AssistantResponse!.Body);
        answer.SubmittedContext.Equals(null).Should().BeFalse();
        (answer with { SubmittedContext = null }).Validate();
        JsonSerializer.Serialize(answer with { SubmittedContext = null }).Should().NotContain("SubmittedContext");
        (answer.SubmittedContext! with { CanonicalUtf8Bytes = 1025 }).Should().NotBe(answer.SubmittedContext);
    }

    [Theory]
    [InlineData("version")]
    [InlineData("provenance")]
    [InlineData("request")]
    [InlineData("selection")]
    [InlineData("invalid-selection")]
    [InlineData("hosted")]
    [InlineData("generation")]
    [InlineData("time")]
    [InlineData("zero-bytes")]
    [InlineData("overflow")]
    [InlineData("default-sources")]
    [InlineData("empty-sources")]
    [InlineData("too-many")]
    [InlineData("null-source")]
    [InlineData("duplicate")]
    [InlineData("own-request")]
    [InlineData("unordered")]
    [InlineData("sequence-collision")]
    [InlineData("profile")]
    [InlineData("profile-service")]
    [InlineData("turn")]
    public void MalformedOrUnboundMetadataIsRejectedWithoutRepair(string fault)
    {
        var value = Answer().SubmittedContext!;
        var source = value.Sources[0];
        value = fault switch
        {
            "version" => value with { Version = 2 },
            "provenance" => value with { Provenance = null! },
            "request" => value with { Provenance = value.Provenance with { Request = null! } },
            "selection" => value with { Provenance = value.Provenance with { Selection = null! } },
            "invalid-selection" => value with { Provenance = value.Provenance with { Selection = value.Provenance.Selection with { Revision = default } } },
            "hosted" => value with { Provenance = value.Provenance with { Selection = ModelProviderSelection.CopilotCandidate } },
            "generation" => value with { Generation = default },
            "time" => value with { SubmittedAt = DateTimeOffset.UnixEpoch.ToOffset(TimeSpan.FromHours(1)) },
            "zero-bytes" => value with { CanonicalUtf8Bytes = 0 },
            "overflow" => value with { CanonicalUtf8Bytes = 32769 },
            "default-sources" => value with { Sources = default },
            "empty-sources" => value with { Sources = [] },
            "too-many" => value with { Sources = [.. Enumerable.Repeat(source, 17)] },
            "null-source" => value with { Sources = [null!] },
            "duplicate" => value with { Sources = [source, source] },
            "own-request" => value with { Sources = [source with { Request = value.Provenance.Request.RequestId }] },
            "unordered" => value with { Sources = [source with { Sequence = 2 }, source with { Role = ConversationHistoryRole.Assistant }] },
            "sequence-collision" => value with { Sources = [source, source with { Request = new(Guid.NewGuid()) }] },
            "profile" => value with { Profile = null! },
            "profile-service" => value with { Profile = value.Profile with { Service = ProviderService.Claude } },
            _ => value with { Provenance = value.Provenance with { Turn = default } },
        };
        value.Invoking(item => item.Validate()).Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData("id")]
    [InlineData("generation")]
    [InlineData("zero-sequence")]
    [InlineData("large-sequence")]
    [InlineData("revision")]
    [InlineData("large-revision")]
    [InlineData("role")]
    public void MalformedSourceMetadataIsRejected(string fault)
    {
        var source = Answer().SubmittedContext!.Sources[0];
        source = fault switch
        {
            "id" => source with { Request = default },
            "generation" => source with { Generation = default },
            "zero-sequence" => source with { Sequence = 0 },
            "large-sequence" => source with { Sequence = ConversationText.MaximumSessionMessages + 1 },
            "revision" => source with { Revision = 1 },
            "large-revision" => source with { Revision = 4 },
            _ => source with { Role = (ConversationHistoryRole)99 },
        };
        source.Invoking(item => item.Validate()).Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData("failed")]
    [InlineData("foreign-turn")]
    [InlineData("foreign-generation")]
    [InlineData("earlier-time")]
    [InlineData("future-source")]
    [InlineData("future-generation")]
    [InlineData("legacy")]
    public void MetadataCannotAttachToAnotherAnswerOrTerminalState(string fault)
    {
        var answer = Answer();
        var context = answer.SubmittedContext!;
        answer = fault switch
        {
            "failed" => answer with { AssistantResponse = null, Outcome = ModelTurnOutcome.Unknown },
            "foreign-turn" => answer with { SubmittedContext = context with { Provenance = context.Provenance with { Turn = new(Guid.NewGuid()) } } },
            "foreign-generation" => answer with { SubmittedContext = context with { Generation = new(2) } },
            "earlier-time" => answer with { SubmittedContext = context with { SubmittedAt = context.SubmittedAt.AddTicks(-1) } },
            "future-source" => answer with { SubmittedContext = context with { Sources = [context.Sources[0] with { Sequence = 2 }] } },
            "legacy" => answer with { Version = 1, Execution = null, ExecutionMetadataRevision = null, AssistantResponse = null,
                Revision = 2, Outcome = ModelTurnOutcome.Unavailable },
            _ => answer with { SubmittedContext = context with { Sources = [context.Sources[0] with { Generation = new(2) }] } },
        };
        answer.Invoking(item => item.Validate()).Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void NativeProjectionIsCompleteInertAndExplicitAboutEvidenceClassAvailabilityAndOmissions()
    {
        var answer = Answer();
        var text = AnswerContextInspection.Describe(answer, [SubmittedHistoryAvailability.RetainedRevision]);
        text.Should().Contain("admitted local adapter").And.Contain("not transport confirmation")
            .And.Contain("LocalOnly").And.Contain("1024").And.Contain("1. Earlier USER message")
            .And.Contain("exact source-row revision is retained").And.Contain("Omissions")
            .And.NotContain("original user").And.NotContain("final answer");
        AnswerContextInspection.Describe(answer, [SubmittedHistoryAvailability.Unavailable]).Should().Contain("missing or changed");
        AnswerContextInspection.Describe(answer).Should().Contain("unknown / unavailable");
        AnswerContextInspection.Describe(answer with { SubmittedContext = null }).Should().Contain("Unknown / not recorded");
        var assistant = answer with { SubmittedContext = answer.SubmittedContext! with
        { Sources = [answer.SubmittedContext.Sources[0] with { Role = ConversationHistoryRole.Assistant, Revision = 3 }] } };
        AnswerContextInspection.Describe(assistant).Should().Contain("Earlier ASSISTANT answer");
        var reference = AnswerContextInspection.Reference(answer, [SubmittedHistoryAvailability.RetainedRevision]);
        reference.Should().Be(AnswerContextInspection.Reference(answer, [SubmittedHistoryAvailability.RetainedRevision]));
        reference.Should().NotBe(AnswerContextInspection.Reference(answer, [SubmittedHistoryAvailability.Unavailable]));
        reference.ItemId.Value.Should().NotBe(answer.Request.RequestId.Value).And.NotBe(answer.Execution!.Turn.Value);
        reference.Revision.Should().Be(answer.Revision);
        var detail = new AdmittedDetailContent(reference, DetailContentKind.PlainText, DetailContentOrigin.RetainedAnswerContext,
            DetailSensitivity.DisclosureConfirmationRequired, "Context", "Historical host handoff", text,
            conversationMessage: answer, contextAvailability: [SubmittedHistoryAvailability.RetainedRevision]);
        detail.IsSameSnapshot(new(reference, detail.Kind, detail.Origin, detail.Sensitivity, detail.Title, detail.Provenance,
            text, conversationMessage: answer, contextAvailability: [SubmittedHistoryAvailability.RetainedRevision])).Should().BeTrue();
        detail.IsSameSnapshot(new(AnswerContextInspection.Reference(answer, [SubmittedHistoryAvailability.Unavailable]),
            detail.Kind, detail.Origin, detail.Sensitivity, detail.Title, detail.Provenance,
            AnswerContextInspection.Describe(answer, [SubmittedHistoryAvailability.Unavailable]), conversationMessage: answer,
            contextAvailability: [SubmittedHistoryAvailability.Unavailable])).Should().BeFalse();
        var wrongText = () => new AdmittedDetailContent(reference, detail.Kind, detail.Origin, detail.Sensitivity, detail.Title,
            detail.Provenance, "model says it used memory", conversationMessage: answer);
        wrongText.Should().Throw<InvalidDataException>();
        var wrongCount = () => AnswerContextInspection.Describe(answer, []);
        wrongCount.Should().Throw<InvalidDataException>();
        var invalidState = () => AnswerContextInspection.Describe(answer, [(SubmittedHistoryAvailability)99]);
        invalidState.Should().Throw<InvalidDataException>();
        var extraMetadata = () => new AdmittedDetailContent(new(new(Guid.NewGuid()), 1), DetailContentKind.PlainText,
            DetailContentOrigin.EmbeddedDocument, DetailSensitivity.Public, "title", "provenance", "text",
            contextAvailability: []);
        extraMetadata.Should().Throw<InvalidDataException>();
        var missingAnswer = () => AnswerContextInspection.Describe(answer with
        { AssistantResponse = null, SubmittedContext = null, Outcome = ModelTurnOutcome.Unknown });
        missingAnswer.Should().Throw<InvalidOperationException>();
    }
}
