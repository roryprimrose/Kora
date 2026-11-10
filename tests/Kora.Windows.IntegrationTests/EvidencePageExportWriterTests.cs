using System.Diagnostics;
using AwesomeAssertions;
using Kora.Application.Diagnostics;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Presentation;
using Kora.Windows.IntegrationTests.Storage;
using Kora.Windows.Presentation;
using Kora.Windows.Storage;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

[Collection("Host tracing")]
public sealed class EvidencePageExportWriterTests : IDisposable
{
    private readonly string root = Path.Combine(OwnedStorageFixture.ScratchParent(), ".scratch", "evidence-export", Guid.NewGuid().ToString("N"));
    private readonly ActivityListener listener = new()
    {
        ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
        Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
    };
    public EvidencePageExportWriterTests() { Directory.CreateDirectory(root); ActivitySource.AddActivityListener(listener); }
    public void Dispose() { Directory.Delete(root, recursive: true); listener.Dispose(); }
    private WindowsEvidencePageExportWriter Writer(IExactAnswerExportCheckpoint? checkpoint = null) =>
        new(new Paths(root), NullLogger<WindowsEvidencePageExportWriter>.Instance, checkpoint);
    private static EvidencePageExportSnapshot Source() => new(new(EvidencePageStatus.Partial, [], "never-exported-cursor",
        [EvidenceSource.Session, EvidenceSource.Conversation], EvidencePage.StorageDisclosure));

