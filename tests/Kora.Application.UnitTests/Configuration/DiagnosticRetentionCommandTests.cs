using AwesomeAssertions;
using Kora.Application.Configuration;
using Kora.Core.Configuration;

namespace Kora.Application.UnitTests.Configuration;

public sealed class DiagnosticRetentionCommandTests
{
    [Theory]
    [InlineData("list logging settings", AppearanceCommandOperation.List)]
    [InlineData("get logging.sqlite-diagnostic-retention-days", AppearanceCommandOperation.Get)]
    [InlineData("status logging.sqlite-diagnostic-retention-days", AppearanceCommandOperation.Get)]
    [InlineData("reset logging.sqlite-diagnostic-retention-days", AppearanceCommandOperation.Reset)]
    [InlineData("set logging.sqlite-diagnostic-retention-days to 365", AppearanceCommandOperation.Set)]
    public void Exact_native_typed_activated_grammar_and_discovery_contract(string input, AppearanceCommandOperation operation)
    {
        foreach (var prefix in new[] { "", "Nova ", "Nova, " })
        {
            DiagnosticRetentionCommand.Parse(prefix + input, "Nova")!.Operation.Should().Be(operation);
        }
        var state = new DiagnosticRetentionState(DiagnosticRetentionDays.Default, new(1), "saved", 7, null);
        state.Schema.Should().Be(1);
        state.Id.Should().Be(DiagnosticRetentionCommand.OptionId);
        state.Type.Should().Be("integer-days");
        state.Minimum.Should().Be(1);
        state.Maximum.Should().Be(365);
        state.Default.Should().Be(30);
        state.Scope.Should().Contain("SQLite");
        state.Available.Should().BeTrue();
        state.ApplyNowAvailable.Should().BeFalse();
        state.ApplicationTiming.Should().Contain("newly committed");
        state.Confirmation.Should().Contain("independent diagnostic session");
        state.ResetEffect.Should().Contain("unchanged");
        state.Excluded.Should().Contain("90").And.Contain("grants");
        state.Syntax.Should().Contain("Apply-now unavailable");
        DiagnosticRetentionState.Serialize(state, 9, "observed").Should().Contain("\"revision\":7");
        var oversized = state with { Recovery = new string('x', 65536) };
        var serialize = () => DiagnosticRetentionState.Serialize(oversized, 9, "observed");
        serialize.Should().Throw<InvalidDataException>();
        DiagnosticRetentionCommand.FixedPhrases.Should().HaveCount(4);
    }

    [Theory]
    [InlineData("list logging")]
    [InlineData("set logging.sqlite-diagnostic-retention-days to ")]
    [InlineData("reset logging.sqlite-diagnostic-retention-days now")]
    [InlineData("set logging.audit-retention-days to 1")]
    [InlineData("get logging.\n")]
    public void Ambiguous_or_excluded_commands_require_clarification(string input) =>
        DiagnosticRetentionCommand.Parse(input, "Kora")!.Operation.Should().Be(AppearanceCommandOperation.Clarify);

    [Fact]
    public void Unrelated_and_oversized_inputs_never_gain_retention_mutation()
    {
        foreach (var input in new[] { "Kora", "Korax, list logging settings", "show history", "Nova, get logging.sqlite-diagnostic-retention-days" })
        {
            DiagnosticRetentionCommand.Parse(input, "Kora").Should().BeNull();
        }
        DiagnosticRetentionCommand.Parse("set logging." + new string('x', 1024), "Kora")!.Error.Should().Contain("1024");
    }
}
