using AwesomeAssertions;
using Kora.Core.Dependencies;
using Kora.Core.Providers;
using Kora.Core.Tools;

namespace Kora.Core.UnitTests.Tools;

public sealed class ProviderSettingsToolsTests
{
    [Fact]
    public void ProviderModeRequiresTheExplicitOriginalRequestAndCannotRunHosted()
    {
        ProviderSettingsTools.IsExplicitRequest("ordinary question").Should().BeFalse();
        ProviderSettingsTools.IsExplicitRequest("ask about AI service settings: List models").Should().BeTrue();
        FluentActions.Invoking(() => ProviderSettingsTools.IsExplicitRequest("ask about AI service settings: "))
            .Should().Throw<InvalidDataException>();
        var request = Kora.Core.Hosting.HostRequest.Create(Kora.Core.Hosting.RequestOrigin.LocalUi);
        var provenance = new ModelTurnProvenance(new(Guid.NewGuid()), request, ModelProviderSelection.CopilotCandidate);
        var envelope = new ModelContextEnvelope(request, "policy", "question", [], DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch.AddMinutes(1), ModelToolMode.ProviderSettings);
        var context = ModelToolProtocol.ReadContext(envelope.Serialize(), provenance);
        FluentActions.Invoking(() => ModelToolProtocol.CreateRequest(provenance, null, context, [], 1)).Should().Throw<InvalidDataException>();
        ModelToolProtocol.GetTools(ModelToolMode.ReadOnly).Should().Equal(ReadOnlyCapabilityCatalog.Descriptors);
    }

    [Theory]
    [InlineData(ProviderSettingsTools.Models, """{"service":"Claude"}""")]
    [InlineData(ProviderSettingsTools.Model, """{"service":"Local","model":"exact/V2"}""")]
    [InlineData(ProviderSettingsTools.Refresh, """{"service":"Copilot"}""")]
    [InlineData(ProviderSettingsTools.Propose, """{"service":"Unset"}""")]
    [InlineData(ProviderSettingsTools.Propose, """{"service":"Claude","clearModel":true}""")]
    [InlineData(ProviderSettingsTools.Propose, """{"service":"Claude","clearModel":false}""")]
    [InlineData(ProviderSettingsTools.Propose, """{"service":"Claude","model":"exact","reasoning":"low","context":"8192","account":"opaque","revision":42}""")]
    public void StrictInputsExposeOnlyBoundedAppConfiguration(string id, string input)
    {
        var parsed = CapabilityInputPolicy.Read(ProviderSettingsTools.FindExact(id)!, input);
        parsed.DenialReason.Should().BeNull();
        parsed.ProviderSettings.Should().NotBeNull();
        parsed.ProviderSettings!.Count.Should().Be(6);
        ProviderSettingsTools.FindExact(id.ToUpperInvariant()).Should().BeNull();
        ProviderSettingsTools.Descriptors.Select(tool => tool.Id).Should().OnlyHaveUniqueItems();
        ProviderSettingsTools.Descriptors.Should().HaveCount(5);
    }

