using System.Diagnostics;
using System.Text;

using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

using AwesomeAssertions;

using Kora.Application.Diagnostics;
using Kora.Controls;
using Kora.Core.Auditing;
using Kora.Core.Context;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Windows.IntegrationTests.Storage;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed partial class EvidenceLiteralFindTests
{
    [Fact]
    public async Task FindUsesOnlyExactSerializedPageWithoutChangingQueryCursorRecordsOrDiagnostics()
    {
        using var listener = WindowsDailyEvidenceReaderTests.Listen();
        var reader = new Reader();
        var logger = new Logger<EvidenceViewModel>();
        var serviceLogger = new Logger<DurableEvidenceQuery>();
        var viewer = Viewer(reader, logger: logger, serviceLogger: serviceLogger);
        await viewer.SearchAsync();
        var text = viewer.ResultText;
        var records = viewer.Records;
        var query = reader.Queries.Single();
        var expected = new EvidencePage(EvidencePageStatus.Available, [reader.Record], null, [], EvidencePage.StorageDisclosure);
        // The cursor is host-issued; every other serialized byte is the admitted service projection.
        text.Should().Contain(Encoding.UTF8.GetString(DurableEvidenceQuery.Serialize(expected)).Split("\"Cursor\":null")[0]);
        Encoding.UTF8.GetByteCount(text).Should().BeLessThanOrEqualTo(EvidencePage.MaximumBytes);
        viewer.Select(records[0]);
        var logs = serviceLogger.Messages.ToArray();
        viewer.FindQuery = "aaa";
        viewer.FindLiteral(SnapshotFindDirection.First);
        var start = text.IndexOf("aaaaaaa", StringComparison.Ordinal);
        viewer.FindSelection.Should().Be(new SnapshotLiteralSpan(start, 3));
        viewer.FindLiteral(SnapshotFindDirection.Next);
        viewer.FindSelection.Should().Be(new SnapshotLiteralSpan(start + 3, 3));
        viewer.FindLiteral(SnapshotFindDirection.Next);
        viewer.FindSelection.Should().BeNull();
        viewer.FindStatus.Should().Contain("End of forward direction").And.Contain("no wrap");
        viewer.CanMoveFind.Should().BeTrue();
        viewer.FindLiteral(SnapshotFindDirection.Previous);
        viewer.FindSelection.Should().Be(new SnapshotLiteralSpan(start, 3));
        viewer.FindLiteral(SnapshotFindDirection.Previous);
        viewer.FindSelection.Should().BeNull();
        viewer.FindStatus.Should().Contain("End of backward direction");
        viewer.FindLiteral(SnapshotFindDirection.First);
        viewer.FindSelection!.Start.Should().Be(start);
        viewer.FindQuery = "AAA";
        viewer.FindLiteral(SnapshotFindDirection.First);
        viewer.FindMatchText.Should().Be("AAA");
        viewer.FindQuery = "secret-volatile-query-not-in-page";
        viewer.FindLiteral(SnapshotFindDirection.First);
        viewer.FindStatus.Should().Contain("No literal match").And.Contain("not a retained-source or all-clear result");
        viewer.ClearFind();
        Retired(viewer);
        viewer.ResultText.Should().BeSameAs(text);
        viewer.Records.Should().BeSameAs(records);
        viewer.CanNext.Should().BeTrue();
        viewer.CanReadTrace.Should().BeTrue();
        reader.Queries.Should().ContainSingle().Which.Should().BeSameAs(query);
        serviceLogger.Messages.Should().Equal(logs);
        logger.Messages.Should().BeEmpty();
        await viewer.NextAsync();
        reader.Queries[1].Should().BeSameAs(query);
        reader.Checkpoints[1].Should().NotBeNull();
        viewer.Close();
    }

    [Theory]
    [InlineData("escaped-rune", "\\uD83D\\uDE42")]
    [InlineData("serialized-not-body", "\ud83d\ude42")]
    [InlineData("regex-literal", ".*?[a]")]
    [InlineData("spaces", " xyz ")]
    [InlineData("bound-characters", "characters")]
    [InlineData("bound-bytes", "bytes")]
    public async Task ValidQueriesPreserveExactSerializedEscapesCaseSpacesAndUtf16Locations(string kind, string literal)
    {
        using var listener = WindowsDailyEvidenceReaderTests.Listen();
        var reader = new Reader();
        var viewer = Viewer(reader);
        await viewer.SearchAsync();
        var query = kind switch
        {
            "bound-characters" => new string('a', 256),
            "bound-bytes" => new string('界', 170) + "é",
            _ => literal,
        };
        viewer.FindQuery = query;
        viewer.FindLiteral(SnapshotFindDirection.First);
        viewer.FindQuery.Should().Be(query);
        var start = viewer.ResultText.IndexOf(query, StringComparison.Ordinal);
        viewer.FindSelection.Should().Be(start < 0 ? null : new SnapshotLiteralSpan(start, query.Length));
        if (start >= 0)
        {
            viewer.FindMatchText.Should().Be(query);
            viewer.FindStatus.Should().Contain($"UTF-16 [{start}, {start + query.Length})");
        }
        reader.Queries.Should().ContainSingle();
        viewer.Close();
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("characters")]
    [InlineData("bytes")]
    [InlineData("high-surrogate")]
    [InlineData("low-surrogate")]
    [InlineData("control")]
    [InlineData("newline")]
    [InlineData("format")]
    [InlineData("line-separator")]
    [InlineData("paragraph-separator")]
    public async Task InvalidQueriesAreRejectedWithoutTruncationContentLoggingOrExtraRead(string kind)
    {
        using var listener = WindowsDailyEvidenceReaderTests.Listen();
        var reader = new Reader();
        var logger = new Logger<EvidenceViewModel>();
        var viewer = Viewer(reader, logger: logger);
        await viewer.SearchAsync();
        var text = viewer.ResultText;
        var query = kind switch
        {
            "empty" => "",
            "characters" => new string('x', 257),
            "bytes" => new string('界', 171),
            "high-surrogate" => new string('\ud800', 1),
            "low-surrogate" => new string('\udc00', 1),
            "control" => "private\0literal",
            "newline" => "private\nliteral",
            "format" => "private\u202eliteral",
            "line-separator" => "private\u2028literal",
            _ => "private\u2029literal",
        };
        viewer.FindQuery = query;
        viewer.FindLiteral(SnapshotFindDirection.First);
        viewer.FindQuery.Should().Be(query);
        viewer.FindSelection.Should().BeNull();
        viewer.CanMoveFind.Should().BeFalse();
        viewer.FindStatus.Should().Contain("rejected").And.Contain("nothing was truncated");
        viewer.ResultText.Should().BeSameAs(text);
        logger.Messages.Should().ContainSingle().Which.Should().Contain(nameof(InvalidDataException)).And.NotContain("private");
        reader.Queries.Should().ContainSingle();
        viewer.Close();
        Retired(viewer);
    }

    [Theory]
    [InlineData("Source")]
    [InlineData("SafeText")]
    [InlineData("SessionFilter")]
    [InlineData("TaskFilter")]
    [InlineData("TraceFilter")]
    [InlineData("RequestFilter")]
    [InlineData("InvocationFilter")]
    [InlineData("ApprovalFilter")]
    [InlineData("CorrelationFilter")]
    [InlineData("FromFilter")]
    [InlineData("UntilFilter")]
    [InlineData("EventIdFilter")]
    [InlineData("CategoryFilter")]
    [InlineData("ActionIdFilter")]
    [InlineData("Severity")]
    [InlineData("AuditOutcome")]
    [InlineData("ClearAdvanced")]
    public async Task EveryFilterEditRetiresFindPageSelectionAndContinuationWithoutReading(string filter)
    {
        using var listener = WindowsDailyEvidenceReaderTests.Listen();
        var reader = new Reader();
        var viewer = Viewer(reader);
        if (string.Equals(filter, "ClearAdvanced", StringComparison.Ordinal)) { viewer.CategoryFilter = "old"; }
        await viewer.SearchAsync();
        Match(viewer);
        viewer.Select(viewer.Records[0]);
        switch (filter)
        {
            case "Source": viewer.Source = EvidenceSource.DailyLog; break;
            case "SafeText": viewer.SafeText = "new"; break;
            case "SessionFilter": viewer.SessionFilter = "new"; break;
            case "TaskFilter": viewer.TaskFilter = "new"; break;
            case "TraceFilter": viewer.TraceFilter = "new"; break;
            case "RequestFilter": viewer.RequestFilter = "new"; break;
            case "InvocationFilter": viewer.InvocationFilter = "new"; break;
            case "ApprovalFilter": viewer.ApprovalFilter = "new"; break;
            case "CorrelationFilter": viewer.CorrelationFilter = "new"; break;
            case "FromFilter": viewer.FromFilter = "new"; break;
            case "UntilFilter": viewer.UntilFilter = "new"; break;
            case "EventIdFilter": viewer.EventIdFilter = "0"; break;
            case "CategoryFilter": viewer.CategoryFilter = "new"; break;
            case "ActionIdFilter": viewer.ActionIdFilter = "new"; break;
            case "Severity": viewer.Severity = EvidenceSeverity.Warning; break;
            case "AuditOutcome": viewer.AuditOutcome = SecurityAuditOutcome.Denied; break;
            default: viewer.ClearAdvancedFilters(); break;
        }
        Retired(viewer);
        viewer.ResultText.Should().BeEmpty();
        viewer.CanFind.Should().BeFalse();
        viewer.CanNext.Should().BeFalse();
        viewer.CanReadTrace.Should().BeFalse();
        reader.Queries.Should().ContainSingle();
        viewer.Close();
    }

    [Theory]
    [InlineData("Search")]
    [InlineData("Next")]
    [InlineData("Trace")]
    [InlineData("Segment")]
    public async Task EachExplicitReadRetiresFindBeforeAwaitAndReplacesOnlyWithItsOwnPage(string operation)
    {
        using var listener = WindowsDailyEvidenceReaderTests.Listen();
        var reader = new Reader();
        var viewer = Viewer(reader);
        await viewer.SearchAsync();
        Match(viewer);
        viewer.Select(viewer.Records[0]);
        var segment = viewer.Segments[0];
        var held = new TaskCompletionSource<EvidenceReadBatch>(TaskCreationOptions.RunContinuationsAsynchronously);
        reader.Read = () => new(held.Task);
        var pending = operation switch
        {
            "Search" => viewer.SearchAsync(),
            "Next" => viewer.NextAsync(),
            "Trace" => viewer.ReadTraceAsync(),
            _ => viewer.NavigateAsync(segment),
        };
        Retired(viewer);
        viewer.CanFind.Should().BeFalse();
        viewer.ResultText.Should().BeEmpty();
        held.SetResult(reader.Batch());
        await pending;
        Retired(viewer);
        viewer.CanFind.Should().BeTrue();
        reader.Queries.Should().HaveCount(2);
        if (string.Equals(operation, "Next", StringComparison.Ordinal)) { reader.Queries[1].Should().BeSameAs(reader.Queries[0]); }
        if (string.Equals(operation, "Trace", StringComparison.Ordinal)) { reader.Queries[1].TraceId.Should().Be(reader.Record.Trace!.TraceId); }
        if (string.Equals(operation, "Segment", StringComparison.Ordinal)) { reader.Queries[1].Record.Should().Be(segment.Record); }
        viewer.Close();
    }

    [Theory]
    [InlineData("filter")]
    [InlineData("source")]
    [InlineData("privacy")]
    [InlineData("close")]
    public async Task CancellationAndLateResultsCannotRestoreRetiredFindOrPage(string invalidation)
    {
        using var listener = WindowsDailyEvidenceReaderTests.Listen();
        var reader = new Reader();
        var access = new Access();
        var viewer = Viewer(reader, access);
        await viewer.SearchAsync();
        Match(viewer);
        var held = new TaskCompletionSource<EvidenceReadBatch>(TaskCreationOptions.RunContinuationsAsynchronously);
        reader.Read = () => new(held.Task);
        var pending = viewer.SearchAsync();
        if (string.Equals(invalidation, "filter", StringComparison.Ordinal)) { viewer.CategoryFilter = "changed"; }
        if (string.Equals(invalidation, "source", StringComparison.Ordinal)) { viewer.Source = EvidenceSource.DailyLog; }
        if (string.Equals(invalidation, "privacy", StringComparison.Ordinal)) { access.CanInspect = false; viewer.FindLiteral(SnapshotFindDirection.First); }
        if (string.Equals(invalidation, "close", StringComparison.Ordinal)) { viewer.Close(); }
        held.SetResult(reader.Batch());
        await pending;
        Retired(viewer);
        viewer.ResultText.Should().BeEmpty();
        viewer.CanFind.Should().BeFalse();
        viewer.Close();
    }

    [Theory]
    [InlineData(EvidencePageStatus.Unavailable)]
    [InlineData(EvidencePageStatus.ScanLimitReached)]
    [InlineData(EvidencePageStatus.MissingOrRemoved)]
    [InlineData(EvidencePageStatus.Partial)]
    [InlineData(EvidencePageStatus.Corrupt)]
    [InlineData(EvidencePageStatus.Truncated)]
    [InlineData(EvidencePageStatus.Changed)]
    [InlineData(EvidencePageStatus.SnapshotExpired)]
    [InlineData(EvidencePageStatus.TimedOut)]
    public async Task IncompleteSourceStatusNeverEnablesFindOrClaimsAllClear(EvidencePageStatus status)
    {
        using var listener = WindowsDailyEvidenceReaderTests.Listen();
        var reader = new Reader();
        var viewer = Viewer(reader);
        await viewer.SearchAsync();
        Match(viewer);
        reader.Read = () => new(reader.Batch() with { Status = status });
        await viewer.SearchAsync();
        Retired(viewer);
        viewer.CanFind.Should().BeFalse();
        viewer.Status.Should().StartWith(status.ToString());
        viewer.FindLiteral(SnapshotFindDirection.First);
        viewer.FindStatus.Should().Contain("unavailable").And.NotContain("No literal match");
        viewer.FindSelection.Should().BeNull();
        viewer.Close();
    }

    [Theory]
    [InlineData("unsupported")]
    [InlineData("failed")]
    [InlineData("cancelled")]
    [InlineData("missing-store")]
    [InlineData("included-source")]
    [InlineData("omitted")]
    [InlineData("unavailable-history")]
    public async Task FailureUnsupportedUnavailableOrOmittedContentRetiresFindWithoutSourceFallback(string kind)
    {
        using var listener = WindowsDailyEvidenceReaderTests.Listen();
        var reader = new Reader();
        var viewer = Viewer(reader);
        await viewer.SearchAsync();
        Match(viewer);
        reader.Read = kind switch
        {
            "unsupported" => () => throw new NotSupportedException(),
            "failed" => () => throw new InvalidDataException(),
            "cancelled" => () => throw new OperationCanceledException(),
            "missing-store" => () => throw new FileNotFoundException(),
            "included-source" => () => new(reader.Batch() with { UnavailableSources = [EvidenceSource.DailyLog] }),
            "omitted" => () => new(reader.Batch() with { Candidates = [new(new(1, EvidenceSource.Log, "one", 0), reader.Record with { ContentOmitted = true })] }),
            _ => reader.Read,
        };
        if (string.Equals(kind, "unavailable-history", StringComparison.Ordinal)) { viewer.Source = EvidenceSource.Conversation; }
        await viewer.SearchAsync();
        Retired(viewer);
        viewer.CanFind.Should().BeFalse();
        viewer.FindLiteral(SnapshotFindDirection.First);
        viewer.FindSelection.Should().BeNull();
        viewer.FindStatus.Should().Contain("unavailable");
        reader.Queries.Should().HaveCount(string.Equals(kind, "unavailable-history", StringComparison.Ordinal) ? 1 : 2);
        viewer.Close();
    }

    [Fact]
    public async Task PrivacyRevocationAndReentrantPublicationRetireTheExactMatchWithoutNewReads()
    {
        using var listener = WindowsDailyEvidenceReaderTests.Listen();
        var reader = new Reader();
        var access = new Access();
        var viewer = Viewer(reader, access);
        await viewer.SearchAsync();
        viewer.FindQuery = "aaa";
        viewer.PropertyChanged += (_, args) =>
        {
            if (string.Equals(args.PropertyName, nameof(EvidenceViewModel.FindSelection), StringComparison.Ordinal) && viewer.FindSelection is not null)
            {
                viewer.Source = EvidenceSource.DailyLog;
            }
        };
        viewer.FindLiteral(SnapshotFindDirection.First);
        Retired(viewer);
        viewer.ResultText.Should().BeEmpty();
        await viewer.SearchAsync();
        Match(viewer);
        access.CanInspect = false;
        viewer.FindQuery = "must-not-survive";
        Retired(viewer);
        viewer.ResultText.Should().BeEmpty();
        viewer.CanFind.Should().BeFalse();
        viewer.Close();
        viewer.FindQuery = "closed";
        viewer.FindLiteral(SnapshotFindDirection.First);
        Retired(viewer);
        reader.Queries.Should().HaveCount(2);
    }

    [Theory]
    [InlineData(EvidenceSource.All, true)]
    [InlineData(EvidenceSource.CombinedLog, false)]
    public async Task OnlyAllMayRetainItsExplicitUnavailableHistoryDisclosure(EvidenceSource source, bool admitted)
    {
        using var listener = WindowsDailyEvidenceReaderTests.Listen();
        var reader = new Reader();
        reader.Read = () => new(reader.Batch() with { UnavailableSources = [EvidenceSource.Session, EvidenceSource.Conversation] });
        var viewer = Viewer(reader);
        viewer.Source = source;
        await viewer.SearchAsync();
        viewer.CanFind.Should().Be(admitted);
        viewer.FindQuery = "aaa";
        viewer.FindLiteral(SnapshotFindDirection.First);
        viewer.HasFindSelection.Should().Be(admitted);
        reader.Queries.Should().ContainSingle();
        viewer.Close();
    }

    [Fact]
    public async Task RealSerializerBudgetOmissionNeverExpandsContentForFind()
    {
        using var listener = WindowsDailyEvidenceReaderTests.Listen();
        var reader = new Reader();
        var oversized = reader.Record with { Text = new string('a', EvidencePage.MaximumBytes) };
        reader.Read = () => new(reader.Batch() with
        {
            Candidates = [new(new(1, EvidenceSource.Log, oversized.Reference.Id.Value.ToString("D"), 0), oversized)],
        });
        var viewer = Viewer(reader);
        await viewer.SearchAsync();
        Encoding.UTF8.GetByteCount(viewer.ResultText).Should().BeLessThanOrEqualTo(EvidencePage.MaximumBytes);
        viewer.Records.Should().ContainSingle().Which.ContentOmitted.Should().BeTrue();
        viewer.Records[0].Text.Should().BeNull();
        viewer.CanFind.Should().BeFalse();
        viewer.FindQuery = "aaa";
        viewer.FindQuery.Should().BeEmpty();
        viewer.FindLiteral(SnapshotFindDirection.First);
        viewer.FindSelection.Should().BeNull();
        reader.Queries.Should().ContainSingle();
        viewer.Close();
    }

    [Fact]
    public async Task NativeButtonsKeyboardFocusAutomationAndClipboardBlockingUseOnlyTheCurrentPage()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var listener = WindowsDailyEvidenceReaderTests.Listen();
            var reader = new Reader();
            var viewer = Viewer(reader);
            var window = new EvidenceWindow(viewer);
            window.Show();
            try
            {
                await viewer.SearchAsync();
                var query = window.FindControl<TextBox>("LiteralQuery")!;
                var match = window.FindControl<NamedTextBlock>("LiteralMatch")!;
                var status = window.FindControl<NamedTextBlock>("FindOutcome")!;
                PressKey(window, Key.F, KeyModifiers.Control);
                query.IsFocused.Should().BeTrue();
                query.Text = "aaa";
                PressKey(query, Key.Enter);
                match.Text.Should().Be("aaa");
                match.IsVisible.Should().BeTrue();
                match.IsFocused.Should().BeTrue();
                ControlAutomationPeer.CreatePeerForElement(match)!.GetName()
                    .Should().Contain("Exact inert Evidence literal selection").And.Contain("aaa");
                ControlAutomationPeer.CreatePeerForElement(status)!.GetName().Should().Contain("UTF-16");
                PressKey(window, Key.F3);
                viewer.FindSelection!.Start.Should().Be(viewer.ResultText.IndexOf("aaaaaaa", StringComparison.Ordinal) + 3);
                PressKey(window, Key.F3, KeyModifiers.Shift);
                viewer.FindSelection!.Start.Should().Be(viewer.ResultText.IndexOf("aaaaaaa", StringComparison.Ordinal));
                Click(window, "FindPrevious");
                match.IsVisible.Should().BeFalse();
                status.Text.Should().Contain("End of backward direction");
                Click(window, "FindFirst");
                match.IsFocused.Should().BeTrue();
                Click(window, "FindNext");
                viewer.FindSelection!.Start.Should().Be(viewer.ResultText.IndexOf("aaaaaaa", StringComparison.Ordinal) + 3);
                foreach (var routedEvent in new[] { TextBox.CopyingToClipboardEvent, TextBox.CuttingToClipboardEvent, TextBox.PastingFromClipboardEvent })
                {
                    var args = new RoutedEventArgs(routedEvent);
                    query.RaiseEvent(args);
                    args.Handled.Should().BeTrue();
                }
                foreach (var key in new[] { Key.C, Key.X, Key.V, Key.Insert })
                {
                    PressKey(query, key, KeyModifiers.Control).Should().BeTrue();
                }
                query.ContextMenu.Should().BeNull();
                query.ContextFlyout.Should().BeNull();
                Click(window, "ClearLiteral");
                Retired(viewer);
                query.Text.Should().BeEmpty();
                match.Text.Should().BeEmpty();
                match.IsVisible.Should().BeFalse();
                query.Text = "aaa";
                Click(window, "FindFirst");
                viewer.CategoryFilter = "edited";
                Retired(viewer);
                match.IsVisible.Should().BeFalse();
                match.Text.Should().BeEmpty();
                reader.Queries.Should().ContainSingle();
                PressKey(window, Key.Escape);
                viewer.CanQuery.Should().BeFalse();
            }
            finally { window.Close(); }
        });
    }

    private static bool PressKey(Control control, Key key, KeyModifiers modifiers = KeyModifiers.None)
    {
        var args = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, KeyModifiers = modifiers };
        control.RaiseEvent(args);
        return args.Handled;
    }

    private static void Click(Window window, string name) =>
        window.FindControl<Button>(name)!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static void Match(EvidenceViewModel viewer)
    {
        viewer.FindQuery = "aaa";
        viewer.FindLiteral(SnapshotFindDirection.First);
        viewer.FindSelection.Should().NotBeNull();
    }

    private static void Retired(EvidenceViewModel viewer)
    {
        viewer.FindQuery.Should().BeEmpty();
        viewer.FindSelection.Should().BeNull();
        viewer.FindMatchText.Should().BeEmpty();
        viewer.HasFindSelection.Should().BeFalse();
        viewer.CanMoveFind.Should().BeFalse();
    }

    private static EvidenceViewModel Viewer(Reader reader, Access? access = null,
        ILogger<EvidenceViewModel>? logger = null, ILogger<DurableEvidenceQuery>? serviceLogger = null)
    {
        access ??= new();
        return new(new(reader, access, TimeProvider.System, serviceLogger ?? NullLogger<DurableEvidenceQuery>.Instance),
            () => access.CanInspect, logger ?? NullLogger<EvidenceViewModel>.Instance);
    }

    private sealed class Access : IEvidenceQueryAccess { public bool CanInspect { get; set; } = true; }

    private sealed class Reader : IEvidenceReader
    {
        internal Reader() => Read = () => new(Batch());
        internal EvidenceRecord Record { get; } = new(new(EvidenceSource.Log, new(Guid.NewGuid())),
            DateTimeOffset.UnixEpoch, null, EvidenceSegmentStatus.Present, null,
            new("0123456789abcdef0123456789abcdef", new string('b', 16), null, ActivityTraceFlags.Recorded,
                "Kora.Application", "1.0.0", "session.request", ActivityKind.Internal),
            null, null, "Information", "test", 0, "aaaaaaa AAA .*?[a]  xyz \ud83d\ude42",
            new Dictionary<string, EvidenceValue>(StringComparer.Ordinal) { ["SecurityAudit"] = new(EvidenceValueKind.Boolean, "true") },
            null, null, [new(new string('c', 32), new string('d', 16), EvidenceSegmentStatus.Present,
                new(EvidenceSource.Span, new(Guid.NewGuid())))]);
        internal List<EvidenceQuery> Queries { get; } = [];
        internal List<EvidenceReadCheckpoint?> Checkpoints { get; } = [];
        internal Func<ValueTask<EvidenceReadBatch>> Read { get; set; }
        internal EvidenceReadBatch Batch() => new(new(1, 0, 0, 0),
            [new(new(1, EvidenceSource.Log, Record.Reference.Id.Value.ToString("D"), 0), Record)], null, true, false,
            UnavailableSources: []);
        public ValueTask<EvidenceReadBatch> ReadAsync(EvidenceQuery query, EvidenceReadCheckpoint? checkpoint,
            HostRequest request, DateTimeOffset now, CancellationToken cancellationToken)
        {
            Queries.Add(query);
            Checkpoints.Add(checkpoint);
            return Read();
        }
    }

    private sealed class Logger<T> : ILogger<T>
    {
        internal List<string> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Messages.Add(formatter(state, exception));
    }
}
