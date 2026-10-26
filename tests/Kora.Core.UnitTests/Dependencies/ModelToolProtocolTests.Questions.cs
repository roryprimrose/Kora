using System.Text;
using System.Text.Json;

using AwesomeAssertions;

using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Interaction;

namespace Kora.Core.UnitTests.Dependencies;

public sealed partial class ModelToolProtocolTests
{
    private const string QuestionWire = """{"kind":"question","prompt":"Which format?","options":["Compact","Detailed"]}""";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StructuredQuestionNeedsExplicitOptInAndKeepsCompleteDeclaredChoiceShape(bool admitted)
    {
        var parse = () => ModelToolProtocol.ParseAssistant(Encoding.UTF8.GetBytes(QuestionWire), admitted);
        if (!admitted) { parse.Should().Throw<InvalidDataException>(); return; }
        var question = parse().Should().BeOfType<ModelClarificationQuestion>().Subject;
        question.Question.Prompt.Should().Be("Which format?");
        question.Question.Options.Should().Equal("Compact", "Detailed");
        var spec = LocalModelQuestionPolicy.CreateSpec(question.Question);
        spec.Kind.Should().Be(QuestionKind.SingleChoice);
        spec.Minimum.Should().Be(1);
        spec.Maximum.Should().Be(1);
        spec.Options.Select(option => option.Id).Should().Equal("option-0", "option-1");
        Encoding.UTF8.GetString(ModelToolProtocol.SerializeMessage(question)).Should().Be(QuestionWire);
    }

    [Theory]
    [InlineData("""{"kind":"question","prompt":"","options":[]}""")]
    [InlineData("""{"kind":"question","prompt":"Choose","options":["Only"]}""")]
    [InlineData("""{"kind":"question","prompt":"Choose","options":["Same","same"]}""")]
    [InlineData("""{"kind":"question","prompt":"Choose","options":["a","b","c","d","e"]}""")]
    [InlineData("""{"kind":"question","prompt":"Choose","options":[null,"b"]}""")]
    [InlineData("""{"kind":"question","prompt":"Choose","options":"not-an-array"}""")]
    [InlineData("""{"kind":"question","prompt":"Choose","options":[],"approved":true}""")]
    [InlineData("""{"kind":"question","prompt":"Choose","options":[],"minimum":0}""")]
    [InlineData("""{"kind":"question","prompt":"Choose","options":[],"prompt":"different"}""")]
    [InlineData("""{"kind":"question","prompt":"api_key=synthetic-only-value","options":[]}""")]
    public void UnsupportedQuestionCannotBecomeAChoiceApprovalOrRepairedQuestion(string wire)
    {
        var parse = () => ModelToolProtocol.ParseAssistant(Encoding.UTF8.GetBytes(wire), clarificationAllowed: true);
        parse.Should().Throw<Exception>();
    }

