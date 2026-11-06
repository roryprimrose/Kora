using System.Collections.Immutable;
using System.Text.Json;
using AwesomeAssertions;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Memory;
using Kora.Core.Providers;

namespace Kora.Core.UnitTests.Conversation;

public sealed class SubmittedProfileMemoryContextTests
{
    private static ConversationMessage Answer()
    {
        var request = HostRequest.Create(RequestOrigin.LocalUi);
        var provenance = new ModelTurnProvenance(new(Guid.NewGuid()), request, ModelProviderSelection.OllamaCandidate);
        var profile = new HostId<DeviceProfileIdentity>(Guid.NewGuid());
        var sources = Enumerable.Range(0, 2).Select(_ =>
            new SubmittedProfileMemorySource(new(Guid.NewGuid()), new(3), profile)).ToArray();
        var envelope = new ModelContextEnvelope(request, SessionMemoryContextPolicy.Policy, "private question",
            sources.Select(source => new ModelContextEvidence(new(source.Id.Value), request, source.Revision,
                "private memory", ModelEvidenceDisclosure.LocalOnly) { ProfileMemorySource = source }),
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
    public void ProfileSnapshotRoundTripsInExactOrderWithoutContentOrLineageAndInspectionGrantsNoUse()
    {
        var answer = Answer();
        answer.Validate();
        var context = answer.SubmittedContext!;
        context.Version.Should().Be(3);
        context.Sources.Should().BeEmpty();
        context.MemorySources.IsDefault.Should().BeTrue();
        context.SourceCount.Should().Be(2);
        JsonSerializer.Deserialize<ConversationMessage>(JsonSerializer.Serialize(answer)).Should().Be(answer);
        var json = JsonSerializer.Serialize(context);
        json.Should().NotContain("private").And.NotContain("Candidate").And.NotContain("ContentClass")
            .And.NotContain("Lineage").And.NotContain("Receipt").And.NotContain("\"MemorySources\":");
        (context with { ProfileMemorySources = context.ProfileMemorySources.Reverse().ToImmutableArray() }).Should().NotBe(context);
        var text = AnswerContextInspection.Describe(answer,
            [SubmittedHistoryAvailability.RetainedRevision, SubmittedHistoryAvailability.Unavailable]);
        text.Should().Contain("1. Selected DEVICE-PROFILE memory").And.Contain("2. Selected DEVICE-PROFILE memory")
            .And.Contain("current device profile").And.Contain("No newer memory substituted")
            .And.NotContain("private question").And.NotContain("private memory").And.NotContain("private answer");
        AnswerContextInspection.Describe(answer).Should().Contain("current profile memory metadata was not recorded");
    }

    [Theory]
    [InlineData("id")]
    [InlineData("profile")]
    [InlineData("revision")]
    [InlineData("duplicate")]
    [InlineData("foreign-profile")]
    [InlineData("null")]
    [InlineData("empty")]
    [InlineData("default")]
    [InlineData("oversized")]
    [InlineData("history-version")]
    [InlineData("session-version")]
    [InlineData("history")]
    [InlineData("session")]
    public void MalformedOrMixedProfileMetadataCannotBeRetained(string fault)
    {
        var context = Answer().SubmittedContext!;
        var source = context.ProfileMemorySources[0];
        context = fault switch
        {
            "id" => context with { ProfileMemorySources = [source with { Id = default }] },
            "profile" => context with { ProfileMemorySources = [source with { Profile = default }] },
            "revision" => context with { ProfileMemorySources = [source with { Revision = default }] },
            "duplicate" => context with { ProfileMemorySources = [source, source] },
            "foreign-profile" => context with
                { ProfileMemorySources = [source, context.ProfileMemorySources[1] with { Profile = new(Guid.NewGuid()) }] },
            "null" => context with { ProfileMemorySources = [null!] },
            "empty" => context with { ProfileMemorySources = [] },
            "default" => context with { ProfileMemorySources = default },
            "oversized" => context with { ProfileMemorySources = [.. Enumerable.Repeat(source, 17)] },
            "history-version" => context with { Version = 1 },
            "session-version" => context with { Version = 2 },
            "history" => context with { Sources = [new(new(Guid.NewGuid()), new(1), 1, 2, ConversationHistoryRole.User)] },
            _ => context with { MemorySources = [new(source.Id, source.Revision, context.Provenance.Request.SessionId)] },
        };
        context.Invoking(value => value.Validate()).Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData("id")]
    [InlineData("revision")]
    [InlineData("hosted")]
    [InlineData("history")]
    [InlineData("session")]
    public void ProfileLineageMustMatchExactCanonicalLocalEvidenceWithoutCompetingLineage(string fault)
    {
        var context = Answer().SubmittedContext!;
        var request = context.Provenance.Request;
        var source = context.ProfileMemorySources[0];
        var evidence = new ModelContextEvidence(new(source.Id.Value), request, source.Revision, "private",
            fault is "hosted" ? ModelEvidenceDisclosure.HostedEligible : ModelEvidenceDisclosure.LocalOnly)
        {
            ProfileMemorySource = fault switch
            {
                "id" => source with { Id = new(Guid.NewGuid()) },
                "revision" => source with { Revision = new(4) },
                _ => source,
            },
            HistorySource = fault is "history" ? new(new(Guid.NewGuid()), new(1), 1, 3, ConversationHistoryRole.User) : null,
            MemorySource = fault is "session" ? new(source.Id, source.Revision, request.SessionId) : null,
        };
        var create = () => new ModelContextEnvelope(request, SessionMemoryContextPolicy.Policy, "question", [evidence],
            DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddSeconds(15), ModelToolMode.None);
        create.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void MixedSessionAndProfileEnvelopeRemainsUnknownDespiteValidIndividualReferences()
    {
        var metadata = Answer().SubmittedContext!;
        var request = metadata.Provenance.Request;
        var source = metadata.ProfileMemorySources[0];
        var session = new SubmittedMemorySource(new(Guid.NewGuid()), new(3), request.SessionId);
        var envelope = new ModelContextEnvelope(request, SessionMemoryContextPolicy.Policy, "question",
            [new(new(source.Id.Value), request, source.Revision, "profile", ModelEvidenceDisclosure.LocalOnly) { ProfileMemorySource = source },
             new(new(session.Id.Value), request, session.Revision, "session", ModelEvidenceDisclosure.LocalOnly) { MemorySource = session }],
            DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddSeconds(15), ModelToolMode.None);
        SubmittedAnswerContext.Capture(metadata.Provenance, metadata.Generation, metadata.Profile, envelope,
            envelope.Serialize(), DateTimeOffset.UnixEpoch).Should().BeNull();
    }
}
