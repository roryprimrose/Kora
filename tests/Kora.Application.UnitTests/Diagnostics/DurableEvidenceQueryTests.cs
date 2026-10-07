using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using AwesomeAssertions;

using Kora.Application.Diagnostics;
using Kora.Core.Auditing;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Application.UnitTests.Diagnostics;

[Collection("Host tracing")]
public sealed class DurableEvidenceQueryTests
{
    [Fact]
    public async Task Daily_source_citations_status_and_report_survive_bounded_serialization()
    {
        using var listener = Listen();
        var fixture = new Fixture();
        var daily = Record() with
        {
            Reference = new(EvidenceSource.DailyLog, new(Guid.NewGuid())),
            CommittedUtc = null, DueUtc = null, Retention = EvidenceSegmentStatus.RetentionUnknown,
            ObservedUtc = DateTimeOffset.UtcNow,
            DailyProvenance = new("kora-20261007.log", "trusted-handle-identity", 0, "digest", new(Guid.NewGuid())),
        };
        fixture.Reader.Batch = Batch(1) with
        {
            Candidates = [new(new(0, EvidenceSource.DailyLog, "kora-20261007.log", 0), daily)],
            Status = EvidencePageStatus.Partial,
            DailyReport = new("snapshot", 1, 123, 2, 0, 1, 0),
        };
        using var host = Root();
        var page = await fixture.Service.QueryAsync(new() { Source = EvidenceSource.DailyLog, Record = daily.Reference },
            null, TestContext.Current.CancellationToken);
        page.Status.Should().Be(EvidencePageStatus.Partial);
        page.DailyReport.Should().Be(fixture.Reader.Batch.DailyReport);
        page.Records.Single().Reference.Citation.Should().StartWith("kora-evidence:dailylog:");
        page.Records.Single().CommittedUtc.Should().BeNull();
        DurableEvidenceQuery.Serialize(page).Length.Should().BeLessThanOrEqualTo(EvidencePage.MaximumBytes);
    }

    [Fact]
    public async Task Pages_count_actual_serialized_UTF8_including_citations_cursor_and_disclosure()
    {
        using var listener = Listen();
        var fixture = new Fixture();
        fixture.Reader.Batch = Batch(51, new string('\u754c', 1800) + "\"\\");
        using var host = Root();
        var page = await fixture.Service.QueryAsync(new(), null, TestContext.Current.CancellationToken);
        page.Records.Count.Should().BeInRange(1, 49);
        DurableEvidenceQuery.Serialize(page).Length.Should().BeLessThanOrEqualTo(65536);
        page.Cursor.Should().NotBeNull();
        page.UnavailableSources.Should().BeEquivalentTo([EvidenceSource.Session, EvidenceSource.Conversation]);
        var next = await fixture.Service.QueryAsync(new(), page.Cursor, TestContext.Current.CancellationToken);
        fixture.Reader.Checkpoint!.Snapshot.Should().Be(fixture.Reader.Batch.Snapshot);
        fixture.Reader.Checkpoint.After.Should().Be(fixture.Reader.Batch.Candidates[page.Records.Count - 1].Position);
        next.Records.Should().NotBeEmpty();
        page.Records[0].Reference.Citation.Should().StartWith("kora-evidence:log:");
        page.Disclosure.Should().Contain("not an atomic interaction audit");
    }

    [Fact]
    public async Task Fifty_record_limit_is_independent_of_byte_budget()
    {
        using var listener = Listen();
        var fixture = new Fixture();
        fixture.Reader.Batch = Batch(51);
        using var host = Root();
        var page = await fixture.Service.QueryAsync(new(), null, TestContext.Current.CancellationToken);
        page.Records.Should().HaveCount(50);
        DurableEvidenceQuery.Serialize(page).Length.Should().BeLessThanOrEqualTo(65536);
    }

