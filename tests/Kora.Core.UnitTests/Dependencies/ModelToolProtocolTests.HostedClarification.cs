using System.Text.Json;
using AwesomeAssertions;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.Core.Providers;
using Kora.Core.Tools;

namespace Kora.Core.UnitTests.Dependencies;

public sealed partial class ModelToolProtocolTests
{
    [Theory]
    [InlineData(ModelProviderIdentity.Claude, ModelToolMode.HostedClarification, true)]
    [InlineData(ModelProviderIdentity.Claude, ModelToolMode.None, false)]
    [InlineData(ModelProviderIdentity.Claude, ModelToolMode.ReadOnly, false)]
    [InlineData(ModelProviderIdentity.Ollama, ModelToolMode.HostedClarification, false)]
    [InlineData(ModelProviderIdentity.Copilot, ModelToolMode.HostedClarification, false)]
    [InlineData(ModelProviderIdentity.Codex, ModelToolMode.HostedClarification, false)]
    public void HostedQuestionRequiresDedicatedModeAndExactSupportedProvider(ModelProviderIdentity provider, ModelToolMode mode, bool expected)
    {
        ModelToolProtocol.AllowsClarification(mode, provider).Should().Be(expected);
        ModelToolProtocol.GetTools(ModelToolMode.HostedClarification).Should().BeEmpty();
    }

    [Theory]
    [InlineData("exact")]
    [InlineData("no-opt-in")]
    [InlineData("no-profile")]
    [InlineData("no-call-id")]
    [InlineData("observation")]
    [InlineData("evidence")]
    [InlineData("foreign-provider")]
    public void HostedQuestionHistoryCannotBorrowSourcesToolsOrImplicitConsent(string fault)
    {
        var request = HostRequest.Create(RequestOrigin.LocalUi);
        var provenance = new ModelTurnProvenance(new(Guid.NewGuid()), request,
            new(fault is "foreign-provider" ? ModelProviderIdentity.Copilot : ModelProviderIdentity.Claude, new(Guid.NewGuid()), new(1)));
        var envelope = new ModelContextEnvelope(request, "question policy", "unchanged original", fault is "evidence"
            ? [new(new(Guid.NewGuid()), request, new(1), "not admitted", ModelEvidenceDisclosure.HostedEligible)] : [],
            DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddMinutes(2), ModelToolMode.HostedClarification);
        ModelToolRequest Create()
        {
            var context = ModelToolProtocol.ReadContext(envelope.Serialize(), provenance);
            var question = new ModelClarificationQuestion(new("Which format?", ["Compact", "Detailed"]),
                fault is "no-call-id" ? null : "question_1");
            var answer = new ModelQuestionAnswerMessage(question, new(["option-1"]), RequestOrigin.ActivatedVoice);
            ModelToolMessage[] history = fault is "observation"
                ? [Calls(Tool()), ModelToolProtocol.CreateToolResult(Calls(Tool()).Calls[0],
                    new(CapabilityOutcome.Succeeded, "observed", Version: new("1.0", "recorded")))]
                : [question, answer];
            ProviderProfileTuple? profile = fault is "no-profile" ? null
                : new(ProviderService.Claude, new("account"), new(1), new("model"), new("disabled"), new("8192"));
            return ModelToolProtocol.CreateRequest(provenance, profile, context, history, 2,
                clarificationAllowed: fault is not "no-opt-in");
        }
        if (fault is not "exact") { Assert.Throws<InvalidDataException>(Create); return; }
        var result = Create();
        result.Context.UserRequest.Should().Be("unchanged original");
        result.Context.Tools.Should().BeEmpty();
        result.Context.Evidence.Should().BeEmpty();
        using var wire = JsonDocument.Parse(ModelToolProtocol.SerializeRequest(result));
        wire.RootElement.GetProperty("schemaVersion").GetInt32().Should().Be(2);
        using var schema = JsonDocument.Parse(ModelToolProtocol.AskUserInputSchema);
        schema.RootElement.GetProperty("additionalProperties").GetBoolean().Should().BeFalse();
    }
}
