using System.Diagnostics;

using Avalonia.Controls;
using Avalonia.Interactivity;

using AwesomeAssertions;

using Kora.Application.Diagnostics;
using Kora.Core.Auditing;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.NativeUxFixture;
using Kora.Windows.IntegrationTests.Audio;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed class EvidenceRequestNavigationUiTests
{
    [WindowsFact]
    public async Task Native_selection_reads_request_outcome_and_opens_its_exact_causal_segment()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var listener = new ActivityListener
            {
                ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            };
            ActivitySource.AddActivityListener(listener);
            var host = HostRequest.Create(RequestOrigin.LocalUi);
            var trace = new TraceSnapshot(new string('a', 32), new string('b', 16), null,
                ActivityTraceFlags.Recorded, "Kora.Application", "1.0.0", "session.request", ActivityKind.Internal);
            var audit = new SecurityAuditEvent(Guid.NewGuid(), SecurityAuditCategory.ApplicationExecution,
                "host.query.version", SecurityAuditOutcome.Failed, SecurityAuditInitiator.LocalUser,
                "application.current", reasonCode: "provider.unavailable");
            var reference = new EvidenceReference(EvidenceSource.Span, new(Guid.NewGuid()));
            var segment = new EvidenceSegment(trace.TraceId, trace.SpanId, EvidenceSegmentStatus.Present, reference);
            var auditRecord = new EvidenceRecord(new(EvidenceSource.Audit, new(Guid.NewGuid())), null, null,
                EvidenceSegmentStatus.Present, host, trace, audit.CorrelationId, null, null, null, null, null,
                new Dictionary<string, EvidenceValue>(StringComparer.Ordinal), audit, null, [segment]);
            var span = auditRecord with { Reference = reference, Audit = null };
            var reader = new Reader(auditRecord, span);
            var model = new EvidenceViewModel(new(reader, new Access(), TimeProvider.System,
                NullLogger<DurableEvidenceQuery>.Instance), () => true, NullLogger<EvidenceViewModel>.Instance)
            { Source = EvidenceSource.Audit };
            var window = new EvidenceWindow(model);
            window.Show();
            try
            {
                await model.SearchAsync();
                window.FindControl<ListBox>("Records")!.SelectedItem = model.Records.Single();
                model.RequestSummary.Should().Contain("Failed").And.Contain("provider.unavailable");
                Click("ReadRequest");
                model.Records.Should().ContainSingle();
                model.RequestSummary.Should().Contain("Recorded terminal outcome: Failed");
                window.FindControl<ListBox>("Records")!.SelectedItem = model.Records.Single();
                Click("ReadActivity");
                model.Records.Should().ContainSingle().Which.Should().Be(span);
                window.FindControl<ListBox>("Records")!.SelectedItem = model.Records.Single();
                model.CanReadRequest.Should().BeFalse();
                model.RequestSummary.Should().Contain("Diagnostic correlation only");
                window.FindControl<ListBox>("Segments")!.SelectedItem = model.Segments.Single();
                Click("OpenSegment");
                model.Records.Should().ContainSingle().Which.Should().Be(span);
                reader.Queries.Should().HaveCount(4);
                reader.Queries[1].RequestId.Should().Be(host.RequestId);
                reader.Queries[2].SessionId.Should().Be(host.SessionId);
                reader.Queries[3].Record.Should().Be(reference);
                reader.Queries[3].SessionId.Should().Be(host.SessionId);
            }
            finally { window.Close(); }
            model.Records.Should().BeEmpty();
            model.CanReadRequest.Should().BeFalse();

            void Click(string name) =>
                window.FindControl<Button>(name)!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        });
    }

    private sealed class Access : IEvidenceQueryAccess { public bool CanInspect => true; }
    private sealed class Reader(EvidenceRecord audit, EvidenceRecord span) : IEvidenceReader
    {
        internal List<EvidenceQuery> Queries { get; } = [];
        public ValueTask<EvidenceReadBatch> ReadAsync(EvidenceQuery query, EvidenceReadCheckpoint? checkpoint,
            HostRequest request, DateTimeOffset now, CancellationToken cancellationToken)
        {
            Queries.Add(query);
            var record = query.Source == EvidenceSource.All ? span : audit;
            return ValueTask.FromResult(new EvidenceReadBatch(new(0, 0, 0, 0),
                [new(new(0, record.Reference.Source, record.Reference.Id.Value.ToString("D"), -1), record)],
                null, false, false));
        }
    }
}
