using System.Diagnostics;

using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Threading;

using AwesomeAssertions;

using Kora.Application.Diagnostics;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.NativeUxFixture;
using Kora.Windows.IntegrationTests.Audio;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed class EvidenceTracePreparationTests
{
    private const string Trace = "0123456789abcdef0123456789abcdef";
    private const string OtherTrace = "1123456789abcdef0123456789abcdef";
    private const string Command = "prepare evidence trace " + Trace;

    [WindowsFact]
    public async Task ExactPreparationFocusesTheSameSingletonWithEveryOtherFilterAndSourceUnchangedAndNoRead()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new Fixture();
            await f.InitializeAsync();
            f.Controller.Open();
            var opened = f.Window!;
            var model = (EvidenceViewModel)opened.DataContext!;
            SetOtherFilters(model);
            var filters = OtherFilters(model);
            using var activity = new Activity("untrusted").Start();
            await f.Run(Command);
            f.Window.Should().BeSameAs(opened);
            f.Opens.Should().Be(1);
            model.TraceFilter.Should().Be(Trace,
                "native active={0}, visible={1}, focused={2}, status={3}", opened.IsActive, opened.IsVisible,
                opened.FindControl<TextBox>("TraceId")!.IsFocused, f.Native.Main.Transcript);
            OtherFilters(model).Should().Equal(filters);
            var trace = opened.FindControl<TextBox>("TraceId")!;
            trace.Text.Should().Be(Trace);
            trace.IsFocused.Should().BeTrue();
            AutomationProperties.GetName(trace).Should().Be("Trace correlation filter");
            f.Native.Main.Transcript.Should().Contain("filter prepared").And.Contain("AND-composed")
                .And.NotContain(Trace);
            f.Reader.Calls.Should().Be(0);
            Activity.Current.Should().BeSameAs(activity);
            await f.Run("evidence trace help");
            OtherFilters(model).Should().Equal(filters);
            model.TraceFilter.Should().Be(Trace);
            f.Reader.Calls.Should().Be(0);
            f.Opens.Should().Be(1);
            await f.Run(Command);
            OtherFilters(model).Should().Equal(filters);
            f.Reader.Calls.Should().Be(0);
        });
    }

    [WindowsFact]
    public async Task NewWindowKeepsExistingDefaultsAndRequiresSeparateExplicitBoundedSearchAndNext()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new Fixture();
            await f.InitializeAsync();
            await f.Run(Command);
            var model = (EvidenceViewModel)f.Window!.DataContext!;
            var source = model.Source;
            source.Should().Be(default(EvidenceSource));
            model.SafeText.Should().BeEmpty();
            model.SessionFilter.Should().BeEmpty();
            model.Records.Should().BeEmpty();
            model.CanNext.Should().BeFalse();
            f.Reader.Calls.Should().Be(0);
            f.Native.Main.Transcript.Should().Contain("filter prepared",
                "native active={0}, visible={1}, focused={2}, trace={3}", f.Window.IsActive, f.Window.IsVisible,
                f.Window.FindControl<TextBox>("TraceId")!.IsFocused, model.TraceFilter);
            await model.SearchAsync();
            f.Reader.Calls.Should().Be(1);
            f.Reader.Queries[0].TraceId.Should().Be(Trace);
            f.Reader.Queries[0].Source.Should().Be(source);
            f.Reader.Queries[0].Limit.Should().Be(50);
            model.CanNext.Should().BeTrue();
            await model.NextAsync();
            f.Reader.Calls.Should().Be(2);
            f.Reader.Checkpoints[1].Should().NotBeNull();
            f.Reader.Queries[1].Should().Be(f.Reader.Queries[0]);
        });
    }

    [WindowsFact]
    public async Task PreparationRetiresRowsSelectionDetailsFindActionsAndCursorIncludingSameValueAndLatePages()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new Fixture();
            await f.InitializeAsync();
            await f.Run(Command);
            var model = (EvidenceViewModel)f.Window!.DataContext!;
            await model.SearchAsync();
            model.Select(model.Records[0]);
            var segment = model.Segments.Single();
            model.FindQuery = "needle";
            model.FindLiteral(Kora.Core.Context.SnapshotFindDirection.First);
            model.HasFindSelection.Should().BeTrue();
            model.CanReadTrace.Should().BeTrue();
            model.CanNext.Should().BeTrue();
            await f.Run(Command);
            AssertRetired(model);
            await model.NavigateAsync(segment);
            f.Reader.Calls.Should().Be(1, "a retired segment cannot open its old record");
            await model.SearchAsync();
            model.Select(model.Records[0]);
            f.Reader.Held = new(TaskCreationOptions.RunContinuationsAsynchronously);
            var pending = model.NextAsync();
            await f.Run("prepare evidence trace " + OtherTrace);
            f.Reader.LastToken.IsCancellationRequested.Should().BeTrue();
            AssertRetired(model);
            f.Reader.Held.SetResult(f.Reader.Page());
            await pending;
            model.TraceFilter.Should().Be(OtherTrace);
            AssertRetired(model);
            f.Reader.Calls.Should().Be(3);
        });
    }

    [Theory]
    [InlineData("owner")]
    [InlineData("input")]
    [InlineData("source")]
    [InlineData("filter-back")]
    [InlineData("close")]
    [InlineData("replace")]
    [InlineData("dispose")]
    [InlineData("data-context")]
    public async Task QueuedPreparationIsFencedByOriginalNativeViewSourceControlAndOwnership(string boundary)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new Fixture();
            await f.InitializeAsync();
            f.Controller.Open();
            var window = f.Window!;
            var model = (EvidenceViewModel)window.DataContext!;
            model.TraceFilter = OtherTrace;
            f.Native.Main.CommandText = Command;
            var preparation = f.Native.Main.RunTypedCommand.ExecuteAsync();
            preparation.IsCompleted.Should().BeFalse();
            switch (boundary)
            {
                case "owner": f.Native.Access.SetOpen(false); f.Native.Access.SetOpen(true); break;
                case "input": f.Native.Main.CommandText = "evidence trace help"; break;
                case "source": model.Source = EvidenceSource.AuthorityAudit; break;
                case "filter-back": model.SafeText = "changed"; model.SafeText = string.Empty; break;
                case "close": window.Close(); break;
                case "replace": window.Close(); f.Controller.Open(); break;
                case "dispose": f.Controller.Dispose(); break;
                case "data-context": window.DataContext = null; break;
            }
            await preparation;
            f.Native.Main.Transcript.Should().NotContain("filter prepared");
            f.Reader.Calls.Should().Be(0);
            if (!model.IsClosed) { model.TraceFilter.Should().Be(OtherTrace); }
        });
    }

    [WindowsFact]
    public async Task FocusFailureClosedModelDeniedHostAndDisposedBindingNeverClaimPreparation()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new Fixture();
            await f.InitializeAsync();
            f.Controller.Open();
            var window = f.Window!;
            var model = (EvidenceViewModel)window.DataContext!;
            window.FindControl<TextBox>("TraceId")!.IsEnabled = false;
            await f.Run(Command);
            f.Native.Main.Transcript.Should().Contain("did not confirm");
            model.TraceFilter.Should().BeEmpty();
            model.Close();
            (await window.PrepareTraceAsync(Trace, () => true)).Should().BeFalse();
            f.Native.Access.SetOpen(false);
            await f.Run(Command);
            f.Native.Access.SetOpen(true);
            f.Controller.Dispose();
            await f.Run(Command);
            f.Native.Main.Transcript.Should().Contain("native Evidence binding");
            f.Reader.Calls.Should().Be(0);
            await Dispatcher.UIThread.InvokeAsync(() => { });
        });
    }

    [Theory]
    [InlineData("source")]
    [InlineData("input")]
    [InlineData("close")]
    [InlineData("dispose")]
    public async Task ReentrantNativeFocusCannotChangeOriginalAdmissionOrSource(string boundary)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new Fixture();
            await f.InitializeAsync();
            f.Controller.Open();
            var window = f.Window!;
            var model = (EvidenceViewModel)window.DataContext!;
            window.FindControl<TextBox>("TraceId")!.GotFocus += (_, _) =>
            {
                switch (boundary)
                {
                    case "source": model.Source = EvidenceSource.AuthorityAudit; break;
                    case "input": f.Native.Main.CommandText = "evidence trace help"; break;
                    case "close": window.Close(); break;
                    case "dispose": f.Controller.Dispose(); break;
                }
            };
            await f.Run(Command);
            f.Native.Main.Transcript.Should().Contain("unavailable");
            model.TraceFilter.Should().BeEmpty();
            f.Reader.Calls.Should().Be(0);
        });
    }

    [WindowsFact]
    public async Task SourceEditsDuringNewWindowOpeningCannotBecomeThePreparationSource()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new Fixture();
            await f.InitializeAsync();
            using var opened = Window.WindowOpenedEvent.AddClassHandler<EvidenceWindow>((window, _) =>
                ((EvidenceViewModel)window.DataContext!).Source = EvidenceSource.AuthorityAudit);
            await f.Run(Command);
            f.Native.Main.Transcript.Should().Contain("unavailable");
            ((EvidenceViewModel)f.Window!.DataContext!).TraceFilter.Should().BeEmpty();
            f.Reader.Calls.Should().Be(0);
        });
    }

    private static void AssertRetired(EvidenceViewModel model)
    {
        model.Records.Should().BeEmpty();
        model.Segments.Should().BeEmpty();
        model.ResultText.Should().BeEmpty();
        model.FindQuery.Should().BeEmpty();
        model.FindMatchText.Should().BeEmpty();
        model.HasFindSelection.Should().BeFalse();
        model.CanFind.Should().BeFalse();
        model.CanReadTrace.Should().BeFalse();
        model.CanNext.Should().BeFalse();
    }

    private static void SetOtherFilters(EvidenceViewModel model)
    {
        foreach (var property in typeof(EvidenceViewModel).GetProperties()
            .Where(property => property.CanWrite && property.PropertyType == typeof(string)
                && !string.Equals(property.Name, nameof(EvidenceViewModel.TraceFilter), StringComparison.Ordinal)
                && !string.Equals(property.Name, nameof(EvidenceViewModel.FindQuery), StringComparison.Ordinal)))
        {
            property.SetValue(model, "retained-" + property.Name);
        }
        model.Source = EvidenceSource.AuthorityAudit;
        model.Severity = EvidenceSeverity.Warning;
        model.AuditOutcome = Kora.Core.Auditing.SecurityAuditOutcome.Succeeded;
        model.ParentKind = EvidenceViewModel.TraceParentKindChoices[2];
        model.TraceVersionAvailability = TraceSourceVersionAvailability.Exact;
        model.ActivityKind = EvidenceViewModel.ActivityKindChoices[1];
        model.TraceFlags = EvidenceViewModel.TraceFlagsChoices[1];
        model.Outcome = EvidenceViewModel.OutcomeChoices[1];
        model.ContentAvailability = EvidenceContentAvailability.Present;
    }

    private static IReadOnlyList<KeyValuePair<string, object?>> OtherFilters(EvidenceViewModel model) =>
        typeof(EvidenceViewModel).GetProperties().Where(property => property.CanWrite
            && !string.Equals(property.Name, nameof(EvidenceViewModel.TraceFilter), StringComparison.Ordinal)
            && !string.Equals(property.Name, nameof(EvidenceViewModel.FindQuery), StringComparison.Ordinal))
            .Select(property => KeyValuePair.Create(property.Name, property.GetValue(model))).ToArray();

    private sealed class Fixture : IDisposable
    {
        private readonly IDisposable opened;
        internal NativeUxFixtureSession Native { get; }
        internal EvidenceWindowController Controller { get; private set; } = null!;
        internal Reader Reader { get; } = new();
        internal EvidenceWindow? Window { get; private set; }
        internal int Opens { get; private set; }

        internal Fixture()
        {
            var parent = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".net-test-artifacts"));
            Directory.CreateDirectory(parent);
            Native = new(parent);
            opened = Avalonia.Controls.Window.WindowOpenedEvent.AddClassHandler<EvidenceWindow>((window, _) => { Window = window; Opens++; });
        }

        internal async Task InitializeAsync()
        {
            await Native.InitializeAsync();
            var query = new DurableEvidenceQuery(Reader, Native.Access, TimeProvider.System, NullLogger<DurableEvidenceQuery>.Instance);
            Controller = new(Native.Main, query, Native.Access, NullLogger<EvidenceViewModel>.Instance, Native.Access);
            Controller.Bind();
        }

        internal async Task Run(string command)
        {
            Native.Main.CommandText = command;
            await Native.Main.RunTypedCommand.ExecuteAsync();
        }

        public void Dispose()
        {
            Controller?.Dispose();
            opened.Dispose();
            Native.Dispose();
        }
    }

    private sealed class Reader : IEvidenceReader
    {
        internal int Calls => Queries.Count;
        internal List<EvidenceQuery> Queries { get; } = [];
        internal List<EvidenceReadCheckpoint?> Checkpoints { get; } = [];
        internal CancellationToken LastToken { get; private set; }
        internal TaskCompletionSource<EvidenceReadBatch>? Held { get; set; }

        public ValueTask<EvidenceReadBatch> ReadAsync(EvidenceQuery query, EvidenceReadCheckpoint? checkpoint,
            HostRequest request, DateTimeOffset now, CancellationToken cancellationToken)
        {
            Queries.Add(query);
            Checkpoints.Add(checkpoint);
            LastToken = cancellationToken;
            return Held is null ? ValueTask.FromResult(Page()) : new(Held.Task);
        }

        internal EvidenceReadBatch Page()
        {
            var reference = new EvidenceReference(EvidenceSource.Span, new(Guid.NewGuid()));
            var segment = new EvidenceSegment(Trace, "0123456789abcdef", EvidenceSegmentStatus.Present, reference);
            var record = new EvidenceRecord(reference, DateTimeOffset.UtcNow, null, EvidenceSegmentStatus.Present,
                null, new(Trace, segment.SpanId, null, ActivityTraceFlags.Recorded, "Kora.Desktop", null, "test", ActivityKind.Internal),
                null, null, null, null, null, "needle", new Dictionary<string, EvidenceValue>(StringComparer.Ordinal),
                null, null, [segment]);
            return new(new(1, 1, 1, 1),
                [new(new(1, EvidenceSource.Span, reference.Id.Value.ToString("D"), 0), record)], null, true, false);
        }
    }
}
