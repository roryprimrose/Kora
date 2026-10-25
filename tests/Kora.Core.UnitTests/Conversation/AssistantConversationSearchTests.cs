using System.Text.Json;

using AwesomeAssertions;

using Kora.Core.Commands;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;

namespace Kora.Core.UnitTests.Conversation;

public sealed class AssistantConversationSearchTests
{
    private const string Id = "abcdef12-1234-1234-1234-123456789012";

    private static ConversationMessage Answer()
    {
        var request = HostRequest.Create(RequestOrigin.LocalUi);
        var provenance = new ModelTurnProvenance(new(Guid.NewGuid()), request, ModelProviderSelection.OllamaCandidate);
        return new(request, new(1), 1, 3, DateTimeOffset.UnixEpoch, "original USER only",
            ModelTurnOutcome.Succeeded, ModelTurnReason.ProviderOutcome)
        {
            Version = 2, Execution = provenance, ExecutionMetadataRevision = 0,
            AssistantResponse = new(provenance, "  saved café e\u0301\r\n<script>inert</script>  ")
        };
    }

    [Theory]
    [InlineData(ConversationSearchRole.ASSISTANT, "CAFE\u0301", true)]
    [InlineData(ConversationSearchRole.ASSISTANT, "original", false)]
    [InlineData(ConversationSearchRole.ASSISTANT, "Succeeded", false)]
    [InlineData(ConversationSearchRole.ASSISTANT, "Ollama", false)]
    [InlineData(ConversationSearchRole.ASSISTANT, "LocalUi", false)]
    [InlineData(ConversationSearchRole.ASSISTANT, "caf", false)]
    [InlineData(ConversationSearchRole.All, "café", false)]
    [InlineData(ConversationSearchRole.USER, "café", false)]
    [InlineData(ConversationSearchRole.HOST, "café", false)]
    public void ExplicitRoleMatchesOnlyAnswerBodyWithoutChangingDefaultFields(
        ConversationSearchRole role, string query, bool expected)
    {
        var message = Answer();
        new ConversationSearch(query, role).Matches(message, TestContext.Current.CancellationToken).Should().Be(expected);
        message.AssistantResponse!.Body.Should().Be("  saved café e\u0301\r\n<script>inert</script>  ");
    }

    [Fact]
    public void MissingAnswerNeverFallsBackAndMalformedExcludedRecordsStillFailClosed()
    {
        var answer = Answer();
        var search = new ConversationSearch("original", ConversationSearchRole.ASSISTANT);
        var legacy = answer with
        {
            Version = 1, Revision = 2, Execution = null, ExecutionMetadataRevision = null, AssistantResponse = null,
            Outcome = ModelTurnOutcome.Unavailable, Reason = ModelTurnReason.UnregisteredModel
        };
        search.Matches(legacy, TestContext.Current.CancellationToken).Should().BeFalse();
        search.Invoking(value => value.Matches(legacy with { Body = "\ud800" }, TestContext.Current.CancellationToken))
            .Should().Throw<InvalidDataException>();
        search.Invoking(value => value.Matches(answer with
        {
            AssistantResponse = answer.AssistantResponse! with { Provenance = answer.Execution! with { Turn = new(Guid.NewGuid()) } }
        }, TestContext.Current.CancellationToken)).Should().Throw<InvalidDataException>();
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        search.Invoking(value => value.Matches(answer, cancelled.Token)).Should().Throw<OperationCanceledException>();
    }

    [Theory]
    [InlineData(ModelTurnOutcome.Succeeded, true)]
    [InlineData(ModelTurnOutcome.Unavailable, false)]
    public void AssistantBodyComposesWithTypedTerminalPredicate(ModelTurnOutcome outcome, bool expected)
    {
        new ConversationSearch("café", ConversationSearchRole.ASSISTANT, new(outcome))
            .Matches(Answer(), TestContext.Current.CancellationToken).Should().Be(expected);
    }

    [Fact]
    public void AssistantRoleIsBoundIntoDigestCursorAndCompleteSerializedPage()
    {
        var message = Answer();
        var search = new ConversationSearch("café", ConversationSearchRole.ASSISTANT);
        foreach (var role in new[] { ConversationSearchRole.All, ConversationSearchRole.USER, ConversationSearchRole.HOST })
        { search.Digest.Should().NotBe(new ConversationSearch("café", role).Digest); }
        var cursor = new ConversationSearchCursor(new(message.Request.SessionId, new(1), 2, 1, 4),
            search.Digest, search.Role);
        var page = new ConversationSearchPage(new(message.Request.SessionId, new(1), true), 4, 2, search.Digest,
            [message], 1, cursor, search.Role);
        var bytes = ConversationSearchPage.Serialize(page);
        bytes.Length.Should().BeLessThanOrEqualTo(ConversationText.MaximumPageUtf8Bytes);
        var restored = JsonSerializer.Deserialize<ConversationSearchPage>(bytes)!;
        restored.Role.Should().Be(ConversationSearchRole.ASSISTANT);
        restored.Next.Should().Be(cursor);
        restored.Messages.Single().Should().Be(message);
    }

    [Theory]
    [InlineData("", "ASSISTANT", ConversationSearchRole.ASSISTANT)]
    [InlineData("Nova, ", "assistant", ConversationSearchRole.ASSISTANT)]
    [InlineData("Nova ", "USER", ConversationSearchRole.USER)]
    [InlineData("", "HOST", ConversationSearchRole.HOST)]
    [InlineData("", "All", ConversationSearchRole.All)]
    public void TypedAndCurrentNameVoiceRolePreserveVerbatimQuery(
        string prefix, string role, ConversationSearchRole expected)
    {
        const string query = "  CAFE\u0301 \"saved\" -- literal\r\n  ";
        var command = SessionCommand.Parse(prefix + "conversation search " + Id + " --role " + role + " -- " + query, "Nova")!;
        command.Operation.Should().Be(SessionCommandOperation.ConversationSearch);
        command.ConversationRole.Should().Be(expected);
        command.ConversationQuery.Should().Be(query);
        command.SessionId.Should().Be(Guid.Parse(Id));
        SessionCommand.Parse("conversation search " + Id + " -- saved", "Nova")!.ConversationRole
            .Should().Be(ConversationSearchRole.All);
    }

    [Theory]
    [InlineData("--role 3")]
    [InlineData("--role 99")]
    [InlineData("--role Unknown")]
    [InlineData("--role USER,HOST")]
    [InlineData("--role")]
    [InlineData("--other ASSISTANT")]
    [InlineData("--role ASSISTANT --role USER")]
    public void InvalidNamedRoleNeverFallsBackToDefault(string role)
    {
        var command = SessionCommand.Parse("conversation search " + Id + " " + role + " -- saved", "Nova")!;
        command.Operation.Should().Be(SessionCommandOperation.Invalid);
        command.Error.Should().Be(SessionCommand.ConversationSearchSyntax);
    }

    [Theory]
    [InlineData("next")]
    [InlineData("cancel")]
    public void ContinuationCannotAcceptCallerRoleReplacement(string verb)
    {
        SessionCommand.Parse("conversation search " + verb + " " + Id + " --role ASSISTANT", "Nova")!
            .Operation.Should().Be(SessionCommandOperation.Invalid);
    }
}
