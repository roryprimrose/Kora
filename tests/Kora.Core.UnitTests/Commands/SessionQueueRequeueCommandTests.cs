using AwesomeAssertions;

using Kora.Core.Commands;

namespace Kora.Core.UnitTests.Commands;

public sealed class SessionQueueRequeueCommandTests
{
    private const string Id = "11111111-1111-1111-1111-111111111111";
    private const string Prefix = Id + " 1 0 " + Id + " 0 " + Id + " 1";

    [Theory]
    [InlineData("review", SessionCommandOperation.QueueRequeueExpiredReview)]
    [InlineData("confirm", SessionCommandOperation.QueueRequeueExpiredConfirm)]
    public void ExactTypedAndActivatedCommandsRetainEveryAddressedIdentity(string verb, SessionCommandOperation operation)
    {
        var input = "queue requeue-expired-" + verb + " " + Prefix + (string.Equals(verb, "confirm", StringComparison.Ordinal) ? " " + Id + " " + Id + " " + Id + " confirm" : "");
        var parsed = SessionCommand.Parse(input, "Kora")!;
        parsed.Operation.Should().Be(operation);
        SessionCommand.Parse("Kora " + input, "Kora").Should().BeEquivalentTo(parsed);
        SessionCommand.Parse("queue requeue-expired-discard " + Id + " " + Id, "Kora")!.QueueRequeueReviewId.Should().Be(Guid.Parse(Id));
    }

    [Fact]
    public void EveryNoncanonicalOrIncompleteReviewAndConfirmationRefusesWithoutInference()
    {
        var review = ("queue requeue-expired-review " + Prefix).Split(' ');
        var confirm = ("queue requeue-expired-confirm " + Prefix + " " + Id + " " + Id + " " + Id + " confirm").Split(' ');
        foreach (var words in new[] { review, confirm })
        {
            for (var index = 2; index < words.Length; index++)
            {
                var changed = words.ToArray();
                changed[index] = index == words.Length - 1 && words == confirm ? "yes" : "invalid";
                SessionCommand.Parse(string.Join(" ", changed), "Kora")!.Operation.Should().Be(SessionCommandOperation.Invalid);
            }
            SessionCommand.Parse(string.Join(" ", words) + " extra", "Kora")!.Operation.Should().Be(SessionCommandOperation.Invalid);
        }
        SessionCommand.Parse("queue requeue-expired-discard " + Id + " invalid", "Kora")!.Operation.Should().Be(SessionCommandOperation.Invalid);
        foreach (var index in new[] { 2, 3, 4, 6 })
        {
            var changed = review.ToArray();
            changed[index] = index == 2 ? "AAAAAAAA-aaaa-aaaa-aaaa-aaaaaaaaaaaa" : "01";
            SessionCommand.Parse(string.Join(" ", changed), "Kora")!.Operation.Should().Be(SessionCommandOperation.Invalid);
        }
        SessionCommand.Parse("queue requeue-expired-review " + Prefix + "\t", "Kora")!.Operation.Should().Be(SessionCommandOperation.Invalid);
        SessionCommand.Parse("queue requeue-expired-review " + Prefix + new string(' ', 1024), "Kora")!.Operation.Should().Be(SessionCommandOperation.Invalid);
    }
}
