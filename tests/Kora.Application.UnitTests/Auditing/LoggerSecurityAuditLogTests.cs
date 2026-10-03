using AwesomeAssertions;

using Kora.Application.Auditing;
using Kora.Core.Auditing;

using Microsoft.Extensions.Logging;

namespace Kora.Application.UnitTests.Auditing;

public sealed class LoggerSecurityAuditLogTests
{
    [Fact]
    public void Write_records_requested_events_as_structured_information()
    {
        var logger = new RecordingLogger<LoggerSecurityAuditLog>();
        var auditLog = new LoggerSecurityAuditLog(logger);
        var auditEvent = CreateEvent(SecurityAuditOutcome.Requested);

        auditLog.Write(auditEvent);

        var entry = logger.Entries.Should().ContainSingle().Subject;
        entry.Level.Should().Be(LogLevel.Information);
        entry.EventId.Id.Should().Be(150);
        entry.Properties["SecurityAudit"].Should().Be(true);
        entry.Properties["CorrelationId"].Should().Be(auditEvent.CorrelationId);
        entry.Properties["AuditCategory"].Should().Be(auditEvent.Category);
        entry.Properties["AuditActionId"].Should().Be(auditEvent.ActionId);
        entry.Properties["AuditOutcome"].Should().Be(auditEvent.Outcome);
        entry.Properties["AuditInitiator"].Should().Be(auditEvent.Initiator);
        entry.Properties["AuditTargetId"].Should().Be(auditEvent.TargetId);
        entry.Properties["ApprovalId"].Should().Be(auditEvent.ApprovalId);
        entry.Properties["ReasonCode"].Should().BeNull();
    }

    [Theory]
    [InlineData(SecurityAuditOutcome.Failed)]
    [InlineData(SecurityAuditOutcome.Denied)]
    public void Write_records_unsuccessful_events_as_structured_warnings(
        SecurityAuditOutcome outcome)
    {
        var logger = new RecordingLogger<LoggerSecurityAuditLog>();
        var auditLog = new LoggerSecurityAuditLog(logger);
        var auditEvent = CreateEvent(outcome, "policy-denied");

        auditLog.Write(auditEvent);

        var entry = logger.Entries.Should().ContainSingle().Subject;
        entry.Level.Should().Be(LogLevel.Warning);
        entry.EventId.Id.Should().Be(151);
        entry.Properties["AuditOutcome"].Should().Be(outcome);
        entry.Properties["ReasonCode"].Should().Be("policy-denied");
    }

    [Fact]
    public void Write_rejects_a_null_event()
    {
        var logger = new RecordingLogger<LoggerSecurityAuditLog>();
        var auditLog = new LoggerSecurityAuditLog(logger);

        var action = () => auditLog.Write(null!);

        action.Should().Throw<ArgumentNullException>()
            .WithParameterName("auditEvent");
    }

    private static SecurityAuditEvent CreateEvent(
        SecurityAuditOutcome outcome,
        string? reasonCode = null) =>
        new(
            Guid.NewGuid(),
            SecurityAuditCategory.SecurityApproval,
            "power.shutdown",
            outcome,
            SecurityAuditInitiator.TypedCommand,
            "machine.current",
            Guid.NewGuid(),
            reasonCode);

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull =>
            null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var properties = ((IEnumerable<KeyValuePair<string, object?>>)state!)
                .ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
            Entries.Add(new LogEntry(logLevel, eventId, properties));
        }
    }

    private sealed record LogEntry(
        LogLevel Level,
        EventId EventId,
        IReadOnlyDictionary<string, object?> Properties);
}
