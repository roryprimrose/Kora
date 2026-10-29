using System.Text;
using System.Text.Json;

using AwesomeAssertions;

using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Interaction;

namespace Kora.Core.UnitTests.Dependencies;

public sealed partial class ModelToolProtocolTests
{
    private static readonly string[] AskUserLabels = ["Version", "Readiness", "Runtime", "Profile"];

    [Theory]
    [InlineData(0, 1, 1, QuestionKind.Text)]
    [InlineData(2, 1, 1, QuestionKind.SingleChoice)]
    [InlineData(4, 2, 3, QuestionKind.MultipleChoice)]
    [InlineData(2, 0, 2, QuestionKind.MultipleChoice)]
    [InlineData(2, 0, 1, QuestionKind.MultipleChoice)]
    public void AskUserKeepsExactShapeBoundsAndCorrelationThroughCanonicalRoundTrip(
        int options, int minimum, int maximum, QuestionKind kind)
    {
        var arguments = JsonSerializer.SerializeToUtf8Bytes(new
        {
            prompt = "Choose the required details",
            options = AskUserLabels.Take(options),
            minimum,
            maximum,
        });
        var question = ModelToolProtocol.ParseAskUser(arguments, "ask_1");
        var spec = LocalModelQuestionPolicy.CreateSpec(question.Question);
        spec.Kind.Should().Be(kind);
        spec.Minimum.Should().Be(minimum);
        spec.Maximum.Should().Be(maximum);
        var roundTrip = (ModelClarificationQuestion)ModelToolProtocol.ParseAssistant(
            ModelToolProtocol.SerializeMessage(question), clarificationAllowed: true);
        roundTrip.CallId.Should().Be("ask_1");
        roundTrip.Question.Should().BeEquivalentTo(question.Question);
        var answer = kind == QuestionKind.Text ? new QuestionAnswer([], "Original text")
            : new QuestionAnswer(spec.Options.Take(minimum).Select(option => option.Id));
        var message = new ModelQuestionAnswerMessage(question, answer, RequestOrigin.ActivatedVoice);
        var provenance = Provenance();
        var context = ModelToolProtocol.ReadContext(Envelope(provenance).Serialize(), provenance);
        var continuation = ModelToolProtocol.CreateRequest(provenance, null, context, [question, message], 2,
            clarificationAllowed: true);
        continuation.Messages.Should().Equal(question, message);
    }

    [Theory]
    [InlineData("""{"prompt":"Choose","options":["A","B"],"minimum":0,"maximum":3}""")]
    [InlineData("""{"prompt":"Choose","options":["A","B"],"minimum":2,"maximum":1}""")]
    [InlineData("""{"prompt":"Choose","options":["A","B"],"minimum":-1,"maximum":2}""")]
    [InlineData("""{"prompt":"Choose","options":["A","B"],"minimum":0,"maximum":0}""")]
    [InlineData("""{"prompt":"Choose","options":[],"minimum":0,"maximum":1}""")]
    [InlineData("""{"prompt":"Choose","options":["A","B"],"minimum":"1","maximum":2}""")]
    [InlineData("""{"prompt":"Choose","options":["A","B"],"minimum":1,"maximum":"2"}""")]
    [InlineData("""{"prompt":"Choose","options":["A","B"],"minimum":1.5,"maximum":2}""")]
    [InlineData("""{"prompt":"Choose","options":["A","B"],"minimum":1,"maximum":1.5}""")]
    [InlineData("""{"prompt":"Choose","options":["A","B"],"minimum":1}""")]
    [InlineData("""{"prompt":"Choose","options":["A","B"],"minimum":1,"maximum":2,"approved":true}""")]
    [InlineData("""{"prompt":"Choose","options":["A","B"],"minimum":1,"maximum":2,"session":"foreign"}""")]
    [InlineData("""{"prompt":"Choose","options":["A","B"],"minimum":1,"minimum":0,"maximum":2}""")]
    public void AskUserRejectsMalformedBoundsAndAuthorityWithoutRepair(string wire)
    {
        var parse = () => ModelToolProtocol.ParseAskUser(Encoding.UTF8.GetBytes(wire), "ask_1");
        parse.Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("000")]
    [InlineData("foreign/session")]
    public void AskUserRejectsInvalidToolCorrelation(string callId)
    {
        var parse = () => ModelToolProtocol.ParseAskUser(
            """{"prompt":"Choose","options":["A","B"],"minimum":1,"maximum":2}"""u8, callId);
        parse.Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData("too-few")]
    [InlineData("too-many")]
    [InlineData("duplicate")]
    [InlineData("foreign")]
    [InlineData("text")]
    public void CheckboxAnswersMustSatisfyTheExactDeclaredQuestion(string fault)
    {
        var question = new ModelClarificationQuestion(new("Choose", ["A", "B", "C", "D"], 2, 3), "ask_1");
        QuestionAnswer answer = fault switch
        {
            "too-few" => new(["option-0"]),
            "too-many" => new(["option-0", "option-1", "option-2", "option-3"]),
            "duplicate" => new(["option-0", "option-0"]),
            "foreign" => new(["option-0", "foreign"]),
            _ => new([], "Not a checkbox answer"),
        };
        var create = () => new ModelQuestionAnswerMessage(question, answer, RequestOrigin.LocalUi);
        create.Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData(1, 2)]
    [InlineData(0, 1)]
    public void StructuredCheckboxBoundsRoundTripWithoutInventingNativeToolCorrelation(int minimum, int maximum)
    {
        var question = new ModelClarificationQuestion(new("Choose", ["A", "B"], minimum, maximum));
        var parsed = (ModelClarificationQuestion)ModelToolProtocol.ParseAssistant(
            ModelToolProtocol.SerializeMessage(question), clarificationAllowed: true);
        parsed.CallId.Should().BeNull();
        parsed.Question.Should().BeEquivalentTo(question.Question);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void QuestionAndObservationCallsCannotReuseEachOthersCorrelation(bool questionFirst)
    {
        var provenance = Provenance();
        var context = ModelToolProtocol.ReadContext(Envelope(provenance).Serialize(), provenance);
        var calls = Calls(Tool("shared"));
        var result = ModelToolProtocol.CreateToolResult(calls.Calls[0],
            new(Kora.Core.Tools.CapabilityOutcome.Succeeded, "observed", Version: new("1.0", "recorded")));
        var question = new ModelClarificationQuestion(new("Choose", ["A", "B"]), "shared");
        var answer = new ModelQuestionAnswerMessage(question, new(["option-0"]), RequestOrigin.LocalUi);
        ModelToolMessage[] history = questionFirst ? [question, answer, calls, result] : [calls, result, question, answer];
        var create = () => ModelToolProtocol.CreateRequest(provenance, null, context, history, 3, clarificationAllowed: true);
        create.Should().Throw<InvalidDataException>();
    }
}
