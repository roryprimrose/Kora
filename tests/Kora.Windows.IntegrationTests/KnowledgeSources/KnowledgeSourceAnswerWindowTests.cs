using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.VisualTree;
using AwesomeAssertions;
using Kora.Application.Context;
using Kora.Application.Infrastructure;
using Kora.Application.KnowledgeSources;
using Kora.Core.Auditing;
using Kora.Core.Context;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.KnowledgeSources;
using Kora.KnowledgeSources;
using Kora.NativeUxFixture;
using Kora.Windows.IntegrationTests.Storage;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests.KnowledgeSources;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed class KnowledgeSourceAnswerWindowTests
{
    [Fact]
    public Task NativeReviewSeparateConsentAndCitationUseOnlyExactRetainedSelectedSource() =>
        HeadlessSession.RunAsync(async () =>
        {
            using var listener = Listen();
            using var host = new NativeUxFixtureSession(OwnedStorageFixture.ScratchParent());
            await host.InitializeAsync();
            host.Main.BindClipboardOwnershipGate(static () => true);
            var f = new Fixture();
            var window = new KnowledgeSourcesWindow(host.Main, f.Manager, answers: f.Workflow);
            window.Show();
            window.UpdateLayout();
            try
            {
                await Press(window, "Reload source inventory");
                window.GetVisualDescendants().OfType<ListBox>().Single().SelectedIndex = 1;
                Question(window).Text = "  alpha question?  ";
                f.Context.Should().BeNull();
                await Press(window, "Review selected named source excerpts for this question");
                f.Context.Should().BeNull();
                Status(window).Text.Should().Contain("Second").And.Contain("alpha SECOND retained")
                    .And.Contain("  alpha question?  ").And.Contain("Private LocalOnly");
                await Press(window, "Confirm one private local-only no-tools named-source answer");
                f.Context!.UserRequest.Should().Be("  alpha question?  ");
                f.Context.Evidence.Should().ContainSingle().Which.Content.Should().Be("alpha SECOND retained");
                Status(window).Text.Should().Contain("Succeeded").And.Contain("synthetic supported claim");
                var storeReads = f.StoreReads;
                var inspections = f.Inspections;
                await Press(window, "Inspect exact answer citation: notes.md, line 1");
                f.StoreReads.Should().Be(storeReads);
                f.Inspections.Should().Be(inspections);
                f.OriginalReads.Should().Be(0);
                var preview = window.GetVisualDescendants().OfType<TextBox>().Single(item =>
                    string.Equals(AutomationProperties.GetName(item), "Exact admitted untrusted text, never executed", StringComparison.Ordinal));
                preview.Text.Should().Be("alpha SECOND retained");
                preview.SelectionStart.Should().Be(0);
                preview.SelectionEnd.Should().Be(preview.Text!.Length);
                Question(window).Text = "new question";
                Status(window).Text.Should().BeNull();
                window.GetVisualDescendants().OfType<Button>().Should().NotContain(item =>
                    item.Content != null && string.Equals(item.Content.ToString(), "Confirm one private local-only no-tools named-source answer", StringComparison.Ordinal));
            }
            finally { await window.StopAsync(); }
        });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task OriginalTypedOrVoiceEntryChoosesExactNameButNeverAutoConfirmsAndMissingNameRequiresExplicitSelection(bool voice) =>
        HeadlessSession.RunAsync(async () =>
        {
            using var listener = Listen();
            using var host = new NativeUxFixtureSession(OwnedStorageFixture.ScratchParent());
            await host.InitializeAsync();
            host.Main.BindClipboardOwnershipGate(static () => true);
            var f = new Fixture();
            var window = new KnowledgeSourcesWindow(host.Main, f.Manager, answers: f.Workflow);
            window.Show();
            window.UpdateLayout();
            try
            {
                using var input = HostActivity.BeginRoot(HostRequest.Create(voice ? RequestOrigin.ActivatedVoice : RequestOrigin.LocalUi),
                    HostActivityLayer.Application, HostOperation.Request);
                await window.OpenAnswerAsync(new("Second", "  alpha?  "), () => true);
                Question(window).Text.Should().Be("  alpha?  ");
                window.GetVisualDescendants().OfType<ListBox>().Single().SelectedItem.Should().Be(f.Sources[1]);
                f.Inspections.Should().Be(0);
                await Press(window, "Review selected named source excerpts for this question");
                f.Context.Should().BeNull();
                await window.OpenAnswerAsync(new("Ambiguous or missing", "alpha?"), () => true);
                window.GetVisualDescendants().OfType<ListBox>().Single().SelectedItem.Should().BeNull();
                Status(window).Text.Should().Contain("Explicitly select");
                f.Context.Should().BeNull();
            }
            finally { await window.StopAsync(); }
        });

    [Fact]
    public Task ClosingPrivateSurfaceCancelsAndJoinsIgnoringModelBeforeDisposalWithoutLateResult() =>
        HeadlessSession.RunAsync(async () =>
        {
            using var listener = Listen();
            using var host = new NativeUxFixtureSession(OwnedStorageFixture.ScratchParent());
            await host.InitializeAsync();
            host.Main.BindClipboardOwnershipGate(static () => true);
            var f = new Fixture { Delay = true };
            var window = new KnowledgeSourcesWindow(host.Main, f.Manager, answers: f.Workflow);
            window.Show();
            window.UpdateLayout();
            try
            {
                await Press(window, "Reload source inventory");
                window.GetVisualDescendants().OfType<ListBox>().Single().SelectedIndex = 0;
                Question(window).Text = "alpha";
                await Press(window, "Review selected named source excerpts for this question");
                var work = Press(window, "Confirm one private local-only no-tools named-source answer");
                await f.Entered.Task.WaitAsync(host.Token);
                var stop = window.StopAsync();
                await f.Cancelled.Task.WaitAsync(host.Token);
                stop.IsCompleted.Should().BeFalse();
                window.IsVisible.Should().BeTrue();
                f.Disposals.Should().Be(0);
                f.Release.SetResult();
                await work;
                await stop;
                window.IsVisible.Should().BeFalse();
                f.Disposals.Should().Be(1);
                Status(window).Text.Should().BeNull();
            }
            finally { f.Release.TrySetResult(); await window.StopAsync(); }
        });

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
    private static TextBox Question(Window window) => window.GetVisualDescendants().OfType<TextBox>().Single(item =>
        string.Equals(AutomationProperties.GetName(item), "New named-source answer question", StringComparison.Ordinal));
    private static TextBlock Status(Window window) => window.GetVisualDescendants().OfType<TextBlock>().Single(item =>
        string.Equals(AutomationProperties.GetName(item), "Named-source answer review and outcome", StringComparison.Ordinal));
    private static Task Press(Window window, string caption) => ((AsyncCommand)window.GetVisualDescendants().OfType<Button>()
        .Single(item => string.Equals(item.Content?.ToString(), caption, StringComparison.Ordinal)).Command!).ExecuteAsync();

    private sealed class Fixture : IKnowledgeSourceInspector, IKnowledgeSourceStore, IModelContextRunner, ISecurityAuditLog
    {
        internal KnowledgeSource[] Sources { get; }
        private readonly KnowledgeSourceRevision[] revisions;
        internal KnowledgeSourceManager Manager { get; }
        internal KnowledgeSourceAnswerWorkflow Workflow { get; }
        internal ModelContextEnvelope? Context;
        internal int StoreReads;
        internal int Inspections;
        internal int OriginalReads;
        internal int Disposals;
        internal bool Delay;
        internal readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource Cancelled = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal Fixture()
        {
            Sources = Enumerable.Range(0, 2).Select(index =>
            {
                var path = index == 0 ? @"C:\First" : @"C:\Second";
                var text = index == 0 ? "alpha FIRST retained" : "alpha SECOND retained";
                var metadata = new KnowledgeSourceMetadata(path, "root", KnowledgeSourceScope.ImmediateFiles,
                    [new(path, "root")], [new(path + @"\notes.md", "file", Encoding.UTF8.GetByteCount(text), DateTimeOffset.UnixEpoch)]);
                return new KnowledgeSource(Guid.NewGuid(), 2, index == 0 ? "First" : "Second", metadata,
                    KnowledgeSourceState.Enabled, DateTimeOffset.UnixEpoch, 100);
            }).ToArray();
            revisions = Sources.Select((source, index) =>
            {
                var review = new KnowledgeSourceReview(Guid.NewGuid(), source.Id, HostRequest.Create(RequestOrigin.LocalUi), source.Folder);
                return new KnowledgeSourceRevision(review, [new LocalFileRevision(
                    new(review.ReviewId, review.SourceId, review.Request, source.Folder.Files[0]),
                    Encoding.UTF8.GetBytes(index == 0 ? "alpha FIRST retained" : "alpha SECOND retained"), DateTimeOffset.UnixEpoch)]);
            }).ToArray();
            Manager = new(this, this, this, TimeProvider.System);
            Workflow = new(Manager, new LocalFileLexicalRetrieval(), LocalSourceUsePolicy.Instance, this, this,
                TimeProvider.System, NullLogger<KnowledgeSourceAnswerWorkflow>.Instance);
        }
        public Task<IKnowledgeSourceSelection> InspectSourceAsync(string selectedPath, KnowledgeSourceScope scope, CancellationToken cancellationToken)
        {
            Inspections++;
            return Task.FromResult<IKnowledgeSourceSelection>(new Selection(this, Sources.Single(item =>
                string.Equals(item.Folder.CanonicalPath, selectedPath, StringComparison.Ordinal)).Folder));
        }
        private sealed class Selection(Fixture owner, KnowledgeSourceMetadata metadata) : IKnowledgeSourceSelection
        {
            public KnowledgeSourceMetadata Metadata => metadata;
            public Task ValidateAsync(CancellationToken cancellationToken) => Task.CompletedTask;
            public Task<byte[]> ReadAsync(LocalFileMetadata exactItem, CancellationToken cancellationToken)
            { owner.OriginalReads++; throw new NotSupportedException(); }
            public void Dispose() => owner.Disposals++;
        }
        public Task<IReadOnlyList<KnowledgeSource>> ListAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<KnowledgeSource>>(Sources);
        public Task<KnowledgeSourceRevision> ReadAsync(KnowledgeSource exact, CancellationToken cancellationToken)
        {
            StoreReads++;
            return Task.FromResult(revisions.Single(item => item.Reference.SourceId == exact.Id));
        }
        public Task<KnowledgeSource> BeginRefreshAsync(KnowledgeSource exact, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<KnowledgeSource> PublishAsync(KnowledgeSource? expected, string name, KnowledgeSourceRevision snapshot,
            IReadOnlyList<ReadOnlyMemory<byte>> originalBytes, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task DisableAsync(KnowledgeSource exact, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task RemoveAsync(KnowledgeSource exact, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ModelContextAdmission> AdmitAsync(LocalContextSessionTarget target, Func<bool> sourceCurrent, CancellationToken token,
            Func<CancellationToken, Task>? validateSource = null) => Task.FromResult(new ModelContextAdmission(new Turn(this, target.Request)));
        private sealed class Turn(Fixture owner, HostRequest request) : IAdmittedModelContextTurn
        {
            public HostRequest Request => request;
            public HostRevision SessionGeneration => new(1);
            public Task<LocalContextTurnReceipt> Completion { get; private set; } =
                Task.FromResult(new LocalContextTurnReceipt(request, ModelTurnOutcome.Cancelled, LocalContextTurnReason.Cancelled));
            public Task<LocalContextTurnReceipt> RunAsync(ModelContextEnvelope context, CancellationToken token) => Completion = owner.Run(context, token);
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
        private async Task<LocalContextTurnReceipt> Run(ModelContextEnvelope context, CancellationToken token)
        {
            Context = context;
            using var registration = token.Register(() => Cancelled.TrySetResult());
            Entered.TrySetResult();
            if (Delay) { await Release.Task; }
            var model = new ModelTurnResult(ModelTurnOutcome.Succeeded, ModelTurnReason.None,
                new(new(Guid.NewGuid()), context.Request, ModelProviderSelection.OllamaCandidate),
                new(JsonSerializer.Serialize(new { unsupported = false, claims = new[] { new { text = "synthetic supported claim",
                    evidenceIds = context.Evidence.Select(item => item.Id.Value) } } }), null));
            return new(context.Request, ModelTurnOutcome.Succeeded, LocalContextTurnReason.Completed, HostTaskState.Succeeded, model);
        }
        public void Write(SecurityAuditEvent auditEvent) { }
    }
}
