using AwesomeAssertions;

using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Interaction;

namespace Kora.Core.UnitTests.Dependencies;

public sealed partial class ModelToolProtocolTests
{
    [Theory]
    [InlineData("exact")]
    [InlineData("not-admitted")]
    [InlineData("read-only")]
    [InlineData("hosted")]
    [InlineData("no-call")]
    public void TypedFormHistoryCannotWidenProviderOrToolModeAdmission(string scenario)
    {
        var question = ModelToolProtocol.ParseAskUser(
            """{"prompt":"Prompt","fields":[{"label":"Audience","type":"text","maximumLength":40,"maximumUtf8Bytes":100}]}"""u8,
            "ask_1", formsAllowed: true);
        if (scenario is "no-call") { question = new(question.Question); }
        var original = Provenance();
        var provenance = scenario is "hosted" ? original with
        { Selection = new(ModelProviderIdentity.Claude, new(Guid.NewGuid()), new(1)) } : original;
        var envelope = new ModelContextEnvelope(provenance.Request, "Policy", "Original request", [],
            DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddMinutes(1),
            scenario is "hosted" ? ModelToolMode.HostedClarification : scenario is "read-only" ? ModelToolMode.ReadOnly : ModelToolMode.None);
        var context = ModelToolProtocol.ReadContext(envelope.Serialize(), provenance);
        var answer = new ModelQuestionAnswerMessage(question, new([], fields: [new("field-0", "Readers")]), RequestOrigin.LocalUi);
        var create = () => ModelToolProtocol.CreateRequest(provenance, null, context, [question, answer], 2,
            clarificationAllowed: scenario is not "not-admitted");
        if (scenario is "exact") { create().FormsAllowed.Should().BeTrue(); }
        else { create.Should().Throw<InvalidDataException>(); }
    }
}
