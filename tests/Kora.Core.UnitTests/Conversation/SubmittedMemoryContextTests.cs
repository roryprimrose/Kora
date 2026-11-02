using System.Collections.Immutable;
using System.Text.Json;
using AwesomeAssertions;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Memory;
using Kora.Core.Providers;

namespace Kora.Core.UnitTests.Conversation;

public sealed class SubmittedMemoryContextTests
{
    private static ConversationMessage Answer()
    {
        var request = HostRequest.Create(RequestOrigin.LocalUi);
        var provenance = new ModelTurnProvenance(new(Guid.NewGuid()), request, ModelProviderSelection.OllamaCandidate);
        var sources = Enumerable.Range(0, 2).Select(_ =>
            new SubmittedMemorySource(new(Guid.NewGuid()), new(3), request.SessionId)).ToArray();
        var envelope = new ModelContextEnvelope(request, SessionMemoryContextPolicy.Policy, "private question",
            sources.Select(source => new ModelContextEvidence(new(source.Id.Value), request, source.Revision,
                "private memory", ModelEvidenceDisclosure.LocalOnly) { MemorySource = source }),
            DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddSeconds(15), ModelToolMode.None);
        return new(request, new(1), 1, 3, DateTimeOffset.UnixEpoch, "private question",
            ModelTurnOutcome.Succeeded, ModelTurnReason.ProviderOutcome)
        {
            Version = 2, Execution = provenance, ExecutionMetadataRevision = 0,
            AssistantResponse = new(provenance, "private answer"),
            SubmittedContext = SubmittedAnswerContext.Capture(provenance, new(1),
                new(ProviderService.Local, new("device"), new(1), new("model"), new("default"), new("default")),
                envelope, envelope.Serialize(), DateTimeOffset.UnixEpoch),
        };
    }

    [Fact]
    public void MemorySnapshotRoundTripsInExactOrderWithoutContentAndInspectionCannotAdmitBodies()
    {
        var answer = Answer();
        answer.Validate();
        var context = answer.SubmittedContext!;
        context.Version.Should().Be(2);
        context.Sources.Should().BeEmpty();
        context.SourceCount.Should().Be(2);
        JsonSerializer.Deserialize<ConversationMessage>(JsonSerializer.Serialize(answer)).Should().Be(answer);
        JsonSerializer.Serialize(context).Should().NotContain("private");
        (context with { MemorySources = context.MemorySources.Reverse().ToImmutableArray() }).Should().NotBe(context);
        var retained = AnswerContextInspection.Describe(answer,
            [SubmittedHistoryAvailability.RetainedRevision, SubmittedHistoryAvailability.Unavailable]);
        retained.Should().Contain("1. Selected SESSION memory").And.Contain("2. Selected SESSION memory")
            .And.Contain("exact enabled memory revision is retained").And.Contain("No newer memory substituted")
            .And.NotContain("private question").And.NotContain("private memory").And.NotContain("private answer");
        AnswerContextInspection.Describe(answer).Should().Contain("current memory metadata was not recorded");
        var wrongCount = () => AnswerContextInspection.Describe(answer, [SubmittedHistoryAvailability.RetainedRevision]);
        wrongCount.Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData("id")]
    [InlineData("session")]
    [InlineData("revision")]
    [InlineData("foreign")]
    [InlineData("duplicate")]
    [InlineData("null")]
    [InlineData("empty")]
    [InlineData("default")]
    [InlineData("oversized")]
    [InlineData("history-version")]
    [InlineData("history-with-memory")]
    [InlineData("mixed")]
    public void MalformedMemoryMetadataCannotBeRetained(string fault)
    {
        var context = Answer().SubmittedContext!;
        var source = context.MemorySources[0];
        context = fault switch
        {
            "id" => context with { MemorySources = [source with { Id = default }] },
            "session" => context with { MemorySources = [source with { Session = default }] },
            "revision" => context with { MemorySources = [source with { Revision = default }] },
            "foreign" => context with { MemorySources = [source with { Session = new(Guid.NewGuid()) }] },
            "duplicate" => context with { MemorySources = [source, source] },
            "null" => context with { MemorySources = [null!] },
            "empty" => context with { MemorySources = [] },
            "default" => context with { MemorySources = default },
            "oversized" => context with { MemorySources = [.. Enumerable.Repeat(source, 17)] },
            "history-version" => context with { Version = 1 },
            "history-with-memory" => context with
            { Version = 1, Sources = [new(new(Guid.NewGuid()), new(1), 1, 2, ConversationHistoryRole.User)] },
            _ => context with { Sources = [new(new(Guid.NewGuid()), new(1), 1, 2, ConversationHistoryRole.User)] },
        };
        context.Invoking(value => value.Validate()).Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData("id")]
    [InlineData("revision")]
    [InlineData("foreign")]
    [InlineData("hosted")]
    [InlineData("dual")]
    public void MemoryLineageMustMatchExactCanonicalEvidenceAndSameSession(string fault)
    {
        var context = Answer().SubmittedContext!;
        var request = context.Provenance.Request;
        var source = context.MemorySources[0];
        var evidence = new ModelContextEvidence(new(source.Id.Value), request, source.Revision, "private",
            fault is "hosted" ? ModelEvidenceDisclosure.HostedEligible : ModelEvidenceDisclosure.LocalOnly)
        {
            MemorySource = fault switch
            {
                "id" => source with { Id = new(Guid.NewGuid()) },
                "revision" => source with { Revision = new(4) },
                "foreign" => source with { Session = new(Guid.NewGuid()) },
                _ => source,
            },
            HistorySource = fault is "dual" ? new(new(Guid.NewGuid()), new(1), 1, 3, ConversationHistoryRole.User) : null,
        };
        var create = () => new ModelContextEnvelope(request, SessionMemoryContextPolicy.Policy, "question", [evidence],
            DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddSeconds(15), ModelToolMode.None);
        create.Should().Throw<InvalidDataException>();
    }
}