    [Theory]
    [InlineData(ProviderSettingsTools.Get, """{"service":"Claude"}""")]
    [InlineData(ProviderSettingsTools.Models, "{}")]
    [InlineData(ProviderSettingsTools.Models, """{"service":"claude"}""")]
    [InlineData(ProviderSettingsTools.Models, """{"service":"1"}""")]
    [InlineData(ProviderSettingsTools.Models, """{"service":"Other"}""")]
    [InlineData(ProviderSettingsTools.Models, """{"service":"999"}""")]
    [InlineData(ProviderSettingsTools.Models, """{"service":"Unset"}""")]
    [InlineData(ProviderSettingsTools.Models, """{"service":1}""")]
    [InlineData(ProviderSettingsTools.Models, """{"service":"Claude","offset":-1}""")]
    [InlineData(ProviderSettingsTools.Models, """{"service":"Claude","count":0}""")]
    [InlineData(ProviderSettingsTools.Models, """{"service":"Claude","count":7}""")]
    [InlineData(ProviderSettingsTools.Models, """{"service":"Claude","count":"1"}""")]
    [InlineData(ProviderSettingsTools.Models, """{"service":"Claude","offset":1.5}""")]
    [InlineData(ProviderSettingsTools.Models, """{"service":"Claude","service":"Local"}""")]
    [InlineData(ProviderSettingsTools.Model, """{"service":"Claude"}""")]
    [InlineData(ProviderSettingsTools.Model, """{"service":"Claude","model":1}""")]
    [InlineData(ProviderSettingsTools.Model, """{"service":"Claude","model":"two ids"}""")]
    [InlineData(ProviderSettingsTools.Refresh, """{"service":"Claude","credential":"not-admitted"}""")]
    [InlineData(ProviderSettingsTools.Refresh, """{"service":"Claude","sessionId":"foreign"}""")]
    [InlineData(ProviderSettingsTools.Propose, """{"service":"Claude","model":"exact"}""")]
    [InlineData(ProviderSettingsTools.Propose, """{"service":"Claude","model":"exact","reasoning":"low"}""")]
    [InlineData(ProviderSettingsTools.Propose, """{"service":"Claude","model":"exact","reasoning":"low","context":"8192"}""")]
    [InlineData(ProviderSettingsTools.Propose, """{"service":"Claude","reasoning":"low"}""")]
    [InlineData(ProviderSettingsTools.Propose, """{"service":"Claude","context":"8192"}""")]
    [InlineData(ProviderSettingsTools.Propose, """{"service":"Claude","account":"opaque"}""")]
    [InlineData(ProviderSettingsTools.Propose, """{"service":"Claude","revision":1}""")]
    [InlineData(ProviderSettingsTools.Propose, """{"service":"Claude","revision":"1"}""")]
    [InlineData(ProviderSettingsTools.Propose, """{"service":"Claude","revision":9223372036854775808}""")]
    [InlineData(ProviderSettingsTools.Propose, """{"service":"Claude","clearModel":"true"}""")]
    [InlineData(ProviderSettingsTools.Propose, """{"service":"Unset","clearModel":true}""")]
    [InlineData(ProviderSettingsTools.Propose, """{"service":"Unset","model":"exact","reasoning":"low","context":"8192","account":"opaque","revision":42}""")]
    [InlineData(ProviderSettingsTools.Propose, """{"service":"Claude","model":"exact","reasoning":"low","context":"8192","account":"opaque","revision":0}""")]
    [InlineData(ProviderSettingsTools.Propose, """{"service":"Claude","model":"exact","reasoning":"low","context":"8192","account":"opaque","revision":42,"clearModel":true}""")]
    public void UnknownPartialCrossSessionAndAuthorityInputsAreRejectedWhole(string id, string input) =>
        CapabilityInputPolicy.Read(ProviderSettingsTools.FindExact(id)!, input).DenialReason.Should().NotBeNull();

    [Fact]
    public void ProtocolAcceptsOnlyTheExactProviderResultAndNeverAcceptsItAsAnOriginalObservation()
    {
        var availability = new ProviderServiceAvailability(ProviderService.Claude, ProviderCatalogueStatus.Unavailable,
            null, new(1), DateTimeOffset.UnixEpoch.AddMinutes(1), 0);
        var input = """{"service":"Claude"}""";
        var call = new ModelToolCall("refresh_1", ProviderSettingsTools.Refresh, input);
        var result = new ProviderSettingsResult(Service: availability);
        var reply = new CapabilityReply(CapabilityOutcome.Succeeded, ProviderSettingsTools.ResultReason, ProviderSettings: result);
        ModelToolProtocol.CreateToolResult(call, reply).CapabilityId.Should().Be(ProviderSettingsTools.Refresh);
        result.PreferenceWritten.Should().BeFalse();
        result.RequiresOriginalUserConfirmation.Should().BeFalse();
        ProviderSettingsTools.Matches(reply, ProviderSettingsTools.Refresh, new(ProviderService.Local)).Should().BeFalse();
        ProviderSettingsTools.Matches(reply with { Reason = "invented" }, ProviderSettingsTools.Refresh, new(ProviderService.Claude)).Should().BeFalse();
        ProviderSettingsTools.Matches(reply with { Version = new("1", "unexpected") },
            ProviderSettingsTools.Refresh, new(ProviderService.Claude)).Should().BeFalse();
        FluentActions.Invoking(() => ModelToolProtocol.CreateToolResult(new("version", ReadOnlyCapabilityCatalog.Version, "{}"), reply))
            .Should().Throw<InvalidDataException>();
        FluentActions.Invoking(() => ModelToolProtocol.ValidateObservationResult(ProviderSettingsTools.Refresh, input, reply))
            .Should().Throw<InvalidDataException>();
        ModelToolProtocol.ParseAssistant(
            """{"kind":"toolCalls","calls":[{"callId":"refresh_1","capabilityId":"providers.refresh","input":{"service":"Claude"}}]}"""u8)
            .Should().BeOfType<ModelAssistantToolCalls>();
    }

