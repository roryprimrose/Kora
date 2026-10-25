using System.Security.Cryptography;
using System.Text;

using AwesomeAssertions;

using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Presentation;

namespace Kora.Core.UnitTests.Presentation;

public sealed class RetainedConversationDetailTests
{
    [Theory]
    [InlineData("valid")]
    [InlineData("source")]
    [InlineData("kind")]
    [InlineData("request-reference")]
    [InlineData("missing-answer")]
    public void RetainedAssistantRequiresItsOwnExactTurnReferenceAndFullAnswer(string fault)
    {
        var original = Message();
        var provenance = new ModelTurnProvenance(new(Guid.NewGuid()), original.Request, ModelProviderSelection.OllamaCandidate);
        var answer = new ConversationResponse(provenance, "# Full answer\n\n```mermaid\ngraph TD\n A-->B\n```");
        var message = original with
        {
            Version = 2, Revision = 3, Execution = provenance, ExecutionMetadataRevision = 0,
            Outcome = ModelTurnOutcome.Succeeded, Reason = ModelTurnReason.ProviderOutcome, AssistantResponse = answer,
        };
        if (fault is "missing-answer") { message = original; }
        var create = () => new AdmittedDetailContent(new(new(fault is "request-reference"
                ? original.Request.RequestId.Value : provenance.Turn.Value), message.Revision),
            fault is "kind" ? DetailContentKind.PlainText : DetailContentKind.Markdown,
            DetailContentOrigin.RetainedAssistant, DetailSensitivity.DisclosureConfirmationRequired,
            "ASSISTANT", "Exact retained answer, never execution authority",
            fault is "source" ? "replaced" : answer.Body, conversationMessage: message);
        if (fault is "valid")
        {
            var content = create();
            content.Source.Should().Be(answer.Body);
            content.ConversationMessage.Should().Be(message);
            content.Reference.ItemId.Value.Should().Be(provenance.Turn.Value);
        }
        else { create.Should().Throw<InvalidDataException>(); }
    }

    [Fact]
    public void ExactOriginalBytesAndTypedHostStaySeparateFromFinalizedResponseAuthority()
    {
        var message = Message();
        var content = Content(message);
        content.Source.Should().Be(message.Body);
        content.Digest.Should().Be(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(message.Body))));
        content.Reference.ItemId.Value.Should().Be(message.Request.RequestId.Value);
        content.Reference.Revision.Should().Be(message.Revision);
        content.ConversationMessage.Should().Be(message);
        content.SessionSource.Should().BeNull();
        content.HistorySession.Should().BeNull();
        content.WebResult.Should().BeNull();
        content.IsSameSnapshot(Content(message)).Should().BeTrue();
        content.IsSameSnapshot(Content(message with { Reason = ModelTurnReason.GenerationNotEnabled })).Should().BeFalse();
        var pending = message with { Revision = 1, Outcome = null, Reason = null };
        Content(pending).ConversationMessage!.ExpectedTaskState.Should().Be(HostTaskState.IntentRecorded);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("foreign-origin")]
    [InlineData("kind")]
    [InlineData("sensitivity")]
    [InlineData("reference")]
    [InlineData("revision")]
    [InlineData("body")]
    [InlineData("invalid-record")]
    public void RetainedOriginalRecordCannotBeFabricatedOrReclassified(string fault)
    {
        var message = Message();
        var create = () => new AdmittedDetailContent(
            new(new(fault is "reference" ? Guid.NewGuid() : message.Request.RequestId.Value),
                fault is "revision" ? 1 : message.Revision),
            fault is "kind" ? DetailContentKind.Markdown : DetailContentKind.PlainText,
            fault is "foreign-origin" ? DetailContentOrigin.EmbeddedDocument : DetailContentOrigin.RetainedConversation,
            fault is "sensitivity" ? DetailSensitivity.Public : DetailSensitivity.DisclosureConfirmationRequired,
            "Original USER", "Private retained record",
            fault is "body" ? message.Body.Trim() : message.Body,
            conversationMessage: fault is "missing" ? null : fault is "invalid-record" ? message with { Sequence = 0 } : message);
        create.Should().Throw<InvalidDataException>();
    }

    private static ConversationMessage Message() =>
        new(HostRequest.Create(RequestOrigin.LocalUi), new(1), 1, 2, DateTimeOffset.UnixEpoch,
            "  e\u0301 😀\r\n\t<script>run()</script> [x](https://example.test)\n  ",
            ModelTurnOutcome.Unavailable, ModelTurnReason.UnregisteredModel);

    private static AdmittedDetailContent Content(ConversationMessage message) =>
        new(new(new(message.Request.RequestId.Value), message.Revision), DetailContentKind.PlainText,
            DetailContentOrigin.RetainedConversation, DetailSensitivity.DisclosureConfirmationRequired,
            "Original USER", "Private retained record", message.Body, conversationMessage: message);
}
