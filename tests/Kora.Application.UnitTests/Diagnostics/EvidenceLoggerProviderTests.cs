using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using AwesomeAssertions;
using Kora.Application.Auditing;
using Kora.Application.Diagnostics;
using Kora.Core.Auditing;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Microsoft.Extensions.Logging;

namespace Kora.Application.UnitTests.Diagnostics;

[Collection("Host tracing")]
public sealed class EvidenceLoggerProviderTests
{
    [Fact]
    public void Envelope_preserves_template_kinds_scopes_and_call_time_context_without_formatter()
    {
        var sink = new RecordingSink();
        using var provider = new EvidenceLoggerProvider([sink], sink);
        var logger = provider.CreateLogger("fixture");
        var request = HostRequest.Create(RequestOrigin.LocalUi);
        using var activity = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request);
        using var scope = logger.BeginScope(new Dictionary<string, object?>(StringComparer.Ordinal) { ["Count"] = 7 });
        var values = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["{OriginalFormat}"] = "Fixture {Count}.",
            ["Nothing"] = null,
            ["Enabled"] = true,
            ["Count"] = 42L,
            ["Ratio"] = 1.25,
            ["Name"] = "safe",
            ["Identifier"] = Guid.NewGuid(),
            ["When"] = DateTimeOffset.Parse("2026-10-05T10:00:00Z", CultureInfo.InvariantCulture),
            ["Password"] = "must-not-be-retained",
        };
        logger.Log(LogLevel.Information, new EventId(42, "fixture"), values, null,
            static (_, _) => throw new InvalidOperationException("Formatter must not run."));
        var row = sink.Diagnostics.Should().ContainSingle().Subject;
        row.Host.Should().Be(request);
        row.Trace!.TraceId.Should().Be(activity.Activity!.TraceId.ToHexString());
        row.Trace.SpanId.Should().Be(activity.Activity.SpanId.ToHexString());
        row.EventName.Should().Be("fixture");
        row.MessageTemplate.Should().Be("Fixture {Count}.");
        row.Properties["Nothing"].Kind.Should().Be(EvidenceValueKind.Null);
        row.Properties["Enabled"].Kind.Should().Be(EvidenceValueKind.Boolean);
        row.Properties["Count"].Kind.Should().Be(EvidenceValueKind.WholeNumber);
        row.Properties["Ratio"].CanonicalValue.Should().Be("1.25");
        row.Properties["Identifier"].Kind.Should().Be(EvidenceValueKind.Identifier);
        row.Properties["When"].Kind.Should().Be(EvidenceValueKind.Timestamp);
        row.Scopes.Should().ContainSingle().Which["Count"].CanonicalValue.Should().Be("7");
        var serialized = JsonSerializer.Serialize(row);
        serialized.Should().NotContain("must-not-be-retained");
        JsonSerializer.Deserialize<DiagnosticEnvelope>(serialized)!.Properties["Count"].Should().Be(row.Properties["Count"]);
        JsonSerializer.Deserialize<DiagnosticEnvelope>(serialized)!.Host.Should().Be(request);
        activity.Complete(HostOperationOutcome.Completed);
    }

    [Fact]
    public void Spoofed_security_marker_and_session_fields_are_only_diagnostic_properties()
    {
        var sink = new RecordingSink();
        using var provider = new EvidenceLoggerProvider([sink], sink);
        var logger = provider.CreateLogger("untrusted");
        var host = HostRequest.Create(RequestOrigin.LocalUi);
        using var activity = HostActivity.BeginRoot(host, HostActivityLayer.Application, HostOperation.Request);
        Log(logger, new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["{OriginalFormat}"] = "Security audit: {SecurityAudit}.",
            ["SecurityAudit"] = true,
            ["SessionId"] = Guid.NewGuid(),
            ["HostSessionId"] = Guid.NewGuid(),
        });
        sink.Audits.Should().BeEmpty();
        sink.Diagnostics.Should().ContainSingle().Which.Host.Should().Be(host);
        activity.Complete(HostOperationOutcome.Completed);
    }

    [Fact]
    public void Trusted_audit_has_separate_envelope_and_deferred_terminal_keeps_business_identity()
    {
        var sink = new RecordingSink();
        using var provider = new EvidenceLoggerProvider([sink], sink);
        using var factory = LoggerFactory.Create(builder => builder.AddProvider(provider));
        var bridge = new LoggerSecurityAuditLog(factory.CreateLogger<LoggerSecurityAuditLog>());
        var audit = CreateAudit();
        HostRequest original;
        using (var request = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi),
            HostActivityLayer.Application, HostOperation.Request))
        {
            original = request.Request;
            bridge.Write(audit);
            request.Complete(HostOperationOutcome.Completed);
        }
        using (var unrelated = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi),
            HostActivityLayer.Application, HostOperation.Request))
        {
            bridge.Write(audit.WithOutcome(SecurityAuditOutcome.Succeeded));
            unrelated.Complete(HostOperationOutcome.Completed);
        }
        var rows = sink.Audits.ToArray();
        rows.Should().HaveCount(2);
        rows.Should().OnlyContain(row => row.Diagnostic.Host == original);
        rows[0].Diagnostic.Trace!.TraceId.Should().NotBe(rows[1].Diagnostic.Trace!.TraceId);
        sink.Diagnostics.Should().BeEmpty();
        sink.Activities.Should().Contain(row => row.Host == original && row.Links.Count == 1);
    }

    [Fact]
    public void Missing_or_hostile_activity_context_cannot_commit_a_typed_audit()
    {
        var sink = new RecordingSink();
        using var provider = new EvidenceLoggerProvider([sink], sink);
        var logger = provider.CreateLogger("fixture");
        var auditState = new TrustedAuditState(CreateAudit());
        var action = () => logger.Log(LogLevel.Information, new EventId(150),
            auditState, null, static (_, _) => "unused");
        action.Should().Throw<InvalidOperationException>();
        using var activity = new Activity("hostile.session").SetIdFormat(ActivityIdFormat.W3C).Start();
        activity.SetTag("kora.session.id", Guid.NewGuid());
        action.Should().Throw<InvalidOperationException>();
        sink.Audits.Should().BeEmpty();
    }

    [Fact]
    public void Sink_failure_reports_gap_without_suppressing_the_other_sink_or_recursion()
    {
        var failed = new RecordingSink { Fail = true };
        var healthy = new RecordingSink();
        using var provider = new EvidenceLoggerProvider([failed, healthy], healthy);
        using var activity = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.HostSystem),
            HostActivityLayer.Application, HostOperation.Request);
        Log(provider.CreateLogger("fixture"), State());
        healthy.Diagnostics.Should().ContainSingle();
        healthy.Gaps.Should().ContainSingle().Which.Reason.Should().Be(EvidenceGapReason.SinkFailure);
        failed.Diagnostics.Should().BeEmpty();
    }

    [Fact]
    public void Storage_gate_is_visible_and_does_not_write_plaintext_replacement()
    {
        var file = new RecordingSink();
        using var provider = new EvidenceLoggerProvider([file, new UnavailableEvidenceSink()], file);
        using var activity = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.HostSystem),
            HostActivityLayer.Application, HostOperation.Request);
        Log(provider.CreateLogger("fixture"), State());
        file.Diagnostics.Should().ContainSingle();
        file.Gaps.Should().ContainSingle().Which.Reason.Should().Be(EvidenceGapReason.StorageNotAdmitted);
    }

    [Fact]
    public void Unsupported_and_oversized_properties_report_explicit_gaps_without_ToString()
    {
        var sink = new RecordingSink();
        using var provider = new EvidenceLoggerProvider([sink], sink);
        var logger = provider.CreateLogger("fixture");
        var state = State();
        state["Unexpected"] = new UnsafeValue();
        Log(logger, state);
        state["Unexpected"] = new string('x', 1025);
        Log(logger, state);
        sink.Diagnostics.Should().BeEmpty();
        sink.Gaps.Should().HaveCount(2);
        sink.Gaps.Should().OnlyContain(gap => gap.Reason == EvidenceGapReason.InvalidStructuredState);
    }

    [Fact]
    public async Task Nested_async_work_preserves_parentage_and_cross_session_isolation()
    {
        var sink = new RecordingSink();
        using var provider = new EvidenceLoggerProvider([sink], sink);
        var logger = provider.CreateLogger("fixture");
        var first = HostRequest.Create(RequestOrigin.LocalUi);
        var second = HostRequest.Create(RequestOrigin.ActivatedVoice);
        await Task.WhenAll(RecordAsync(first), RecordAsync(second));
        var rows = sink.Diagnostics.ToArray();
        rows.Should().HaveCount(2);
        rows.Select(row => row.Host!.SessionId).Should().BeEquivalentTo([first.SessionId, second.SessionId]);
        rows.Select(row => row.Trace!.TraceId).Distinct(StringComparer.Ordinal).Should().HaveCount(2);
        rows.Should().OnlyContain(row => row.Trace!.ParentSpanId != null);
        sink.Activities.Where(row => row.Host == first || row.Host == second).Should().HaveCount(4);

        async Task RecordAsync(HostRequest request)
        {
            using var parent = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request);
            await Task.Yield();
            using var child = HostActivity.BeginChild(HostActivityLayer.Windows, HostOperation.Storage);
            Log(logger, State());
            child.Complete(HostOperationOutcome.Completed);
            parent.Complete(HostOperationOutcome.Completed);
        }
    }

    [Fact]
    public async Task Deferred_work_is_a_new_root_with_link_and_suppressed_flow_does_not_invent_context()
    {
        var sink = new RecordingSink();
        using var provider = new EvidenceLoggerProvider([sink], sink);
        var request = HostRequest.Create(RequestOrigin.LocalUi);
        ActivityContext cause;
        using (var prior = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request))
        {
            cause = prior.Activity!.Context;
            prior.Complete(HostOperationOutcome.Completed);
        }
        Task queued;
        using (ExecutionContext.SuppressFlow())
        {
            queued = Task.Run(() =>
            {
                var missing = () => HostActivity.RequireCurrent();
                missing.Should().Throw<InvalidOperationException>();
                using var recovery = HostActivity.BeginRoot(request, HostActivityLayer.Application,
                    HostOperation.Recovery, [new ActivityLink(cause)]);
                recovery.Activity!.TraceId.Should().NotBe(cause.TraceId);
                recovery.Activity.ParentSpanId.Should().Be(default(ActivitySpanId));
                recovery.Activity.Baggage.Should().BeEmpty();
                recovery.Complete(HostOperationOutcome.Completed);
            }, TestContext.Current.CancellationToken);
        }
        await queued;
        sink.Activities.Where(row => row.Host == request).Should().HaveCount(2);
        sink.Activities.Should().Contain(row => row.Host == request && row.Links.Count == 1);
    }

    [Theory]
    [InlineData(HostOperationOutcome.Completed)]
    [InlineData(HostOperationOutcome.Failed)]
    [InlineData(HostOperationOutcome.Cancelled)]
    [InlineData(HostOperationOutcome.Unknown)]
    public void Completed_spans_retain_truthful_outcomes_and_no_sensitive_tags(HostOperationOutcome outcome)
    {
        var sink = new RecordingSink();
        using var provider = new EvidenceLoggerProvider([sink], sink);
        var request = HostRequest.Create(RequestOrigin.LocalUi);
        using (var activity = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request))
        {
            activity.Activity!.Baggage.Should().BeEmpty();
            activity.Activity.TagObjects.Should().OnlyContain(tag => tag.Key.StartsWith("kora.", StringComparison.Ordinal));
            activity.Complete(outcome);
        }
        sink.Activities.Should().Contain(row => row.Host == request && row.Outcome == outcome);
        var missing = () => HostActivity.RequireCurrent();
        missing.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void All_operation_and_layer_names_are_stable_and_invalid_values_restore_ambient_context()
    {
        var sink = new RecordingSink();
        using var provider = new EvidenceLoggerProvider([sink], sink);
        using var incoming = new Activity("hostile.parent").Start();
        incoming.AddBaggage("untrusted", "must-not-flow");
        var request = HostRequest.Create(RequestOrigin.HostSystem);
        foreach (var layer in Enum.GetValues<HostActivityLayer>())
        {
            foreach (var operation in Enum.GetValues<HostOperation>())
            {
                using var root = HostActivity.BeginRoot(request, layer, operation);
                root.Activity!.ParentSpanId.Should().Be(default(ActivitySpanId));
                root.Activity.TraceId.Should().NotBe(incoming.TraceId);
                root.Activity.Baggage.Should().BeEmpty();
                root.Activity.Source.Version.Should().Be(typeof(HostActivity).Assembly.GetName().Version!.ToString());
                root.Complete(HostOperationOutcome.Completed);
            }
        }
        var invalidLayer = () => HostActivity.BeginRoot(request, (HostActivityLayer)999, HostOperation.Request);
        invalidLayer.Should().Throw<ArgumentOutOfRangeException>();
        var invalidName = () => HostActivity.BeginRoot(request, HostActivityLayer.Core, (HostOperation)999);
        invalidName.Should().Throw<ArgumentOutOfRangeException>();
        ReferenceEquals(Activity.Current, incoming).Should().BeTrue();
    }

    [Fact]
    public void Unfinished_invalid_and_out_of_order_scopes_fail_without_fabricated_success()
    {
        var sink = new RecordingSink();
        using var provider = new EvidenceLoggerProvider([sink], sink);
        var request = HostRequest.Create(RequestOrigin.HostSystem);
        var parent = HostActivity.BeginRoot(request, HostActivityLayer.Core, HostOperation.Request);
        var child = HostActivity.BeginChild(HostActivityLayer.Application, HostOperation.Policy);
        var wrongOrder = () => parent.Dispose();
        wrongOrder.Should().Throw<InvalidOperationException>();
        var invalidOutcome = () => child.Complete((HostOperationOutcome)999);
        invalidOutcome.Should().Throw<ArgumentOutOfRangeException>();
        child.Dispose();
        child.Dispose();
        parent.Dispose();
        var expired = () => parent.Complete(HostOperationOutcome.Completed);
        expired.Should().Throw<ObjectDisposedException>();
        sink.Activities.Where(row => row.Host == request).Should().OnlyContain(row => row.Outcome == HostOperationOutcome.Unknown);
    }

    [Fact]
    public async Task Expired_execution_context_and_foreign_child_never_acquire_host_authority()
    {
        var sink = new RecordingSink();
        using var provider = new EvidenceLoggerProvider([sink], sink);
        var request = HostRequest.Create(RequestOrigin.HostSystem);
        Task queued;
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using (var host = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request))
        {
            queued = Task.Run(async () =>
            {
                await release.Task;
                HostActivity.Current.Should().BeNull();
                var expired = () => HostActivity.RequireCurrent();
                expired.Should().Throw<InvalidOperationException>();
            }, TestContext.Current.CancellationToken);
            using (var foreign = new Activity("foreign.child").Start())
            {
                HostActivity.Current.Should().BeNull();
                var missing = () => HostActivity.RequireCurrent();
                missing.Should().Throw<InvalidOperationException>();
                Log(provider.CreateLogger("fixture"), State());
            }
            host.Complete(HostOperationOutcome.Completed);
        }
        release.SetResult();
        await queued;
        sink.Gaps.Should().Contain(gap => gap.Reason == EvidenceGapReason.MissingHostContext);
    }

    [Fact]
    public void Unsampled_host_context_is_not_eligible_for_a_durable_commit()
    {
        var request = new HostRequest(new(Guid.NewGuid()), new(Guid.NewGuid()), new(Guid.NewGuid()),
            RequestOrigin.HostSystem, new HostId<InvocationIdentity>(Guid.NewGuid()));
        using var root = HostActivity.BeginRoot(request,
            HostActivityLayer.Application, HostOperation.Request);
        root.Activity.Should().BeNull();
        var missing = () => HostActivity.RequireCurrent();
        missing.Should().Throw<InvalidOperationException>();
        var audit = new SecurityAuditEvent(Guid.NewGuid(), SecurityAuditCategory.SecurityApproval,
            "fixture", SecurityAuditOutcome.Requested, SecurityAuditInitiator.LocalUser, "fixture", Guid.NewGuid());
        var bridge = new LoggerSecurityAuditLog(Microsoft.Extensions.Logging.Abstractions.NullLogger<LoggerSecurityAuditLog>.Instance);
        bridge.Write(audit);
        bridge.Write(audit.WithOutcome(SecurityAuditOutcome.Succeeded));
        using var auditScope = HostActivity.BeginAudit(root.Request, audit);
        auditScope.Complete(HostOperationOutcome.Completed);
    }

    [Fact]
    public void Invocation_and_approval_ids_are_host_stamped_not_taken_from_properties()
    {
        var sink = new RecordingSink();
        using var provider = new EvidenceLoggerProvider([sink], sink);
        var request = new HostRequest(new(Guid.NewGuid()), new(Guid.NewGuid()), new(Guid.NewGuid()),
            RequestOrigin.LocalUi, new HostId<InvocationIdentity>(Guid.NewGuid()));
        var audit = new SecurityAuditEvent(Guid.NewGuid(), SecurityAuditCategory.SecurityApproval,
            "fixture.approve", SecurityAuditOutcome.Requested, SecurityAuditInitiator.LocalUser,
            "fixture.target", Guid.NewGuid());
        using var root = HostActivity.BeginAudit(request, audit);
        using var child = HostActivity.BeginChild(HostActivityLayer.Core, HostOperation.Policy);
        Log(provider.CreateLogger("fixture"), State());
        var row = sink.Diagnostics.Should().ContainSingle().Which;
        row.Host!.InvocationId.Should().Be(request.InvocationId);
        row.ApprovalId.Should().Be(audit.ApprovalId);
        row.AuditCorrelationId.Should().Be(audit.CorrelationId);
        child.Complete(HostOperationOutcome.Completed);
        root.Complete(HostOperationOutcome.Completed);
    }

    [Fact]
    public void Logger_None_and_untrusted_source_spans_do_not_enter_evidence()
    {
        var sink = new RecordingSink();
        var clock = TimeProvider.System;
        using var provider = new EvidenceLoggerProvider([sink], sink, clock);
        var logger = provider.CreateLogger("fixture");
        var state = State();
        logger.IsEnabled(LogLevel.None).Should().BeFalse();
        logger.Log(LogLevel.None, new EventId(1), state, null, static (_, _) => "unused");
        using var foreign = new ActivitySource("Kora.Application");
        using (var span = foreign.StartActivity("lookalike"))
        {
            span.Should().NotBeNull();
        }
        sink.Diagnostics.Should().BeEmpty();
        sink.Activities.Should().BeEmpty();
    }

    [Fact]
    public void Both_unavailable_audit_and_span_projections_report_gaps_independently()
    {
        var file = new RecordingSink();
        using var provider = new EvidenceLoggerProvider([file, new UnavailableEvidenceSink()], file);
        using var factory = LoggerFactory.Create(builder => builder.AddProvider(provider));
        new LoggerSecurityAuditLog(factory.CreateLogger<LoggerSecurityAuditLog>()).Write(CreateAudit());
        file.Audits.Should().ContainSingle();
        file.Gaps.Should().Contain(gap => gap.Reason == EvidenceGapReason.StorageNotAdmitted);
    }

    [Fact]
    public void Scope_depth_and_exception_projection_are_explicit_and_bounded()
    {
        var sink = new RecordingSink();
        using var provider = new EvidenceLoggerProvider([sink], sink);
        var logger = provider.CreateLogger("fixture");
        var scopes = new Stack<IDisposable?>();
        try
        {
            for (var index = 0; index < 9; index++)
            {
                scopes.Push(logger.BeginScope(State()));
            }
            Log(logger, State());
        }
        finally
        {
            while (scopes.TryPop(out var scope))
            {
                scope?.Dispose();
            }
        }
        sink.Gaps.Should().ContainSingle().Which.Reason.Should().Be(EvidenceGapReason.InvalidStructuredState);
        using var root = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.HostSystem),
            HostActivityLayer.Application, HostOperation.Request);
        var exception = new IOException("private exception payload");
        logger.Log(LogLevel.Error, new EventId(1), State(), exception, static (_, _) => "unused");
        var row = sink.Diagnostics.Should().ContainSingle().Which;
        row.ExceptionType.Should().Be(typeof(IOException).FullName);
        JsonSerializer.Serialize(row).Should().NotContain("private exception payload");
        root.Complete(HostOperationOutcome.Completed);
    }

    [Fact]
    public void Pending_audit_capacity_fails_visibly_instead_of_evicting_request_context()
    {
        var bridge = new LoggerSecurityAuditLog(Microsoft.Extensions.Logging.Abstractions.NullLogger<LoggerSecurityAuditLog>.Instance);
        for (var index = 0; index < 1024; index++)
        {
            bridge.Write(CreateAudit());
        }
        var audit = CreateAudit();
        var overflow = () => bridge.Write(audit);
        overflow.Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData(HostOperationOutcome.Completed)]
    [InlineData(HostOperationOutcome.Failed)]
    [InlineData(HostOperationOutcome.Cancelled)]
    public async Task Request_runner_terminates_truthfully_and_propagates_failures(HostOperationOutcome outcome)
    {
        var sink = new RecordingSink();
        using var provider = new EvidenceLoggerProvider([sink], sink);
        HostRequest? request = null;
        var run = () => Kora.Application.Hosting.HostRequestRunner.RunAsync(RequestOrigin.LocalUi, () =>
        {
            request = HostActivity.RequireCurrent().Request;
            return outcome switch
            {
                HostOperationOutcome.Completed => Task.CompletedTask,
                HostOperationOutcome.Cancelled => Task.FromException(new OperationCanceledException()),
                _ => Task.FromException(new IOException("Synthetic routing failure.")),
            };
        });
        if (outcome == HostOperationOutcome.Completed)
        {
            await run();
        }
        else if (outcome == HostOperationOutcome.Cancelled)
        {
            await run.Should().ThrowAsync<OperationCanceledException>();
        }
        else
        {
            await run.Should().ThrowAsync<IOException>();
        }
        sink.Activities.Should().Contain(row => row.Host == request && row.Outcome == outcome);
        var missing = () => Kora.Application.Hosting.HostRequestRunner.RunAsync(RequestOrigin.LocalUi, null!);
        await missing.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public void Gap_reporter_failure_still_allows_both_independent_sink_attempts()
    {
        var failed = new RecordingSink { Fail = true };
        var healthy = new RecordingSink { FailReport = true };
        using var provider = new EvidenceLoggerProvider([failed, healthy], healthy);
        var log = () => Log(provider.CreateLogger("fixture"), State());
        log.Should().Throw<IOException>();
        healthy.Diagnostics.Should().ContainSingle();
    }

    private static SecurityAuditEvent CreateAudit() =>
        new(Guid.NewGuid(), SecurityAuditCategory.SecurityApproval, "fixture.approve",
            SecurityAuditOutcome.Requested, SecurityAuditInitiator.LocalUser, "fixture.target");

    private static Dictionary<string, object?> State() => new(StringComparer.Ordinal)
    {
        ["{OriginalFormat}"] = "Fixture {Count}.",
        ["Count"] = 1,
    };

    private static void Log(ILogger logger, Dictionary<string, object?> state) =>
        logger.Log(LogLevel.Information, new EventId(1), state, null, static (_, _) => "unused");

    private sealed class UnsafeValue
    {
        public override string ToString() => throw new InvalidOperationException("Unadmitted ToString.");
    }

    private sealed class RecordingSink : IEvidenceSink, IEvidenceGapReporter
    {
        public string Name => "fixture";
        public ConcurrentQueue<DiagnosticEnvelope> Diagnostics { get; } = new();
        public ConcurrentQueue<AuditEnvelope> Audits { get; } = new();
        public ConcurrentQueue<CompletedActivityEnvelope> Activities { get; } = new();
        public ConcurrentQueue<EvidenceGap> Gaps { get; } = new();
        public bool Fail { get; init; }
        public bool FailReport { get; init; }
        public void WriteDiagnostic(DiagnosticEnvelope envelope)
        {
            if (Fail)
            {
                throw new IOException("Synthetic failure.");
            }
            Diagnostics.Enqueue(envelope);
        }
        public void WriteAudit(AuditEnvelope envelope) => Audits.Enqueue(envelope);
        public void WriteActivity(CompletedActivityEnvelope envelope) => Activities.Enqueue(envelope);
        public void Report(EvidenceGap gap)
        {
            if (FailReport)
            {
                throw new IOException("Synthetic reporting failure.");
            }
            Gaps.Enqueue(gap);
        }
    }
}
