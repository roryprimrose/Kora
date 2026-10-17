using System.Text.Json;

using AwesomeAssertions;

using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;

namespace Kora.Core.UnitTests.Conversation;

public sealed class ConversationCorrelationFilterTests
{
    [Theory]
    [InlineData("bad")]
    [InlineData("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa")]
    [InlineData("AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("{aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa}")]
    [InlineData(" aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa")]
    [InlineData("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa ")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public void MalformedNonemptyInputNeverBecomesAny(string invalid)
    {
        var request = () => ConversationCorrelationFilter.Parse(invalid, "");
        var task = () => ConversationCorrelationFilter.Parse("", invalid);
        request.Should().Throw<InvalidDataException>();
        task.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void MissingInputAndInvalidTypedPredicatesFailClosed()
    {
        var missingRequest = () => ConversationCorrelationFilter.Parse(null!, "");
        var missingTask = () => ConversationCorrelationFilter.Parse("", null!);
        missingRequest.Should().Throw<ArgumentNullException>();
        missingTask.Should().Throw<ArgumentNullException>();
        foreach (var filter in new[] { new ConversationCorrelationFilter(default(HostId<RequestIdentity>)),
            new ConversationCorrelationFilter(TaskId: default(HostId<TaskIdentity>)) })
        {
            var create = () => new ConversationSearch("marker", correlation: filter);
            create.Should().Throw<ArgumentException>();
        }
    }

    [Fact]
    public void ExactPredicatesAndAllOtherCriteriaComposeAndBindIdentityWithoutChangingBody()
    {
        var message = Message();
        var token = TestContext.Current.CancellationToken;
        var exact = ConversationCorrelationFilter.Parse(message.Request.RequestId.Value.ToString("D"),
            message.Request.TaskId.Value.ToString("D"));
        exact.Should().Be(new ConversationCorrelationFilter(message.Request.RequestId, message.Request.TaskId));
        ConversationCorrelationFilter.Parse("", "").Should().Be(default(ConversationCorrelationFilter));
        var range = new ConversationRangeFilter(2, 2, message.SubmittedAt, message.SubmittedAt);
        var terminal = new ConversationTerminalFilter(message.Outcome, message.Reason, message.ExpectedTaskState);
        foreach (var filter in new[] { default, exact, new ConversationCorrelationFilter(message.Request.RequestId),
            new ConversationCorrelationFilter(TaskId: message.Request.TaskId) })
        {
            filter.Validate();
            new ConversationSearch("marker", correlation: filter).Matches(message, token).Should().BeTrue();
            new ConversationSearch("Unavailable", ConversationSearchRole.HOST, terminal, range, filter)
                .Matches(message, token).Should().BeTrue();
        }
        foreach (var filter in new[] { exact with { RequestId = new(Guid.NewGuid()) },
            exact with { TaskId = new(Guid.NewGuid()) } })
        { new ConversationSearch("marker", correlation: filter).Matches(message, token).Should().BeFalse(); }
        new ConversationSearch("marker", ConversationSearchRole.USER, terminal, range, exact).Matches(message, token).Should().BeFalse();
        new ConversationSearch("Denied", ConversationSearchRole.HOST, correlation: exact).Matches(message, token).Should().BeFalse();
        new ConversationSearch("marker", range: new(3), correlation: exact).Matches(message, token).Should().BeFalse();
        new ConversationSearch("absent", correlation: exact).Matches(message, token).Should().BeFalse();
        var search = new ConversationSearch("MARKER marker", correlation: exact);
        search.Digest.Should().Be(new ConversationSearch("marker", correlation: exact).Digest);
        foreach (var changed in new[] { default, exact with { RequestId = null }, exact with { TaskId = null },
            exact with { RequestId = new(Guid.NewGuid()) }, exact with { TaskId = new(Guid.NewGuid()) } })
        { search.Digest.Should().NotBe(new ConversationSearch("marker", correlation: changed).Digest); }
        var cursor = new ConversationSearchCursor(new(message.Request.SessionId, new(1), 3, 2, 6),
            search.Digest, Correlation: exact);
        var page = new ConversationSearchPage(new(message.Request.SessionId, new(1), true), 6, 3, search.Digest,
            [message], 2, cursor, Correlation: exact);
        page.Correlation.Should().Be(exact);
        page.Next!.Correlation.Should().Be(exact);
        ConversationSearchPage.Serialize(page).Length.Should().BeLessThanOrEqualTo(ConversationText.MaximumPageUtf8Bytes);
        message.Body.Should().Be("marker Denied\r\n e\u0301");
    }

    [Theory]
    [InlineData("body")]
    [InlineData("request")]
    [InlineData("origin")]
    [InlineData("invocation")]
    [InlineData("status")]
    public void ExcludedMessagesAndRequestsAreValidatedFirst(string invalid)
    {
        var message = Message();
        message = invalid switch
        {
            "body" => message with { Body = "\ud800" },
            "request" => message with { Request = null! },
            "origin" => message with { Request = new(message.Request.RequestId, message.Request.SessionId,
                message.Request.TaskId, RequestOrigin.HostSystem) },
            "invocation" => message with { Request = new(message.Request.RequestId, message.Request.SessionId,
                message.Request.TaskId, RequestOrigin.LocalUi, new(Guid.NewGuid())) },
            _ => message with { Reason = (ModelTurnReason)999 },
        };
        var match = () => new ConversationSearch("absent", correlation: new(new(Guid.NewGuid()), new(Guid.NewGuid())))
            .Matches(message, TestContext.Current.CancellationToken);
        match.Should().Throw<Exception>();
    }

    [Fact]
    public void InvalidSerializedRequestIdsCannotEnterMessagesAndCancellationPrecedesMatching()
    {
        var json = JsonSerializer.Serialize(Message().Request);
        foreach (var id in new[] { Message().Request.RequestId.Value, Message().Request.SessionId.Value, Message().Request.TaskId.Value })
        {
            var read = () => JsonSerializer.Deserialize<HostRequest>(json.Replace(id.ToString("D"), Guid.Empty.ToString("D"), StringComparison.Ordinal));
            read.Should().Throw<ArgumentException>();
        }
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var match = () => new ConversationSearch("marker", correlation: new(new(Guid.NewGuid())))
            .Matches(Message(), cancellation.Token);
        match.Should().Throw<OperationCanceledException>();
    }

    private static ConversationMessage Message() => new(
        new(new(new Guid("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa")), new(new Guid("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb")),
            new(new Guid("cccccccc-cccc-cccc-cccc-cccccccccccc")), RequestOrigin.LocalUi),
        new(1), 2, 2, DateTimeOffset.UnixEpoch, "marker Denied\r\n e\u0301", ModelTurnOutcome.Unavailable, ModelTurnReason.UnregisteredModel);
}