    [Theory]
    [InlineData(500, true)]
    [InlineData(501, false)]
    public void QuestionPromptAndFreeTextAnswerUseExactDeclaredBounds(int characters, bool accepted)
    {
        var construct = () => new ModelClarificationQuestion(new(new string('x', characters), []));
        if (!accepted) { construct.Should().Throw<InvalidDataException>(); return; }
        var question = construct();
        var spec = LocalModelQuestionPolicy.CreateSpec(question.Question);
        spec.Kind.Should().Be(QuestionKind.Text);
        spec.MaximumTextLength.Should().Be(500);
        var reply = new ModelQuestionAnswerMessage(question, new([], new string('a', 500)), RequestOrigin.LocalUi);
        reply.Answer.Text.Should().HaveLength(500);
        var excessive = () => new ModelQuestionAnswerMessage(question, new([], new string('a', 501)), RequestOrigin.LocalUi);
        excessive.Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData("no-opt-in")]
    [InlineData("partial")]
    [InlineData("foreign-question")]
    [InlineData("repeat")]
    [InlineData("wrong-round")]
    public void ClarificationHistoryCannotBorrowToolAuthorityOrRepeatOrSubstituteAnAnswer(string fault)
    {
        var provenance = Provenance();
        var original = Envelope(provenance);
        var context = ModelToolProtocol.ReadContext(new ModelContextEnvelope(provenance.Request,
            original.SystemPolicy, original.UserRequest, [], original.CreatedAt, original.ExpiresAt,
            ModelToolMode.None).Serialize(), provenance);
        var question = new ModelClarificationQuestion(new("Which format?", ["Compact", "Detailed"]));
        var other = new ModelClarificationQuestion(new("Which format?", ["Compact", "Detailed"]));
        var answer = new ModelQuestionAnswerMessage(fault is "foreign-question" ? other : question,
            new(["option-0"]), RequestOrigin.ActivatedVoice);
        ModelToolMessage[] history = fault switch
        {
            "partial" => [question],
            "repeat" => [question, answer, question, answer],
            _ => [question, answer],
        };
        var create = () => ModelToolProtocol.CreateRequest(provenance, null, context, history,
            fault is "wrong-round" ? 1 : 2, clarificationAllowed: fault is not "no-opt-in");
        create.Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData("not-question")]
    [InlineData("not-answer")]
    public void ClarificationHistoryRejectsOtherwiseValidMessagesInEitherQuestionAnswerPosition(string fault)
    {
        var provenance = Provenance();
        var original = Envelope(provenance);
        var context = ModelToolProtocol.ReadContext(new ModelContextEnvelope(provenance.Request, original.SystemPolicy,
            original.UserRequest, [], original.CreatedAt, original.ExpiresAt, ModelToolMode.None).Serialize(), provenance);
        var question = new ModelClarificationQuestion(new("Choose format", ["Compact", "Detailed"]));
        var answer = new ModelQuestionAnswerMessage(question, new(["option-0"]), RequestOrigin.LocalUi);
        var final = ModelToolProtocol.ParseAssistant("""{"kind":"answer","answer":"A genuine answer"}"""u8);
        ModelToolMessage[] history = fault is "not-question" ? [final, answer] : [question, final];
        var create = () => ModelToolProtocol.CreateRequest(provenance, null, context, history, 2, clarificationAllowed: true);
        create.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void MissingOptionLabelIsRejectedWithoutRepairOrPartialQuestion()
    {
        var create = () => LocalModelQuestionPolicy.CreateSpec(new("Choose format", [null!, "Detailed"]));
        create.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void AnswerSerializesOnlyOriginalUserDataAndChannelNeverQuestionOrSessionAuthority()
    {
        var question = new ModelClarificationQuestion(new("Which format?", ["Compact", "Detailed"]));
        var answer = new ModelQuestionAnswerMessage(question, new(["option-1"]), RequestOrigin.ActivatedVoice);
        using var json = JsonDocument.Parse(ModelToolProtocol.SerializeMessage(answer));
        json.RootElement.GetProperty("kind").GetString().Should().Be("clarificationAnswer");
        json.RootElement.GetProperty("channel").GetString().Should().Be("ActivatedVoice");
        json.RootElement.GetProperty("choices")[0].GetString().Should().Be("option-1");
        json.RootElement.GetProperty("text").ValueKind.Should().Be(JsonValueKind.Null);
        json.RootElement.EnumerateObject().Count().Should().Be(4);
        var forbidden = () => new ModelQuestionAnswerMessage(question, new(["option-0"]), RequestOrigin.HostSystem);
        forbidden.Should().Throw<InvalidDataException>();
        var multi = () => new ModelQuestionAnswerMessage(question, new(["option-0", "option-1"]), RequestOrigin.LocalUi);
        multi.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void ClarificationUsesExplicitVersionWithoutChangingTheDefaultFinalAnswerProtocol()
    {
        var provenance = Provenance();
        var original = Envelope(provenance);
        var context = ModelToolProtocol.ReadContext(new ModelContextEnvelope(provenance.Request, original.SystemPolicy,
            original.UserRequest, [], original.CreatedAt, original.ExpiresAt, ModelToolMode.None).Serialize(), provenance);
        var request = ModelToolProtocol.CreateRequest(provenance, null, context, [], 1, clarificationAllowed: true);
        using var wire = JsonDocument.Parse(ModelToolProtocol.SerializeRequest(request));
        wire.RootElement.GetProperty("schemaVersion").GetInt32().Should().Be(2);
        var defaultRequest = ModelToolProtocol.CreateRequest(provenance, null, context, [], 1);
        using var defaultWire = JsonDocument.Parse(ModelToolProtocol.SerializeRequest(defaultRequest));
        defaultWire.RootElement.GetProperty("schemaVersion").GetInt32().Should().Be(1);
    }
}
