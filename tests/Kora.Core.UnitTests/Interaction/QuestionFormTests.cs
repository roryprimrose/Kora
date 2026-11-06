using System.Text;
using System.Text.Json;

using AwesomeAssertions;

using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Interaction;

namespace Kora.Core.UnitTests.Interaction;

public sealed class QuestionFormTests
{
    private const string Form = """
        {"prompt":"Describe the answer","fields":[
          {"label":"Audience","type":"text","maximumLength":20,"maximumUtf8Bytes":24},
          {"label":"Style","type":"singleChoice","options":["Compact","Detailed"]}
        ]}
        """;

    private static ModelClarificationQuestion Question() =>
        ModelToolProtocol.ParseAskUser(Encoding.UTF8.GetBytes(Form), "ask_1", formsAllowed: true);

    [Fact]
    public void TypedFormRoundTripsCanonicalFieldOrderAndLiteralUserDataWithoutAuthority()
    {
        var question = Question();
        var spec = LocalModelQuestionPolicy.CreateSpec(question.Question);
        spec.Kind.Should().Be(QuestionKind.Form);
        spec.Fields.Select(field => field.Id).Should().Equal("field-0", "field-1");
        spec.Fields[0].Label.Should().Be("Audience");
        spec.Fields[0].MaximumLength.Should().Be(20);
        spec.Fields[0].MaximumUtf8Bytes.Should().Be(24);
        spec.Fields[1].Options.Select(option => option.Id).Should().Equal("option-0", "option-1");
        spec.Accepts(new([]), submitting: false).Should().BeTrue();
        spec.Accepts(new([]), submitting: true).Should().BeFalse();
        var answer = new QuestionAnswer([], fields: [new("field-1", Choice: "option-1"), new("field-0", "${literal}")]);
        var accepted = new ModelQuestionAnswerMessage(question, answer, RequestOrigin.LocalUi);
        accepted.Answer.Fields.Select(field => field.FieldId).Should().Equal("field-0", "field-1");
        using var wire = JsonDocument.Parse(ModelToolProtocol.SerializeMessage(accepted));
        wire.RootElement.GetProperty("fields")[0].GetProperty("text").GetString().Should().Be("${literal}");
        wire.RootElement.GetProperty("fields")[1].GetProperty("choice").GetString().Should().Be("option-1");
        var roundTrip = (ModelClarificationQuestion)ModelToolProtocol.ParseAssistant(
            ModelToolProtocol.SerializeMessage(question), clarificationAllowed: true, formsAllowed: true);
        spec.Matches(LocalModelQuestionPolicy.CreateSpec(roundTrip.Question)).Should().BeTrue();
        var arguments = ModelToolProtocol.SerializeQuestionArguments(question.Question);
        spec.Matches(LocalModelQuestionPolicy.CreateSpec(
            ModelToolProtocol.ParseAskUser(arguments, "ask_1", formsAllowed: true).Question)).Should().BeTrue();
        using var schema = JsonDocument.Parse(ModelToolProtocol.FormAskUserInputSchema);
        schema.RootElement.GetProperty("oneOf").GetArrayLength().Should().Be(2);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("unknown")]
    [InlineData("duplicate")]
    [InlineData("blank")]
    [InlineData("option")]
    [InlineData("mixed-text")]
    [InlineData("text-choice")]
    [InlineData("choice-text")]
    [InlineData("length")]
    [InlineData("bytes")]
    [InlineData("unicode")]
    [InlineData("control")]
    [InlineData("credential")]
    public void WholeAnswerRejectsInvalidOrPartialFieldsWithoutRepair(string fault)
    {
        var values = new List<QuestionFieldAnswer> { new("field-0", "Readers"), new("field-1", Choice: "option-1") };
        switch (fault)
        {
            case "missing": values.RemoveAt(0); break;
            case "unknown": values[0] = values[0] with { FieldId = "foreign" }; break;
            case "duplicate": values[1] = values[0]; break;
            case "blank": values[0] = values[0] with { Text = " " }; break;
            case "option": values[1] = values[1] with { Choice = "foreign" }; break;
            case "text-choice": values[0] = values[0] with { Choice = "option-1" }; break;
            case "choice-text": values[1] = values[1] with { Text = "text" }; break;
            case "length": values[0] = values[0] with { Text = new('a', 21) }; break;
            case "bytes": values[0] = values[0] with { Text = new('\u754c', 9) }; break;
            case "unicode": values[0] = values[0] with { Text = "\ud800" }; break;
            case "control": values[0] = values[0] with { Text = "a\0b" }; break;
            case "credential": values[0] = values[0] with { Text = "api_key=synthetic" }; break;
        }
        var answer = new QuestionAnswer([], fault is "mixed-text" ? "extra" : null, values);
        var spec = LocalModelQuestionPolicy.CreateSpec(Question().Question);
        spec.Accepts(answer, submitting: true).Should().BeFalse();
        var create = () => new ModelQuestionAnswerMessage(Question(), answer, RequestOrigin.LocalUi);
        create.Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData("unknown-type")]
    [InlineData("numeric-type")]
    [InlineData("unknown-property")]
    [InlineData("duplicate-property")]
    [InlineData("zero-fields")]
    [InlineData("non-array")]
    [InlineData("too-many")]
    [InlineData("length-zero")]
    [InlineData("length-large")]
    [InlineData("bytes-zero")]
    [InlineData("bytes-large")]
    [InlineData("float")]
    [InlineData("number-string")]
    [InlineData("one-option")]
    [InlineData("many-options")]
    [InlineData("option-number")]
    [InlineData("ambiguous-options")]
    [InlineData("duplicate-labels")]
    [InlineData("mixed-shape")]
    public void MalformedFormSchemasFailClosed(string fault)
    {
        var wire = fault switch
        {
            "unknown-type" => Form.Replace("\"text\"", "\"secure\"", StringComparison.Ordinal),
            "numeric-type" => Form.Replace("\"text\"", "2", StringComparison.Ordinal),
            "unknown-property" => Form.Replace("\"type\":\"text\"", "\"type\":\"text\",\"script\":\"run\"", StringComparison.Ordinal),
            "duplicate-property" => Form.Replace("\"type\":\"text\"", "\"type\":\"text\",\"type\":\"text\"", StringComparison.Ordinal),
            "zero-fields" => """{"prompt":"Choose","fields":[]}""",
            "non-array" => """{"prompt":"Choose","fields":{}}""",
            "too-many" => """{"prompt":"Choose","fields":[""" + string.Join(',', Enumerable.Repeat(
                """{"label":"Text","type":"text","maximumLength":20,"maximumUtf8Bytes":24}""", 5)) + "]}",
            "length-zero" => Form.Replace("\"maximumLength\":20", "\"maximumLength\":0", StringComparison.Ordinal),
            "length-large" => Form.Replace("\"maximumLength\":20", "\"maximumLength\":501", StringComparison.Ordinal),
            "bytes-zero" => Form.Replace("\"maximumUtf8Bytes\":24", "\"maximumUtf8Bytes\":0", StringComparison.Ordinal),
            "bytes-large" => Form.Replace("\"maximumUtf8Bytes\":24", "\"maximumUtf8Bytes\":2001", StringComparison.Ordinal),
            "float" => Form.Replace("\"maximumLength\":20", "\"maximumLength\":1.5", StringComparison.Ordinal),
            "number-string" => Form.Replace("\"maximumLength\":20", "\"maximumLength\":\"20\"", StringComparison.Ordinal),
            "one-option" => Form.Replace("[\"Compact\",\"Detailed\"]", "[\"Compact\"]", StringComparison.Ordinal),
            "many-options" => Form.Replace("[\"Compact\",\"Detailed\"]", "[\"A\",\"B\",\"C\",\"D\",\"E\"]", StringComparison.Ordinal),
            "option-number" => Form.Replace("[\"Compact\",\"Detailed\"]", "[1,2]", StringComparison.Ordinal),
            "ambiguous-options" => Form.Replace("\"Detailed\"", "\"option one\"", StringComparison.Ordinal),
            "duplicate-labels" => Form.Replace("\"Style\"", "\"Audience\"", StringComparison.Ordinal),
            _ => Form.Replace("\"fields\":", "\"options\":[],\"fields\":", StringComparison.Ordinal),
        };
        var parse = () => ModelToolProtocol.ParseAskUser(Encoding.UTF8.GetBytes(wire), "ask_1", formsAllowed: true);
        parse.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void FormAndItsFieldsCannotBecomeAnotherPurposeOrLegacyAnswerShape()
    {
        var spec = LocalModelQuestionPolicy.CreateSpec(Question().Question);
        var authorization = () => new QuestionSpec(spec.Text, QuestionKind.Form, [], purpose: "authorization", fields: spec.Fields);
        authorization.Should().Throw<ArgumentException>();
        var plugin = () => new QuestionSpec(spec.Text, QuestionKind.Form, [], sourceId: "plugin", fields: spec.Fields);
        plugin.Should().Throw<ArgumentException>();
        var legacy = () => new QuestionSpec(spec.Text, QuestionKind.Text, [], maximumTextLength: 20, fields: spec.Fields);
        legacy.Should().Throw<ArgumentException>();
        var unsupported = () => new QuestionField("field", "Field", (QuestionKind)99, []);
        unsupported.Should().Throw<ArgumentException>();
        spec.Fields[0].Accepts(new("foreign", "Text"), true).Should().BeFalse();
        spec.Fields[0].Accepts(new("field-0"), false).Should().BeFalse();
        spec.Fields[1].Accepts(new("field-1"), false).Should().BeTrue();
        spec.Fields[1].Accepts(new("field-1"), true).Should().BeFalse();
        var text = spec.Fields[0].VoiceSpec();
        text.Accepts(new([], "Text"), true).Should().BeTrue();
        text.Accepts(new([], "Text", [new("field-0", "Text")]), true).Should().BeFalse();
    }

    [Fact]
    public void FormParsingRequiresExplicitOptInAndProviderModeBoundaryRemainsNarrow()
    {
        var parse = () => ModelToolProtocol.ParseAskUser(Encoding.UTF8.GetBytes(Form), "ask_1");
        parse.Should().Throw<InvalidDataException>();
        var serialized = ModelToolProtocol.SerializeMessage(Question());
        var structured = () => ModelToolProtocol.ParseAssistant(serialized, clarificationAllowed: true);
        structured.Should().Throw<InvalidDataException>();
        ModelToolProtocol.AllowsForms(ModelToolMode.None, ModelProviderIdentity.Ollama).Should().BeTrue();
        ModelToolProtocol.AllowsForms(ModelToolMode.ReadOnly, ModelProviderIdentity.Ollama).Should().BeFalse();
        ModelToolProtocol.AllowsForms(ModelToolMode.HostedClarification, ModelProviderIdentity.Claude).Should().BeFalse();
        ModelToolProtocol.AllowsForms(ModelToolMode.None, ModelProviderIdentity.Claude).Should().BeFalse();
    }

    [Theory]
    [InlineData("label")]
    [InlineData("text-options")]
    [InlineData("choice-count-low")]
    [InlineData("choice-count-high")]
    [InlineData("choice-length")]
    [InlineData("choice-bytes")]
    [InlineData("option-label")]
    [InlineData("option-ids")]
    public void FieldDomainRejectsInvalidConstraintsIndependentlyOfWireParsing(string fault)
    {
        var choices = new[] { new QuestionOption("a", "Compact"), new("b", "Detailed") };
        var create = () => fault switch
        {
            "label" => new QuestionField("field", new('a', 81), QuestionKind.Text, [], 20, 24),
            "text-options" => new QuestionField("field", "Field", QuestionKind.Text, choices, 20, 24),
            "choice-count-low" => new QuestionField("field", "Field", QuestionKind.SingleChoice, choices.Take(1)),
            "choice-count-high" => new QuestionField("field", "Field", QuestionKind.SingleChoice,
                Enumerable.Range(0, 5).Select(index => new QuestionOption("option-" + index.ToString(System.Globalization.CultureInfo.InvariantCulture), "Choice"))),
            "choice-length" => new QuestionField("field", "Field", QuestionKind.SingleChoice, choices, 1),
            "choice-bytes" => new QuestionField("field", "Field", QuestionKind.SingleChoice, choices, 0, 1),
            "option-label" => new QuestionField("field", "Field", QuestionKind.SingleChoice, [new("a", new('a', 81)), choices[1]]),
            _ => new QuestionField("field", "Field", QuestionKind.SingleChoice, [choices[0], new("a", "Detailed")]),
        };
        create.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("options")]
    [InlineData("minimum")]
    [InlineData("maximum")]
    [InlineData("text-bound")]
    [InlineData("prompt")]
    [InlineData("no-fields")]
    [InlineData("many-fields")]
    [InlineData("duplicate-id")]
    public void WholeFormDomainRejectsUnsupportedBoundsAndDuplicateFields(string fault)
    {
        var fields = LocalModelQuestionPolicy.CreateSpec(Question().Question).Fields;
        var create = () => new QuestionSpec(fault is "prompt" ? new('a', 501) : "Prompt", QuestionKind.Form,
            fault is "options" ? [new("a", "A")] : [], minimum: fault is "minimum" ? 0 : 1,
            maximum: fault is "maximum" ? 2 : 1, maximumTextLength: fault is "text-bound" ? 20 : 0,
            fields: fault switch
            {
                "no-fields" => [],
                "many-fields" => Enumerable.Repeat(fields[0], 5),
                "duplicate-id" => [fields[0], new("field-0", "Other", QuestionKind.Text, [], 20, 24)],
                _ => fields,
            });
        create.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("zero")]
    [InlineData("many")]
    [InlineData("field-id")]
    [InlineData("choice-id")]
    [InlineData("mixed")]
    public void ModelPolicyCannotBorrowHostFieldIdentitiesOrMixShapes(string fault)
    {
        var fields = Question().Question.Fields!;
        var model = new LocalModelQuestion("Prompt", fault is "mixed" ? ["Compact", "Detailed"] : [], Fields: fault switch
        {
            "zero" => [],
            "many" => Enumerable.Repeat(fields[0], 5).ToArray(),
            "field-id" => [new("foreign", "Text", QuestionKind.Text, [], 20, 24)],
            "choice-id" => [new("field-0", "Choice", QuestionKind.SingleChoice, [new("a", "Compact"), new("b", "Detailed")])],
            _ => fields,
        });
        var create = () => LocalModelQuestionPolicy.CreateSpec(model);
        create.Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LegacyQuestionArgumentsStillSerializeTheirExactOriginalShape(bool text)
    {
        var model = new LocalModelQuestion("Prompt", text ? [] : ["Compact", "Detailed"]);
        using var wire = JsonDocument.Parse(ModelToolProtocol.SerializeQuestionArguments(model));
        wire.RootElement.GetProperty("options").GetArrayLength().Should().Be(text ? 0 : 2);
        wire.RootElement.GetProperty("minimum").GetInt32().Should().Be(1);
        wire.RootElement.GetProperty("maximum").GetInt32().Should().Be(1);
        var host = new QuestionSpec("Host clarification", QuestionKind.Form, [], fields: Question().Question.Fields);
        host.Purpose.Should().Be("clarification");
    }
}
