using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using Kora.Core.Dependencies;
using Kora.Core.Skills;
using Kora.Core.Tools;

namespace Kora.Core.UnitTests.Skills;

public sealed class ObservationWorkflowDefinitionTests
{
    [Fact]
    public void CompleteSixIdDeclarationRoundTripsExactBytesAndNeverEnablesItself()
    {
        const string source = """
            {"steps":[
              {"tool":"capabilities.list","input":{"offset":0,"count":6}},
              {"tool":"capabilities.get","input":{"id":"readiness.get"}},
              {"tool":"application.get_version","input":{}},
              {"tool":"readiness.get","input":{}},
              {"tool":"runtime.list","input":{}},
              {"tool":"runtime.get_status","input":{"id":"local.inference"}}
            ]}
            """;
        var saved = OwnedDeclarativeSkillDocument.CreateObservationWorkflow("inspection", "1.0.0",
            "Inspection", "Literal description", source);
        saved.Tool.Should().Be(OwnedSkillToolSelection.ReadOnlyObservations);
        saved.IsAvailableForInvocation.Should().BeFalse();
        saved.InputTemplate.Should().BeNull();
        saved.Name.Should().Be("Inspection");
        saved.Description.Should().Be("Literal description");
        var reopened = OwnedDeclarativeSkillDocument.Parse(saved.ManifestBytes.Span, saved.InstructionBytes.Span);
        reopened.Guidance.Should().Be(source);
        reopened.RevisionDigest.Should().Be(saved.RevisionDigest);
        reopened.ObservationWorkflow!.Steps.Select(step => step.Number).Should().Equal(1, 2, 3, 4, 5, 6);
        reopened.ObservationWorkflow.Steps.Select(step => step.Tool).Should().Equal(ReadOnlyCapabilityCatalog.Descriptors.Select(item => item.Id));
        reopened.ObservationWorkflow.ToolDeclaration.Should().Be("capabilities.list,capabilities.get,application.get_version,readiness.get,runtime.list,runtime.get_status");
        var changed = Encoding.UTF8.GetBytes(saved.ManifestText.Replace("tools: capabilities.list,", "tools: ", StringComparison.Ordinal));
        var parse = () => OwnedDeclarativeSkillDocument.Parse(changed, saved.InstructionBytes.Span);
        parse.Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("{\"steps\":[]}")]
    [InlineData("{\"steps\":[null]}")]
    [InlineData("{\"steps\":1}")]
    [InlineData("{\"steps\":[],\"steps\":[]}")]
    [InlineData("{\"steps\":[{\"tool\":\"application.get_version\",\"input\":{},\"approve\":true}]}")]
    [InlineData("{\"steps\":[{\"tool\":\"application.get_version\",\"input\":{},\"input\":{}}]}")]
    [InlineData("{\"steps\":[{\"tool\":\"WebPageGet\",\"input\":{}}]}")]
    [InlineData("{\"steps\":[{\"tool\":\"sessions.list\",\"input\":{}}]}")]
    [InlineData("{\"steps\":[{\"tool\":\"settings.get\",\"input\":{}}]}")]
    [InlineData("{\"steps\":[{\"tool\":\"application.get_version\",\"input\":{\"script\":\"run\"}}]}")]
    [InlineData("{\"steps\":[{\"tool\":\"capabilities.get\",\"input\":{\"id\":\"other\"}}]}")]
    [InlineData("{\"steps\":[{\"tool\":\"runtime.get_status\",\"input\":{\"id\":\"remote\"}}]}")]
    [InlineData("{\"steps\":[{\"tool\":\"readiness.get\",\"input\":{\"count\":7}}]}")]
    [InlineData("{\"steps\":[{\"tool\":1,\"input\":{}}]}")]
    [InlineData("{\"steps\":[{\"tool\":\"application.get_version\"}]}")]
    [InlineData("{\"steps\":[{\"tool\":\"application.get_version\",\"input\":{}}],\"loop\":true}")]
    [InlineData("not JSON")]
    public void UnknownExecutableOrIncompleteDeclarationsAreRejectedInFull(string source)
    {
        var parse = () => ObservationWorkflowDefinition.Parse(source);
        parse.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void ExactSourceInputAndStepBoundsRejectWithoutDroppingAnything()
    {
        const string step = "{\"tool\":\"application.get_version\",\"input\":{}}";
        var source = "{\"steps\":[" + step + "]}";
        ObservationWorkflowDefinition.Parse(source.PadRight(ObservationWorkflowDefinition.MaximumSourceBytes)).Steps.Should().ContainSingle();
        var overSource = () => ObservationWorkflowDefinition.Parse(source.PadRight(ObservationWorkflowDefinition.MaximumSourceBytes + 1));
        overSource.Should().Throw<InvalidDataException>();
        var overCount = () => ObservationWorkflowDefinition.Parse("{\"steps\":[" + string.Join(',', Enumerable.Repeat(step, 7)) + "]}");
        overCount.Should().Throw<InvalidDataException>();
        var input = "{" + new string(' ', 1022) + "}";
        var padded = "{\"tool\":\"application.get_version\",\"input\":" + input + "}";
        ObservationWorkflowDefinition.Parse("{\"steps\":[" + string.Join(',', Enumerable.Repeat(padded, 4)) + "]}").Steps.Should().HaveCount(4);
        var overInputs = () => ObservationWorkflowDefinition.Parse("{\"steps\":[" + string.Join(',', Enumerable.Repeat(padded, 5)) + "]}");
        overInputs.Should().Throw<InvalidDataException>().WithMessage("*input budget*");
        var perInput = () => ObservationWorkflowDefinition.Parse("{\"steps\":[{\"tool\":\"application.get_version\",\"input\":" + input.Insert(1, " ") + "}]}");
        perInput.Should().Throw<InvalidDataException>();
        var unicode = () => ObservationWorkflowDefinition.Parse("\ud800");
        unicode.Should().Throw<InvalidDataException>();
        ObservationWorkflowDefinition.MaximumDuration.Should().Be(TimeSpan.FromSeconds(15));
        ObservationWorkflowDefinition.MaximumResultBytes.Should().Be(8192);
        var repeated = ObservationWorkflowDefinition.Parse("{\"steps\":[" + step + "," + step + "]}");
        repeated.ToolDeclaration.Should().Be(ReadOnlyCapabilityCatalog.Version);
        repeated.Steps.Should().HaveCount(2, "repeated explicitly declared actions are not silently deduplicated");
    }

    [Fact]
    public void NativeObservationResultValidationUsesTheExistingCompleteSchemaAndWireLimits()
    {
        var good = new CapabilityReply(CapabilityOutcome.Succeeded, "observed", Version: new("1.0", "unknown"));
        ModelToolProtocol.ValidateObservationResult(ReadOnlyCapabilityCatalog.Version, "{}", good);
        foreach (var reply in new[]
        {
            good with { Outcome = CapabilityOutcome.Denied },
            good with { Reason = "" },
            good with { Version = new("", "unknown") },
            good with { Version = new(new string('x', 4096), "unknown") },
            good with { Version = null },
        })
        {
            var validate = () => ModelToolProtocol.ValidateObservationResult(ReadOnlyCapabilityCatalog.Version, "{}", reply);
            validate.Should().Throw<InvalidDataException>();
        }
        foreach (var pair in new[] { ("other", "{}"), (ReadOnlyCapabilityCatalog.Version, "{\"unknown\":1}") })
        {
            var validate = () => ModelToolProtocol.ValidateObservationResult(pair.Item1, pair.Item2, good);
            validate.Should().Throw<InvalidDataException>();
        }
        JsonSerializer.Serialize(good).Should().NotBeEmpty();
    }
}
