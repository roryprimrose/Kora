using AwesomeAssertions;

using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.Core.Providers;

namespace Kora.Core.UnitTests.Conversation;

public sealed class ConversationHandoffLineageTests
{
    [Theory]
    [InlineData("observation")]
    [InlineData("source")]
    [InlineData("source-request")]
    [InlineData("source-selection")]
    [InlineData("source-selection-invalid")]
    [InlineData("review-null")]
    [InlineData("session")]
    [InlineData("origin")]
    [InlineData("request")]
    [InlineData("task")]
    [InlineData("provider")]
    [InlineData("revision")]
    [InlineData("review")]
    [InlineData("expiry")]
    [InlineData("digest-null")]
    [InlineData("digest-length")]
    [InlineData("digest-encoding")]
    [InlineData("profile-null")]
    [InlineData("profile-service")]
    [InlineData("destination-null")]
    [InlineData("destination-invalid")]
    [InlineData("destination-provider")]
    [InlineData("destination-revision")]
    public void ImmutableLineageRejectsMalformedForeignOrRenewedIdentities(string change)
    {
        var source = HostRequest.Create(RequestOrigin.LocalUi);
        var successor = new HostRequest(new(Guid.NewGuid()), source.SessionId, new(Guid.NewGuid()), source.Origin);
        var execution = new ModelTurnProvenance(new(Guid.NewGuid()), source, ModelProviderSelection.OllamaCandidate);
        var review = new HostQuestionKey(source, new(Guid.NewGuid()), new(1));
        var profile = new ProviderProfileTuple(ProviderService.Copilot, new("account"), new(1),
            new("model"), new("reasoning"), new("context"));
        var lineage = new ConversationHandoffLineage(Guid.NewGuid(), execution, new(2), review,
            DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddMinutes(1), new string('a', 64),
            profile, ModelProviderSelection.CopilotCandidate);
        lineage.Validate(successor);
        lineage = change switch
        {
            "observation" => lineage with { Observation = Guid.Empty },
            "source" => lineage with { Source = null! },
            "source-request" => lineage with { Source = execution with { Request = null! } },
            "source-selection" => lineage with { Source = execution with { Selection = null! } },
            "source-selection-invalid" => lineage with { Source = execution with { Selection = ModelProviderSelection.OllamaCandidate with { Revision = default } } },
            "review-null" => lineage with { Review = null! },
            "session" => lineage with { Source = execution with { Request = HostRequest.Create(source.Origin) } },
            "origin" => lineage with { Source = execution with { Request = new(source.RequestId, source.SessionId, source.TaskId, RequestOrigin.ActivatedVoice) } },
            "request" => lineage with { Source = execution with { Request = new(successor.RequestId, source.SessionId, source.TaskId, source.Origin) } },
            "task" => lineage with { Source = execution with { Request = new(source.RequestId, source.SessionId, successor.TaskId, source.Origin) } },
            "provider" => lineage with { Source = execution with { Selection = ModelProviderSelection.CopilotCandidate } },
            "revision" => lineage with { SourceRevision = new(1) },
            "review" => lineage with { Review = new(HostRequest.Create(RequestOrigin.LocalUi), review.QuestionId, review.Revision) },
            "expiry" => lineage with { ExpiresAt = lineage.CreatedAt },
            "digest-null" => lineage with { ContextDigest = null! },
            "digest-length" => lineage with { ContextDigest = "short" },
            "digest-encoding" => lineage with { ContextDigest = new string('z', 64) },
            "profile-null" => lineage with { Profile = null! },
            "profile-service" => lineage with { Profile = profile with { Service = ProviderService.Local } },
            "destination-null" => lineage with { Destination = null! },
            "destination-invalid" => lineage with { Destination = ModelProviderSelection.CopilotCandidate with { Revision = default } },
            "destination-provider" => lineage with { Destination = ModelProviderSelection.OllamaCandidate },
            "destination-revision" => lineage with { Destination = ModelProviderSelection.CopilotCandidate with { Revision = new(2) } },
            _ => throw new InvalidOperationException("Unknown test mutation."),
        };
        lineage.Invoking(value => value.Validate(successor)).Should().Throw<InvalidDataException>();
    }
}
