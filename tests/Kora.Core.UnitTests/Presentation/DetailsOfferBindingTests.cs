using AwesomeAssertions;

using Kora.Core.Authorization;
using Kora.Core.Commands;
using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.Core.Presentation;

namespace Kora.Core.UnitTests.Presentation;

public sealed class DetailsOfferBindingTests
{
    [Fact]
    public void FixedQuestionBindsIndependentHostControlAndRejectsEveryForeignOrMalformedSource()
    {
        var original = HostRequest.Create(RequestOrigin.ActivatedVoice);
        var now = DateTimeOffset.UnixEpoch;
        var binding = new DetailsOfferBinding(original, new(1), 0, 1, 3, Guid.NewGuid(), new('A', 64),
            Guid.NewGuid(), now, now.AddSeconds(60), Guid.NewGuid());
        var control = new HostRequest(new(Guid.NewGuid()), original.SessionId, new(Guid.NewGuid()), RequestOrigin.HostSystem);
        var question = new HostQuestionRecord(new(control, new(Guid.NewGuid()), new(1)),
            DetailsOfferBinding.CreateSpec(), new(1), binding.ExpiresAt)
        { DetailsOffer = binding };
        binding.Validate(question);
        (binding with { OriginalRequest = new(original.RequestId, original.SessionId, original.TaskId, RequestOrigin.LocalUi) }).Validate(question);
        question.Spec.Accepts(new(["yes"]), true).Should().BeTrue();
        question.Spec.Accepts(new(["not-now"]), true).Should().BeTrue();
        question.Spec.Accepts(new([]), true).Should().BeFalse();
        var invalid = new[]
        {
            binding with { OriginalRequest = null! },
            binding with { OriginalRequest = HostRequest.Create(RequestOrigin.HostSystem) },
            binding with { OriginalRequest = new(original.RequestId, original.SessionId, original.TaskId, original.Origin, new(Guid.NewGuid())) },
            binding with { Generation = default },
            binding with { MetadataRevision = -1 },
            binding with { Sequence = 0 },
            binding with { Sequence = 101 },
            binding with { MessageRevision = 2 },
            binding with { Item = Guid.Empty },
            binding with { OptIn = Guid.Empty },
            binding with { RunId = Guid.Empty },
            binding with { SourceDigest = null! },
            binding with { SourceDigest = "A" },
            binding with { SourceDigest = new('Z', 64) },
            binding with { CreatedAt = now.ToOffset(TimeSpan.FromHours(1)) },
            binding with { ExpiresAt = binding.ExpiresAt.ToOffset(TimeSpan.FromHours(1)) },
            binding with { ExpiresAt = now },
            binding with { ExpiresAt = now.AddSeconds(61) },
        };
        foreach (var bad in invalid)
        { bad.Invoking(value => value.Validate(question)).Should().Throw<InvalidDataException>(); }
        var hash = new string('a', 64);
        var proposalRequest = new HostRequest(control.RequestId, control.SessionId, control.TaskId, control.Origin, new(Guid.NewGuid()));
        var proposal = new HostOperationProposal(proposalRequest, new(Guid.NewGuid()), new(1),
            new("read", "builtin", "inspect", hash, hash, hash, hash, hash, hash, hash, hash, hash, new(1)),
            HostOperationEffect.BoundedRead, binding.ExpiresAt);
        var badQuestions = new[]
        {
            question with { Key = null! },
            question with { Spec = null! },
            question with { Key = new(new(control.RequestId, control.SessionId, control.TaskId, RequestOrigin.LocalUi), question.Key.QuestionId, new(1)) },
            question with { Key = new(proposalRequest, question.Key.QuestionId, new(1)) },
            question with { Key = new(HostRequest.Create(RequestOrigin.HostSystem), question.Key.QuestionId, new(1)) },
            question with { Key = new(new(original.RequestId, control.SessionId, control.TaskId, control.Origin), question.Key.QuestionId, new(1)) },
            question with { Key = new(new(control.RequestId, control.SessionId, original.TaskId, control.Origin), question.Key.QuestionId, new(1)) },
            question with { SessionGeneration = new(2) },
            question with { ExpiresAt = now.AddSeconds(59) },
            question with { Proposal = proposal },
            question with { Spec = new("Different", QuestionKind.Text, [], maximumTextLength: 50) },
        };
        foreach (var bad in badQuestions)
        { binding.Invoking(value => value.Validate(bad)).Should().Throw<InvalidDataException>(); }
    }

    [Theory]
    [InlineData("ask this conversation with details offer  exact \r\nbody", true, " exact \r\nbody")]
    [InlineData("Kora, ask this conversation WITH DETAILS OFFER body", true, "body")]
    [InlineData("ask this conversation ordinary", false, "ordinary")]
    public void ExplicitOptInPreservesOriginalBytesAndDoesNotInventAnotherPreference(string input, bool opted, string body)
    {
        var command = SessionCommand.Parse(input, "Kora")!;
        command.Operation.Should().Be(SessionCommandOperation.RequestReview);
        command.RequestForegroundTarget.Should().BeTrue();
        command.RequestDetailsOffer.Should().Be(opted);
        command.RequestBody.Should().Be(body);
    }

    [Fact]
    public void MissingOptedOriginalBodyIsNotAnOrdinarySubmission() =>
        SessionCommand.Parse("ask this conversation with details offer", "Kora")!.Operation.Should().Be(SessionCommandOperation.Invalid);
}