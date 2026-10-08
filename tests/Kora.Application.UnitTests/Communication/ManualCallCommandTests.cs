using System.Text.Json;
using AwesomeAssertions;
using Kora.Application.Communication;
using Kora.Application.Configuration;
using Kora.Core.Communication;

namespace Kora.Application.UnitTests.Communication;

public sealed class ManualCallCommandTests
{
    [Theory]
    [InlineData("list call settings", AppearanceCommandOperation.List, null)]
    [InlineData("get call.manual-active", AppearanceCommandOperation.Get, null)]
    [InlineData("status call.manual-active", AppearanceCommandOperation.Get, null)]
    [InlineData("set call.manual-active to on", AppearanceCommandOperation.Set, true)]
    [InlineData("set call.manual-active to off", AppearanceCommandOperation.Set, false)]
    [InlineData("reset call.manual-active", AppearanceCommandOperation.Reset, false)]
    [InlineData(" Nova, SET CALL.MANUAL-ACTIVE TO ON ", AppearanceCommandOperation.Set, true)]
    [InlineData("Nova get call.manual-active", AppearanceCommandOperation.Get, null)]
    public void Exact_grammar_uses_current_prefix_and_host_boolean(string input, AppearanceCommandOperation operation, bool? active)
    {
        var command = ManualCallCommand.Parse(input, "Nova")!;
        command.Operation.Should().Be(operation);
        command.Active.Should().Be(active);
        command.Error.Should().BeNull();
        ManualCallCommand.FixedPhrases.Should().HaveCount(6);
    }

    [Theory]
    [InlineData("call active")]
    [InlineData("Novation get call.manual-active")]
    [InlineData("Nova")]
    [InlineData("Kora get call.manual-active")]
    [InlineData("speak once during call")]
    public void Unrelated_and_noncurrent_prefix_do_not_route(string input) =>
        ManualCallCommand.Parse(input, "Nova").Should().BeNull();

    [Theory]
    [InlineData("set call.manual-active to true")]
    [InlineData("set call.manual-active to on please")]
    [InlineData("get call.automatic")]
    [InlineData("list call")]
    [InlineData("reset call.manual-active now")]
    [InlineData("status call.manual-active\n")]
    [InlineData("Nova\tget call.manual-active")]
    public void Reserved_malformed_input_clarifies_without_fallthrough(string input)
    {
        ManualCallCommand.Parse(input, "Nova")!.Operation.Should().Be(AppearanceCommandOperation.Clarify);
        ManualCallCommand.Parse(input, "Nova")!.Error.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Input_byte_bound_and_complete_result_bound_are_enforced()
    {
        ManualCallCommand.Parse("set call.manual-active to " + new string('é', 513), "Kora")!.Error.Should().Contain("1024");
        ManualCallCommand.Parse("set call.manual-active to " + new string('a', 100), "Kora")!.Error.Should().Be(ManualCallCommand.Syntax);
        var observation = new CallPolicyObservation(7, CallState.Unknown, false, CallAwareSettings.Default);
        var result = new ManualCallCommandResult("observed", null, observation);
        using var json = JsonDocument.Parse(ManualCallCommandResult.Serialize(result));
        json.RootElement.GetProperty("observation").GetProperty("automaticState").GetString().Should().Be("Unknown");
        json.RootElement.GetProperty("observation").GetProperty("isProtected").GetBoolean().Should().BeTrue();
        json.RootElement.GetProperty("sourceRevision").GetInt64().Should().Be(7);
        result.Scope.Should().Contain("never persisted");
        var overflow = () => ManualCallCommandResult.Serialize(result with { Recovery = new string('x', 65536) });
        overflow.Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData(CallState.Unavailable, false)]
    [InlineData(CallState.Clear, true)]
    [InlineData((CallState)99, false)]
    public void Cached_availability_does_not_claim_automatic_clear(CallState state, bool available)
    {
        var result = new ManualCallCommandResult("observed", null, new(0, state, true, CallAwareSettings.Default));
        result.AutomaticDetectorAvailable.Should().Be(available);
        result.Observation.AutomaticState.Should().Be(state);
        result.AutomaticEvidence.Should().Contain("Unavailable never means Clear");
        ManualCallCommandResult.Serialize(result).Should().NotBeNullOrEmpty();
    }
}
