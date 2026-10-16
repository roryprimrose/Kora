using AwesomeAssertions;

using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;

namespace Kora.Core.UnitTests.Conversation;

public sealed class ConversationTerminalFilterTests
{
    [Fact]
    public void EveryDefinedTerminalValueAndAnyComposeExactlyWithEveryRoleAndLexicalField()
    {
        var token = TestContext.Current.CancellationToken;
        foreach (var outcome in Enum.GetValues<ModelTurnOutcome>().Where(value => value != ModelTurnOutcome.Succeeded))
        {
            foreach (var reason in Enum.GetValues<ModelTurnReason>())
            {
                var message = Message() with { Outcome = outcome, Reason = reason, Body = "marker" };
                message.Validate();
                foreach (var filter in new[] { default, new ConversationTerminalFilter(outcome),
                    new(null, reason), new(null, null, message.ExpectedTaskState),
                    new(outcome, reason, message.ExpectedTaskState) })
                {
                    filter.Validate();
                    foreach (var role in Enum.GetValues<ConversationSearchRole>())
                    {
                        var search = new ConversationSearch("marker", role, filter);
                        search.Terminal.Should().Be(filter);
                        search.Matches(message, token).Should().Be(role != ConversationSearchRole.HOST
                            && (filter == default || role != ConversationSearchRole.USER));
                        new ConversationSearch(reason.ToString(), role, filter).Matches(message, token)
                            .Should().Be(role != ConversationSearchRole.USER);
                        new ConversationSearch("absent", role, filter).Matches(message, token).Should().BeFalse();
                    }
                }
            }
        }
    }

    [Fact]
    public void AllAnyExactCombinationsAreAndedIncludingValidImpossiblePredicates()
    {
        var message = Message();
        var token = TestContext.Current.CancellationToken;
        ModelTurnOutcome?[] outcomes = [null, .. Enum.GetValues<ModelTurnOutcome>().Select(value => (ModelTurnOutcome?)value)];
        ModelTurnReason?[] reasons = [null, .. Enum.GetValues<ModelTurnReason>().Select(value => (ModelTurnReason?)value)];
        HostTaskState?[] states = [null, .. Enum.GetValues<HostTaskState>().Select(value => (HostTaskState?)value)];
        foreach (var outcome in outcomes)
        {
            foreach (var reason in reasons)
            {
                foreach (var state in states)
                {
                    var filter = new ConversationTerminalFilter(outcome, reason, state);
                    var expected = (outcome is null || outcome == message.Outcome)
                        && (reason is null || reason == message.Reason)
                        && (state is null || state == message.ExpectedTaskState);
                    foreach (var role in Enum.GetValues<ConversationSearchRole>())
                    {
                        new ConversationSearch("Unavailable", role, filter).Matches(message, token).Should()
                            .Be(expected && (role != ConversationSearchRole.USER || filter == default));
                    }
                }
            }
        }
    }

    [Fact]
    public void PendingUserTextCannotSupplyTerminalMetadataEvenWhenItMimicsEveryStatus()
    {
        var pending = Message() with { Revision = 1, Outcome = null, Reason = null };
        pending.Validate();
        var token = TestContext.Current.CancellationToken;
        foreach (var filter in new[] { new ConversationTerminalFilter(ModelTurnOutcome.Unavailable),
            new(null, ModelTurnReason.UnregisteredModel), new(null, null, HostTaskState.IntentRecorded),
            new(ModelTurnOutcome.Unavailable, ModelTurnReason.UnregisteredModel, HostTaskState.Unavailable) })
        {
            foreach (var role in Enum.GetValues<ConversationSearchRole>())
            {
                new ConversationSearch("Unavailable", role, filter).Matches(pending, token).Should().BeFalse();
            }
        }
        new ConversationSearch("Unavailable", ConversationSearchRole.USER).Matches(pending, token).Should().BeTrue();
        new ConversationSearch("IntentRecorded", ConversationSearchRole.HOST).Matches(pending, token).Should().BeTrue();
        Message().Body.Should().Be(pending.Body);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var match = () => new ConversationSearch("marker", ConversationSearchRole.USER,
            new(ModelTurnOutcome.Unavailable)).Matches(pending, cancelled.Token);
        match.Should().Throw<OperationCanceledException>();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(99)]
    public void UndefinedEnumsFailExplicitlyRatherThanBecomingAny(int value)
    {
        foreach (var filter in new[] { new ConversationTerminalFilter((ModelTurnOutcome)value),
            new(null, (ModelTurnReason)value), new(null, null, (HostTaskState)value) })
        {
            var validate = () => filter.Validate();
            validate.Should().Throw<ArgumentException>();
            var construct = () => new ConversationSearch("marker", terminal: filter);
            construct.Should().Throw<ArgumentException>();
        }
    }

    [Fact]
    public void CompletePageAndContinuationRetainEveryPredicateWithoutChangingBodies()
    {
        var message = Message();
        var filter = new ConversationTerminalFilter(message.Outcome, message.Reason, message.ExpectedTaskState);
        var cursor = new ConversationSearchCursor(new(message.Request.SessionId, new(1), 2, 1, 4),
            new ConversationSearch("marker", ConversationSearchRole.HOST, filter).Digest, ConversationSearchRole.HOST, filter);
        var page = new ConversationSearchPage(new(message.Request.SessionId, new(1), true), 4, 2,
            cursor.QueryDigest, [message], 1, cursor, cursor.Role, filter);
        var serialized = ConversationSearchPage.Serialize(page);
        var actual = System.Text.Json.JsonSerializer.Deserialize<ConversationSearchPage>(serialized)!;
        actual.Terminal.Should().Be(filter);
        actual.Next.Should().Be(cursor);
        actual.Messages.Single().Should().Be(message);
        new ConversationSearch("marker", ConversationSearchRole.HOST, filter).Digest.Should().Be(cursor.QueryDigest);
    }

    private static ConversationMessage Message() => new(HostRequest.Create(RequestOrigin.LocalUi), new(1), 1, 2,
        DateTimeOffset.UnixEpoch, "marker Unavailable UnregisteredModel IntentRecorded",
        ModelTurnOutcome.Unavailable, ModelTurnReason.UnregisteredModel);
}
