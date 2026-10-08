using AwesomeAssertions;
using Kora.Application.Configuration;
using Kora.Core.Configuration;

namespace Kora.Application.UnitTests.Configuration;

public sealed class AuditRetentionCommandTests
{
    [Theory]
    [InlineData("get logging.audit-retention-days", AppearanceCommandOperation.Get)]
    [InlineData("status logging.audit-retention-days", AppearanceCommandOperation.Get)]
    [InlineData("reset logging.audit-retention-days", AppearanceCommandOperation.Reset)]
    [InlineData("set logging.audit-retention-days to 365", AppearanceCommandOperation.Set)]
    public void Exact_current_name_grammar_native_typed_and_activated_contract(string text, AppearanceCommandOperation operation)
    {
        foreach (var prefix in new[] { "", "Nova ", "Nova, " })
        {
            AuditRetentionCommand.Parse(prefix + text, "Nova")!.Operation.Should().Be(operation);
        }
        var state = new AuditRetentionState(new(365), new(365), "saved", 7, null);
        state.Schema.Should().Be(1);
        state.Id.Should().Be(AuditRetentionCommand.OptionId);
        state.Type.Should().Be("integer-days");
        state.Minimum.Should().Be(30);
        state.Maximum.Should().Be(365);
        state.Default.Should().Be(90);
        state.Desired.Should().Be(new AuditRetentionDays(365));
        state.Scope.Should().Contain("required authority").And.Contain("projections");
        state.Available.Should().BeTrue();
        state.ApplyNowAvailable.Should().BeFalse();
        state.ApplicationTiming.Should().Contain("prior policy").And.Contain("newly committed");
        state.Confirmation.Should().Contain("independent audit session");
        state.ResetEffect.Should().Contain("90").And.Contain("unchanged");
        state.Excluded.Should().Contain("grant records").And.Contain("30 days/30 files");
        state.Syntax.Should().Contain("Apply-now unavailable");
        AuditRetentionState.Serialize(state, 9, "observed").Should().Contain("\"revision\":7");
        new AuditRetentionState(null, null, "unavailable", 8, "held").Available.Should().BeFalse();
        var oversized = state with { Recovery = new string('x', 65536) };
        var serialize = () => AuditRetentionState.Serialize(oversized, 9, "observed");
        serialize.Should().Throw<InvalidDataException>();
        AuditRetentionCommand.FixedPhrases.Should().HaveCount(3);
        DiagnosticRetentionState.Serialize(new(null, null, "unavailable", 3, "held"), 9, "observed", state)
            .Should().Contain("\"auditState\":").And.Contain(AuditRetentionCommand.OptionId);
    }

    [Theory]
    [InlineData("set logging.audit-retention-days to ")]
    [InlineData("reset logging.audit-retention-days now")]
    [InlineData("get logging.audit-retention-days\n")]
    [InlineData("get logging.audit-retention-days-other")]
    public void Reserved_malformed_audit_input_is_not_model_dispatch(string text) =>
        AuditRetentionCommand.Parse(text, "Kora")!.Operation.Should().Be(AppearanceCommandOperation.Clarify);

    [Fact]
    public void Other_registry_scopes_names_and_unbounded_input_do_not_gain_authority()
    {
        foreach (var text in new[] { "Kora", "Korax, get logging.audit-retention-days", "show history", "Nova, get logging.audit-retention-days",
            "list logging settings", "get logging.sqlite-diagnostic-retention-days" })
        {
            AuditRetentionCommand.Parse(text, "Kora").Should().BeNull();
        }
        AuditRetentionCommand.Parse("set logging.audit-retention-days" + new string('x', 1024), "Kora")!.Error.Should().Contain("1024");
    }
}
