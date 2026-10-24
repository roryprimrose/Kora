using System.Text.Json;

using Avalonia.Controls;
using Avalonia.Interactivity;

using AwesomeAssertions;

using Kora.Application.Configuration;
using Kora.Application.Dependencies;
using Kora.Application.Definitions;
using Kora.Application.Diagnostics;
using Kora.Application.Hosting;
using Kora.Application.Tools;
using Kora.Application.Voice;
using Kora.Core.Artifacts;
using Kora.Core.Auditing;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Providers;
using Kora.Core.Storage;
using Kora.Core.Tools;
using Kora.Windows.IntegrationTests.Storage;
using Kora.Windows.Storage;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed class SessionInstructionScopeWorkflowTests
{
    [Fact]
    public async Task RealStoreRetainsScopedOrdinarySubmission()
    {
        await using var f = await Fixture.CreateAsync();
        var target = f.Model.SelectedSessionRecord!;
        var review = await f.Scopes.ReviewAsync(target, f.Commands.Choices.Single(), static () => true, static () => true, f.Storage.Token);
        await f.Scopes.ConfirmAsync(review, static () => true, f.Storage.Token);
        var receipt = await f.Conversation.SubmitAsync(target, "Original ordinary request", static () => true, f.Storage.Token);
        receipt.Message.AssistantResponse!.Body.Should().Be("Genuine guided local answer");
    }

    [Fact]
    public async Task NativeApplyTwoActualOrdinaryRequestsStopAndNextRequestUseExactLocalScope()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            await using var f = await Fixture.CreateAsync();
            var failures = new List<string>();
            var controller = new SessionInstructionScopeController(static () => static () => true, static () => true,
                failures.Add, f.Commands, f.Scopes, () => f.Model, static () => null);
            f.Model.OpenSessionInstructionScopes = controller.Open;
            var sessions = new SessionsWindow(f.Model);
            sessions.Show();
            try
            {
                sessions.FindControl<Button>("SessionInstructionScopes")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var scope = controller.Window!;
                f.Source.Reads.Should().Be(0);
                scope.Sources.SelectedItem = f.Commands.Choices.Single();
                scope.Review.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await controller.Operation;
                scope.Preview.Text.Should().Contain(f.Source.Snapshot!.Document.Text);
                f.Storage.Count("conversation_messages").Should().Be(0);
                scope.Confirm.IsEnabled.Should().BeTrue();
                scope.Confirm.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await controller.Operation;
                f.Storage.Count("conversation_messages").Should().Be(0);
                scope.Close();
                await controller.CloseOperation;
                f.Scopes.Status(f.Model.SelectedSessionRecord!).Should().StartWith("Applying example");
                for (var index = 0; index < 2; index++)
                {
                    f.Model.ComposerBody = "Ordinary request " + index;
                    f.Model.CanSubmitConversation.Should().BeTrue(f.Model.ConversationStatus);
                    sessions.FindControl<Button>("SubmitConversation")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    await sessions.ConversationOperation;
                    f.Model.ConversationStatus.Should().Contain("Succeeded");
                    f.Model.ConversationMessages.Single().AssistantResponse!.Body.Should().Be("Genuine guided local answer");
                }
                f.Adapter.Guidance.Should().Equal(true, true);
                controller.Open();
                controller.Window!.Stop.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await controller.Operation;
                f.Model.ComposerBody = "After stop";
                await f.Model.SubmitConversationAsync();
                f.Model.ConversationMessages.Single().AssistantResponse!.Body.Should().Be("Genuine context-free answer");
                f.Adapter.Guidance.Should().Equal(true, true, false);
                f.Storage.Count("conversation_messages").Should().Be(3);
                f.Storage.Count("host_questions").Should().Be(0);
                f.Storage.Count("scoped_grants").Should().Be(0);
                failures.Should().BeEmpty();
            }
            finally { await controller.ShutdownAsync(); sessions.ClosePrivate(); }
        });
    }

    internal sealed class Fixture : IAsyncDisposable
    {
        internal InteractionStorageFixture Storage { get; } = new();
        internal WindowsSqliteSessionWorkspaceTests.Access Access { get; } = new();
        internal Source Source { get; } = new();
        internal Adapter Adapter { get; } = new();
        internal SessionWorkspaceService Workspace { get; private set; } = null!;
        internal SessionInstructionScopeBroker Scopes { get; private set; } = null!;
        internal SessionInstructionScopeCommandService Commands { get; private set; } = null!;
        internal SessionsViewModel Model { get; private set; } = null!;
        internal SessionConversationService Conversation { get; private set; } = null!;
        private ModelTurnHost host = null!;
        private AudioControlAdmission admission = null!;

        internal static async Task<Fixture> CreateAsync()
        {
            var f = new Fixture();
            await f.Storage.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f.Storage);
            f.Workspace = WindowsSqliteSessionWorkspaceTests.Service(f.Storage, f.Access);
            var snapshot = f.Source.Snapshot!;
            var revisions = new InstructionRevisionCatalogue(f.Storage.Paths);
            var catalogue = new ArtifactCatalogue(revisions.Reconcile(
                [snapshot.Document.Definition with { OwnedInstructionRevision = snapshot.Revision }]));
            revisions.CompleteActivation(snapshot, revisions.PrepareActivation(snapshot), catalogue);
            var sources = new ScopedInstructionUseBroker(catalogue, revisions, f.Source, f.Storage.Store, f.Access, f.Storage.Time);
            var audit = new Audit();
            f.admission = new(f.Storage.Store, f.Storage.Store, new(f.Storage.Tasks));
            var mode = new ProviderModeConfigurationService(new LocalModelProviderModePreferences(f.Storage.Paths),
                f.admission, audit);
            mode.Observe();
            f.Scopes = new(sources, f.Workspace, audit, f.Storage.Time);
            f.Commands = new(f.Scopes);
            var boundary = new Boundary();
            var dependencies = new DependencyBootstrapper([], NullLogger<DependencyBootstrapper>.Instance);
            var runtimes = new Kora.Tools.Runtime.RecordedRuntimeObservation(dependencies);
            var registry = new ReadOnlyCapabilityRegistry(boundary, new(), new(), new(boundary), new(dependencies),
                new(runtimes), new(runtimes), NullLogger<ReadOnlyCapabilityRegistry>.Instance);
            f.host = new(f.Storage.Store, f.Access, boundary, registry, audit, NullLogger<ModelTurnHost>.Instance, f.Storage.Time,
                [new(ModelProviderSelection.OllamaCandidate,
                    ModelQualificationGate.LocalCandidateSelection | ModelQualificationGate.IntegratedHost,
                    f.Storage.Time.Now.AddMinutes(5), f.Adapter)], mode);
            var conversation = new SessionConversationService(f.Storage.Store, f.Storage.Store, f.Workspace, f.Access,
                f.host, f.Storage.Time, NullLogger<SessionConversationService>.Instance, execution: f.host,
                sessionInstructionScopes: f.Scopes);
            f.Conversation = conversation;
            var sink = new WindowsSqliteEvidenceSink(f.Storage.Paths);
            sink.Initialize();
            f.Model = new(f.Workspace, new DurableEvidenceQuery(new WindowsSqliteEvidenceReader(sink), f.Access,
                f.Storage.Time, NullLogger<DurableEvidenceQuery>.Instance), f.Access, NullLogger<SessionsViewModel>.Instance,
                conversation: conversation);
            await f.Model.RefreshAsync();
            await f.Model.SelectAsync(f.Model.Sessions.Single());
            await f.Model.ReadConversationAsync();
            return f;
        }
        public async ValueTask DisposeAsync()
        { Model.Close(); Scopes.Dispose(); await host.DisposeAsync(); await admission.DisposeAsync(); Storage.Dispose(); }
    }

    internal sealed class Source : IOwnedInstructionStore
    {
        internal int Reads { get; private set; }
        internal Func<Task>? BeforeRead { get; set; }
        internal OwnedInstructionSnapshot? Snapshot { get; set; } = new("example", new string('a', 48), new string('b', 48),
            ArtifactFileDocument.Parse(ArtifactFileDocument.Encode(
                "---\nkora-format: 1\nname: example\ndescription: Brief style\n---\nUse concise guidance.", false),
                ArtifactKind.Instruction, "kora-profile", "example"));
        public Task<IReadOnlyList<string>> List(CancellationToken token) => throw new NotSupportedException();
        public async Task<OwnedInstructionSnapshot?> Read(string name, CancellationToken token)
        { Reads++; if (BeforeRead is { } before) { await before(); } return Snapshot; }
        public Task<InstructionWriteResult> Write(string name, OwnedInstructionSnapshot? expected, ArtifactFileDocument? replacement,
            Func<bool> eligible, Func<Func<InstructionWriteResult>, Task<InstructionWriteResult>> commit, CancellationToken token) =>
            throw new NotSupportedException();
    }
    internal sealed class Adapter : IModelTurnAdapter
    {
        internal List<bool> Guidance { get; } = [];
        public Task<ModelProviderReply> RunAsync(ModelTurnProvenance provenance, ProviderProfileTuple? profile,
            ReadOnlyMemory<byte> context, Func<string, string, CancellationToken, ValueTask<CapabilityReply>> invokeTool,
            CancellationToken token)
        {
            using var wire = JsonDocument.Parse(context);
            var guided = wire.RootElement.GetProperty("Evidence").GetArrayLength() != 0;
            if (guided) { wire.RootElement.GetProperty("Tools").GetArrayLength().Should().Be(0); }
            Guidance.Add(guided);
            return Task.FromResult(new ModelProviderReply(ModelTurnOutcome.Succeeded, provenance.Selection,
                new(guided ? "Genuine guided local answer" : "Genuine context-free answer", null)));
        }
    }
    private sealed class Boundary : ICapabilityHostAccess, IApplicationInfo
    { public bool IsCurrentHost => true; public string Version => "1.0.0"; }
    private sealed class Audit : ISecurityAuditLog
    { public void Write(SecurityAuditEvent auditEvent) { } }
}