    [Fact]
    public async Task NativeSourceComparisonPreservesOriginalReadRequestButAuditsFreshExportActor()
    {
        using var paths = new OwnedStorageFixture();
        var sink = new WindowsSqliteEvidenceSink(paths);
        sink.Initialize();
        var trace = new TraceSnapshot(new string('a', 32), new string('b', 16), null,
            ActivityTraceFlags.Recorded, "Kora.Application", "1.0.0", "native.export.test", ActivityKind.Internal);
        sink.WriteActivity(new(new(Guid.NewGuid()), trace, HostRequest.Create(RequestOrigin.LocalUi), DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow, HostOperationOutcome.Completed, []));
        var query = new DurableEvidenceQuery(new WindowsSqliteEvidenceReader(sink), new Access(), TimeProvider.System,
            NullLogger<DurableEvidenceQuery>.Instance);
        EvidencePageExportSource source;
        var original = HostRequest.Create(RequestOrigin.LocalUi);
        using (var read = HostActivity.BeginRoot(original, HostActivityLayer.Application, HostOperation.Evidence))
        {
            var page = await query.QueryAsync(new() { Source = EvidenceSource.Span }, null, TestContext.Current.CancellationToken);
            page.Records.Should().ContainSingle();
            source = query.CaptureExportSource(page);
            source.Snapshot.Text.Should().Contain("native.export.test").And.Contain("\"Source\":\"Span\"").And.Contain("\"Cursor\":null");
        }
        using var export = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Application, HostOperation.Storage);
        export.Request.Should().NotBe(original);
        var audit = new Audit();
        var path = Path.Combine(root, "native-source.json");
        await using var destination = await Writer().SelectAsync(path, TestContext.Current.CancellationToken);
        await using var review = new EvidencePageExportReview(source, destination, () => true, audit, NullLogger<EvidencePageExportReview>.Instance);
        (await review.ConfirmAsync(TestContext.Current.CancellationToken)).Should().Be(EvidencePageExportOutcome.Saved);
        audit.Actors.Should().Equal(export.Request, export.Request);
        (await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken)).Should().Equal(source.Snapshot.CopyBytes());
    }

    [Fact]
    public async Task ExactJsonReadbackIsCreateOnlyAndIndependentFromAnswerAuthority()
    {
        using var host = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Windows, HostOperation.Storage);
        var path = Path.Combine(root, "page.json");
        var source = Source();
        await using (var destination = await Writer().SelectAsync(path, TestContext.Current.CancellationToken))
        {
            File.Exists(path).Should().BeFalse();
            (await destination.PublishAsync(source, _ => Task.CompletedTask, () => true, TestContext.Current.CancellationToken))
                .Should().Be(EvidencePageExportOutcome.Saved);
            var replay = () => destination.PublishAsync(source, _ => Task.CompletedTask, () => true, TestContext.Current.CancellationToken);
            await replay.Should().ThrowAsync<InvalidOperationException>();
        }
        (await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken)).Should().Equal(source.CopyBytes());
        (await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken)).Should().NotContain("never-exported-cursor");
        var replace = () => Writer().SelectAsync(path, TestContext.Current.CancellationToken);
        await replace.Should().ThrowAsync<IOException>();
        var answer = new WindowsExactAnswerExportWriter(new Paths(root), NullLogger<WindowsExactAnswerExportWriter>.Instance);
        var borrow = () => answer.SelectAsync(Path.Combine(root, "not-an-answer.json"), TestContext.Current.CancellationToken);
        await borrow.Should().ThrowAsync<InvalidDataException>();
        var wrongFormat = () => Writer().SelectAsync(Path.Combine(root, "not-json.md"), TestContext.Current.CancellationToken);
        await wrongFormat.Should().ThrowAsync<InvalidDataException>();
    }

    [Theory]
    [InlineData(ExactAnswerExportPhase.BeforeStaging, "source")]
    [InlineData(ExactAnswerExportPhase.Staged, "source")]
    [InlineData(ExactAnswerExportPhase.Staged, "privacy")]
    [InlineData(ExactAnswerExportPhase.BeforeStaging, "race")]
    [InlineData(ExactAnswerExportPhase.Staged, "race")]
    [InlineData(ExactAnswerExportPhase.BeforeStaging, "cancel")]
    [InlineData(ExactAnswerExportPhase.Staged, "cancel")]
    [InlineData(ExactAnswerExportPhase.Published, "failure")]
    [InlineData(ExactAnswerExportPhase.Readback, "failure")]
    public async Task PublicationBoundariesPreserveTruthAndNeverReplace(ExactAnswerExportPhase phase, string fault)
    {
        using var host = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Windows, HostOperation.Storage);
        using var cancellation = new CancellationTokenSource();
        var path = Path.Combine(root, "page.json");
        var live = true;
        var current = true;
        var checkpoint = new Checkpoint(observed =>
        {
            if (observed != phase) { return; }
            switch (fault)
            {
                case "source": current = false; break;
                case "privacy": live = false; break;
                case "race": File.WriteAllText(path, "foreign file"); break;
                case "cancel": cancellation.Cancel(); break;
                default: throw new IOException("synthetic readback/publication uncertainty");
            }
        });
        await using var destination = await Writer(checkpoint).SelectAsync(path, TestContext.Current.CancellationToken);
        var outcome = await destination.PublishAsync(Source(), _ => current ? Task.CompletedTask
            : Task.FromException(new InvalidOperationException("source changed")), () => live, cancellation.Token);
        outcome.Should().Be(fault is "failure" ? EvidencePageExportOutcome.Uncertain
            : fault is "cancel" ? EvidencePageExportOutcome.Cancelled : EvidencePageExportOutcome.NotWritten);
        if (fault is "race") { (await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken)).Should().Be("foreign file"); }
        else { File.Exists(path).Should().Be(fault is "failure"); }
        Directory.GetFiles(root, ".kora-export-*").Should().BeEmpty();
    }

    [Fact]
    public async Task AncestorsRemainHeldUntilDestinationDisposal()
    {
        using var host = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Windows, HostOperation.Storage);
        var parent = Path.Combine(root, "held");
        Directory.CreateDirectory(parent);
        var destination = await Writer().SelectAsync(Path.Combine(parent, "page.json"), TestContext.Current.CancellationToken);
        Action race = () => Directory.Move(parent, parent + "-moved");
        race.Should().Throw<IOException>();
        await destination.DisposeAsync();
        race.Should().NotThrow();
    }

    private sealed class Paths(string root) : IApplicationDataPaths
    {
        public string LocalRoot => Path.Combine(root, "private-local");
        public string RoamingRoot => Path.Combine(root, "private-roaming");
    }
    private sealed class Access : IEvidenceQueryAccess { public bool CanInspect => true; }
    private sealed class Audit : Kora.Core.Auditing.ISecurityAuditLog
    {
        internal List<HostRequest> Actors { get; } = [];
        public void Write(Kora.Core.Auditing.SecurityAuditEvent auditEvent) => Actors.Add(HostActivity.RequireCurrent().Request);
    }
    private sealed class Checkpoint(Action<ExactAnswerExportPhase> action) : IExactAnswerExportCheckpoint
    {
        public Task ReachedAsync(ExactAnswerExportPhase phase, CancellationToken token) { action(phase); return Task.CompletedTask; }
    }
}