    [Fact]
    public async Task Single_oversize_content_is_explicitly_omitted_not_silently_truncated()
    {
        using var listener = Listen();
        var fixture = new Fixture();
        fixture.Reader.Batch = Batch(1, new string('\u754c', 15000));
        using var host = Root();
        var page = await fixture.Service.QueryAsync(new(), null, TestContext.Current.CancellationToken);
        page.Records.Single().ContentOmitted.Should().BeTrue();
        page.Records.Single().Text.Should().BeNull();
        page.Records.Single().Properties.Should().BeEmpty();
        DurableEvidenceQuery.Serialize(page).Length.Should().BeLessThanOrEqualTo(65536);
        fixture.Reader.Batch = Batch(1) with
        {
            Candidates = [new(new(1, EvidenceSource.Log, Guid.NewGuid().ToString("D"), -1),
                Record() with { Category = new string('x', 65536) })],
        };
        var invalid = async () => await fixture.Service.QueryAsync(new(), null, TestContext.Current.CancellationToken);
        await invalid.Should().ThrowAsync<InvalidDataException>();
    }

    [Theory]
    [InlineData(EvidenceSource.Session)]
    [InlineData(EvidenceSource.Conversation)]
    public async Task Unimplemented_sources_are_unavailable_without_reading_a_store(EvidenceSource source)
    {
        using var listener = Listen();
        var fixture = new Fixture();
        using var host = Root();
        var result = await fixture.Service.QueryAsync(new() { Source = source }, null, TestContext.Current.CancellationToken);
        result.Status.Should().Be(EvidencePageStatus.Unavailable);
        result.UnavailableSources.Should().Equal(source);
        fixture.Reader.Calls.Should().Be(0);
    }

    [Fact]
    public async Task Empty_trace_and_exact_record_report_missing_not_a_complete_trace()
    {
        using var listener = Listen();
        var fixture = new Fixture();
        using var host = Root();
        var trace = await fixture.Service.QueryAsync(new() { TraceId = new string('a', 32) }, null, TestContext.Current.CancellationToken);
        trace.Status.Should().Be(EvidencePageStatus.MissingOrRemoved);
        var record = await fixture.Service.QueryAsync(new() { Record = Record().Reference }, null, TestContext.Current.CancellationToken);
        record.Status.Should().Be(EvidencePageStatus.MissingOrRemoved);
        var empty = await fixture.Service.QueryAsync(new(), null, TestContext.Current.CancellationToken);
        empty.Status.Should().Be(EvidencePageStatus.Available);
        empty.Cursor.Should().BeNull();
        fixture.Reader.Batch = fixture.Reader.Batch with
        {
            ScannedThrough = new(10, EvidenceSource.Log, Guid.NewGuid().ToString("D"), -1),
            HasMore = true, ScanLimitReached = true,
        };
        var limited = await fixture.Service.QueryAsync(new(), null, TestContext.Current.CancellationToken);
        limited.Status.Should().Be(EvidencePageStatus.ScanLimitReached);
        limited.Cursor.Should().NotBeNull();
        await fixture.Service.QueryAsync(new(), limited.Cursor, TestContext.Current.CancellationToken);
        fixture.Reader.Checkpoint!.After.Should().Be(fixture.Reader.Batch.ScannedThrough);
    }

    [Theory]
    [InlineData(EvidenceSource.All)]
    [InlineData(EvidenceSource.Log)]
    public async Task Missing_store_is_explicitly_unavailable_and_never_created(EvidenceSource source)
    {
        using var listener = Listen();
        var fixture = new Fixture();
        fixture.Reader.Error = new FileNotFoundException();
        using var host = Root();
        var page = await fixture.Service.QueryAsync(new() { Source = source }, null, TestContext.Current.CancellationToken);
        page.Status.Should().Be(EvidencePageStatus.Unavailable);
        page.UnavailableSources.Should().Contain(EvidenceSource.Log);
        page.Disclosure.Should().Contain("no replacement");
    }