    [Fact]
    public void ResultValidationRejectsMixedPayloadsWrongServiceBindingsAndInvalidPages()
    {
        var state = new ProviderServiceAvailability(ProviderService.Local, ProviderCatalogueStatus.Current,
            new("account"), new(1), DateTimeOffset.UnixEpoch.AddMinutes(1), 1);
        var tuple = new ProviderProfileTuple(ProviderService.Local, state.Account!, state.Revision,
            new("exact"), new("default"), new("default"));
        var model = new ProviderCatalogueModel(tuple.Model, "Exact", ModelProviderSelection.OllamaCandidate,
            [new(tuple.Reasoning, "Default", true)], [new(tuple.Context, "Default", true)]);
        ProviderSettingsResult[] results =
        [
            new(Settings: new(new(ProviderService.Local, [tuple]), true, tuple, LocalProviderInstallation.Installed,
                Enum.GetValues<ProviderService>().Where(service => service != ProviderService.Unset).Select(service => state with { Service = service }).ToArray())),
            new(Models: new(state, [new(tuple.Model, "Exact")], null)),
            new(Model: new(state, model)),
            new(Service: state),
            new(Proposal: new(ProviderService.Local, [tuple])),
        ];
        var input = new ProviderSettingsInput(ProviderService.Local, Model: "exact");
        static CapabilityReply Reply(ProviderSettingsResult result) => new(CapabilityOutcome.Succeeded,
            ProviderSettingsTools.ResultReason, ProviderSettings: result);
        for (var index = 0; index < results.Length; index++)
        {
            var id = ProviderSettingsTools.Descriptors[index].Id;
            ProviderSettingsTools.Matches(Reply(results[index]), id, input).Should().BeTrue();
            ProviderSettingsTools.Matches(Reply(results[(index + 1) % results.Length]), id, input).Should().BeFalse();
            if (id is not ProviderSettingsTools.Get)
            { ProviderSettingsTools.Matches(Reply(results[index]), id).Should().BeFalse(); }
        }
        ProviderSettingsTools.Matches(Reply(results[0]), "unknown", input).Should().BeFalse();
        ProviderSettingsTools.Matches(Reply(new()), ProviderSettingsTools.Get, input).Should().BeFalse();
        ProviderSettingsTools.Matches(new(CapabilityOutcome.Succeeded, ProviderSettingsTools.ResultReason),
            ProviderSettingsTools.Get, input).Should().BeFalse();
        ProviderSettingsTools.Matches(Reply(results[0] with { Service = state }), ProviderSettingsTools.Get, input).Should().BeFalse();

        foreach (var invalid in new[]
        {
            state with { Service = ProviderService.Unset }, state with { Status = (ProviderCatalogueStatus)99 },
            state with { Revision = default }, state with { ModelCount = -1 }, state with { ModelCount = 65 },
            state with { Account = null }, state with { Status = ProviderCatalogueStatus.Unavailable },
        })
        { ProviderSettingsTools.Matches(Reply(new(Service: invalid)), ProviderSettingsTools.Refresh, input).Should().BeFalse(); }
        var snapshot = results[0].Settings!;
        foreach (var services in new[] { snapshot.Services.Take(3).ToArray(),
            Enumerable.Repeat(state, 4).ToArray(), snapshot.Services.Select(service => service with { ModelCount = -1 }).ToArray() })
        { ProviderSettingsTools.Matches(Reply(new(Settings: snapshot with { Services = services })), ProviderSettingsTools.Get).Should().BeFalse(); }

        foreach (var page in new[]
        {
            results[1].Models! with { Service = state with { Revision = default } },
            results[1].Models! with { Service = state with { Service = ProviderService.Claude } },
            results[1].Models! with { Records = [] }, results[1].Models! with { NextOffset = 1 },
        })
        { ProviderSettingsTools.Matches(Reply(new(Models: page)), ProviderSettingsTools.Models, input).Should().BeFalse(); }
        ProviderSettingsTools.Matches(Reply(results[1]), ProviderSettingsTools.Models, input with { Offset = 2 }).Should().BeFalse();
        var six = Enumerable.Range(0, 6).Select(index => new ProviderModelSummary(new("model-" + index), "Exact")).ToArray();
        var more = new ProviderModelPage(state with { ModelCount = 7 }, six, 6);
        ProviderSettingsTools.Matches(Reply(new(Models: more)), ProviderSettingsTools.Models, input).Should().BeTrue();

        var details = results[2].Model!;
        foreach (var changed in new[]
        {
            details with { Service = state with { Revision = default } },
            details with { Service = state with { Status = ProviderCatalogueStatus.Unavailable, ModelCount = 0 } },
            details with { Service = state with { Service = ProviderService.Claude } },
            details with { Model = new(new("other"), model.Label, model.Selection, model.Reasoning, model.Context) },
            details with { Model = new(model.Id, model.Label, model.Selection with { Revision = new(2) }, model.Reasoning, model.Context) },
            details with { Model = new(model.Id, model.Label, model.Selection with { Provider = ModelProviderIdentity.Claude }, model.Reasoning, model.Context) },
        })
        { ProviderSettingsTools.Matches(Reply(new(Model: changed)), ProviderSettingsTools.Model, input).Should().BeFalse(); }
        ProviderSettingsTools.Matches(Reply(new(Proposal: new(ProviderService.Claude, []))), ProviderSettingsTools.Propose, input).Should().BeFalse();
        ProviderSettingsTools.Matches(Reply(new(Proposal: new(ProviderService.Local, []))), ProviderSettingsTools.Propose, input with { Tuple = tuple }).Should().BeFalse();
        ProviderSettingsTools.Matches(Reply(results[4]), ProviderSettingsTools.Propose, input with { Tuple = tuple }).Should().BeTrue();
        ProviderSettingsTools.Matches(Reply(results[4]), ProviderSettingsTools.Propose, input with { ClearModel = true }).Should().BeFalse();
        ProviderSettingsTools.Matches(Reply(new(Proposal: new(ProviderService.Local, []))), ProviderSettingsTools.Propose, input with { ClearModel = true }).Should().BeTrue();

        var original = Reply(new(Service: state));
        var request = Kora.Core.Hosting.HostRequest.Create(Kora.Core.Hosting.RequestOrigin.LocalUi);
        var address = new Uri("https://example.com");
        foreach (var mixed in new[]
        {
            original with { Capabilities = new([], 0, null) }, original with { Descriptor = ReadOnlyCapabilityCatalog.Descriptors[0] },
            original with { Version = new("1", "observed") }, original with { Readiness = new([], 0, null) },
            original with { Runtimes = new([], 0, null) },
            original with { Runtime = new("local.inference", RuntimeLocality.Local, CapabilityAvailability.Unavailable, null, "unavailable", null, false) },
            original with { WebPage = new(request, address, address, DateTimeOffset.UnixEpoch, "inert", false, "text/plain") },
            original with { Mcp = new("tool", new string('a', 64), DateTimeOffset.UnixEpoch, "inert", null) },
            original with { Sessions = new([], 0, null) }, original with { Customizations = new([], 0, null) },
            original with { Settings = new([], 0, null) },
        })
        { ProviderSettingsTools.Matches(mixed, ProviderSettingsTools.Refresh, input).Should().BeFalse(); }
        var metadata = new CustomizationMetadata(Guid.NewGuid().ToString("N"), Kora.Core.Customizations.CustomizationKind.Skill,
            "Fixture", "Read-only metadata", Kora.Core.Customizations.CustomizationScope.Kora, true, true, CapabilityAvailability.NotObserved);
        var clean = original with { Reason = CustomizationDiscoveryTools.ResultReason, ProviderSettings = null,
            Customizations = new([metadata], 1, null) };
        CustomizationDiscoveryTools.Matches(clean, CustomizationDiscoveryTools.List, new(null, Page: new(0, 1))).Should().BeTrue();
        CustomizationDiscoveryTools.Matches(clean with { ProviderSettings = original.ProviderSettings },
            CustomizationDiscoveryTools.List, new(null, Page: new(0, 1))).Should().BeFalse();
        CustomizationDiscoveryTools.Matches(clean with { Settings = new([], 0, null) },
            CustomizationDiscoveryTools.List, new(null, Page: new(0, 1))).Should().BeFalse();
        FluentActions.Invoking(() => ModelToolProtocol.CreateToolResult(new("p", ProviderSettingsTools.Refresh, "{}"), original)).Should().Throw<InvalidDataException>();
        FluentActions.Invoking(() => ModelToolProtocol.CreateToolResult(new("p", ProviderSettingsTools.Refresh, """{"service":"Local"}"""),
            original with { Reason = "invented" })).Should().Throw<InvalidDataException>();
        using var json = System.Text.Json.JsonDocument.Parse("{}");
        FluentActions.Invoking(() => ProviderSettingsTools.ReadInput("unknown", json.RootElement)).Should().Throw<InvalidDataException>();
    }
}
