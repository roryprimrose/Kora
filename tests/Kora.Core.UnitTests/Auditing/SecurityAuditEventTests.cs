using AwesomeAssertions;

using Kora.Core.Auditing;

namespace Kora.Core.UnitTests.Auditing;

public sealed class SecurityAuditEventTests
{
    [Fact]
    public void Constructor_preserves_content_minimizing_audit_metadata()
    {
        var correlationId = Guid.NewGuid();
        var approvalId = Guid.NewGuid();

        var auditEvent = new SecurityAuditEvent(
            correlationId,
            SecurityAuditCategory.ScriptExecution,
            "script.a0.-",
            SecurityAuditOutcome.Requested,
            SecurityAuditInitiator.VoiceCommand,
            "script.sha256.0123456789",
            approvalId,
            "approval-required");

        auditEvent.CorrelationId.Should().Be(correlationId);
        auditEvent.Category.Should().Be(SecurityAuditCategory.ScriptExecution);
        auditEvent.ActionId.Should().Be("script.a0.-");
        auditEvent.Outcome.Should().Be(SecurityAuditOutcome.Requested);
        auditEvent.Initiator.Should().Be(SecurityAuditInitiator.VoiceCommand);
        auditEvent.TargetId.Should().Be("script.sha256.0123456789");
        auditEvent.ApprovalId.Should().Be(approvalId);
        auditEvent.ReasonCode.Should().Be("approval-required");
    }

    [Fact]
    public void WithOutcome_preserves_identity_and_replaces_outcome_metadata()
    {
        var requested = CreateEvent();

        var completed = requested.WithOutcome(SecurityAuditOutcome.Denied, "policy-denied");

        completed.CorrelationId.Should().Be(requested.CorrelationId);
        completed.Category.Should().Be(requested.Category);
        completed.ActionId.Should().Be(requested.ActionId);
        completed.Initiator.Should().Be(requested.Initiator);
        completed.TargetId.Should().Be(requested.TargetId);
        completed.ApprovalId.Should().Be(requested.ApprovalId);
        completed.Outcome.Should().Be(SecurityAuditOutcome.Denied);
        completed.ReasonCode.Should().Be("policy-denied");
    }

    [Fact]
    public void Empty_correlation_identifier_is_rejected()
    {
        var action = () => new SecurityAuditEvent(
            Guid.Empty,
            SecurityAuditCategory.ProtectedOperation,
            "session.lock",
            SecurityAuditOutcome.Requested,
            SecurityAuditInitiator.LocalUser,
            "windows-session.current");

        action.Should().Throw<ArgumentException>()
            .WithParameterName("correlationId");
    }

    [Theory]
    [InlineData(100, 0, 0, "category")]
    [InlineData(0, 100, 0, "outcome")]
    [InlineData(0, 0, 100, "initiator")]
    public void Invalid_enum_values_are_rejected(
        int category,
        int outcome,
        int initiator,
        string parameterName)
    {
        var action = () => new SecurityAuditEvent(
            Guid.NewGuid(),
            (SecurityAuditCategory)category,
            "session.lock",
            (SecurityAuditOutcome)outcome,
            (SecurityAuditInitiator)initiator,
            "windows-session.current");

        action.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName(parameterName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("Session.Lock")]
    [InlineData("session_lock")]
    public void Invalid_action_identifiers_are_rejected(string? actionId)
    {
        var action = () => new SecurityAuditEvent(
            Guid.NewGuid(),
            SecurityAuditCategory.ProtectedOperation,
            actionId!,
            SecurityAuditOutcome.Requested,
            SecurityAuditInitiator.LocalUser,
            "windows-session.current");

        action.Should().Throw<ArgumentException>()
            .WithParameterName(nameof(actionId));
    }

    [Fact]
    public void Overlong_identifiers_are_rejected()
    {
        var action = () => new SecurityAuditEvent(
            Guid.NewGuid(),
            SecurityAuditCategory.ProtectedOperation,
            new string('a', 129),
            SecurityAuditOutcome.Requested,
            SecurityAuditInitiator.LocalUser,
            "windows-session.current");

        action.Should().Throw<ArgumentException>()
            .WithParameterName("actionId");
    }

    [Fact]
    public void Invalid_target_identifier_is_rejected()
    {
        var action = () => new SecurityAuditEvent(
            Guid.NewGuid(),
            SecurityAuditCategory.ProtectedOperation,
            "session.lock",
            SecurityAuditOutcome.Requested,
            SecurityAuditInitiator.LocalUser,
            "C:\\Windows");

        action.Should().Throw<ArgumentException>()
            .WithParameterName("targetId");
    }

    [Fact]
    public void Invalid_reason_code_is_rejected()
    {
        var action = () => CreateEvent().WithOutcome(
            SecurityAuditOutcome.Failed,
            "failure contains raw details");

        action.Should().Throw<ArgumentException>()
            .WithParameterName("reasonCode");
    }

    private static SecurityAuditEvent CreateEvent() =>
        new(
            Guid.NewGuid(),
            SecurityAuditCategory.ApplicationExecution,
            "application.restart",
            SecurityAuditOutcome.Requested,
            SecurityAuditInitiator.TypedCommand,
            "application.current",
            Guid.NewGuid());
}
