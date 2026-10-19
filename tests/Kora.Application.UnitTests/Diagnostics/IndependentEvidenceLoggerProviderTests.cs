using System.Collections.Concurrent;

using AwesomeAssertions;

using Kora.Application.Auditing;
using Kora.Application.Diagnostics;
using Kora.Core.Auditing;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Kora.Application.UnitTests.Diagnostics;

[Collection("Host tracing")]
public sealed partial class IndependentEvidenceLoggerProviderTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Projection_saturation_retains_every_required_copy_and_exact_gap_without_poisoning_admission(bool activities)
    {
        using var copy = new Sink("copy");
        using var projection = new Sink("projection", blocked: true);
        await using var provider = Create(copy, projection);
        var logger = provider.CreateLogger("fixture");
        try
        {
            for (var index = 0; index < 1100; index++)
            {
                if (activities) { CompleteActivity(); }
                else { Log(logger, index); }
                if (index == 0) { await projection.Started.Task.WaitAsync(Deadline, TestContext.Current.CancellationToken); }
                await copy.NextDeliveryAsync();
            }
            var flushing = provider.FlushAsync(TestContext.Current.CancellationToken);
            flushing.IsCompleted.Should().BeFalse("the projection lane is still blocked and its barrier must wait asynchronously");
            projection.Release.Set();
            await flushing.WaitAsync(Deadline, TestContext.Current.CancellationToken);

            var copied = copy.Identities();
            var projected = projection.Identities();
            var gaps = copy.Gaps.Where(gap => gap.Reason == EvidenceGapReason.ProjectionBackpressure).ToArray();
            copied.Should().HaveCount(1100).And.OnlyHaveUniqueItems();
            projected.Should().HaveCount(1025).And.OnlyHaveUniqueItems();
            gaps.Should().HaveCount(75).And.OnlyContain(gap => gap.Sink == "projection" && gap.ExceptionType == null);
            gaps.Select(gap => gap.EvidenceId!.Value).Should().BeEquivalentTo(copied.Except(projected));
            copy.PersistenceHadHostContext.Should().BeFalse();
            projection.PersistenceHadHostContext.Should().BeFalse();

            Log(logger, 1101);
            await provider.FlushAsync(TestContext.Current.CancellationToken);
            copy.Diagnostics.Should().Contain(row => row.Properties["Sequence"].CanonicalValue == "1101");
            projection.Diagnostics.Should().Contain(row => row.Properties["Sequence"].CanonicalValue == "1101");
        }
        finally { projection.Release.Set(); }
    }

    [Fact]
    public async Task Separate_lanes_preserve_emission_identity_scopes_and_links_without_ambient_authority()
    {
        using var copy = new Sink("copy");
        using var projection = new Sink("projection", blocked: true);
        await using var provider = Create(copy, projection);
        var logger = provider.CreateLogger("fixture");
        var request = HostRequest.Create(RequestOrigin.LocalUi);
        string trace;
        string span;
        try
        {
            using (var host = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request))
            {
                trace = host.Activity!.TraceId.ToHexString();
                span = host.Activity.SpanId.ToHexString();
                using var scope = logger.BeginScope(new Dictionary<string, object?>(StringComparer.Ordinal) { ["Sequence"] = 7 });
                var state = State(1);
                logger.Log(LogLevel.Information, new EventId(1), state, null, static (_, _) => "unused");
                await projection.Started.Task.WaitAsync(Deadline, TestContext.Current.CancellationToken);
                await copy.NextDeliveryAsync();
                state["Sequence"] = 99;
                copy.Diagnostics.Should().ContainSingle();
                projection.Diagnostics.Should().BeEmpty();
                var continuation = HostActivity.CaptureContinuation(HostActivityLayer.Desktop, HostOperation.Presentation);
                using (var child = continuation()) { child.Complete(HostOperationOutcome.Completed); }
                host.Complete(HostOperationOutcome.Completed);
            }
            projection.Release.Set();
            await provider.FlushAsync(TestContext.Current.CancellationToken);
            foreach (var sink in new[] { copy, projection })
            {
                var row = sink.Diagnostics.Should().ContainSingle().Subject;
                row.Host.Should().BeSameAs(request);
                row.Trace!.TraceId.Should().Be(trace);
                row.Trace.SpanId.Should().Be(span);
                row.Properties["Sequence"].CanonicalValue.Should().Be("1");
                row.Scopes.Single()["Sequence"].CanonicalValue.Should().Be("7");
                sink.Activities.Should().HaveCount(2).And.OnlyContain(activity => activity.Host == request);
                sink.Activities.Should().Contain(activity => activity.Links.Count == 1
                    && activity.Links[0].TraceId == trace && activity.Links[0].SpanId == span);
                sink.PersistenceHadHostContext.Should().BeFalse();
            }
        }
        finally { projection.Release.Set(); }
    }

    [Fact]
    public async Task Required_audit_waits_for_both_sinks_while_file_diagnostics_remain_independent()
    {
        using var copy = new Sink("copy");
        using var projection = new Sink("projection", blocked: true);
        await using var provider = Create(copy, projection);
        using var factory = LoggerFactory.Create(builder => builder.AddProvider(provider));
        var audit = new SecurityAuditEvent(Guid.NewGuid(), SecurityAuditCategory.ConfigurationWrite,
            "fixture.save", SecurityAuditOutcome.Requested, SecurityAuditInitiator.LocalUser, "fixture.target");
        var logger = provider.CreateLogger("fixture");
        try
        {
            for (var index = 0; index <= 1024; index++)
            {
                Log(logger, index);
                if (index == 0) { await projection.Started.Task.WaitAsync(Deadline, TestContext.Current.CancellationToken); }
                await copy.NextDeliveryAsync();
            }
            var writing = new LoggerSecurityAuditLog(factory.CreateLogger<LoggerSecurityAuditLog>())
                .WriteAsync(audit, TestContext.Current.CancellationToken);
            await copy.AuditStarted.Task.WaitAsync(Deadline, TestContext.Current.CancellationToken);
            writing.IsCompleted.Should().BeFalse();
            projection.Audits.Should().BeEmpty();
            copy.Audits.Should().ContainSingle();
            Log(logger, 1025);
            await copy.NextDeliveryAsync();
            copy.Diagnostics.Should().HaveCount(1026);
            projection.Release.Set();
            await writing;
            projection.Audits.Should().ContainSingle().Which.Audit.Should().Be(audit);
        }
        finally { projection.Release.Set(); }
    }

    [Fact]
    public async Task Required_copy_saturation_still_fails_closed_and_disposal_drains_accepted_records()
    {
        using var copy = new Sink("copy", blocked: true);
        using var projection = new Sink("projection");
        var provider = Create(copy, projection);
        var logger = provider.CreateLogger("fixture");
        try
        {
            Log(logger, 0);
            await copy.Started.Task.WaitAsync(Deadline, TestContext.Current.CancellationToken);
            for (var index = 1; index <= 1024; index++) { Log(logger, index); }
            var overflow = () => Log(logger, 1025);
            overflow.Should().Throw<IOException>().WithMessage("*buffer is full*");
        }
        finally { copy.Release.Set(); }
        var dispose = async () => await provider.DisposeAsync();
        await dispose.Should().ThrowAsync<IOException>();
        copy.Diagnostics.Should().HaveCount(1025);
    }

    [Fact]
    public async Task Cancelling_a_flush_waiting_for_capacity_does_not_poison_projection_admission()
    {
        using var copy = new Sink("copy");
        using var projection = new Sink("projection", blocked: true);
        await using var provider = Create(copy, projection);
        var logger = provider.CreateLogger("fixture");
        try
        {
            for (var index = 0; index <= 1024; index++)
            {
                Log(logger, index);
                if (index == 0) { await projection.Started.Task.WaitAsync(Deadline, TestContext.Current.CancellationToken); }
                await copy.NextDeliveryAsync();
            }
            using var cancellation = new CancellationTokenSource();
            var flushing = provider.FlushAsync(cancellation.Token);
            cancellation.Cancel();
            var cancelled = () => flushing;
            await cancelled.Should().ThrowAsync<OperationCanceledException>();
            Log(logger, 1025);
            await copy.NextDeliveryAsync();
            projection.Release.Set();
            await provider.FlushAsync(TestContext.Current.CancellationToken);
            copy.Diagnostics.Should().HaveCount(1026);
            copy.Gaps.Should().ContainSingle(gap => gap.Reason == EvidenceGapReason.ProjectionBackpressure);
        }
        finally { projection.Release.Set(); }
    }

    [Fact]
    public async Task Real_sink_failures_close_admission_and_shutdown_preserves_both_lane_failures()
    {
        using var copy = new Sink("copy", blocked: true) { FailWrites = true };
        using var projection = new Sink("projection", blocked: true) { FailWrites = true };
        var provider = Create(copy, projection);
        var logger = provider.CreateLogger("fixture");
        try
        {
            Log(logger, 0);
            await Task.WhenAll(copy.Started.Task, projection.Started.Task).WaitAsync(Deadline, TestContext.Current.CancellationToken);
        }
        finally
        {
            copy.Release.Set();
            projection.Release.Set();
        }
        var flush = () => provider.FlushAsync(TestContext.Current.CancellationToken);
        await flush.Should().ThrowAsync<IOException>();
        var later = () => Log(logger, 1);
        later.Should().Throw<IOException>();
        var dispose = async () => await provider.DisposeAsync();
        var failure = await dispose.Should().ThrowAsync<IOException>();
        failure.Which.InnerException.Should().BeOfType<AggregateException>().Which.InnerExceptions.Should().HaveCount(2);
        failure.Which.ToString().Should().Contain("copy unavailable").And.Contain("projection unavailable");
    }

    [Fact]
    public async Task Failed_backpressure_gap_is_not_reported_as_successful_required_delivery()
    {
        using var copy = new Sink("copy");
        using var projection = new Sink("projection", blocked: true);
        var provider = Create(copy, projection);
        var logger = provider.CreateLogger("fixture");
        try
        {
            for (var index = 0; index <= 1024; index++)
            {
                Log(logger, index);
                if (index == 0) { await projection.Started.Task.WaitAsync(Deadline, TestContext.Current.CancellationToken); }
                await copy.NextDeliveryAsync();
            }
            copy.FailGaps = true;
            CompleteActivity();
            await copy.NextDeliveryAsync();
        }
        finally { projection.Release.Set(); }
        var dispose = async () => await provider.DisposeAsync();
        await dispose.Should().ThrowAsync<IOException>();
        copy.Activities.Should().ContainSingle();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Service_provider_owns_both_drains_including_partial_startup_and_reports_delivery_failure(
        bool partialStartup, bool fail)
    {
        using var copy = new Sink("copy", blocked: true) { FailWrites = fail };
        using var projection = new Sink("projection", blocked: true);
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.ClearProviders());
        services.AddSingleton<ILoggerProvider>(_ => Create(copy, projection));
        services.AddSingleton<UnavailableStartup>(_ => throw new InvalidOperationException("Synthetic startup failure."));
        var serviceProvider = services.BuildServiceProvider();
        Log(serviceProvider.GetRequiredService<ILogger<IndependentEvidenceLoggerProviderTests>>(), 0);
        try
        {
            await Task.WhenAll(copy.Started.Task, projection.Started.Task).WaitAsync(Deadline, TestContext.Current.CancellationToken);
            if (partialStartup)
            {
                var startup = () => serviceProvider.GetRequiredService<UnavailableStartup>();
                startup.Should().Throw<InvalidOperationException>();
            }
            var disposing = serviceProvider.DisposeAsync().AsTask();
            disposing.IsCompleted.Should().BeFalse();
            copy.Release.Set();
            disposing.IsCompleted.Should().BeFalse("the independent projection drain is still blocked");
            projection.Release.Set();
            var dispose = () => disposing;
            if (fail) { await dispose.Should().ThrowAsync<IOException>(); }
            else { await disposing.WaitAsync(Deadline, TestContext.Current.CancellationToken); }
            projection.Diagnostics.Should().ContainSingle();
            copy.Diagnostics.Should().HaveCount(fail ? 0 : 1);
        }
        finally
        {
            copy.Release.Set();
            projection.Release.Set();
        }
    }

    [Fact]
    public async Task Unexpected_worker_termination_unblocks_a_full_queue_flush_and_still_joins_the_required_copy()
    {
        using var copy = new Sink("copy");
        using var projection = new Sink("projection", blocked: true) { UnexpectedFailure = true };
        var provider = Create(copy, projection);
        try
        {
            var logger = provider.CreateLogger("fixture");
            for (var index = 0; index <= 1024; index++)
            {
                Log(logger, index);
                if (index == 0) { await projection.Started.Task.WaitAsync(Deadline, TestContext.Current.CancellationToken); }
                await copy.NextDeliveryAsync();
            }
            var flushing = provider.FlushAsync(TestContext.Current.CancellationToken);
            projection.Release.Set();
            var flush = () => flushing.WaitAsync(Deadline, TestContext.Current.CancellationToken);
            await flush.Should().ThrowAsync<IOException>();
        }
        finally { projection.Release.Set(); }
        var dispose = async () => await provider.DisposeAsync();
        await dispose.Should().ThrowAsync<IOException>();
        copy.Diagnostics.Should().HaveCount(1025);
    }

    [Theory]
    [InlineData("disabled")]
    [InlineData("missing-copy")]
    [InlineData("wrong-reporter")]
    [InlineData("single-sink")]
    public void Independent_policy_requires_an_explicit_admitted_copy_and_its_gap_reporter(string kind)
    {
        using var copy = new Sink("copy");
        using var projection = new Sink("projection");
        using var other = new Sink("other");
        var create = () => new EvidenceLoggerProvider(kind is "single-sink" ? [copy] : [copy, projection],
            kind is "wrong-reporter" ? other : copy, bufferDiagnostics: kind is not "disabled",
            requiredOrdinaryCopy: kind is "missing-copy" ? other : copy);
        create.Should().Throw<ArgumentException>();
    }

    private static EvidenceLoggerProvider Create(Sink copy, Sink projection) =>
        new([copy, projection], copy, bufferDiagnostics: true, requiredOrdinaryCopy: copy);

    [Fact]
    public async Task Shutdown_joins_the_entire_accepted_projection_backlog_after_required_copies_have_completed()
    {
        using var copy = new Sink("copy");
        using var projection = new Sink("projection", blocked: true);
        await using var provider = Create(copy, projection);
        var logger = provider.CreateLogger("fixture");
        Task draining;
        try
        {
            for (var index = 0; index < 64; index++)
            {
                Log(logger, index);
                if (index == 0) { await projection.Started.Task.WaitAsync(Deadline, TestContext.Current.CancellationToken); }
                await copy.NextDeliveryAsync();
            }
            copy.Diagnostics.Should().HaveCount(64);
            projection.Diagnostics.Should().BeEmpty();
            draining = provider.DisposeAsync().AsTask();
            draining.IsCompleted.Should().BeFalse(
                "closing the UI and finishing required copies cannot discard accepted projection work");
        }
        finally { projection.Release.Set(); }
        await draining.WaitAsync(Deadline, TestContext.Current.CancellationToken);
        projection.Diagnostics.Should().HaveCount(64);
        projection.Identities().Should().BeEquivalentTo(copy.Identities());
    }

    [Fact]
    public async Task Activating_di_logging_before_startup_supplies_live_authority_to_deferred_lifecycle_work()
    {
        using var copy = new Sink("copy");
        using var projection = new Sink("projection");
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.ClearProviders());
        services.AddSingleton<ILoggerProvider>(_ => Create(copy, projection));
        await using var serviceProvider = services.BuildServiceProvider();
        var logger = serviceProvider.GetRequiredService<ILogger<IndependentEvidenceLoggerProviderTests>>();
        var request = HostRequest.Create(RequestOrigin.HostSystem);
        using (var startup = HostActivity.BeginRoot(request, HostActivityLayer.Desktop, HostOperation.Startup))
        {
            startup.Activity.Should().NotBeNull();
            HostActivity.RequireCurrent().Should().BeSameAs(startup);
            var admitted = await Task.Run(() => HostActivity.RequireCurrent().Request, TestContext.Current.CancellationToken);
            admitted.Should().BeSameAs(request);
            Log(logger, 0);
            startup.Complete(HostOperationOutcome.Completed);
        }
        await serviceProvider.DisposeAsync();
        foreach (var sink in new[] { copy, projection })
        {
            sink.Diagnostics.Should().ContainSingle().Which.Host.Should().BeSameAs(request);
            sink.Activities.Should().ContainSingle().Which.Host.Should().BeSameAs(request);
            sink.Activities.Single().Outcome.Should().Be(HostOperationOutcome.Completed);
            sink.PersistenceHadHostContext.Should().BeFalse();
        }
        copy.Gaps.Should().BeEmpty();
    }

    private sealed class UnavailableStartup;

    private static void CompleteActivity()
    {
        using var activity = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi),
            HostActivityLayer.Desktop, HostOperation.Presentation);
        activity.Complete(HostOperationOutcome.Completed);
    }

    private static Dictionary<string, object?> State(int sequence) => new(StringComparer.Ordinal)
    {
        ["{OriginalFormat}"] = "Fixture {Sequence}.", ["Sequence"] = sequence,
    };

    private static void Log(ILogger logger, int sequence)
    {
        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.Log(LogLevel.Information, new EventId(1), State(sequence), null, static (_, _) => "unused");
        }
    }

    private sealed class Sink(string name, bool blocked = false) : IDeferredEvidenceSink, IEvidenceGapReporter, IDisposable
    {
        private readonly SemaphoreSlim delivered = new(0);
        public string Name => name;
        public ManualResetEventSlim Release { get; } = new(!blocked);
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource AuditStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ConcurrentQueue<DiagnosticEnvelope> Diagnostics { get; } = new();
        public ConcurrentQueue<CompletedActivityEnvelope> Activities { get; } = new();
        public ConcurrentQueue<AuditEnvelope> Audits { get; } = new();
        public ConcurrentQueue<EvidenceGap> Gaps { get; } = new();
        public bool FailWrites { get; init; }
        public bool UnexpectedFailure { get; init; }
        public bool FailGaps { get; set; }
        public Exception? PreparationFailure { get; init; }
        public bool PersistenceHadHostContext { get; private set; }
        public Action PrepareDiagnostic(DiagnosticEnvelope envelope)
        {
            if (PreparationFailure is { } failure) { throw failure; }
            return () => WriteDiagnostic(envelope);
        }
        public Action PrepareActivity(CompletedActivityEnvelope envelope)
        {
            if (PreparationFailure is { } failure) { throw failure; }
            return () => WriteActivity(envelope);
        }
        public async Task NextDeliveryAsync() =>
            (await delivered.WaitAsync(Deadline, TestContext.Current.CancellationToken)).Should().BeTrue();
        public HostId<EvidenceIdentity>[] Identities() => Diagnostics.Select(row => row.EvidenceId)
            .Concat(Activities.Select(row => row.EvidenceId)).ToArray();

        private void Admit()
        {
            Started.TrySetResult();
            Release.Wait(TestContext.Current.CancellationToken);
            PersistenceHadHostContext |= HostActivity.Current is not null;
            if (FailWrites) { throw new IOException(name + " unavailable"); }
            if (UnexpectedFailure) { throw new NotSupportedException("Synthetic unexpected worker failure."); }
        }
        public void WriteDiagnostic(DiagnosticEnvelope envelope)
        {
            Admit();
            Diagnostics.Enqueue(envelope);
            delivered.Release();
        }
        public void WriteActivity(CompletedActivityEnvelope envelope)
        {
            Admit();
            Activities.Enqueue(envelope);
            delivered.Release();
        }
        public void WriteAudit(AuditEnvelope envelope)
        {
            Audits.Enqueue(envelope);
            AuditStarted.TrySetResult();
        }
        public void Report(EvidenceGap gap)
        {
            if (FailGaps) { throw new IOException(name + " gap unavailable"); }
            Gaps.Enqueue(gap);
        }
        public void Dispose()
        {
            Release.Dispose();
            delivered.Dispose();
        }
    }
}