    [Fact]
    public async Task Host_context_origin_privacy_and_late_revocation_fail_closed()
    {
        using var listener = Listen();
        var fixture = new Fixture();
        var missing = async () => await fixture.Service.QueryAsync(new(), null, TestContext.Current.CancellationToken);
        await missing.Should().ThrowAsync<InvalidOperationException>();
        using (var voice = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.ActivatedVoice), HostActivityLayer.Application, HostOperation.Request))
        {
            await missing.Should().ThrowAsync<InvalidOperationException>();
        }
        using var host = Root();
        fixture.Access.CanInspect = false;
        await missing.Should().ThrowAsync<InvalidOperationException>();
        fixture.Access.CanInspect = true;
        fixture.Reader.OnRead = () => fixture.Access.CanInspect = false;
        await missing.Should().ThrowAsync<InvalidOperationException>();
        fixture.Access.CanInspect = true;
        fixture.Reader.OnRead = null;
        fixture.Reader.Error = new InvalidDataException("corrupt");
        await missing.Should().ThrowAsync<InvalidDataException>();
        host.Activity!.IsStopped.Should().BeFalse();
        var nullQuery = async () => await fixture.Service.QueryAsync(null!, null, TestContext.Current.CancellationToken);
        await nullQuery.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task Pre_and_late_cancellation_never_publish_a_success_result()
    {
        using var listener = Listen();
        var fixture = new Fixture();
        using var host = Root();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var pre = async () => await fixture.Service.QueryAsync(new(), null, cancellation.Token);
        await pre.Should().ThrowAsync<OperationCanceledException>();
        fixture.Reader.Calls.Should().Be(0);
        using var late = new CancellationTokenSource();
        fixture.Reader.OnRead = late.Cancel;
        var pending = async () => await fixture.Service.QueryAsync(new(), null, late.Token);
        await pending.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Stopped_context_and_authenticated_malformed_cursor_data_are_rejected()
    {
        using var listener = Listen();
        var fixture = new Fixture();
        using (var host = Root())
        {
            foreach (var content in new[] { "null", "{", "{\"Unknown\":true}" })
            {
                var bytes = Encoding.UTF8.GetBytes(content);
                var cursor = Convert.ToBase64String(bytes) + "."
                    + Convert.ToBase64String(HMACSHA256.HashData(fixture.Key, bytes));
                var malformed = async () => await fixture.Service.QueryAsync(new(), cursor, TestContext.Current.CancellationToken);
                await malformed.Should().ThrowAsync<InvalidDataException>();
            }
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var queued = Task.Run(async () =>
            {
                entered.SetResult();
                await resume.Task;
                var stopped = async () => await fixture.Service.QueryAsync(new(), null, TestContext.Current.CancellationToken);
                await stopped.Should().ThrowAsync<InvalidOperationException>().WithMessage("*stopped host activity*");
            }, TestContext.Current.CancellationToken);
            await entered.Task;
            host.Activity!.Stop();
            resume.SetResult();
            await queued;
        }
    }

    [Fact]
    public async Task Cursors_are_authenticated_query_session_process_and_expiry_bound()
    {
        using var listener = Listen();
        var fixture = new Fixture();
        fixture.Reader.Batch = Batch(2);
        var query = new EvidenceQuery { Limit = 1 };
        string cursor;
        using (var host = Root())
        {
            cursor = (await fixture.Service.QueryAsync(query, null, TestContext.Current.CancellationToken)).Cursor!;
            var changed = async () => await fixture.Service.QueryAsync(query with { Text = "changed" }, cursor, TestContext.Current.CancellationToken);
            await changed.Should().ThrowAsync<InvalidDataException>();
            var parts = cursor.Split('.');
            var hostile = parts[0] + "." + Convert.ToBase64String(new byte[32]);
            var tamper = async () => await fixture.Service.QueryAsync(query, hostile, TestContext.Current.CancellationToken);
            await tamper.Should().ThrowAsync<InvalidDataException>();
            var other = new Fixture();
            var restart = async () => await other.Service.QueryAsync(query, cursor, TestContext.Current.CancellationToken);
            await restart.Should().ThrowAsync<InvalidDataException>();
            fixture.Clock.Now = fixture.Clock.Now.AddMinutes(15);
            var expired = async () => await fixture.Service.QueryAsync(query, cursor, TestContext.Current.CancellationToken);
            await expired.Should().ThrowAsync<InvalidDataException>();
        }
        fixture.Clock.Now = fixture.Clock.Now.AddMinutes(-15);
        using var foreign = Root();
        var wrongSession = async () => await fixture.Service.QueryAsync(query, cursor, TestContext.Current.CancellationToken);
        await wrongSession.Should().ThrowAsync<InvalidDataException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("x.y.z")]
    [InlineData("!.!")]
    [InlineData("eA==.!")]
    public async Task Malformed_cursors_are_visible_failures(string cursor)
    {
        using var listener = Listen();
        var fixture = new Fixture();
        using var host = Root();
        var invalid = async () => await fixture.Service.QueryAsync(new(), cursor, TestContext.Current.CancellationToken);
        await invalid.Should().ThrowAsync<InvalidDataException>();
        var tooLong = async () => await fixture.Service.QueryAsync(new(), new string('x', 2049), TestContext.Current.CancellationToken);
        await tooLong.Should().ThrowAsync<InvalidDataException>();
    }

    [Fact]
    public void Filters_preserve_typed_correlation_and_allow_plain_unicode_without_SQL_interpretation()
    {
        var query = new EvidenceQuery
        {
            SessionId = new(Guid.NewGuid()), TaskId = new(Guid.NewGuid()), RequestId = new(Guid.NewGuid()),
            InvocationId = new(Guid.NewGuid()), ApprovalId = Guid.NewGuid(), CorrelationId = Guid.NewGuid(),
            TraceId = new string('a', 32), SpanId = new string('b', 16),
            FromUtc = DateTimeOffset.UnixEpoch, UntilUtc = DateTimeOffset.UnixEpoch.AddDays(1),
            Severity = EvidenceSeverity.Information, EventId = 1, Category = "test", ActionId = "test.read",
            AuditOutcome = SecurityAuditOutcome.Succeeded,
            Property = new("Count", new(EvidenceValueKind.WholeNumber, "1")), Text = "'; SELECT * \U0001f642",
            Record = new(EvidenceSource.Link, new(Guid.NewGuid()), 31),
        };
        DurableEvidenceQuery.Validate(query);
        DurableEvidenceQuery.Validate(query with { Record = new(EvidenceSource.Log, new(Guid.NewGuid())), Property = new("Null", new(EvidenceValueKind.Null, null)) });
        var bytes = JsonSerializer.SerializeToUtf8Bytes(query);
        Encoding.UTF8.GetString(bytes).Should().Contain("SessionId");
        query.Record.Citation.Should().EndWith(":31");
    }

    public static TheoryData<EvidenceQuery> InvalidFilters => new()
    {
        new EvidenceQuery { Source = (EvidenceSource)99 }, new EvidenceQuery { Limit = 0 }, new EvidenceQuery { Limit = 51 },
        new EvidenceQuery { FromUtc = DateTimeOffset.UnixEpoch.AddDays(1), UntilUtc = DateTimeOffset.UnixEpoch },
        new EvidenceQuery { Severity = (EvidenceSeverity)99 }, new EvidenceQuery { AuditOutcome = (SecurityAuditOutcome)99 },
        new EvidenceQuery { SessionId = default(HostId<SessionIdentity>) }, new EvidenceQuery { TaskId = default(HostId<TaskIdentity>) },
        new EvidenceQuery { RequestId = default(HostId<RequestIdentity>) }, new EvidenceQuery { InvocationId = default(HostId<InvocationIdentity>) },
        new EvidenceQuery { ApprovalId = Guid.Empty }, new EvidenceQuery { CorrelationId = Guid.Empty },
        new EvidenceQuery { TraceId = "" }, new EvidenceQuery { TraceId = new string('0', 32) }, new EvidenceQuery { TraceId = new string('Z', 32) },
        new EvidenceQuery { SpanId = new string('A', 16) }, new EvidenceQuery { Text = new string('x', 257) },
        new EvidenceQuery { Text = "\n" }, new EvidenceQuery { Text = "\ud800" }, new EvidenceQuery { Category = new string('x', 257) },
        new EvidenceQuery { ActionId = new string('x', 129) }, new EvidenceQuery { Record = new(EvidenceSource.All, new(Guid.NewGuid())) },
        new EvidenceQuery { Record = new(EvidenceSource.Link, new(Guid.NewGuid())) },
        new EvidenceQuery { Record = new(EvidenceSource.Link, new(Guid.NewGuid()), -1) },
        new EvidenceQuery { Record = new(EvidenceSource.Link, new(Guid.NewGuid()), 32) },
        new EvidenceQuery { Record = new(EvidenceSource.Span, new(Guid.NewGuid()), 0) },
        new EvidenceQuery { Property = new("", new(EvidenceValueKind.Text, "")) },
        new EvidenceQuery { Property = new("Secret", new(EvidenceValueKind.Text, "test")) },
        new EvidenceQuery { Property = new("Count", null!) },
        new EvidenceQuery { Property = new("Count", new((EvidenceValueKind)99, "test")) },
        new EvidenceQuery { Property = new("Count", new(EvidenceValueKind.Null, "test")) },
        new EvidenceQuery { Property = new("Count", new(EvidenceValueKind.Text, null)) },
        new EvidenceQuery { Property = new("Count", new(EvidenceValueKind.Text, new string('x', 1025))) },
    };

    [Theory]
    [MemberData(nameof(InvalidFilters))]
    public void Hostile_filters_are_rejected_before_store_access(EvidenceQuery query)
    {
        var invalid = () => DurableEvidenceQuery.Validate(query);
        invalid.Should().Throw<Exception>();
    }

    private static HostActivity Root() => HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi),
        HostActivityLayer.Application, HostOperation.Request);

    private static ActivityListener Listen()
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
        };
        ActivitySource.AddActivityListener(listener);
        return listener;
    }

    private static EvidenceRecord Record(string text = "test") => new(new(EvidenceSource.Log, new(Guid.NewGuid())),
        DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddDays(30), EvidenceSegmentStatus.Present,
        null, null, null, null, "Information", "test", 1, text,
        new Dictionary<string, EvidenceValue>(StringComparer.Ordinal), null, null, []);

    private static EvidenceReadBatch Batch(int count, string text = "test") => new(new(100, 100, 100, 100),
        Enumerable.Range(1, count).Select(index =>
        {
            var record = Record(text);
            return new EvidenceCandidate(new(index, EvidenceSource.Log, record.Reference.Id.Value.ToString("D"), -1), record);
        }).ToArray(), null, HasMore: false, ScanLimitReached: false);

    private sealed class Fixture
    {
        internal byte[] Key { get; } = RandomNumberGenerator.GetBytes(32);
        internal FakeReader Reader { get; } = new();
        internal Access Access { get; } = new();
        internal Clock Clock { get; } = new();
        internal DurableEvidenceQuery Service { get; }
        internal Fixture() => Service = new(Reader, Access, Clock, NullLogger<DurableEvidenceQuery>.Instance, Key);
    }

    private sealed class Access : IEvidenceQueryAccess { public bool CanInspect { get; set; } = true; }
    private sealed class Clock : TimeProvider
    {
        internal DateTimeOffset Now { get; set; } = DateTimeOffset.UnixEpoch;
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private sealed class FakeReader : IEvidenceReader
    {
        internal EvidenceReadBatch Batch { get; set; } = new(new(0, 0, 0, 0), [], null, false, false);
        internal EvidenceReadCheckpoint? Checkpoint { get; private set; }
        internal Action? OnRead { get; set; }
        internal Exception? Error { get; set; }
        internal int Calls { get; private set; }
        public ValueTask<EvidenceReadBatch> ReadAsync(EvidenceQuery query, EvidenceReadCheckpoint? checkpoint,
            HostRequest request, DateTimeOffset now, CancellationToken cancellationToken)
        {
            Calls++;
            Checkpoint = checkpoint;
            OnRead?.Invoke();
            if (Error is not null) { throw Error; }
            return ValueTask.FromResult(Batch);
        }
    }
}
